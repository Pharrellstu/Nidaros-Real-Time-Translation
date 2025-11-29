using System.Diagnostics;

namespace NidarosRTT.Infrastructure
{
    /// <summary>
    /// Interface for tracking stream timing and calculating caption offsets
    /// </summary>
    public interface IStreamTimingTracker
    {
        /// <summary>
        /// Initialize tracking when the first audio chunk is captured
        /// </summary>
        void Start();

        /// <summary>
        /// Calculate the absolute stream position for a given audio chunk
        /// </summary>
        TimeSpan GetStreamTimeOffset(AudioChunk chunk);

        /// <summary>
        /// Get the elapsed time since stream started
        /// </summary>
        TimeSpan GetElapsedTime();

        /// <summary>
        /// Reset the tracker (for stream restarts)
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// Tracks cumulative stream time to calculate proper caption timestamps.
    /// Based on Java reference implementation's timing logic.
    /// </summary>
    public class StreamTimingTracker : IStreamTimingTracker
    {
        private readonly Stopwatch _stopwatch;
        private DateTime? _streamStartTime;
        private long _firstChunkWallClockStart;
        private bool _isStarted;
        private readonly object _lock = new object();

        public StreamTimingTracker()
        {
            _stopwatch = new Stopwatch();
            _isStarted = false;
        }

        public void Start()
        {
            lock (_lock)
            {
                if (_isStarted)
                    return;

                _streamStartTime = DateTime.UtcNow;
                _stopwatch.Start();
                _isStarted = true;
            }
        }

        public TimeSpan GetStreamTimeOffset(AudioChunk chunk)
        {
            lock (_lock)
            {
                if (!_isStarted)
                {
                    // First chunk - initialize timing
                    _firstChunkWallClockStart = chunk.wallClockStartTS;
                    Start();
                    return TimeSpan.Zero;
                }

                // Calculate offset from first chunk
                // chunk.wallClockStartTS is Unix milliseconds
                long millisecondsSinceFirstChunk = chunk.wallClockStartTS - _firstChunkWallClockStart;
                
                return TimeSpan.FromMilliseconds(millisecondsSinceFirstChunk);
            }
        }

        public TimeSpan GetElapsedTime()
        {
            lock (_lock)
            {
                return _stopwatch.Elapsed;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _stopwatch.Reset();
                _streamStartTime = null;
                _firstChunkWallClockStart = 0;
                _isStarted = false;
            }
        }
    }
}
