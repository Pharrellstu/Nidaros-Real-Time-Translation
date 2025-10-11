using System.Collections.Concurrent;

namespace NidarosRTT.Infrastructure
{
    public class AudioProcessingQueue
    {
        private readonly ConcurrentQueue<string> _queue = new();
        private readonly SemaphoreSlim _signal = new(0);

        public void Enqueue(string filePath)
        {
            _queue.Enqueue(filePath);
            _signal.Release();
        }

        public async Task<string> DequeueAsync(CancellationToken token)
        {
            await _signal.WaitAsync(token);
            _queue.TryDequeue(out var filePath);
            return filePath!;
        }
    }
}
