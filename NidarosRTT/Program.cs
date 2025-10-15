using System.Buffers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NidarosRTT;

internal static class Program
{
    // Simple contract:
    // - Default: describe -> prints info JSON
    // - transcribe <pcmFile> -> streams 16kHz mono s16le PCM audio and prints transcript
    // - live <hls-url> [segmentSeconds] -> pulls live audio via ffmpeg, segments, transcribes, prints
    private const string DefaultHost = "127.0.0.1";
    private const int DefaultPort = 10300;

    public static async Task Main(string[] args)
    {
        var host = Environment.GetEnvironmentVariable("WYOMING_HOST") ?? DefaultHost;
        var portStr = Environment.GetEnvironmentVariable("WYOMING_PORT");
        var port = DefaultPort;
        if (!string.IsNullOrEmpty(portStr) && int.TryParse(portStr, out var p))
        {
            port = p;
        }

        // Commands:
        // - live <hls-url> [segmentSeconds] [host] [port]
        // - transcribe <path-to-16k-mono-s16le.pcm> [host] [port]
        // - [host] [port] (describe)
        if (args.Length > 0 && string.Equals(args[0], "live", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: NidarosRTT live <hls-url> [segmentSeconds=8] [host] [port]");
                return;
            }

            var url = args[1];
            int segmentSeconds = 8;
            int nextIndex = 2;
            if (args.Length > nextIndex && int.TryParse(args[nextIndex], out var ss))
            {
                segmentSeconds = Math.Max(2, ss); // minimum 2s
                nextIndex++;
            }

            if (args.Length > nextIndex)
            {
                host = args[nextIndex++];
            }

            if (args.Length > nextIndex && int.TryParse(args[nextIndex], out var tp))
            {
                port = tp;
            }

            Console.WriteLine($"Connecting to Wyoming server at {host}:{port}... (live from {url})");
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            await LiveTranscribeAsync(url, host, port, segmentSeconds, cts.Token);
            return;
        }
        if (args.Length > 0 && string.Equals(args[0], "transcribe", StringComparison.OrdinalIgnoreCase))
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: NidarosRTT transcribe <path-to-16k-mono-s16le.pcm> [host] [port]");
                return;
            }

            var pcmPath = args[1];
            if (!File.Exists(pcmPath))
            {
                Console.Error.WriteLine($"File not found: {pcmPath}");
                return;
            }

            if (args.Length > 2) host = args[2];
            if (args.Length > 3 && int.TryParse(args[3], out var tp)) port = tp;

