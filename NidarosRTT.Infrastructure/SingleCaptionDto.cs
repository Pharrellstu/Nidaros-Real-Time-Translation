namespace NidarosRTT.Infrastructure
{
    public class SingleCaptionDto
    {
        public string originalText { get; set; }
        public string translatedText { get; set; }
        public long wallClockStartTS { get; set; }
        public long wallClockEndTS { get; set; }

        public SingleCaptionDto(string originalText, string translatedText, long wallClockStartTS, long wallClockEndTS)
        {
            this.originalText = originalText;
            this.translatedText = translatedText;
            this.wallClockStartTS = wallClockStartTS;
            this.wallClockEndTS = wallClockEndTS;
        }
    }
}