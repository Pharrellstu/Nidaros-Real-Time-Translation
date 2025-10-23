using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;

namespace NidarosRTT.Infrastructure
{
    public interface IWhisperService
    {
        Task<string?> TranscribeAsync(string audioFilePath, CancellationToken token);
    }

    public class WhisperService : IWhisperService
    {
        private readonly HttpClient _httpClient;
        private readonly string _whisperUrl;

        public WhisperService(string whisperUrl)
        {
            _whisperUrl = whisperUrl; // e.g., "http://whisper-service:5001/transcribe"
            _httpClient = new HttpClient();
        }

        public async Task<string?> TranscribeAsync(string audioFilePath, CancellationToken token)
        {
            if (!System.IO.File.Exists(audioFilePath))
                return null;

            using var fileStream = System.IO.File.OpenRead(audioFilePath);
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/wav");
            content.Add(streamContent, "file", System.IO.Path.GetFileName(audioFilePath));

            var response = await _httpClient.PostAsync(_whisperUrl, content, token);
            response.EnsureSuccessStatusCode();

            var responseText = await response.Content.ReadAsStringAsync(token);
            // Whisper microservice returns JSON: { "text": "..." }
            using var jsonDoc = JsonDocument.Parse(responseText);
            return jsonDoc.RootElement.GetProperty("text").GetString();
        }
    }
}
