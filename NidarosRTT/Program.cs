using System.Buffers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace NidarosRTT;

internal static class Program
{
    // Simple contract: connect to Wyoming server, send {"type":"describe"}\n and print first info JSON line
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

        if (args.Length > 0)
        {
            // Allow override via args: Program [host] [port]
            host = args[0];
        }

        if (args.Length > 1 && int.TryParse(args[1], out var ap))
        {
            port = ap;
        }

        Console.WriteLine($"Connecting to Wyoming server at {host}:{port}...");

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(host, port);
            using var network = client.GetStream();

            // Send describe request per Wyoming protocol (JSONL)
            var header = new { type = "describe" };
            var json = JsonSerializer.Serialize(header);
            var message = json + "\n";
            var outBytes = Encoding.UTF8.GetBytes(message);
            await network.WriteAsync(outBytes, 0, outBytes.Length);
            await network.FlushAsync();

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
}
