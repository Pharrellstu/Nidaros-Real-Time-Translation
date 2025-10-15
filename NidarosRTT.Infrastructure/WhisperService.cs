using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Whisper.net;

namespace NidarosRTT.Infrastructure
{
    public interface IWhisperService
    {
        Task<string?> TranscribeAsync(string audioFilePath, CancellationToken token);
    }

    public class WhisperService : IWhisperService
    {
        private readonly string _modelPath;
        private readonly WhisperFactory _factory;
        private readonly string _language;
        private readonly int _threads;

        public WhisperService(string modelPath, string language = "auto", int threads = 4)
        {
            _modelPath = modelPath;
            if (!File.Exists(_modelPath))
            {
                throw new FileNotFoundException($"Whisper model not found at '{_modelPath}'.");
            }

            // Initialize Whisper factory from the provided model path (cross-platform)
            _factory = WhisperFactory.FromPath(_modelPath);
            _language = string.IsNullOrWhiteSpace(language) ? "auto" : language;
            _threads = Math.Max(1, threads);
        }

        public async Task<string?> TranscribeAsync(string audioFilePath, CancellationToken token)
        {
            if (!File.Exists(audioFilePath))
            {
                return null;
            }

            using var audioStream = File.OpenRead(audioFilePath);

            var builder = _factory
                .CreateBuilder()
                .WithLanguage(_language)
                .WithThreads(_threads)
                .Build();

            var sb = new StringBuilder();
            await foreach (var segment in builder.ProcessAsync(audioStream, token))
            {
                if (!string.IsNullOrWhiteSpace(segment.Text))
                {
                    sb.Append(segment.Text);
                }
            }

            var result = sb.ToString();
            return string.IsNullOrWhiteSpace(result) ? null : result;
        }
    }
}
