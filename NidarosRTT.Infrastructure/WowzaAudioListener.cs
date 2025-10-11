using System.Diagnostics;
using Xabe.FFmpeg;

namespace NidarosRTT.Infrastructure
{
    public class WowzaAudioListener
    {
        private readonly string _streamUrl;
        private readonly string _tempFolder;

        public WowzaAudioListener(string streamUrl)
        {
            _streamUrl = streamUrl;
            _tempFolder = Path.Combine(Directory.GetCurrentDirectory(), "temp");
            Directory.CreateDirectory(_tempFolder);
        }

        public async Task<string> CaptureAudioChunkAsync()
        {
            var outputFile = Path.Combine(_tempFolder, $"chunk_{DateTime.UtcNow.Ticks}.wav");

            // Ensure FFmpeg executable path is correct
            FFmpeg.SetExecutablesPath("C:\\ffmpeg\\bin");

            // Create custom FFmpeg command to extract *only audio* and encode to PCM WAV
            var arguments = $"-i \"{_streamUrl}\" -vn -acodec pcm_s16le -ar 44100 -ac 2 -t 5 \"{outputFile}\"";

            var conversion = FFmpeg.Conversions.New();
            conversion.AddParameter(arguments, ParameterPosition.PostInput);
            conversion.SetOverwriteOutput(true);

            await conversion.Start();

            return outputFile;
        }
    }
}
