using System.Buffers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace NidarosRTT.Infrastructure
{
    public interface IWhisperService
    {
        Task<string?> TranscribeAsync(string audioFilePath, CancellationToken token);
    }

    public class WhisperService : IWhisperService
    {
        private readonly string _host;
        private readonly int _port;

        public WhisperService()
        {
            // For backward compatibility, we keep the constructor signature but
            // ignore the URL and use host/port via env vars with sensible defaults.
            _host = Environment.GetEnvironmentVariable("WYOMING_HOST") ?? "localhost";
            var portStr = Environment.GetEnvironmentVariable("WYOMING_PORT");
            _port = 10300;
            if (!string.IsNullOrEmpty(portStr) && int.TryParse(portStr, out var p))
                _port = p;
        }

        public async Task<string?> TranscribeAsync(string audioFilePath, CancellationToken token)
        {
            if (!File.Exists(audioFilePath)) return null;

            // Try to get PCM bytes (supports raw .pcm or WAV with RIFF header)
            var (pcm, rate, width, channels) = await ReadPcmAsync(audioFilePath, token).ConfigureAwait(false);
            if (pcm is null || pcm.Length == 0) return null;

            try
            {
                using var client = new TcpClient();
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(15));
                await client.ConnectAsync(_host, _port, timeoutCts.Token).ConfigureAwait(false);
                using var network = client.GetStream();

                // Wyoming flow: transcribe -> audio-start -> audio-chunk -> audio-stop -> read transcript
                await SendHeaderLineAsync(network, new { type = "transcribe" }, token).ConfigureAwait(false);
                await SendHeaderLineAsync(network, new { type = "audio-start", data = new { rate, width, channels } }, token).ConfigureAwait(false);
                await SendHeaderLineAsync(network, new { type = "audio-chunk", data = new { rate, width, channels }, payload_length = pcm.Length }, token).ConfigureAwait(false);
                await network.WriteAsync(pcm, 0, pcm.Length, token).ConfigureAwait(false);
                await network.FlushAsync(token).ConfigureAwait(false);
                await SendHeaderLineAsync(network, new { type = "audio-stop" }, token).ConfigureAwait(false);

                // Collect transcript
                string collected = string.Empty;
                while (true)
                {
                    var line = await ReadLineAsync(network, token).ConfigureAwait(false);
                    if (string.IsNullOrWhiteSpace(line)) break;

                    using var hdrDoc = JsonDocument.Parse(line);
                    var root = hdrDoc.RootElement;
                    var type = root.TryGetProperty("type", out var tEl) ? tEl.GetString() : null;
                    int dataLen = root.TryGetProperty("data_length", out var dlEl) && dlEl.TryGetInt32(out var dl) ? dl : 0;

                    string? addJson = null;
                    if (dataLen > 0)
                    {
                        var addBytes = await ReadExactAsync(network, dataLen, token).ConfigureAwait(false);
                        addJson = Encoding.UTF8.GetString(addBytes);
                    }

                    if (string.Equals(type, "transcript-chunk", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(addJson))
                        {
                            using var addDoc = JsonDocument.Parse(addJson);
                            if (addDoc.RootElement.TryGetProperty("text", out var textEl))
                            {
                                var text = textEl.GetString() ?? string.Empty;
                                collected += text;
                            }
                        }
                    }
                    else if (string.Equals(type, "transcript", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrEmpty(addJson))
                        {
                            using var addDoc = JsonDocument.Parse(addJson);
                            if (addDoc.RootElement.TryGetProperty("text", out var textEl))
                            {
                                var final = textEl.GetString();
                                return string.IsNullOrWhiteSpace(final) ? (string.IsNullOrWhiteSpace(collected) ? null : collected) : final;
                            }
                        }
                        return string.IsNullOrWhiteSpace(collected) ? null : collected;
                    }
                    else if (string.Equals(type, "transcript-stop", StringComparison.OrdinalIgnoreCase))
                    {
                        break;
                    }
                }

                return string.IsNullOrWhiteSpace(collected) ? null : collected;
            }
            catch
            {
                return null;
            }
        }

        // --- Helpers ---

        private static async Task SendHeaderLineAsync(Stream network, object header, CancellationToken token)
        {
            var json = JsonSerializer.Serialize(header);
            var message = json + "\n";
            var outBytes = Encoding.UTF8.GetBytes(message);
            await network.WriteAsync(outBytes, 0, outBytes.Length, token).ConfigureAwait(false);
            await network.FlushAsync(token).ConfigureAwait(false);
        }

        private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken token, int maxBytes = 1_048_576)
        {
            // Reads bytes until '\n' and returns UTF-8 string without the trailing newline
            var rented = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                int total = 0;
                while (total < maxBytes)
                {
                    int read = await stream.ReadAsync(rented, total, 1, token).ConfigureAwait(false);
                    if (read == 0) break; // EOF

                    if (rented[total] == (byte)'\n')
                    {
                        int len = total;
                        if (len > 0 && rented[len - 1] == (byte)'\r') len -= 1;
                        return Encoding.UTF8.GetString(rented, 0, len);
                    }

                    total += read;
                    if (total == rented.Length)
                    {
                        var newBuf = ArrayPool<byte>.Shared.Rent(rented.Length * 2);
                        Buffer.BlockCopy(rented, 0, newBuf, 0, rented.Length);
                        ArrayPool<byte>.Shared.Return(rented);
                        rented = newBuf;
                    }
                }

                return total > 0 ? Encoding.UTF8.GetString(rented, 0, total) : null;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }

        private static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken token)
        {
            var buffer = new byte[length];
            int offset = 0;
            while (offset < length)
            {
                int read = await stream.ReadAsync(buffer, offset, length - offset, token).ConfigureAwait(false);
                if (read == 0) throw new EndOfStreamException($"Expected {length} bytes but reached end of stream at {offset} bytes.");
                offset += read;
            }
            return buffer;
        }

        private static async Task<(byte[]? pcm, int rate, int width, int channels)> ReadPcmAsync(string path, CancellationToken token)
        {
            // Defaults expected by the pipeline
            const int defaultRate = 16000;
            const int defaultWidth = 2; // s16le
            const int defaultChannels = 1; // mono

            await using var fs = File.OpenRead(path);
            if (fs.Length < 8) // too small to be wav or pcm
                return (null, defaultRate, defaultWidth, defaultChannels);

            // Peek first 12 bytes for 'RIFF....WAVE'
            var header = new byte[12];
            int read = await fs.ReadAsync(header, 0, header.Length, token).ConfigureAwait(false);
            if (read == header.Length && Encoding.ASCII.GetString(header, 0, 4) == "RIFF" && Encoding.ASCII.GetString(header, 8, 4) == "WAVE")
            {
                // WAV container: iterate chunks to find 'fmt ' and 'data'
                int rate = defaultRate, width = defaultWidth, channels = defaultChannels;
                long dataOffset = -1; int dataSize = 0;

                while (fs.Position + 8 <= fs.Length)
                {
                    var chunkHdr = new byte[8];
                    await fs.ReadAsync(chunkHdr, 0, 8, token).ConfigureAwait(false);
                    var chunkId = Encoding.ASCII.GetString(chunkHdr, 0, 4);
                    int chunkSize = BitConverter.ToInt32(chunkHdr, 4);

                    if (chunkId == "fmt ")
                    {
                        var fmt = new byte[chunkSize];
                        int r = await fs.ReadAsync(fmt, 0, fmt.Length, token).ConfigureAwait(false);
                        if (r == fmt.Length && fmt.Length >= 16)
                        {
                            ushort audioFormat = BitConverter.ToUInt16(fmt, 0);
                            channels = BitConverter.ToUInt16(fmt, 2);
                            rate = BitConverter.ToInt32(fmt, 4);
                            ushort bitsPerSample = BitConverter.ToUInt16(fmt, 14);
                            width = bitsPerSample / 8;
                            // We expect PCM (1) or EXTENSIBLE that is PCM-compatible
                            if (audioFormat != 1 && audioFormat != 65534)
                            {
                                // Unsupported format, bail out and let caller decide
                                return (null, defaultRate, defaultWidth, defaultChannels);
                            }
                        }
                        else
                        {
                            return (null, defaultRate, defaultWidth, defaultChannels);
                        }
                    }
                    else if (chunkId == "data")
                    {
                        dataOffset = fs.Position;
                        dataSize = chunkSize;
                        // Move to end of data and stop scanning
                        fs.Position += chunkSize;
                    }
                    else
                    {
                        // skip other chunks
                        fs.Position += chunkSize;
                    }

                    // Chunks are even-byte aligned; if odd size, skip pad byte
                    if ((chunkSize & 1) == 1 && fs.Position < fs.Length)
                        fs.Position += 1;
                }

                if (dataOffset >= 0 && dataSize > 0)
                {
                    var pcm = new byte[dataSize];
                    fs.Position = dataOffset;
                    int got = await fs.ReadAsync(pcm, 0, dataSize, token).ConfigureAwait(false);
                    if (got == dataSize)
                        return (pcm, rate, width, channels);
                }

                return (null, defaultRate, defaultWidth, defaultChannels);
            }
            else
            {
                // Not a WAV header -> treat as raw PCM s16le 16k mono by default
                // Return entire file content
                var pcm = new byte[fs.Length - fs.Position];
                int got = await fs.ReadAsync(pcm, 0, pcm.Length, token).ConfigureAwait(false);
                if (got == pcm.Length)
                    return (pcm, defaultRate, defaultWidth, defaultChannels);
                return (null, defaultRate, defaultWidth, defaultChannels);
            }
        }
    }
}
