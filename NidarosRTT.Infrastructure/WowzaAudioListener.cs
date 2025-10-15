using System.IO;
using System.Net.Http;
using System.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xabe.FFmpeg;
using Xabe.FFmpeg.Downloader;

namespace NidarosRTT.Infrastructure
{
    public interface IWowzaAudioListener
    {
        Task<string?> CaptureAudioChunkAsync(CancellationToken token);
    }

    public class WowzaAudioListener : IWowzaAudioListener
    {
        private readonly string _streamUrl;
        private readonly string _tempFolder;
    private static bool _ffmpegReady = false;
        private static readonly SemaphoreSlim _ffmpegInitLock = new(1, 1);
    private static readonly HttpClient _http = new HttpClient();
    private static string? _ffmpegPath;

        public WowzaAudioListener(string streamUrl)
        {
            _streamUrl = streamUrl;
            _tempFolder = Path.Combine(Path.GetTempPath(), "LiveSubtitleTemp");
            Directory.CreateDirectory(_tempFolder);
        }

        public async Task<string?> CaptureAudioChunkAsync(CancellationToken token)
        {
            await EnsureFFmpegAsync(token);

            var outputFileName = $"chunk_{DateTime.UtcNow.Ticks}.wav";
            var outputFile = Path.Combine(_tempFolder, outputFileName);

            // Quick probe: ensure there is at least one audio stream available
            try
            {
                var info = await FFmpeg.GetMediaInfo(_streamUrl);
                var audioCount = info.AudioStreams?.Count() ?? 0;
                if (audioCount == 0)
                {
                    Console.WriteLine("[FFMPEG PROBE] No audio streams detected in source. Skipping chunk.");
                    return null;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FFMPEG PROBE ERROR] {ex.Message}");
                // Continue to attempt capture; in some cases probe can fail while direct read works
            }

            // If HLS master playlist is provided, resolve to a media playlist
            var inputUrl = await ResolveHlsUrlAsync(_streamUrl, token) ?? _streamUrl;

            // Build robust FFmpeg parameters depending on protocol
            var isRtsp = inputUrl.StartsWith("rtsp", StringComparison.OrdinalIgnoreCase);
            var inputFlags = isRtsp
                ? "-rtsp_transport tcp -stimeout 15000000"
                : "-protocol_whitelist file,http,https,tcp,tls,crypto -allowed_extensions ALL -user_agent 'Mozilla/5.0' -rw_timeout 15000000 -reconnect 1 -reconnect_streamed 1 -reconnect_on_http_error 4xx,5xx -reconnect_delay_max 2";

            var durationSec = 10; // HLS segments are ~8-10s; ensure we span at least one
            var ffArgs = $"-loglevel info -hide_banner {inputFlags} -i \"{inputUrl}\" -t {durationSec} -map 0:a:0? -dn -acodec pcm_s16le -ar 16000 -ac 1 -f wav -y \"{outputFile}\"";

            // Use the downloaded FFmpeg binary directly for predictable behavior
            if (string.IsNullOrEmpty(_ffmpegPath) || !File.Exists(_ffmpegPath))
            {
                Console.WriteLine($"[FFMPEG] Executable not found at '{_ffmpegPath}'.");
                return null;
            }

            Console.WriteLine($"[FFMPEG] exec: {_ffmpegPath}");
            Console.WriteLine($"[FFMPEG] args: {ffArgs}");

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = ffArgs,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var proc = new System.Diagnostics.Process { StartInfo = psi };
            try
            {
                proc.Start();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync(token);
                var stderr = await stderrTask;
                var stdout = await stdoutTask;
                if (!string.IsNullOrWhiteSpace(stdout)) Console.WriteLine($"[FFMPEG OUT] {stdout}");
                if (!string.IsNullOrWhiteSpace(stderr)) Console.WriteLine($"[FFMPEG ERR] {stderr}");

                if (proc.ExitCode == 0 && File.Exists(outputFile))
                {
                    return outputFile;
                }

                Console.WriteLine($"[FFMPEG] ExitCode={proc.ExitCode}. Output exists: {File.Exists(outputFile)}");
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[FFMPEG ERROR] {ex}");
                return null;
            }
        }

        private static async Task<string?> ResolveHlsUrlAsync(string url, CancellationToken token)
        {
            if (!url.Contains(".m3u8", StringComparison.OrdinalIgnoreCase))
                return url;

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, token);
                if (!resp.IsSuccessStatusCode)
                    return url;
                var text = await resp.Content.ReadAsStringAsync(token);
                if (string.IsNullOrWhiteSpace(text)) return url;

                var lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].Trim();
                    if (line.StartsWith("#EXT-X-STREAM-INF"))
                    {
                        // Next non-empty, non-comment line should be variant URI
                        for (int j = i + 1; j < lines.Length; j++)
                        {
                            var candidate = lines[j].Trim();
                            if (string.IsNullOrEmpty(candidate) || candidate.StartsWith("#")) continue;
                            var resolved = ResolveRelative(url, candidate);
                            Console.WriteLine($"[HLS] Resolved master -> variant: {resolved}");
                            return resolved;
                        }
                    }
                }

                // If it's already a media playlist, just return original
                if (text.Contains("#EXTINF"))
                {
                    return url;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[HLS RESOLVE ERROR] {ex.Message}");
            }
            return url;
        }

        private static string ResolveRelative(string baseUrl, string relativeOrAbsolute)
        {
            if (Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var abs))
                return abs.ToString();
            var b = new Uri(baseUrl);
            return new Uri(b, relativeOrAbsolute).ToString();
        }

        private static async Task EnsureFFmpegAsync(CancellationToken token)
        {
            if (_ffmpegReady) return;
            await _ffmpegInitLock.WaitAsync(token);
            try
            {
                if (_ffmpegReady) return;
                // Prefer system ffmpeg if available (more stable on many Linux distros)
                var candidates = new List<string>();
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ffmpeg", "bin", "ffmpeg.exe"));
                }
                else
                {
                    candidates.Add("/usr/bin/ffmpeg");
                    candidates.Add("/usr/local/bin/ffmpeg");
                }

                _ffmpegPath = candidates.FirstOrDefault(File.Exists);
                if (string.IsNullOrEmpty(_ffmpegPath))
                {
                    var ffmpegDir = Path.Combine(Path.GetTempPath(), "ffmpeg_binaries");
                    Directory.CreateDirectory(ffmpegDir);
                    await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official, ffmpegDir);
                    FFmpeg.SetExecutablesPath(ffmpegDir);
                    var fileName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
                    _ffmpegPath = Path.Combine(ffmpegDir, fileName);
                }
                Console.WriteLine($"[FFMPEG] Ready at '{_ffmpegPath}'");
                _ffmpegReady = true;
            }
            finally
            {
                _ffmpegInitLock.Release();
            }
        }
    }
}
