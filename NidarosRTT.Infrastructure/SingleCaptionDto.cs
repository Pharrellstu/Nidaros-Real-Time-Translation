namespace NidarosRTT.Infrastructure
{
    public class SingleCaptionDto
    {
        /// <summary>
        /// The text to display (translated text if available, otherwise original)
        /// </summary>
        public string text { get; set; }

        /// <summary>
        /// The original transcribed text (e.g., Dutch)
        /// </summary>
        public string originalText { get; set; }

        /// <summary>
        /// Whether translation was successful
        /// </summary>
        public bool translationAvailable { get; set; }

        /// <summary>
        /// Translation status: "success", "failed", "service_unavailable"
        /// </summary>
        public string translationStatus { get; set; }

        /// <summary>
        /// Source language code (e.g., "nl" for Dutch)
        /// </summary>
        public string sourceLanguage { get; set; }

        /// <summary>
        /// Target language code (e.g., "en" for English)
        /// </summary>
        public string? targetLanguage { get; set; }

        public long wallClockStartTS { get; set; }
        public long wallClockEndTS { get; set; }
        public double ptsStart { get; set; }

        public SingleCaptionDto(
            string text,
            string originalText,
            bool translationAvailable,
            string translationStatus,
            string sourceLanguage,
            string? targetLanguage,
            long wallClockStartTS,
            long wallClockEndTS,
            double ptsStart)
        {
            this.text = text;
            this.originalText = originalText;
            this.translationAvailable = translationAvailable;
            this.translationStatus = translationStatus;
            this.sourceLanguage = sourceLanguage;
            this.targetLanguage = targetLanguage;
            this.wallClockStartTS = wallClockStartTS;
            this.wallClockEndTS = wallClockEndTS;
            this.ptsStart = ptsStart;
        }

        // Backwards compatibility constructor
        public SingleCaptionDto(string text, long wallClockStartTS, long wallClockEndTS)
            : this(text, text, true, "success", "nl", "en", wallClockStartTS, wallClockEndTS, 0)
        {
        }
    }
}