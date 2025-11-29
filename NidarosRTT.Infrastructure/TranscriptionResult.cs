namespace NidarosRTT.Infrastructure
{
    /// <summary>
    /// Result from transcription service including translation status and timing data
    /// </summary>
    public class TranscriptionResult
    {
        public string? OriginalText { get; set; }
        public string? TranslatedText { get; set; }
        public string TranslationStatus { get; set; } = "unknown";
        public string SourceLanguage { get; set; } = "nl";
        public string? TargetLanguage { get; set; } = "en";
        
        /// <summary>
        /// Start time in seconds (from Whisper)
        /// </summary>
        public float? StartSeconds { get; set; }
        
        /// <summary>
        /// End time in seconds (from Whisper)
        /// </summary>
        public float? EndSeconds { get; set; }
        
        /// <summary>
        /// Convert Whisper timestamps to TimeSpan for VTT generation
        /// </summary>
        public TimeSpan GetStartTime() => StartSeconds.HasValue 
            ? TimeSpan.FromSeconds(StartSeconds.Value) 
            : TimeSpan.Zero;
        
        /// <summary>
        /// Convert Whisper timestamps to TimeSpan for VTT generation
        /// </summary>
        public TimeSpan GetEndTime() => EndSeconds.HasValue 
            ? TimeSpan.FromSeconds(EndSeconds.Value) 
            : TimeSpan.FromSeconds(5.0); // Default 5s chunk duration
    }
}