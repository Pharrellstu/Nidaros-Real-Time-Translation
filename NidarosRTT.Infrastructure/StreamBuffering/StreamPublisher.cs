using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

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
        private readonly object _statsLock = new object();
        
        private StreamPublisherStats _stats = new StreamPublisherStats();
        private Process? _ffmpegProcess;
        private Stream? _ffmpegStdin;
        private bool _headerWritten = false;
        private int _reconnectAttempts = 0;
        private const int MAX_RECONNECT_DELAY_MS = 30000;

        public StreamPublisher(string outputUrl, string ffmpegPath, IStreamBuffer streamBuffer)
        {
            _outputUrl = outputUrl;
            _ffmpegPath = ffmpegPath;
            _streamBuffer = streamBuffer;

            if (!File.Exists(_ffmpegPath))
            {
                throw new FileNotFoundException($"FFmpeg executable not found at '{_ffmpegPath}'");
            }
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
            // -re: Read input at native frame rate (important for live streaming)
            // -f flv: Input format is FLV
            // -i pipe:0: Read from stdin
            // -c copy: Copy streams without re-encoding
            // -f flv: Output format is FLV
            // -rtmp_buffer 2000: Set RTMP buffer size (ms)
            var arguments = $"-re -f flv -i pipe:0 -c copy -f flv -rtmp_buffer 2000 \"{_outputUrl}\"";

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

            // Construct FLV tag
            var tagType = packet.Type switch
            {
                MediaPacketType.Audio => (byte)0x08,
                MediaPacketType.Video => (byte)0x09,
                MediaPacketType.Data => (byte)0x12,
                _ => (byte)0x12
            };

            var dataSize = packet.Data.Length;
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

            // Write tag header
            await _ffmpegStdin.WriteAsync(tagHeader, 0, 11, cancellationToken);
            
            // Write tag data
            await _ffmpegStdin.WriteAsync(packet.Data, 0, packet.Data.Length, cancellationToken);
            
            // Write previous tag size (4 bytes, big-endian)
            var prevTagSize = 11 + dataSize;
            var prevTagSizeBytes = new byte[4];
            prevTagSizeBytes[0] = (byte)((prevTagSize >> 24) & 0xFF);
            prevTagSizeBytes[1] = (byte)((prevTagSize >> 16) & 0xFF);
            prevTagSizeBytes[2] = (byte)((prevTagSize >> 8) & 0xFF);
            prevTagSizeBytes[3] = (byte)(prevTagSize & 0xFF);
            
            await _ffmpegStdin.WriteAsync(prevTagSizeBytes, 0, 4, cancellationToken);
            await _ffmpegStdin.FlushAsync(cancellationToken);
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
        public DateTime? LastConnectedTime { get; set; }
        public DateTime? LastPacketTime { get; set; }
    }
}
