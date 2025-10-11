using System.Diagnostics;

namespace NidarosRTT.Infrastructure
{
    public class WhisperService
    {
        private readonly string _whisperExePath;
        private readonly string _modelPath;

        public WhisperService(string whisperExePath, string modelPath)
        {
            _whisperExePath = whisperExePath;
            _modelPath = modelPath;
        }

        public async Task<string?> TranscribeAsync(string audioFilePath)
        {
            var psi = new ProcessStartInfo
            {
                FileName = _whisperExePath,
                Arguments = $"-m \"{_modelPath}\" -f \"{audioFilePath}\" -otxt",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = psi };
            process.Start();

            string output = await process.StandardOutput.ReadToEndAsync();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (!string.IsNullOrEmpty(error))
                Console.WriteLine($"[Whisper error] {error}");

            return output.Trim();
        }
    }
}
