namespace NidarosRTT.Infrastructure
{
    /// <summary>
    /// DTO for WebVTT caption cues
    /// INRT-603: WebVTT formatting for Wowza caption injection
    /// </summary>
    public class WebVTTCueDto
    {
        /// <summary>
        /// Formatted WebVTT cue string (e.g., "00:00:01.000 --> 00:00:03.000\nHello world")
        /// </summary>
        public string webvttCue { get; set; }
        
        /// <summary>
        /// Unix millisecond timestamp when caption should start
        /// </summary>
        public long startTimestamp { get; set; }
        
        /// <summary>
        /// Unix millisecond timestamp when caption should end
        /// </summary>
        public long endTimestamp { get; set; }
        
        /// <summary>
        /// The translated caption text
        /// </summary>
        public string text { get; set; }
        
        /// <summary>
        /// The original transcribed text
        /// </summary>
        public string originalText { get; set; }
        
        public WebVTTCueDto(long startTs, long endTs, string text, string originalText)
        {
            this.startTimestamp = startTs;
            this.endTimestamp = endTs;
            this.text = text;
            this.originalText = originalText;
            
            // Format as WebVTT cue
            this.webvttCue = FormatAsWebVTT(startTs, endTs, text);
        }
        
        /// <summary>
        /// Converts Unix millisecond timestamps to WebVTT format
        /// Format: HH:MM:SS.mmm --> HH:MM:SS.mmm\nCaption text
        /// </summary>
        private string FormatAsWebVTT(long startTs, long endTs, string captionText)
        {
            // Convert Unix milliseconds to TimeSpan
            var startTime = TimeSpan.FromMilliseconds(startTs % 86400000); // Modulo to reset daily
            var endTime = TimeSpan.FromMilliseconds(endTs % 86400000);
            
            // Format as WebVTT timestamp: HH:MM:SS.mmm
            var startVTT = $"{startTime.Hours:00}:{startTime.Minutes:00}:{startTime.Seconds:00}.{startTime.Milliseconds:000}";
            var endVTT = $"{endTime.Hours:00}:{endTime.Minutes:00}:{endTime.Seconds:00}.{endTime.Milliseconds:000}";
            
            // Combine into WebVTT cue format
            return $"{startVTT} --> {endVTT}\n{captionText}";
        }
    }
}
