using FFMpegCore;
using FFMpegCore.Pipes;


class Program
{
    static async Task Main(string[] args)
    {
        // Parse command line arguments
        var options = ParseArguments(args);

        Console.WriteLine("=== Audio Capture Tool ===");
        Console.WriteLine();

        // Get stream URLs from environment or use defaults
        string rtmpUrl = Environment.GetEnvironmentVariable("RTMP_STREAM_URL") ??
                        "rtmp://100.110.10.66:1935/Test01/_definst_/CPHTD";
        string hlsUrl = Environment.GetEnvironmentVariable("HLS_STREAM_URL") ??
                       "http://100.110.10.66:1935/Test01/_definst_/CPHTD/playlist.m3u8";

        Console.WriteLine($"Stream URL: {hlsUrl}");
        Console.WriteLine();

        // Output directory for WAV files
        string outputDir = "/app/output";
        Directory.CreateDirectory(outputDir);

        // Test connectivity first (only if not in help mode)
        if (options.Mode != "help")
        {
            Console.WriteLine("Testing stream connectivity...");
            await TestConnectivity(hlsUrl);
            Console.WriteLine();
        }

        // Run the appropriate mode
        switch (options.Mode)
        {
            case "single":
                await CaptureSingleSegment(rtmpUrl, outputDir, options.SegmentDuration, options.SegmentCount);
                break;

            case "continuous":
                await RunContinuousCapture(rtmpUrl, outputDir, options.SegmentDuration, options.MaxSegments);
                break;

            case "test":
            // Run standard test only when explicitly requested
            await RunStandardTest(hlsUrl, outputDir);
                break;

            case "help":
            default:
            // Default: show help and exit (no automatic execution)
            ShowHelp();
                break;
        }
    }

