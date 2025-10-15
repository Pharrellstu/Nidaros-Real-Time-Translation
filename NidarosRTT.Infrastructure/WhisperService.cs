using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace NidarosRTT.Infrastructure
{
    public interface IWhisperService
    {
        Task<string?> TranscribeAsync(string audioFilePath, CancellationToken token);
    }

    public class WhisperService : IWhisperService
    {
        private readonly string _whisperCliPath;
        private readonly string _modelPath;

        public WhisperService(string whisperCliPath, string modelPath)
        {
            _whisperCliPath = whisperCliPath;
            _modelPath = modelPath;

            if (!File.Exists(_whisperCliPath))
            {
                throw new FileNotFoundException($"Whisper executable not found at '{_whisperCliPath}'.");
            }
            if (!File.Exists(_modelPath))
            {
                throw new FileNotFoundException($"Whisper model not found at '{_modelPath}'.");
            }
        }

        public async Task<string?> TranscribeAsync(string audioFilePath, CancellationToken token)
        {
            // Arguments for whisper.cpp
            // -m: path to the model
            // -f: path to the audio file
            // -otxt: output format as plain text
            // -l auto: auto-detect language
            // --no-timestamps: we don't need timestamps for this simple demo
            var arguments = $"-m \"{_modelPath}\" -f \"{audioFilePath}\" -otxt -l auto --no-timestamps -t 6";

            var processStartInfo = new ProcessStartInfo
            {
                FileName = _whisperCliPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = processStartInfo };

            process.Start();

            // Asynchronously read output and error
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await process.WaitForExitAsync(token);

            if (process.ExitCode != 0)
            {
                var error = await errorTask;
                Console.WriteLine($"[WHISPER ERROR] {error}");
                return null;
            }

            // The output of whisper.cpp with -otxt creates a file named audioFilePath.txt
            // We'll read that file to get the transcription.
            var outputTxtFile = $"{audioFilePath}.txt";
            if (File.Exists(outputTxtFile))
            {
                var transcribedText = await File.ReadAllTextAsync(outputTxtFile, token);
                File.Delete(outputTxtFile); // Clean up the text file
                return transcribedText;
            }

            // Fallback to stdout if the file isn't created for some reason
            return await outputTask;
        }
    }
}