            Console.WriteLine($"Connecting to Wyoming server at {host}:{port}...");
            await TranscribeFileAsync(host, port, pcmPath);
            return;
        }
        else
        {
            if (args.Length > 0) host = args[0];
            if (args.Length > 1 && int.TryParse(args[1], out var ap2)) port = ap2;

            Console.WriteLine($"Connecting to Wyoming server at {host}:{port}...");
            await DescribeAsync(host, port);
            return;
        }
    }

    private static async Task DescribeAsync(string host, int port)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port);
            using var network = client.GetStream();

            // Send describe request per Wyoming protocol (JSONL)
            await SendHeaderLineAsync(network, new { type = "describe" });

            // Read header line (JSON) and then optional additional data bytes
            var headerLine = await ReadLineAsync(network).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(headerLine))
            {
                Console.Error.WriteLine("No response received from server.");
                return;
            }

            using var headerDoc = JsonDocument.Parse(headerLine);
            var root = headerDoc.RootElement;
            string? respType = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
            string? version = root.TryGetProperty("version", out var verEl) ? verEl.GetString() : null;
            int dataLen = root.TryGetProperty("data_length", out var dlEl) && dlEl.TryGetInt32(out var dl) ? dl : 0;

            string? additionalJson = null;
            if (dataLen > 0)
            {
                var addBytes = await ReadExactAsync(network, dataLen).ConfigureAwait(false);
                additionalJson = Encoding.UTF8.GetString(addBytes);
            }

            // Print combined JSON: { type, version, data: <additionalJson or {}> }
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions { Indented = true }))
            {
                writer.WriteStartObject();
                if (!string.IsNullOrEmpty(respType)) writer.WriteString("type", respType);
                if (!string.IsNullOrEmpty(version)) writer.WriteString("version", version);
                if (!string.IsNullOrEmpty(additionalJson))
                {
                    using var addDoc = JsonDocument.Parse(additionalJson);
                    writer.WritePropertyName("data");
                    addDoc.RootElement.WriteTo(writer);
                }
                else if (root.TryGetProperty("data", out var dataEl))
                {
                    writer.WritePropertyName("data");
                    dataEl.WriteTo(writer);
                }
                writer.WriteEndObject();
            }

            Console.WriteLine(Encoding.UTF8.GetString(buffer.ToArray()));
        }
        catch (SocketException ex)
        {
            Console.Error.WriteLine($"Socket error connecting to {host}:{port} - {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
    }

    private static async Task TranscribeFileAsync(string host, int port, string pcmPath)
    {
        const int rate = 16000; // Hz
        const int width = 2;    // bytes per sample (s16le)
        const int channels = 1; // mono

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port);
            using var network = client.GetStream();

            // 1) transcribe (optional fields omitted to allow autodetect)
            await SendHeaderLineAsync(network, new { type = "transcribe" });

            // 2) audio-start
            await SendHeaderLineAsync(network, new
            {
                type = "audio-start",
                data = new { rate, width, channels }
            });

            // 3) audio-chunk(s)
            await foreach (var chunk in ReadFileChunksAsync(pcmPath, 32 * 1024)) // 32KB chunks
            {
                await SendHeaderLineAsync(network, new
                {
                    type = "audio-chunk",
                    data = new { rate, width, channels },
                    payload_length = chunk.Length
                });

                await network.WriteAsync(chunk, 0, chunk.Length);
                await network.FlushAsync();
            }

            // 4) audio-stop
            await SendHeaderLineAsync(network, new { type = "audio-stop" });

            // 5) Read until transcript received
            string collected = string.Empty;
            while (true)
            {
                var line = await ReadLineAsync(network).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line))
                {
                    Console.Error.WriteLine("Server closed connection or sent empty line before transcript.");
                    break;
                }

                using var hdrDoc = JsonDocument.Parse(line);
                var root = hdrDoc.RootElement;
                var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                int dataLen = root.TryGetProperty("data_length", out var dlEl) && dlEl.TryGetInt32(out var dl) ? dl : 0;
                string? addJson = null;
                if (dataLen > 0)
                {
                    var addBytes = await ReadExactAsync(network, dataLen).ConfigureAwait(false);
                    addJson = Encoding.UTF8.GetString(addBytes);
                }

                if (string.Equals(type, "transcript-start", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine("Transcript (streaming) started...");
                }
                else if (string.Equals(type, "transcript-chunk", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(addJson))
                    {
                        using var addDoc = JsonDocument.Parse(addJson);
                        if (addDoc.RootElement.TryGetProperty("text", out var textEl))
                        {
                            var text = textEl.GetString() ?? string.Empty;
                            collected += text;
                            Console.Write(text);
                        }
                    }
                }
                else if (string.Equals(type, "transcript", StringComparison.OrdinalIgnoreCase))
                {
                    string finalText = string.Empty;
                    if (!string.IsNullOrEmpty(addJson))
                    {
                        using var addDoc = JsonDocument.Parse(addJson);
                        if (addDoc.RootElement.TryGetProperty("text", out var textEl))
                        {
                            finalText = textEl.GetString() ?? string.Empty;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(finalText))
                    {
                        Console.WriteLine();
                        Console.WriteLine($"Final transcript: {finalText}");
                    }
                    else if (!string.IsNullOrWhiteSpace(collected))
                    {
                        Console.WriteLine();
                        Console.WriteLine($"Final transcript: {collected}");
                    }
                    else
                    {
                        Console.WriteLine("Final transcript received (empty).");
                    }
                    break;
                }
                else if (string.Equals(type, "transcript-stop", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine();
                    Console.WriteLine("Transcript stream stopped.");
                    break;
                }
            }
        }
        catch (SocketException ex)
        {
            Console.Error.WriteLine($"Socket error connecting to {host}:{port} - {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
        }
    }

    private static async Task LiveTranscribeAsync(string hlsUrl, string host, int port, int segmentSeconds, CancellationToken cancellationToken)
    {
        const int rate = 16000; // Hz
        const int width = 2;    // bytes per sample (s16le)
        const int channels = 1; // mono
        int bytesPerSecond = rate * width * channels; // 32000
        int bytesPerSegment = segmentSeconds * bytesPerSecond;

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = $"-hide_banner -loglevel error -i {EscapeArg(hlsUrl)} -vn -ac 1 -ar 16000 -f s16le -",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            CreateNoWindow = true
        };

        using var ffmpeg = new System.Diagnostics.Process { StartInfo = psi, EnableRaisingEvents = true };
        try
        {
            if (!ffmpeg.Start())
            {
                Console.Error.WriteLine("Failed to start ffmpeg process.");
                return;
            }

            var stdout = ffmpeg.StandardOutput.BaseStream;
            var segmentBuffer = new byte[bytesPerSegment];
            int segmentIndex = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                int filled = 0;
                while (filled < bytesPerSegment && !cancellationToken.IsCancellationRequested)
                {
                    int read = await stdout.ReadAsync(segmentBuffer, filled, bytesPerSegment - filled, cancellationToken).ConfigureAwait(false);
                    if (read == 0)
                    {
                        // End of stream
                        break;
                    }
                    filled += read;
                }

                if (filled == 0)
                {
                    // No more audio
                    break;
                }

                byte[] payload;
                if (filled == segmentBuffer.Length)
                {
                    payload = segmentBuffer.ToArray();
                }
                else
                {
                    payload = new byte[filled];
                    Buffer.BlockCopy(segmentBuffer, 0, payload, 0, filled);
                }

                // Transcribe this segment
                var prefix = $"[seg {segmentIndex++} @{DateTimeOffset.Now:HH:mm:ss}]";
                Console.WriteLine($"{prefix} sending {filled / (double)bytesPerSecond:F1}s of audio");
                await TranscribeBufferAsync(host, port, payload, rate, width, channels, prefix);
            }

            try { if (!ffmpeg.HasExited) ffmpeg.Kill(entireProcessTree: true); } catch { /* ignore */ }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Live transcription error: {ex.Message}");
            try { if (!ffmpeg.HasExited) ffmpeg.Kill(entireProcessTree: true); } catch { /* ignore */ }
        }
    }

    private static async Task TranscribeBufferAsync(string host, int port, byte[] audio, int rate, int width, int channels, string? logPrefix = null)
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port);
            using var network = client.GetStream();

            // transcribe
            await SendHeaderLineAsync(network, new { type = "transcribe" });
            // start
            await SendHeaderLineAsync(network, new { type = "audio-start", data = new { rate, width, channels } });
            // chunk (single payload for the segment)
            await SendHeaderLineAsync(network, new { type = "audio-chunk", data = new { rate, width, channels }, payload_length = audio.Length });
            await network.WriteAsync(audio, 0, audio.Length);
            await network.FlushAsync();
            // stop
            await SendHeaderLineAsync(network, new { type = "audio-stop" });

            // read until transcript
            string collected = string.Empty;
            while (true)
            {
                var line = await ReadLineAsync(network).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(line)) break;

                using var hdrDoc = JsonDocument.Parse(line);
                var root = hdrDoc.RootElement;
                var type = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() : null;
                int dataLen = root.TryGetProperty("data_length", out var dlEl) && dlEl.TryGetInt32(out var dl) ? dl : 0;
                string? addJson = null;
                if (dataLen > 0)
                {
                    var addBytes = await ReadExactAsync(network, dataLen).ConfigureAwait(false);
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
                            Console.Write(text);
                        }
                    }
                }
                else if (string.Equals(type, "transcript", StringComparison.OrdinalIgnoreCase))
                {
                    string finalText = string.Empty;
                    if (!string.IsNullOrEmpty(addJson))
                    {
                        using var addDoc = JsonDocument.Parse(addJson);
                        if (addDoc.RootElement.TryGetProperty("text", out var textEl))
                            finalText = textEl.GetString() ?? string.Empty;
                    }

                    if (!string.IsNullOrWhiteSpace(finalText))
                    {
                        Console.WriteLine();
                        Console.WriteLine($"{logPrefix} {finalText}");
                    }
                    else if (!string.IsNullOrWhiteSpace(collected))
                    {
                        Console.WriteLine();
                        Console.WriteLine($"{logPrefix} {collected}");
                    }
                    break;
                }
                else if (string.Equals(type, "transcript-stop", StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine();
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Segment transcription error: {ex.Message}");
        }
    }

    private static async Task<string?> ReadLineAsync(Stream stream, int maxBytes = 1_048_576)
    {
        // Reads bytes until '\n' and returns UTF-8 string without the trailing newline
        var rented = ArrayPool<byte>.Shared.Rent(4096);
        try
        {
            int total = 0;
            while (total < maxBytes)
            {
                int read = await stream.ReadAsync(rented, total, 1).ConfigureAwait(false);
                if (read == 0)
                {
                    break; // EOF
                }

                if (rented[total] == (byte)'\n')
                {
                    // Found newline; slice up to previous byte (trim optional CR)
                    int len = total; // exclude '\n'
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

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length)
    {
        var buffer = new byte[length];
        int offset = 0;
        while (offset < length)
        {
            int read = await stream.ReadAsync(buffer, offset, length - offset).ConfigureAwait(false);
            if (read == 0) throw new EndOfStreamException($"Expected {length} bytes but reached end of stream at {offset} bytes.");
            offset += read;
        }
        return buffer;
    }

    private static async Task SendHeaderLineAsync(Stream network, object header)
    {
        var json = JsonSerializer.Serialize(header);
        var message = json + "\n";
        var outBytes = Encoding.UTF8.GetBytes(message);
        await network.WriteAsync(outBytes, 0, outBytes.Length);
        await network.FlushAsync();
    }

    private static async IAsyncEnumerable<byte[]> ReadFileChunksAsync(string path, int chunkSize)
    {
        await using var fs = File.OpenRead(path);
        var buffer = new byte[chunkSize];
        int read;
        while ((read = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
        {
            if (read == buffer.Length)
            {
                yield return buffer.ToArray();
            }
            else
            {
                var last = new byte[read];
                Buffer.BlockCopy(buffer, 0, last, 0, read);
                yield return last;
            }
        }
    }

    private static string EscapeArg(string arg)
    {
        // Minimal escaping for ffmpeg args (avoid shell, direct exec)
        if (OperatingSystem.IsWindows())
        {
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }
        else
        {
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }
    }
}
