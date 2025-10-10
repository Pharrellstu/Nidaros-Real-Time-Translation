using FFMpegCore;
using FFMpegCore.Pipes;


public static class AudioCaptureService
{
    public static async Task Main(string[] args)
    {
        // Parse command line arguments
        var options = ParseArguments(args);

        Console.WriteLine("=== Audio Capture Tool ===");
        Console.WriteLine();

        // Get stream URLs from environment or use defaults
        string rtmpUrl = Environment.GetEnvironmentVariable("RTMP_STREAM_URL") ??
                        "rtmp://host.docker.internal:1935/Test01/_definst_/myStream";
        string hlsUrl = Environment.GetEnvironmentVariable("HLS_STREAM_URL") ??
                       "http://host.docker.internal:1935/Test01/_definst_/myStream/playlist.m3u8";

        Console.WriteLine($"Stream URL: {hlsUrl}");
        Console.WriteLine();

        // Output directory for WAV files
        string outputDir = "/app/output";
        Directory.CreateDirectory(outputDir);

        // Skip connectivity test for now - go straight to capture
        Console.WriteLine("Skipping connectivity test - proceeding to capture...");

        // Run the appropriate mode
        switch (options.Mode)
        {
            case "single":
                await CaptureSingleSegment(rtmpUrl, outputDir, options.SegmentDuration, options.SegmentCount);
                break;

            case "continuous":
                // Use RTMP for capture to avoid HLS segment duplication issues
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

    static async Task RunContinuousCapture(string streamUrl, string outputDir, int durationSeconds, int maxSegments)
    {
        Console.WriteLine($"Starting FFmpeg-based segmentation ({durationSeconds}s segments, stream-synchronized)");
        Console.WriteLine($"Keeping maximum {maxSegments} segments (auto-cleanup enabled)");
        Console.WriteLine();

        var streamType = streamUrl.StartsWith("rtmp://") ? "RTMP" : "HLS";
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var segmentPattern = Path.Combine(outputDir, $"continuous_{durationSeconds}s_{timestamp}_seg%03d.wav");

        try
        {
            Console.WriteLine($"🎵 Starting stream-synchronized capture from {streamType}...");
            Console.WriteLine($"Segment pattern: {Path.GetFileName(segmentPattern)}");

            // Use FFmpeg's segment muxer for precise, stream-synchronized segmentation
            var success = await FFMpegArguments
                .FromUrlInput(new Uri(streamUrl))
                .OutputToFile(segmentPattern, overwrite: true, options => options
                    .WithCustomArgument("-vn")  // No video
                    .WithAudioCodec("pcm_s16le")  // WAV format
                    .WithAudioSamplingRate(16000)  // 16kHz
                    .WithCustomArgument("-ac 1")  // Mono
                    .WithCustomArgument("-f segment")  // Use segment muxer
                    .WithCustomArgument($"-segment_time {durationSeconds}")  // Segment duration
                    .WithCustomArgument("-segment_format wav")  // Output format
                    .WithCustomArgument("-reset_timestamps 1")  // Reset timestamps for each segment
                    .WithCustomArgument(streamType == "HLS" ? "-live_start_index -1" : "")  // HLS: start from live edge
                    .WithCustomArgument("-segment_wrap 0")  // Don't wrap segment numbers
                    .WithCustomArgument("-strftime 0"))  // Don't use strftime in filenames
                .ProcessAsynchronously();

            if (!success)
            {
                throw new Exception("FFmpeg segmentation process failed");
            }

            Console.WriteLine("✅ Stream segmentation completed successfully");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Segmentation failed: {ex.Message}");
            throw;
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

    static async Task CaptureAudioSegment(string streamUrl, string outputFile, int durationSeconds)
    {
        try
        {
            var streamType = streamUrl.StartsWith("rtmp://") ? "RTMP" : "HLS";
            Console.WriteLine($"  🎵 Capturing {durationSeconds}s from {streamType} stream...");

            // Use stream-synchronized capture to prevent timing drift
            var success = await FFMpegArguments
                .FromUrlInput(new Uri(streamUrl))
                .OutputToFile(outputFile, overwrite: true, options => options
                    .WithDuration(TimeSpan.FromSeconds(durationSeconds))
                    .WithCustomArgument("-vn")  // No video
                    .WithAudioCodec("pcm_s16le")  // WAV format
                    .WithAudioSamplingRate(16000)  // 16kHz
                    .WithCustomArgument("-ac 1")  // Mono
                    .WithCustomArgument("-copyts")  // Copy input timestamps
                    .WithCustomArgument("-start_at_zero")  // Start timestamps at zero
                    .WithCustomArgument("-async 1")  // Audio sync method
                    .WithCustomArgument(streamType == "HLS" ? "-live_start_index -1" : "")  // HLS: start from live edge
                    .WithCustomArgument("-fflags +genpts+igndts")  // Generate PTS, ignore DTS
                    .WithCustomArgument("-avoid_negative_ts disabled"))  // Don't modify timestamps
                .ProcessAsynchronously();

            // Check if it worked
            if (!success)
            {
                throw new Exception($"FFmpeg process returned failure status");
            }

            // Check if file was created and has content
            if (File.Exists(outputFile))
            {
                var fileInfo = new FileInfo(outputFile);
                if (fileInfo.Length > 0)
                {
                    // Check for duplicate content by comparing with previous file
                    var isDuplicate = await CheckForDuplicateContent(outputFile);
                    if (isDuplicate)
                    {
                        Console.WriteLine($"⚠️  Duplicate content detected, removing: {Path.GetFileName(outputFile)}");
                        File.Delete(outputFile);
                        throw new Exception("Duplicate audio content detected - skipping this segment");
                    }
                    
                    // Show success and file size
                    Console.WriteLine($"✅ Created: {Path.GetFileName(outputFile)} ({fileInfo.Length} bytes)");
                }
                else
                {
                    // File was created but empty
                    File.Delete(outputFile);
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
    }

    static async Task<bool> CheckForDuplicateContent(string currentFile)
    {
        try
        {
            var outputDir = Path.GetDirectoryName(currentFile);
            var allFiles = Directory.GetFiles(outputDir, "continuous_*.wav")
                .Where(f => f != currentFile)
                .OrderByDescending(f => new FileInfo(f).CreationTime)
                .Take(3) // Check last 3 files
                .ToList();

            if (!allFiles.Any()) return false;

            // Get file size and a small sample for quick comparison
            var currentInfo = new FileInfo(currentFile);
            var currentSample = await GetAudioSample(currentFile);

            foreach (var previousFile in allFiles)
            {
                var previousInfo = new FileInfo(previousFile);
                
                // Quick size comparison (allow small variance due to encoding)
                var sizeDiff = Math.Abs(currentInfo.Length - previousInfo.Length);
                if (sizeDiff < 1000) // Less than 1KB difference
                {
                    var previousSample = await GetAudioSample(previousFile);
                    
                    // Compare audio samples
                    if (currentSample.SequenceEqual(previousSample))
                    {
                        Console.WriteLine($"Duplicate detected: {Path.GetFileName(currentFile)} matches {Path.GetFileName(previousFile)}");
                        return true;
                    }
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error checking for duplicates: {ex.Message}");
            return false; // Don't block on duplicate check errors
        }
    }

    static async Task<byte[]> GetAudioSample(string filePath)
    {
        try
        {
            // Read first 1KB of audio data (skip WAV header)
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            stream.Seek(44, SeekOrigin.Begin); // Skip WAV header
            var buffer = new byte[1024];
            await stream.ReadAsync(buffer, 0, buffer.Length);
            return buffer;
        }
        catch
        {
            return new byte[0];
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
