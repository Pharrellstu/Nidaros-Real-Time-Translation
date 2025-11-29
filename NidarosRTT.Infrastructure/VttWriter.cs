namespace NidarosRTT.Infrastructure
{
    /// <summary>
    /// Interface for writing WebVTT subtitle files
    /// </summary>
    public interface IVttWriter
    {
        /// <summary>
        /// Initialize a new VTT file with header
        /// </summary>
        void Initialize(string streamName);

        /// <summary>
        /// Append a caption to the VTT file
        /// </summary>
        Task AppendCaptionAsync(Caption caption);

        /// <summary>
        /// Flush and close the VTT file
        /// </summary>
        void Close();
    }

    /// <summary>
    /// Writes WebVTT subtitle files to shared volume for Wowza to serve.
    /// Thread-safe implementation for concurrent caption appending.
    /// </summary>
    public class VttWriter : IVttWriter, IDisposable
    {
        private readonly string _vttOutputPath;
        private readonly object _writeLock = new object();
        private StreamWriter? _writer;
        private string? _currentFilePath;

        public VttWriter(string vttOutputPath)
        {
            _vttOutputPath = vttOutputPath;
            
            // Ensure output directory exists
            if (!Directory.Exists(_vttOutputPath))
            {
                Directory.CreateDirectory(_vttOutputPath);
            }
        }

        public void Initialize(string streamName)
        {
            lock (_writeLock)
            {
                // Close existing file if any
                _writer?.Close();

                // Create new VTT file for this stream
                _currentFilePath = Path.Combine(_vttOutputPath, $"{streamName}.vtt");
                
                // Open in append mode (allows continuous writing)
                _writer = new StreamWriter(_currentFilePath, append: false);
                
                // Write VTT header
                _writer.WriteLine("WEBVTT");
                _writer.WriteLine();
                _writer.Flush();
            }
        }

        public async Task AppendCaptionAsync(Caption caption)
        {
            if (_writer == null)
            {
                throw new InvalidOperationException("VttWriter not initialized. Call Initialize() first.");
            }

            lock (_writeLock)
            {
                // Format: HH:MM:SS.mmm --> HH:MM:SS.mmm
                string startTime = FormatVttTimestamp(caption.Start);
                string endTime = FormatVttTimestamp(caption.End);

                _writer.WriteLine($"{startTime} --> {endTime}");
                _writer.WriteLine(caption.Text);
                _writer.WriteLine(); // Empty line between captions
                _writer.Flush(); // Ensure written immediately for live streaming
            }

            await Task.CompletedTask;
        }

        public void Close()
        {
            lock (_writeLock)
            {
                _writer?.Flush();
                _writer?.Close();
                _writer = null;
            }
        }

        /// <summary>
        /// Format TimeSpan to VTT timestamp: HH:MM:SS.mmm
        /// </summary>
        private string FormatVttTimestamp(TimeSpan time)
        {
            return $"{time.Hours:D2}:{time.Minutes:D2}:{time.Seconds:D2}.{time.Milliseconds:D3}";
        }

        public void Dispose()
        {
            Close();
        }
    }
}
