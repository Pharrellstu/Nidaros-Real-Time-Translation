namespace NidarosRTT.Infrastructure
{
    public class SingleCaptionDto
    {
        public string text { get; set; }
        public long wallClockStartTS { get; set; }
        public long wallClockEndTS { get; set; }

        public SingleCaptionDto(string text, long wallClockStartTS, long wallClockEndTS)
        {
            this.text = text;
            this.wallClockStartTS = wallClockStartTS;
            this.wallClockEndTS = wallClockEndTS;
        }
    }
}