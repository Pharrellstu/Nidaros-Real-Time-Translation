using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace NidarosRTT.Infrastructure.StreamBuffering
{
    /// <summary>
    /// Interface for pulling RTMP/RTSP streams and feeding packets to buffer
    /// </summary>
    public interface IStreamPuller
    {
        /// <summary>
        /// Start pulling the stream and feeding packets to buffer
        /// </summary>
        Task StartAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Get statistics about stream pulling
        /// </summary>
        StreamPullerStats GetStats();
    }

    /// <summary>
    /// Statistics for stream puller
    /// </summary>
    public class StreamPullerStats
    {
        public long TotalPacketsReceived { get; set; }
        public long VideoPacketsReceived { get; set; }
        public long AudioPacketsReceived { get; set; }
        public long DataPacketsReceived { get; set; }
        public long TotalBytesReceived { get; set; }
        public DateTime? LastPacketTime { get; set; }
    }

    /// <summary>
    /// Pulls RTMP/RTSP stream via FFmpeg, parses FLV packets, feeds to buffer
    /// </summary>
    public class StreamPuller : IStreamPuller
    {
        private readonly string _streamUrl;
        private readonly string _ffmpegPath;
        private readonly IStreamBuffer _streamBuffer;
        private readonly StreamPullerStats _stats = new();
        private readonly object _statsLock = new();

        public StreamPuller(string streamUrl, IStreamBuffer streamBuffer, string ffmpegPath = "/usr/bin/ffmpeg")
        {
            _streamUrl = streamUrl;
            _streamBuffer = streamBuffer;
            _ffmpegPath = ffmpegPath;
        }

        public StreamPullerStats GetStats()
        {
            lock (_statsLock)
            {
                return new StreamPullerStats
                {
                    TotalPacketsReceived = _stats.TotalPacketsReceived,
                    VideoPacketsReceived = _stats.VideoPacketsReceived,
                    AudioPacketsReceived = _stats.AudioPacketsReceived,
                    DataPacketsReceived = _stats.DataPacketsReceived,
                    TotalBytesReceived = _stats.TotalBytesReceived,
                    LastPacketTime = _stats.LastPacketTime
                };
            }
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var retryAttempt = 0;
            var maxRetryDelay = TimeSpan.FromSeconds(30);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await PullStreamAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[STREAM PULLER] Error: {ex.Message}");
                    Console.ResetColor();
                }

                // Exponential backoff
                retryAttempt++;
                var delay = TimeSpan.FromSeconds(Math.Min(Math.Pow(2, retryAttempt - 1) * 2, maxRetryDelay.TotalSeconds));
                
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[STREAM PULLER] Reconnecting in {delay.TotalMilliseconds}ms (attempt {retryAttempt})...");
                Console.ResetColor();

                await Task.Delay(delay, cancellationToken);
            }
        }

        private async Task PullStreamAsync(CancellationToken cancellationToken)
        {
            // Convert RTSP URLs to RTMP for Wowza compatibility
            var effectiveUrl = _streamUrl;
            if (_streamUrl.StartsWith("rtsp://", StringComparison.OrdinalIgnoreCase))
            {
                effectiveUrl = _streamUrl.Replace("rtsp://", "rtmp://");
                Console.WriteLine($"[STREAM PULLER] Converting RTSP to RTMP: {effectiveUrl}");
            }

            // FFmpeg command to capture stream and output FLV to stdout
            // CRITICAL FIX: -loglevel error prevents progress output from corrupting FLV binary data on stdout
            var ffmpegCommand = $"-loglevel error -i \"{effectiveUrl}\" -c copy -f flv -flvflags no_duration_filesize pipe:1";
            
            Console.WriteLine($"[STREAM PULLER] Starting FFmpeg: {_ffmpegPath} {ffmpegCommand}");

            var ffmpegStartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = ffmpegCommand,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var ffmpegProcess = new Process { StartInfo = ffmpegStartInfo };
            
            // Capture stderr (FFmpeg error logs) without blocking
            ffmpegProcess.ErrorDataReceived += (sender, args) =>
            {
                if (!string.IsNullOrEmpty(args.Data))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"[STREAM PULLER FFMPEG] {args.Data}");
                    Console.ResetColor();
                }
            };

            ffmpegProcess.Start();
            ffmpegProcess.BeginErrorReadLine();

            var stdout = ffmpegProcess.StandardOutput.BaseStream;

            // Parse FLV header
            if (!FlvParser.ParseHeader(stdout))
            {
                throw new Exception("Failed to parse FLV header");
            }

            Console.WriteLine("[STREAM PULLER] Connected and parsing FLV stream...");

            // Parse and buffer FLV tags
            var lastStatsReport = DateTime.UtcNow;
            var packetCount = 0;
            
            while (!cancellationToken.IsCancellationRequested)
            {
                var tag = FlvParser.ParseTag(stdout);
                
                if (tag == null)
                {
                    // End of stream or parse error
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("[STREAM PULLER] Stream ended or parse error");
                    Console.ResetColor();
                    break;
                }

                // Convert FLV tag to MediaPacket
                var packet = new MediaPacket(tag.Type, tag.Timestamp, tag.Data);
                _streamBuffer.EnqueuePacket(packet);
                
                packetCount++;

                // Log first few packets for debugging
                if (packetCount <= 10)
                {
                    Console.WriteLine($"[STREAM PULLER DEBUG] Packet #{packetCount}: Type={tag.Type}, Timestamp={tag.Timestamp}ms, Size={tag.Data.Length} bytes");
                }

                // Update statistics
                lock (_statsLock)
                {
                    _stats.TotalPacketsReceived++;
                    switch (tag.Type)
                    {
                        case MediaPacketType.Video:
                            _stats.VideoPacketsReceived++;
                            break;
                        case MediaPacketType.Audio:
                            _stats.AudioPacketsReceived++;
                            break;
                        case MediaPacketType.Data:
                            _stats.DataPacketsReceived++;
                            break;
                    }
                    _stats.TotalBytesReceived += tag.Data.Length + 11 + 4; // data + header + prevTagSize
                    _stats.LastPacketTime = DateTime.UtcNow;
                }

                // Report statistics every 10 seconds
                if ((DateTime.UtcNow - lastStatsReport).TotalSeconds >= 10)
                {
                    ReportStats();
                    lastStatsReport = DateTime.UtcNow;
                }
            }

            // Cleanup
            if (!ffmpegProcess.HasExited)
            {
                ffmpegProcess.Kill();
                await ffmpegProcess.WaitForExitAsync(cancellationToken);
            }
        }

        private void ReportStats()
        {
            lock (_statsLock)
            {
                var bufferDurationMs = _streamBuffer.GetBufferDurationMs();
                
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"[STREAM PULLER STATS] Total: {_stats.TotalPacketsReceived} packets, " +
                    $"Video: {_stats.VideoPacketsReceived}, Audio: {_stats.AudioPacketsReceived}, Data: {_stats.DataPacketsReceived}, " +
                    $"Bytes: {_stats.TotalBytesReceived / (1024.0 * 1024):F2} MB, Buffer: {bufferDurationMs / 1000.0:F1}s");
                Console.ResetColor();
            }
        }
    }
}
