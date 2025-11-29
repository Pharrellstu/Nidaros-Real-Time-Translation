namespace NidarosRTT.Infrastructure
{
    /// <summary>
    /// Represents a single caption/subtitle with timing information.
    /// Used for VTT file generation.
    /// </summary>
    public class Caption
    {
        /// <summary>
        /// Start time of the caption relative to stream start
        /// </summary>
        public TimeSpan Start { get; set; }

        /// <summary>
        /// End time of the caption relative to stream start
        /// </summary>
        public TimeSpan End { get; set; }

        /// <summary>
        /// The caption text (can be multi-line, separated by \n)
        /// </summary>
        public string Text { get; set; }

        /// <summary>
        /// Language code (e.g., "en", "nl")
        /// </summary>
        public string Language { get; set; }

        public Caption(TimeSpan start, TimeSpan end, string text, string language = "en")
        {
            Start = start;
            End = end;
            Text = text;
            Language = language;
        }
    }
}
