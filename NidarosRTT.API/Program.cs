using NidarosRTT.Infrastructure;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main(string[] args)
    {
        // --- Configuration ---
        // IMPORTANT: Update these paths to match your system.
        var streamUrl = "rtsp://10.8.5.53:1935/live/OBSstream";
        var whisperCliPath = @"C:\Users\Pharrell\whisper.cpp\build\bin\whisper-cli.exe"; // Common path for whisper.cpp main executable
        var modelPath = @"C:\Users\Pharrell\whisper.cpp\models\ggml-base.en.bin";
        var ffmpegPath = @"C:\ffmpeg\bin"; // Path to the folder containing ffmpeg.exe

        // --- Dependency Injection Setup ---
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddSingleton<AudioProcessingQueue>();
        builder.Services.AddSingleton<IWowzaAudioListener>(new WowzaAudioListener(streamUrl, ffmpegPath));
        builder.Services.AddSingleton<IWhisperService>(new WhisperService(whisperCliPath, modelPath));

        var app = builder.Build();

        // --- API Endpoint (for future use) ---
        app.MapGet("/", () => "Live Subtitle Translation Service is running.");

        // --- Background Service Logic ---
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) =>
        {
            Console.WriteLine("Shutting down...");
            cts.Cancel();
            e.Cancel = true;
        };

        var audioListener = app.Services.GetRequiredService<IWowzaAudioListener>();
        var processingQueue = app.Services.GetRequiredService<AudioProcessingQueue>();
        var whisperService = app.Services.GetRequiredService<IWhisperService>();

        Console.WriteLine("Starting Live STT Demo...");
        Console.WriteLine($"Listening to stream: {streamUrl}");
        Console.WriteLine("Press Ctrl+C to exit.");

        // Task 1: Capture audio from Wowza stream and add to queue
        var captureTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    Console.WriteLine("Attempting to capture audio chunk...");
                    var audioFile = await audioListener.CaptureAudioChunkAsync(cts.Token);
                    if (audioFile != null)
                    {
                        processingQueue.Enqueue(audioFile);
                        Console.WriteLine($"[CAPTURE] Queued: {Path.GetFileName(audioFile)}");
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[CAPTURE ERROR] {ex.Message}");
                    await Task.Delay(5000, cts.Token); // Wait before retrying on error
                }
            }
        }, cts.Token);

        // Task 2: Dequeue audio file and transcribe it
        var transcribeTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    var audioFile = await processingQueue.DequeueAsync(cts.Token);
                    Console.WriteLine($"[PROCESS] Transcribing: {Path.GetFileName(audioFile)}...");

                    var text = await whisperService.TranscribeAsync(audioFile, cts.Token);

                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"[TRANSCRIPTION] {DateTime.Now:T} → {text.Trim()}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.WriteLine("[PROCESS] No text transcribed.");
                    }

                    // Clean up the processed audio file
                    if (File.Exists(audioFile))
                    {
                        File.Delete(audioFile);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected on shutdown
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[PROCESS ERROR] {ex.Message}");
                }
            }
        }, cts.Token);

        await Task.WhenAll(captureTask, transcribeTask);

        // This part will not be reached until cancellation,
        // but it's good practice for a web app context.
        await app.RunAsync(cts.Token);
    }
}
