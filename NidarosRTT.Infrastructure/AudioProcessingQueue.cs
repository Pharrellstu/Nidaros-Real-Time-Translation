using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace NidarosRTT.Infrastructure
{
    /// <summary>
    /// A simple thread-safe queue for managing audio file paths
    /// between the capturing and processing tasks.
    /// </summary>
    public class AudioProcessingQueue
    {
        private readonly ConcurrentQueue<AudioChunk> _queue = new();
        private readonly SemaphoreSlim _signal = new(0);

        public void Enqueue(AudioChunk chunk)
        {
            _queue.Enqueue(chunk);
            _signal.Release();
        }

        public async Task<AudioChunk> DequeueAsync(CancellationToken token)
        {
            await _signal.WaitAsync(token);
            _queue.TryDequeue(out var chunk);
            return chunk!;
        }
    }
}
