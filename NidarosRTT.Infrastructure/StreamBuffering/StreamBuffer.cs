using System.Collections.Concurrent;
using System.Diagnostics;

namespace NidarosRTT.Infrastructure.StreamBuffering
{
    /// <summary>
    /// Interface for buffering stream packets with 30-second delay.
    /// Implements the Java reference architecture's timing logic.
    /// </summary>
    public interface IStreamBuffer
    {
        /// <summary>
        /// Add a packet to the buffer
        /// </summary>
        void EnqueuePacket(MediaPacket packet);

        /// <summary>
        /// Get packets that are ready for emission (past the buffer delay)
        /// </summary>
        IEnumerable<MediaPacket> DequeueReadyPackets();

        /// <summary>
        /// Get the current buffer duration in milliseconds
        /// </summary>
        long GetBufferDurationMs();

        /// <summary>
        /// Check if buffer has reached minimum delay
        /// </summary>
        bool IsBufferReady();

        /// <summary>
        /// Reset the buffer (for stream restarts)
        /// </summary>
        void Reset();
    }

    /// <summary>
    /// Buffers stream packets for 30 seconds before emission.
    /// Based on Java reference implementation's timing synchronization:
    /// startTime - startOffset + packetTimecode > currentTime - bufferDelay
    /// </summary>
    public class StreamBuffer : IStreamBuffer
    {
        private readonly int _bufferDelayMs;
        private readonly object _lock = new object();
        private readonly PriorityQueue<MediaPacket, long> _packetQueue;
        
        private DateTime? _streamStartTime;
        private long _streamStartOffset = -1;
        private bool _isStarted = false;

        public StreamBuffer(int bufferDelayMs = 30000)
        {
            _bufferDelayMs = bufferDelayMs;
            _packetQueue = new PriorityQueue<MediaPacket, long>();
        }

        public void EnqueuePacket(MediaPacket packet)
        {
            lock (_lock)
            {
                // Initialize timing on first packet
                if (!_isStarted)
                {
                    _streamStartTime = DateTime.UtcNow;
                    _streamStartOffset = packet.Timecode;
                    _isStarted = true;
                    
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[STREAM BUFFER] Started buffering at {_streamStartTime:T}");
                    Console.WriteLine($"[STREAM BUFFER] First packet timecode: {_streamStartOffset}ms");
                    Console.ResetColor();
                }

                // Add packet to priority queue (sorted by timecode)
                _packetQueue.Enqueue(packet, packet.Timecode);
            }
        }

        public IEnumerable<MediaPacket> DequeueReadyPackets()
        {
            var readyPackets = new List<MediaPacket>();

            lock (_lock)
            {
                if (!_isStarted || _streamStartTime == null)
                    return readyPackets;

                var currentTime = DateTime.UtcNow;
                var elapsedMs = (long)(currentTime - _streamStartTime.Value).TotalMilliseconds;

                // Java timing formula:
                // Packet is ready when: startTime + (packetTimecode - startOffset) <= currentTime - bufferDelay
                // Rearranged: packetTimecode <= startOffset + (elapsedMs - bufferDelay)
                var readyThreshold = _streamStartOffset + (elapsedMs - _bufferDelayMs);

                // Dequeue all packets that are ready
                while (_packetQueue.Count > 0 && _packetQueue.Peek().Timecode <= readyThreshold)
                {
                    var packet = _packetQueue.Dequeue();
                    readyPackets.Add(packet);
                }
            }

            return readyPackets;
        }

        public long GetBufferDurationMs()
        {
            lock (_lock)
            {
                if (_packetQueue.Count == 0)
                    return 0;

                // Calculate duration between oldest and newest packet
                var packets = _packetQueue.UnorderedItems.OrderBy(p => p.Priority).ToList();
                if (packets.Count < 2)
                    return 0;

                return packets.Last().Priority - packets.First().Priority;
            }
        }

        public bool IsBufferReady()
        {
            lock (_lock)
            {
                if (!_isStarted || _streamStartTime == null)
                    return false;

                var elapsedMs = (DateTime.UtcNow - _streamStartTime.Value).TotalMilliseconds;
                return elapsedMs >= _bufferDelayMs;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _packetQueue.Clear();
                _streamStartTime = null;
                _streamStartOffset = -1;
                _isStarted = false;
                
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[STREAM BUFFER] Reset buffer");
                Console.ResetColor();
            }
        }
    }
}