    static CaptureOptions ParseArguments(string[] args)
    {
        var options = new CaptureOptions();

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLower())
            {
                case "--mode":
                case "-m":
                    if (i + 1 < args.Length)
                    {
                        options.Mode = args[++i].ToLower();
                    }
                    break;
                case "--duration":
                case "-d":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int duration))
                    {
                        options.SegmentDuration = duration;
                    }
                    break;
                case "--count":
                case "-c":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int count))
                    {
                        options.SegmentCount = count;
                    }
                    break;
                case "--max-segments":
                case "-max":
                    if (i + 1 < args.Length && int.TryParse(args[++i], out int max))
                    {
                        options.MaxSegments = max;
                    }
                    break;
                case "--help":
                case "-h":
                    ShowHelp();
                    Environment.Exit(0);
                    break;
            }
        }

        return options;
    }

    static void ShowHelp()
    {
        Console.WriteLine("Audio Capture Tool - Command Line Options");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet AudioCapture.dll [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  -m, --mode <mode>          Mode: continuous, single, test");
        Console.WriteLine("  -d, --duration <seconds>   Segment duration in seconds (default: 5)");
        Console.WriteLine("  -c, --count <number>       Number of segments for single mode");
        Console.WriteLine("  -max, --max-segments <n>   Max segments to keep (for continuous mode)");
        Console.WriteLine("  -h, --help                 Show this help");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  dotnet AudioCapture.dll --mode continuous --duration 5 --max-segments 10");
        Console.WriteLine("  dotnet AudioCapture.dll -m single -d 10 -c 3");
        Console.WriteLine("  dotnet AudioCapture.dll (runs default test)");
    }

    static async Task RunStandardTest(string hlsUrl, string outputDir)
    {
        Console.WriteLine("Running standard test...");

        // Test 1: Capture a 10-second audio segment
        Console.WriteLine("Test 1: Capturing 10-second audio segment...");
        await CaptureAudioSegment(hlsUrl, Path.Combine(outputDir, "test_audio_10s.wav"), 10);

        // Test 2: Capture a 5-second audio segment
        Console.WriteLine("Test 2: Capturing 5-second audio segment...");
        await CaptureAudioSegment(hlsUrl, Path.Combine(outputDir, "test_audio_5s.wav"), 5);

        Console.WriteLine();
        Console.WriteLine("✓ Audio capture tests completed!");
        Console.WriteLine();
        Console.WriteLine("WAV files saved to: /app/output/");
        Console.WriteLine("Check your host machine's AudioCapture/output/ folder for:");
        Console.WriteLine("  - test_audio_10s.wav");
        Console.WriteLine("  - test_audio_5s.wav");

        // Keep container running briefly to see results
        await Task.Delay(2000);
    }

    static async Task CaptureSingleSegment(string rtmpUrl, string outputDir, int durationSeconds, int count)
    {
        Console.WriteLine($"Recording {count} segment(s) of {durationSeconds} second(s) each...");

        for (int i = 0; i < count; i++)
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"audio_{durationSeconds}s_{timestamp}_seg{i + 1}.wav";
            var outputFile = Path.Combine(outputDir, filename);

            await CaptureAudioSegment(rtmpUrl, outputFile, durationSeconds);

            if (i < count - 1) // Don't delay after last segment
            {
                Console.WriteLine($"  Waiting 2 seconds...");
                await Task.Delay(2000);
            }
        }

        Console.WriteLine($"All {count} segments completed!");
    }

    static async Task RunContinuousCapture(string rtmpUrl, string outputDir, int durationSeconds, int maxSegments)
    {
        Console.WriteLine($"Starting continuous recording ({durationSeconds}s segments with 5s spacing)");
        Console.WriteLine($"Keeping maximum {maxSegments} segments (auto-cleanup enabled)");
        Console.WriteLine();

        int segmentCount = 1;
        int consecutiveFailures = 0;
        const int maxConsecutiveFailures = 3;
        var lastCaptureTime = DateTime.MinValue;

        while (true)
        {
            var currentTime = DateTime.Now;
            var timeSinceLastCapture = (currentTime - lastCaptureTime).TotalSeconds;

            Console.WriteLine($"Time since last capture: {timeSinceLastCapture:F1}s");

            var timestamp = currentTime.ToString("yyyyMMdd_HHmmss");
            var filename = $"continuous_{durationSeconds}s_{timestamp}_seg{segmentCount}.wav";
            var outputFile = Path.Combine(outputDir, filename);

            try
            {
                Console.WriteLine($"Recording segment {segmentCount}...");
                var startTime = DateTime.Now;
                await CaptureAudioSegment(rtmpUrl, outputFile, durationSeconds);
                var endTime = DateTime.Now;

                lastCaptureTime = currentTime;
                consecutiveFailures = 0; // Reset on success

                var captureDuration = (endTime - startTime).TotalSeconds;
                Console.WriteLine($"Completed segment {segmentCount} in {captureDuration:F1}s");

                segmentCount++;

                // Cleanup old segments if we exceed max
                if (maxSegments > 0 && segmentCount > maxSegments)
                {
                    await CleanupOldSegments(outputDir, maxSegments);
                }
            }
            catch (Exception ex)
            {
                consecutiveFailures++;
                Console.WriteLine($"Failed to capture segment {segmentCount} ({consecutiveFailures}/{maxConsecutiveFailures}): {ex.Message}");

                if (consecutiveFailures >= maxConsecutiveFailures)
                {
                    Console.WriteLine($"Too many consecutive failures ({maxConsecutiveFailures}). Stopping continuous recording.");
                    break;
                }
            }

            Console.WriteLine($"Waiting 5 seconds for next segment...");
            await Task.Delay(5000);
        }
    }

    static async Task CleanupOldSegments(string outputDir, int keepCount)
    {
        try
        {
            var allFiles = Directory.GetFiles(outputDir, "continuous_*.wav");
            Console.WriteLine($"Found {allFiles.Length} continuous segments in output directory");

            var files = allFiles
                .Select(f => new FileInfo(f))
                .OrderBy(f => f.CreationTime)
                .ToList();

            Console.WriteLine($"Latest segments:");
            for (int i = Math.Max(0, files.Count - 5); i < files.Count; i++)
            {
                Console.WriteLine($"   {files[i].Name} ({files[i].CreationTime:HH:mm:ss})");
            }

            if (files.Count > keepCount)
            {
                var filesToDelete = files.Take(files.Count - keepCount);
                Console.WriteLine($"Deleting {filesToDelete.Count()} old segments...");

                foreach (var file in filesToDelete)
                {
                    try
                    {
                        file.Delete();
                        Console.WriteLine($"Deleted: {file.Name}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed to delete {file.Name}: {ex.Message}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"Keeping all {files.Count} segments (under limit of {keepCount})");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Could not cleanup old segments: {ex.Message}");
        }
    }

    static async Task TestConnectivity(string hlsUrl)
    {
        try
        {
            Console.Write($"  Testing connection to: {hlsUrl}... ");

            using var httpClient = new HttpClient();
            httpClient.Timeout = TimeSpan.FromSeconds(10);

            var response = await httpClient.GetAsync(hlsUrl);
            Console.WriteLine($"✓ HTTP Status: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"  ✓ Content length: {content.Length} characters");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Connectivity test failed: {ex.Message}");
            throw;
        }
    }

    static async Task CaptureAudioSegment(string rtmpUrl, string outputFile, int durationSeconds)
    {
        // Make a temp file for the audio chunk
        var tempFile = Path.Combine(Path.GetTempPath(), $"temp_{Guid.NewGuid()}.wav");

        try
        {
            Console.WriteLine($"  🎵 Capturing {durationSeconds}s from RTMP stream...");

            // Grab audio from the live stream using ffmpeg
            var success = await FFMpegArguments
                .FromUrlInput(new Uri(rtmpUrl))  // Connect to the live RTMP feed
                .OutputToFile(tempFile, overwrite: true, options => options
                    .WithDuration(TimeSpan.FromSeconds(durationSeconds))  // How long to record
                    .WithAudioCodec("pcm_s16le")  // Make it a WAV file
                    .WithAudioSamplingRate(16000)  // Good quality for talking
                    .WithCustomArgument("-ac 1")  // One audio channel (mono)
                    .WithCustomArgument("-vn"))   // Skip any video
                .ProcessAsynchronously();

            // Check if it worked
            if (!success)
            {
                throw new Exception($"FFmpeg process returned failure status");
            }

            // Check if file was created and has content
            if (File.Exists(tempFile))
            {
                var fileInfo = new FileInfo(tempFile);
                if (fileInfo.Length > 0)
                {
                    // Show success and file size
                    Console.WriteLine($"Created: {Path.GetFileName(outputFile)} ({fileInfo.Length} bytes)");

                    // Move to the output folder
                    File.Move(tempFile, outputFile, overwrite: true);
                }
                else
                {
                    // File was created but empty
                    File.Delete(tempFile);
                    throw new Exception($"Output file {outputFile} was created but is empty (0 bytes)");
                }
            }
            else
            {
                throw new Exception($"Output file {outputFile} was not created");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to capture audio segment: {ex.Message}");
            throw;
        }
        finally
        {
            // Clean up temp file if it still exists
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }
}

class CaptureOptions
{
    public string Mode { get; set; } = "help";
    public int SegmentDuration { get; set; } = 5;
    public int SegmentCount { get; set; } = 1;
    public int MaxSegments { get; set; } = 10;
}
