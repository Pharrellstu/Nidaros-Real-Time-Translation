namespace NidarosRTT.Infrastructure
{
    /// <summary>
    /// Result from transcription service including translation status
    /// </summary>
    public class TranscriptionResult
    {
        public string? OriginalText { get; set; }
        public string? TranslatedText { get; set; }
        public string TranslationStatus { get; set; } = "unknown";
        public string SourceLanguage { get; set; } = "nl";
        public string? TargetLanguage { get; set; } = "en";
    }
}