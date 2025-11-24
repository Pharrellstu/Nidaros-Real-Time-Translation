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

        public WowzaAudioListener(string streamUrl, string ffmpegExecutablePath)
        {
            _streamUrl = streamUrl;
            _ffmpegPath = ffmpegExecutablePath;
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
            // INRT-602: Changed from 5 seconds to 2 seconds for lower latency
            var arguments = $"-rtsp_transport tcp -i \"{_streamUrl}\" -t 2 -vn -acodec pcm_s16le -ar 16000 -ac 1 -progress pipe:1 -y \"{outputFile}\"";

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
            Console.WriteLine($"[AUDIOCHUNK DEBUG] {outputFile}, StartTS: {wallClockStartTS}, EndTS: {wallClockEndTS}, Difference: {(wallClockEndTS - wallClockStartTS)/1000} s");
            return new AudioChunk(outputFile, wallClockStartTS, wallClockEndTS);
        }
    }
}