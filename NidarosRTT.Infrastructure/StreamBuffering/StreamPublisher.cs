using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NidarosRTT.Infrastructure.Captions;

namespace NidarosRTT.Infrastructure.StreamBuffering
{
    /// <summary>
    /// Interface for publishing buffered stream back to Wowza
    /// </summary>
    public interface IStreamPublisher
    {
        /// <summary>
        /// Start publishing the delayed stream
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Queue a caption to be injected into the video stream
        /// </summary>
        void QueueCaption(string text, uint timecode);

        /// <summary>
        /// Get statistics about stream publishing
        /// </summary>
        StreamPublisherStats GetStats();
    }

    /// <summary>
    /// Publishes buffered stream packets back to Wowza as a delayed stream.
    /// Uses FFmpeg to handle RTMP protocol and re-ingestion.
    /// </summary>
    public class StreamPublisher : IStreamPublisher
    {
        private readonly string _outputUrl;
        private readonly string _ffmpegPath;
        private readonly IStreamBuffer _streamBuffer;
        private readonly CaptionQueue _captionQueue;
        private readonly Cea608Encoder _cea608Encoder;
        private readonly H264SeiBuilder _seiBuilder;
        private readonly FlvVideoTagModifier _videoTagModifier;
        private readonly object _statsLock = new object();
        
        private StreamPublisherStats _stats = new StreamPublisherStats();
        private Process? _ffmpegProcess;
        private Stream? _ffmpegStdin;
        private bool _headerWritten = false;
        private int _reconnectAttempts = 0;
        private const int MAX_RECONNECT_DELAY_MS = 30000;
        
        // Caption injection settings
        private DateTime _lastCaptionInjection = DateTime.MinValue;
        private const int MIN_CAPTION_INTERVAL_MS = 2000; // Inject captions at most every 2 seconds

        public StreamPublisher(string outputUrl, string ffmpegPath, IStreamBuffer streamBuffer, CaptionQueue? captionQueue = null)
        {
            _outputUrl = outputUrl;
            _ffmpegPath = ffmpegPath;
            _streamBuffer = streamBuffer;
            _captionQueue = captionQueue ?? new CaptionQueue();
            _cea608Encoder = new Cea608Encoder();
            _seiBuilder = new H264SeiBuilder();
            _videoTagModifier = new FlvVideoTagModifier();

            if (!File.Exists(_ffmpegPath))
            {
                throw new FileNotFoundException($"FFmpeg executable not found at '{_ffmpegPath}'");
            }
        }
        
        /// <summary>
        /// Queue a caption to be injected into the video stream
        /// </summary>
        public void QueueCaption(string text, uint timecode)
        {
            _captionQueue.QueueCaption(text, timecode);
            
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"[STREAM PUBLISHER] Queued caption at {timecode}ms: '{text.Substring(0, Math.Min(text.Length, 50))}'");
            Console.ResetColor();
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[STREAM PUBLISHER] Starting delayed stream publish to: {_outputUrl}");
            Console.ResetColor();

            // Wait for buffer to be ready (30 seconds accumulated)
            Console.WriteLine("[STREAM PUBLISHER] Waiting for buffer to reach 30 seconds...");
            while (!_streamBuffer.IsBufferReady() && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
                var bufferDuration = _streamBuffer.GetBufferDurationMs() / 1000.0;
                if (bufferDuration > 0)
                {
                    Console.WriteLine($"[STREAM PUBLISHER] Buffer: {bufferDuration:F1}s / 30.0s");
                }
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[STREAM PUBLISHER] Buffer ready, starting publication...");
            Console.ResetColor();

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await PublishStreamAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("[STREAM PUBLISHER] Cancelled");
                    break;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[STREAM PUBLISHER] Error: {ex.Message}");
                    Console.ResetColor();

                    // Exponential backoff for reconnection
                    _reconnectAttempts++;
                    var delay = Math.Min(1000 * (int)Math.Pow(2, _reconnectAttempts), MAX_RECONNECT_DELAY_MS);
                    
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[STREAM PUBLISHER] Reconnecting in {delay}ms (attempt {_reconnectAttempts})...");
                    Console.ResetColor();

                    await Task.Delay(delay, cancellationToken);
                }
            }

