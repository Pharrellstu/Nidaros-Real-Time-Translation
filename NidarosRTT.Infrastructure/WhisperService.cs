using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Text.Json;
using System;
using System.IO;

namespace NidarosRTT.Infrastructure
{
    public interface IWhisperService
    {
        Task<TranscriptionResult?> TranscribeAsync(string audioFilePath, CancellationToken token);
    }

    public class WhisperService : IWhisperService
    {
        private readonly HttpClient _httpClient;
        private readonly string _whisperUrl;

        public WhisperService(string whisperUrl)
        {
            _whisperUrl = whisperUrl; // e.g., "http://whisper-service:5000/transcribe"
            _httpClient = new HttpClient();
        }

        public async Task<TranscriptionResult?> TranscribeAsync(string audioFilePath, CancellationToken token)
        {
            if (!System.IO.File.Exists(audioFilePath))
                return null;
            
            //clean audio first 
            var noiseReducer = new NoiseReductionService();
            string cleanedPath = await noiseReducer.CleanAudioAsync(audioFilePath);
            if (!File.Exists(cleanedPath))
                cleanedPath = audioFilePath;
                
            string cleanedPath = audioFilePath;

            using var fileStream = System.IO.File.OpenRead(cleanedPath);
            using var content = new MultipartFormDataContent();
            var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = MediaTypeHeaderValue.Parse("audio/wav");
            content.Add(streamContent, "file", System.IO.Path.GetFileName(cleanedPath));

            var response = await _httpClient.PostAsync(_whisperUrl, content, token);
            response.EnsureSuccessStatusCode();

            var responseText = await response.Content.ReadAsStringAsync(token);
            Console.WriteLine($"[WHISPER RESPONSE] {responseText}"); // Debug: see what we get

            using var jsonDoc = JsonDocument.Parse(responseText);
            var root = jsonDoc.RootElement;

            string? original = null;
            string? translated = null;

            // Get translated text
            if (root.TryGetProperty("translated_text", out var translatedProp))
            {
                translated = translatedProp.GetString();
            }

            // Get original text
            if (root.TryGetProperty("original_text", out var originalProp))
            {
                original = originalProp.GetString();
            }
            // Fallback: if 'original_text' is missing, use 'text'
            else if (root.TryGetProperty("text", out var textProp))
            {
                original = textProp.GetString();
            }

            // If both are empty, return null
            if (string.IsNullOrEmpty(original) && string.IsNullOrEmpty(translated))
                return null;

            // Fallbacks: If one is missing, use the other's content.
            if (string.IsNullOrEmpty(original))
                original = translated;
            if (string.IsNullOrEmpty(translated))
                translated = original;


            return new TranscriptionResult { OriginalText = original, TranslatedText = translated };
        }
    }
}