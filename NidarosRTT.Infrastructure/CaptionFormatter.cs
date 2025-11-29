using System.Text;

namespace NidarosRTT.Infrastructure
{
    /// <summary>
    /// Interface for formatting transcribed text into captions
    /// </summary>
    public interface ICaptionFormatter
    {
        /// <summary>
        /// Format text into captions with proper line breaking
        /// </summary>
        List<Caption> FormatText(string text, TimeSpan start, TimeSpan end, string language = "en");

        /// <summary>
        /// Format transcription result with stream time offset for proper VTT timing
        /// </summary>
        List<Caption> FormatWithOffset(string text, TimeSpan whisperStart, TimeSpan whisperEnd, TimeSpan streamOffset, string language = "en");
    }

    /// <summary>
    /// Formats transcription text into properly formatted captions.
    /// Implements Java CaptionHelper.java logic:
    /// - Max 37 characters per line (SBCS) or 30 (MBCS)
    /// - Max 2-4 lines per caption (configurable, default 2)
    /// - Break at punctuation: . ? ! , ;
    /// - Speaker change (>>) starts new caption
    /// - Minimum 2.0 seconds per caption for readability
    /// </summary>
    public class CaptionFormatter : ICaptionFormatter
    {
        private readonly int _maxLineLength;
        private readonly int _maxLinesPerCaption;
        private readonly double _minCaptionDurationSeconds;
        private readonly char[] _punctuationTerminators = { '.', '?', '!', ',', ';' };

        public CaptionFormatter(int maxLineLength = 37, int maxLinesPerCaption = 2, double minCaptionDurationSeconds = 2.0)
        {
            _maxLineLength = maxLineLength;
            _maxLinesPerCaption = maxLinesPerCaption;
            _minCaptionDurationSeconds = minCaptionDurationSeconds;
        }

        public List<Caption> FormatWithOffset(string text, TimeSpan whisperStart, TimeSpan whisperEnd, TimeSpan streamOffset, string language = "en")
        {
            // Add stream offset to Whisper's relative timing
            var absoluteStart = streamOffset + whisperStart;
            var absoluteEnd = streamOffset + whisperEnd;

            return FormatText(text, absoluteStart, absoluteEnd, language);
        }

        public List<Caption> FormatText(string text, TimeSpan start, TimeSpan end, string language = "en")
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new List<Caption>();
            }

            var captions = new List<Caption>();
            
            // Check for speaker change
            var segments = text.Split(">>", StringSplitOptions.RemoveEmptyEntries);
            
            var totalDuration = end - start;
            var durationPerSegment = totalDuration / segments.Length;

            for (int i = 0; i < segments.Length; i++)
            {
                var segmentText = segments[i].Trim();
                if (string.IsNullOrEmpty(segmentText))
                    continue;

                var segmentStart = start + (durationPerSegment * i);
                var segmentEnd = segmentStart + durationPerSegment;

                // Format this segment into captions
                var segmentCaptions = FormatSegment(segmentText, segmentStart, segmentEnd, language);
                captions.AddRange(segmentCaptions);
            }

            return captions;
        }

        private List<Caption> FormatSegment(string text, TimeSpan start, TimeSpan end, string language)
        {
            var captionTexts = new List<string>();
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            if (words.Length == 0)
                return new List<Caption>();

            var lines = new List<string>();
            var currentLine = new StringBuilder();

            foreach (var word in words)
            {
                var testLine = currentLine.Length == 0 ? word : $"{currentLine} {word}";

                if (testLine.Length <= _maxLineLength)
                {
                    if (currentLine.Length > 0)
                        currentLine.Append(' ');
                    currentLine.Append(word);

                    // Check if word ends with punctuation - good break point
                    if (currentLine.Length > 0 && EndsWithPunctuation(word))
                    {
                        lines.Add(currentLine.ToString());
                        currentLine.Clear();

                        // If we've reached max lines, create a caption text
                        if (lines.Count >= _maxLinesPerCaption)
                        {
                            captionTexts.Add(string.Join("\n", lines));
                            lines.Clear();
                        }
                    }
                }
                else
                {
                    // Current line is full, start new line
                    if (currentLine.Length > 0)
                    {
                        lines.Add(currentLine.ToString());
                        currentLine.Clear();
                    }

                    // If we've reached max lines, create a caption text
                    if (lines.Count >= _maxLinesPerCaption)
                    {
                        captionTexts.Add(string.Join("\n", lines));
                        lines.Clear();
                    }

                    // Start new line with current word
                    currentLine.Append(word);
                }
            }

            // Add remaining text
            if (currentLine.Length > 0)
            {
                lines.Add(currentLine.ToString());
            }

            if (lines.Count > 0)
            {
                captionTexts.Add(string.Join("\n", lines));
            }

            // Now create Caption objects with proper timing
            var captions = new List<Caption>();
            var totalCaptionCount = captionTexts.Count;

            for (int i = 0; i < captionTexts.Count; i++)
            {
                var caption = CreateCaptionFromLines(captionTexts[i], i, totalCaptionCount, start, end, language);
                captions.Add(caption);
            }

            return captions;
        }

        private Caption CreateCaptionFromLines(string text, int captionIndex, int totalCaptionCount, TimeSpan start, TimeSpan end, string language)
        {
            var totalDuration = end - start;

            // If only one caption, use the full duration
            if (totalCaptionCount <= 1)
            {
                // Enforce minimum duration for readability
                var duration = totalDuration;
                if (duration.TotalSeconds < _minCaptionDurationSeconds)
                {
                    duration = TimeSpan.FromSeconds(_minCaptionDurationSeconds);
                }
                
                return new Caption(start, start + duration, text, language);
            }

            // Multiple captions: distribute time evenly
            var durationPerCaption = TimeSpan.FromSeconds(totalDuration.TotalSeconds / totalCaptionCount);
            
            // Enforce minimum duration
            if (durationPerCaption.TotalSeconds < _minCaptionDurationSeconds)
            {
                durationPerCaption = TimeSpan.FromSeconds(_minCaptionDurationSeconds);
            }

            var captionStart = start + (durationPerCaption * captionIndex);
            var captionEnd = captionStart + durationPerCaption;

            return new Caption(captionStart, captionEnd, text, language);
        }

        private bool EndsWithPunctuation(string word)
        {
            if (string.IsNullOrEmpty(word))
                return false;

            return _punctuationTerminators.Contains(word[word.Length - 1]);
        }
    }
}
