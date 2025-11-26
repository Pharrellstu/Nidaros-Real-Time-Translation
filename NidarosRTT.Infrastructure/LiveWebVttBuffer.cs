using System.Collections.Concurrent;
using System.Text;

namespace NidarosRTT.Infrastructure;

public class LiveWebVttBuffer
{
    private readonly TimeSpan _window;
    private readonly ConcurrentDictionary<string, LinkedList<CaptionEntry>> _buffers = new();
    private readonly ConcurrentDictionary<string, object> _locks = new();

    public LiveWebVttBuffer(TimeSpan? retentionWindow = null)
    {
        _window = retentionWindow ?? TimeSpan.FromSeconds(90);
    }

    public void Append(string streamKey, SingleCaptionDto caption)
    {
        if (string.IsNullOrWhiteSpace(streamKey) || caption == null)
        {
            return;
        }

        var text = string.IsNullOrWhiteSpace(caption.text)
            ? caption.originalText
            : caption.text;

        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var entry = new CaptionEntry(
            caption.wallClockStartTS,
            caption.wallClockEndTS,
            text.Trim());

        var buffer = _buffers.GetOrAdd(streamKey, _ => new LinkedList<CaptionEntry>());
        var gate = _locks.GetOrAdd(streamKey, _ => new object());

        lock (gate)
        {
            buffer.AddLast(entry);
            TrimExpired(buffer);
        }
    }

    public string BuildWebVtt(string streamKey)
    {
        if (string.IsNullOrWhiteSpace(streamKey))
        {
            return "WEBVTT\n\n";
        }

        var buffer = _buffers.GetOrAdd(streamKey, _ => new LinkedList<CaptionEntry>());
        var gate = _locks.GetOrAdd(streamKey, _ => new object());

        lock (gate)
        {
            TrimExpired(buffer);

            if (buffer.Count == 0)
            {
                return "WEBVTT\n\nNOTE No live captions yet.\n";
            }

            var firstStart = buffer.First!.Value.StartMs;
            if (firstStart <= 0)
            {
                firstStart = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            }

            var sb = new StringBuilder();
            sb.AppendLine("WEBVTT");
            sb.AppendLine($"X-TIMESTAMP-MAP=LOCAL:00:00:00.000,MPEGTS:{ToMpegTs(firstStart)}");
            sb.AppendLine();

            foreach (var entry in buffer)
            {
                var relativeStart = Math.Max(0, entry.StartMs - firstStart);
                var relativeEnd = Math.Max(relativeStart + 1, entry.EndMs - firstStart);

                sb.AppendLine($"{FormatTime(relativeStart)} --> {FormatTime(relativeEnd)}");
                sb.AppendLine(entry.DisplayText);
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }

    private void TrimExpired(LinkedList<CaptionEntry> buffer)
    {
        if (buffer.Count == 0)
        {
            return;
        }

        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - (long)_window.TotalMilliseconds;

        while (buffer.First is not null && buffer.First.Value.EndMs < cutoff)
        {
            buffer.RemoveFirst();
        }
    }

    private static string FormatTime(long relativeMilliseconds)
    {
        if (relativeMilliseconds < 0)
        {
            relativeMilliseconds = 0;
        }

        var span = TimeSpan.FromMilliseconds(relativeMilliseconds);
        return span.ToString(@"hh\:mm\:ss\.fff");
    }

    private static long ToMpegTs(long wallClockMs)
    {
        try
        {
            checked
            {
                return wallClockMs * 90;
            }
        }
        catch (OverflowException)
        {
            return long.MaxValue;
        }
    }

    private record CaptionEntry(long StartMs, long EndMs, string DisplayText);
}

