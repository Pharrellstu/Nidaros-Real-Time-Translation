namespace NidarosRTT.Infrastructure
{
    public class AudioChunk
    {
        public string FilePath { get; set; }
        public long wallClockStartTS { get; set; }
        public long wallClockEndTS { get; set; }
        public double ptsStartTS { get; set; }

        public AudioChunk(string filePath, long wallClockStartTS, long wallClockEndTS, double ptsStartTS)
        {
            FilePath = filePath;
            this.wallClockStartTS = wallClockStartTS;
            this.wallClockEndTS = wallClockEndTS;
            this.ptsStartTS = ptsStartTS;
        }
    }
}