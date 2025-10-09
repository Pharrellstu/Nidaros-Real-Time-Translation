using FFMpegCore;
using FFMpegCore.Pipes;
using System.Diagnostics;

// Configure FFMpeg
GlobalFFOptions.Configure(options => options.BinaryFolder = "/usr/bin");

// Ensure output directories exist
var outputDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "output");
var hlsOutputDir = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "hls");
Directory.CreateDirectory(outputDir);
Directory.CreateDirectory(hlsOutputDir);

// Start the audio capture process
StartAudioCapture();

// Keep the application running
Console.WriteLine("Audio capture is running. Press Ctrl+C to stop.");
await Task.Delay(Timeout.Infinite);

void StartAudioCapture()
{
    var rtmpUrl = Environment.GetEnvironmentVariable("RTMP_STREAM_URL") ??
                 "rtmp://100.110.10.66:1935/Test01/_definst_/CPHTD";

    var processStartInfo = new ProcessStartInfo
    {
        FileName = "ffmpeg",
        Arguments = $@"-i ""{rtmpUrl}"" -c copy -f hls -hls_time 6 -hls_list_size 5 -hls_flags delete_segments ""{Path.Combine(hlsOutputDir, "playlist.m3u8")}""",
        UseShellExecute = false,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        CreateNoWindow = true
    };

    var process = new Process { StartInfo = processStartInfo };
    process.ErrorDataReceived += (sender, e) => Console.Error.WriteLine(e.Data);
    process.OutputDataReceived += (sender, e) => Console.WriteLine(e.Data);
    
    process.Start();
    process.BeginErrorReadLine();
    process.BeginOutputReadLine();
    
    Console.WriteLine($"Started FFmpeg process (ID: {process.Id}) for stream: {rtmpUrl}");
    
    // Handle process exit
    process.EnableRaisingEvents = true;
    process.Exited += (sender, e) => 
    {
        Console.WriteLine($"FFmpeg process (ID: {process.Id}) exited with code {process.ExitCode}");
        Environment.Exit(process.ExitCode);
    };
    
    // Handle Ctrl+C
    Console.CancelKeyPress += (sender, e) => 
    {
        Console.WriteLine("Stopping FFmpeg process...");
        if (!process.HasExited)
        {
            process.Kill();
        }
    };
}