            CleanupFFmpeg();
        }

        private async Task PublishStreamAsync(CancellationToken cancellationToken)
        {
            // FFmpeg command to publish FLV stream to RTMP
            // FIXED: Removed bsf:v filter that was corrupting the stream
            // -re: Read input at native frame rate
            // -f flv: Input format is FLV  
            // -i pipe:0: Read from stdin
            // -c copy: Copy streams without re-encoding
            // -f flv: Output format is FLV
            var arguments = $"-re -f flv -i pipe:0 -c copy -f flv \"{_outputUrl}\"";


            var processStartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            _ffmpegProcess = new Process { StartInfo = processStartInfo };
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[STREAM PUBLISHER] Starting FFmpeg: {_ffmpegPath} {arguments}");
            Console.ResetColor();

            _ffmpegProcess.Start();
            _ffmpegStdin = _ffmpegProcess.StandardInput.BaseStream;

            // Log FFmpeg stderr in background
            _ = Task.Run(async () =>
            {
                try
                {
                    while (!_ffmpegProcess.StandardError.EndOfStream)
                    {
                        var line = await _ffmpegProcess.StandardError.ReadLineAsync();
                        if (!string.IsNullOrEmpty(line))
                        {
                            // Only log errors or important messages
                            if (line.Contains("error", StringComparison.OrdinalIgnoreCase) ||
                                line.Contains("failed", StringComparison.OrdinalIgnoreCase))
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"[STREAM PUBLISHER FFMPEG] {line}");
                                Console.ResetColor();
                            }
                        }
                    }
                }
                catch { /* Ignore errors when process exits */ }
            });

            lock (_statsLock)
            {
                _stats.IsPublishing = true;
                _stats.LastConnectedTime = DateTime.UtcNow;
                _reconnectAttempts = 0;
            }

            // Write FLV header
            await WriteFLVHeaderAsync();

            var lastStatsReport = DateTime.UtcNow;

            // Publish packets from buffer
            while (!cancellationToken.IsCancellationRequested)
            {
                var readyPackets = _streamBuffer.DequeueReadyPackets();
                
                foreach (var packet in readyPackets)
                {
                    try
                    {
                        await WritePacketAsync(packet, cancellationToken);

                        lock (_statsLock)
                        {
                            _stats.TotalPacketsPublished++;
                            switch (packet.Type)
                            {
                                case MediaPacketType.Video:
                                    _stats.VideoPacketsPublished++;
                                    break;
                                case MediaPacketType.Audio:
                                    _stats.AudioPacketsPublished++;
                                    break;
                                case MediaPacketType.Data:
                                    _stats.DataPacketsPublished++;
                                    break;
                            }
                            _stats.TotalBytesPublished += packet.Data.Length;
                            _stats.LastPacketTime = DateTime.UtcNow;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[STREAM PUBLISHER] Error writing packet: {ex.Message}");
                        Console.ResetColor();
                        throw; // Re-throw to trigger reconnection
                    }
                }

                // Report statistics every 10 seconds
                if ((DateTime.UtcNow - lastStatsReport).TotalSeconds >= 10)
                {
                    ReportStats();
                    lastStatsReport = DateTime.UtcNow;
                }

                // Small delay to avoid busy-wait if no packets ready
                if (!readyPackets.Any())
                {
                    await Task.Delay(100, cancellationToken);
                }
            }

            lock (_statsLock)
            {
                _stats.IsPublishing = false;
            }

            CleanupFFmpeg();
        }

        private async Task WriteFLVHeaderAsync()
        {
            if (_ffmpegStdin == null)
                throw new InvalidOperationException("FFmpeg stdin not initialized");

            // FLV header: "FLV" + version (1) + flags (5 = audio+video) + data offset (9)
            var header = new byte[]
            {
                (byte)'F', (byte)'L', (byte)'V',  // Signature
                0x01,                              // Version 1
                0x05,                              // Flags: audio + video
                0x00, 0x00, 0x00, 0x09,           // Data offset (big-endian)
                0x00, 0x00, 0x00, 0x00            // First previous tag size (0)
            };

            await _ffmpegStdin.WriteAsync(header, 0, header.Length);
            await _ffmpegStdin.FlushAsync();
            _headerWritten = true;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[STREAM PUBLISHER] FLV header written");
            Console.ResetColor();
        }

        private async Task WritePacketAsync(MediaPacket packet, CancellationToken cancellationToken)
        {
            if (_ffmpegStdin == null || !_headerWritten)
                throw new InvalidOperationException("FFmpeg stdin not ready");

            byte[] packetData = packet.Data;
            
            // Try to inject caption if this is a video packet
            if (packet.Type == MediaPacketType.Video)
            {
                packetData = TryInjectCaption(packet.Data, (uint)packet.Timecode);
            }

            // Construct FLV tag
            var tagType = packet.Type switch
            {
                MediaPacketType.Audio => (byte)0x08,
                MediaPacketType.Video => (byte)0x09,
                MediaPacketType.Data => (byte)0x12,
                _ => (byte)0x12
            };

            var dataSize = packetData.Length;
            var timestamp = packet.Timecode;

            // Build tag header (11 bytes)
            var tagHeader = new byte[11];
            tagHeader[0] = tagType;
            
            // Data size (3 bytes, big-endian)
            tagHeader[1] = (byte)((dataSize >> 16) & 0xFF);
            tagHeader[2] = (byte)((dataSize >> 8) & 0xFF);
            tagHeader[3] = (byte)(dataSize & 0xFF);
            
            // Timestamp (4 bytes, special format)
            tagHeader[4] = (byte)((timestamp >> 16) & 0xFF);
            tagHeader[5] = (byte)((timestamp >> 8) & 0xFF);
            tagHeader[6] = (byte)(timestamp & 0xFF);
            tagHeader[7] = (byte)((timestamp >> 24) & 0xFF); // Extended timestamp
            
            // Stream ID (3 bytes, always 0)
            tagHeader[8] = 0x00;
            tagHeader[9] = 0x00;
            tagHeader[10] = 0x00;

            // DEBUG: Log first video packet details
            if (packet.Type == MediaPacketType.Video && _stats.VideoPacketsWritten < 3)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"[STREAM PUBLISHER DEBUG] Video packet #{_stats.VideoPacketsWritten}:");
                Console.WriteLine($"  Timestamp: {timestamp}ms");
                Console.WriteLine($"  Data size: {dataSize} bytes");
                Console.WriteLine($"  First 20 bytes: {BitConverter.ToString(packetData.Take(Math.Min(20, packetData.Length)).ToArray())}");
                if (packetData.Length >= 5)
                {
                    var frameType = (packetData[0] & 0xF0) >> 4;
                    var codecId = packetData[0] & 0x0F;
                    var avcPacketType = packetData[1];
                    Console.WriteLine($"  Frame type: 0x{frameType:X} ({(frameType == 1 ? "keyframe" : frameType == 2 ? "inter" : "unknown")})");
                    Console.WriteLine($"  Codec ID: 0x{codecId:X} ({(codecId == 7 ? "AVC/H.264" : "unknown")})");
                    Console.WriteLine($"  AVC packet type: 0x{avcPacketType:X} ({(avcPacketType == 0 ? "sequence header" : avcPacketType == 1 ? "NALU" : "unknown")})");
                }
                Console.ResetColor();
            }
            
            // CRITICAL FIX: Write complete FLV tag atomically to avoid partial reads
            // Build complete tag in memory first: header + data + prevTagSize
            var prevTagSize = 11 + dataSize;
            var completeTag = new byte[11 + dataSize + 4];
            
            // Copy tag header
            Array.Copy(tagHeader, 0, completeTag, 0, 11);
            
            // Copy tag data
            Array.Copy(packetData, 0, completeTag, 11, dataSize);
            
            // Write previous tag size (4 bytes, big-endian)
            completeTag[11 + dataSize + 0] = (byte)((prevTagSize >> 24) & 0xFF);
            completeTag[11 + dataSize + 1] = (byte)((prevTagSize >> 16) & 0xFF);
            completeTag[11 + dataSize + 2] = (byte)((prevTagSize >> 8) & 0xFF);
            completeTag[11 + dataSize + 3] = (byte)(prevTagSize & 0xFF);
            
            // Write complete tag in one atomic operation
            await _ffmpegStdin.WriteAsync(completeTag, 0, completeTag.Length, cancellationToken);
            await _ffmpegStdin.FlushAsync(cancellationToken);
            
            // Track packet stats
            lock (_statsLock)
            {
                if (packet.Type == MediaPacketType.Video) _stats.VideoPacketsWritten++;
            }
        }
        
        /// <summary>
        /// Try to inject a caption into a video packet if one is available
        /// </summary>
        private byte[] TryInjectCaption(byte[] videoData, uint timecode)
        {
            try
            {
                // Check if this is a keyframe and if enough time has passed since last injection
                if (!_videoTagModifier.IsKeyframe(videoData))
                    return videoData;
                
                var timeSinceLastInjection = (DateTime.UtcNow - _lastCaptionInjection).TotalMilliseconds;
                if (timeSinceLastInjection < MIN_CAPTION_INTERVAL_MS)
                    return videoData;
                
                // Try to get a caption for this timecode
                var captionText = _captionQueue.TryGetCaptionForTimecode(timecode, toleranceMs: 500);
                if (string.IsNullOrEmpty(captionText))
                    return videoData;
                
                // Encode caption to CEA-608
                var cea608Data = _cea608Encoder.EncodeCaptionText(captionText);
                
                // Inject caption into video packet
                var modifiedData = _videoTagModifier.InjectCaption(videoData, cea608Data, injectOnKeyframesOnly: true);
                
                _lastCaptionInjection = DateTime.UtcNow;
                
                lock (_statsLock)
                {
                    _stats.CaptionsInjected++;
                }
                
                var sizeDiff = modifiedData?.Length - videoData.Length ?? 0;
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[CAPTION INJECT] Timecode: {timecode}ms, Text: '{captionText.Substring(0, Math.Min(captionText.Length, 50))}', " +
                                $"Original: {videoData.Length} bytes, Modified: {modifiedData?.Length ?? 0} bytes (+{sizeDiff})");
                Console.ResetColor();
                
                return modifiedData ?? videoData;
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[CAPTION INJECT] Error injecting caption: {ex.Message}");
                Console.ResetColor();
                return videoData; // Return original on error
            }
        }

        private void CleanupFFmpeg()
        {
            try
            {
                _ffmpegStdin?.Close();
                _ffmpegStdin?.Dispose();
                _ffmpegStdin = null;
            }
            catch { }

            if (_ffmpegProcess != null && !_ffmpegProcess.HasExited)
            {
                try
                {
                    _ffmpegProcess.Kill(entireProcessTree: true);
                    _ffmpegProcess.WaitForExit(5000);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[STREAM PUBLISHER] Error killing FFmpeg: {ex.Message}");
                }
            }
            _ffmpegProcess?.Dispose();
            _ffmpegProcess = null;
            _headerWritten = false;
        }

        private void ReportStats()
        {
            lock (_statsLock)
            {
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"[STREAM PUBLISHER STATS] Total: {_stats.TotalPacketsPublished} packets, " +
                                $"Video: {_stats.VideoPacketsPublished}, Audio: {_stats.AudioPacketsPublished}, " +
                                $"Data: {_stats.DataPacketsPublished}, " +
                                $"Captions: {_stats.CaptionsInjected}, " +
                                $"Bytes: {_stats.TotalBytesPublished / 1024 / 1024:F2} MB");
                Console.ResetColor();
            }
        }

        public StreamPublisherStats GetStats()
        {
            lock (_statsLock)
            {
                return new StreamPublisherStats
                {
                    IsPublishing = _stats.IsPublishing,
                    TotalPacketsPublished = _stats.TotalPacketsPublished,
                    VideoPacketsPublished = _stats.VideoPacketsPublished,
                    AudioPacketsPublished = _stats.AudioPacketsPublished,
                    DataPacketsPublished = _stats.DataPacketsPublished,
                    TotalBytesPublished = _stats.TotalBytesPublished,
                    CaptionsInjected = _stats.CaptionsInjected,
                    LastConnectedTime = _stats.LastConnectedTime,
                    LastPacketTime = _stats.LastPacketTime
                };
            }
        }
    }

    /// <summary>
    /// Statistics about stream publishing
    /// </summary>
    public class StreamPublisherStats
    {
        public bool IsPublishing { get; set; }
        public long TotalPacketsPublished { get; set; }
        public long VideoPacketsPublished { get; set; }
        public long AudioPacketsPublished { get; set; }
        public long DataPacketsPublished { get; set; }
        public long TotalBytesPublished { get; set; }
        public long CaptionsInjected { get; set; }
        public long VideoPacketsWritten { get; set; } // Debug counter
        public DateTime? LastConnectedTime { get; set; }
        public DateTime? LastPacketTime { get; set; }
    }
}
