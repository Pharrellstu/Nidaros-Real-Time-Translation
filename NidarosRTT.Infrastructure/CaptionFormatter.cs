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
    }

    /// <summary>
    /// Formats transcription text into properly formatted captions.
    /// Implements Java CaptionHelper.java logic:
    /// - Max 37 characters per line (SBCS) or 30 (MBCS)
    /// - Max 2-4 lines per caption (configurable, default 2)
    /// - Break at punctuation: . ? ! , ;
    /// - Speaker change (>>) starts new caption
    /// </summary>
    public class CaptionFormatter : ICaptionFormatter
    {
        private readonly int _maxLineLength;
        private readonly int _maxLinesPerCaption;
        private readonly char[] _punctuationTerminators = { '.', '?', '!', ',', ';' };

        public CaptionFormatter(int maxLineLength = 37, int maxLinesPerCaption = 2)
        {
            _maxLineLength = maxLineLength;
            _maxLinesPerCaption = maxLinesPerCaption;
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
            var captions = new List<Caption>();
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            if (words.Length == 0)
                return captions;

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

                        // If we've reached max lines, create a caption
                        if (lines.Count >= _maxLinesPerCaption)
                        {
                            var caption = CreateCaptionFromLines(lines, captions.Count, words.Length, start, end, language);
                            captions.Add(caption);
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

                    // If we've reached max lines, create a caption
                    if (lines.Count >= _maxLinesPerCaption)
                    {
                        var caption = CreateCaptionFromLines(lines, captions.Count, words.Length, start, end, language);
                        captions.Add(caption);
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
                var caption = CreateCaptionFromLines(lines, captions.Count, words.Length, start, end, language);
                captions.Add(caption);
            }

            return captions;
        }

        private Caption CreateCaptionFromLines(List<string> lines, int captionIndex, int totalWords, TimeSpan start, TimeSpan end, string language)
        {
            var text = string.Join("\n", lines);
            var duration = end - start;

            // Distribute timing evenly if multiple captions
            var captionStart = start + (duration * captionIndex / Math.Max(1, totalWords));
            var captionEnd = captionStart + (duration / Math.Max(1, totalWords / _maxLinesPerCaption));

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
