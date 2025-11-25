using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NidarosRTT.Infrastructure
{
    public interface IWowzaAudioListener
    {
        Task<AudioChunk?> CaptureAudioChunkAsync(CancellationToken token);
    }

    public class WowzaAudioListener : IWowzaAudioListener
    {
        private readonly string _streamUrl;
        private readonly string _tempFolder;
        private readonly string _ffmpegPath;
        private readonly string _fprobePath;

        public WowzaAudioListener(string streamUrl, string ffmpegExecutablePath)
        {
            _streamUrl = streamUrl;
            _ffmpegPath = ffmpegExecutablePath;
            _fprobePath = ffmpegExecutablePath.Replace("ffmpeg", "ffprobe");
            _tempFolder = Path.Combine(Path.GetTempPath(), "LiveSubtitleTemp");
            Directory.CreateDirectory(_tempFolder);

            if (!File.Exists(_ffmpegPath))
            {
                throw new FileNotFoundException($"FFmpeg executable not found at '{_ffmpegPath}'. Please ensure FFmpeg is installed and the path is correct.");
            }
        }

        public async Task<AudioChunk?> CaptureAudioChunkAsync(CancellationToken token)
        {
            var outputFileName = $"chunk_{DateTime.UtcNow.Ticks}.wav";
            var outputFile = Path.Combine(_tempFolder, outputFileName);

            // Updated FFmpeg command for better RTSP handling with timing information
            // -rtsp_transport tcp: use TCP instead of UDP for more reliable RTSP streaming
            // -t 5: duration of 5 seconds
            // -vn: no video
            // -acodec pcm_s16le: standard WAV audio codec
            // -ar 16000: sample rate of 16kHz (standard for speech recognition)
            // -ac 1: mono channel
            // -progress pipe:1: timestamps
            // -y: overwrite output file if it exists
            var arguments = $"-rtsp_transport tcp -i \"{_streamUrl}\" -t 5 -vn -acodec pcm_s16le -ar 16000 -ac 1 -progress pipe:1 -y \"{outputFile}\"";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = processStartInfo };

            //mark wall-clock time when starting the process for timestamping
            var wallClockStartTS = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            double ptsStartTS = await GetCurrentStreamPtsAsync();
            process.Start();

            // Capture FFmpeg output for debugging
            var errorOutput = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync(token);

            if (process.ExitCode != 0)
            {
                Console.WriteLine($"[FFMPEG ERROR] Exit code: {process.ExitCode}");
                Console.WriteLine($"[FFMPEG ERROR] {errorOutput}");
                return null;
            }

            // Check if file exists and has content
            if (!File.Exists(outputFile))
            {
                Console.WriteLine($"[FFMPEG ERROR] Output file was not created: {outputFile}");
                return null;
            }

            var fileInfo = new FileInfo(outputFile);
            if (fileInfo.Length < 1000) // Less than 1KB is likely empty or corrupted
            {
                Console.WriteLine($"[FFMPEG WARNING] Output file is very small ({fileInfo.Length} bytes), may be empty or have no audio");
                Console.WriteLine($"[FFMPEG DEBUG] Last 500 chars of FFmpeg output:");
                Console.WriteLine(errorOutput.Length > 500 ? errorOutput.Substring(errorOutput.Length - 500) : errorOutput);
                return null;
            }
            
            //Noise reduction before returning the chunk
            var noiseReducer = new NoiseReductionService();
            outputFile = await noiseReducer.CleanAudioAsync(outputFile);
            Console.WriteLine($"[NOISE REDUCTION] Cleaned audio saved at: {outputFile}");

            //get wall-clock end timestamp
            var wallClockEndTS = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            double ptsEndTS = await GetCurrentStreamPtsAsync();
            Console.WriteLine($"[AUDIOCHUNK DEBUG] {outputFile}, StartPTSTS: {ptsStartTS}, EndPTSTS: {ptsEndTS}, Difference: {ptsEndTS - ptsStartTS} s");
            return new AudioChunk(outputFile, wallClockStartTS, wallClockEndTS, ptsStartTS, ptsEndTS);
        }

        public async Task<double> GetCurrentStreamPtsAsync()
        {
            // expecting rtsp://localhost:1935/live/OBSstream format as _streamUrl
            // TODO: make stream source an object with variable adresses, port, etc...
            string streamUrl_http = _streamUrl.Replace("rtsp://", "http://") + "/playlist.m3u8";

            var arguments = $"-v error -show_entries packet=pts_time -select_streams a:0 -of csv=p=0 -read_intervals %+1 \"{streamUrl_http}\"";
            
            var processStartInfo = new ProcessStartInfo
            {
                FileName = _fprobePath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = processStartInfo };
            
            process.Start();
            
            // Read the first line of output
            var output = await process.StandardOutput.ReadLineAsync();
            
            await process.WaitForExitAsync();
            
            // Parse the PTS value (remove any trailing commas)
            if (!string.IsNullOrWhiteSpace(output))
            {
                var cleanedOutput = output.Trim().TrimEnd(',');
                if (double.TryParse(cleanedOutput, System.Globalization.NumberStyles.Float, 
                                System.Globalization.CultureInfo.InvariantCulture, out var pts))
                {
                    return pts;
                }
            }
            
            throw new InvalidOperationException("Failed to retrieve PTS timestamp from stream");
        }

    }
}