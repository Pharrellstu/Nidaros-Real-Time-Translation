using Microsoft.AspNetCore.SignalR;
using NidarosRTT.Infrastructure;
using NidarosRTT.API.Hubs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public class Program
{
    public static async Task Main(string[] args)
    {
        // --- Configuration ---
        var streamUrl = Environment.GetEnvironmentVariable("STREAM_URL")
                        ?? "rtsp://<your-ip>:1935/live/OBSstream";
        var whisperUrl = Environment.GetEnvironmentVariable("WHISPER_URL")
                         ?? "http://whisper-service:5001/transcribe";
        var ffmpegPath = Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "/usr/bin/ffmpeg";

        var builder = WebApplication.CreateBuilder(args);

        // --- Dependency Injection Setup ---
        builder.Services.AddSingleton<AudioProcessingQueue>();
        builder.Services.AddSingleton<IWowzaAudioListener>(new WowzaAudioListener(streamUrl, ffmpegPath));
        builder.Services.AddSingleton<IWhisperService>(new WhisperService(whisperUrl));
        builder.Services.AddSignalR();

        // 1. Add CORS services and define a policy
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.SetIsOriginAllowed(origin => true) // Allow any origin for development
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials(); // This is crucial for SignalR
            });
        });

        var app = builder.Build();

        // --- Middleware Pipeline ---
        // Serve the static web interface
        app.UseDefaultFiles();
        app.UseStaticFiles();

        // 2. Apply the CORS policy. The order is important.
        app.UseCors("AllowAll");

        // Map the SignalR Hub
        app.MapHub<SubtitlesHub>("/subtitlesHub"); // Using camelCase is a common convention for URLs

        // Basic API route
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
        var hubContext = app.Services.GetRequiredService<IHubContext<SubtitlesHub>>();

        Console.WriteLine("Starting Live STT Demo...");
        Console.WriteLine($"Listening to stream: {streamUrl}");
        Console.WriteLine("Press Ctrl+C to exit.");

        // Task 1: Capture audio from Wowza stream
        var captureTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    Console.WriteLine("[CAPTURE] Attempting to capture audio chunk...");
                    var audioFile = await audioListener.CaptureAudioChunkAsync(cts.Token);
                    if (audioFile != null)
                    {
                        processingQueue.Enqueue(audioFile.FilePath);
                        Console.WriteLine($"[CAPTURE] ✓ Queued: {Path.GetFileName(audioFile.FilePath)}");
                    }
                    else
                    {
                        Console.WriteLine("[CAPTURE] ✗ No audio file captured (stream may be down). Retrying in 5s...");
                        await Task.Delay(5000, cts.Token);
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[CAPTURE ERROR] {ex.Message}");
                    Console.ResetColor();
                    await Task.Delay(5000, cts.Token);
                }
            }
        }, cts.Token);

        // Task 2: Multiple transcription workers
        var transcribeTasks = new List<Task>();
        int maxConcurrentTranscriptions = 6;
        for (int i = 0; i < maxConcurrentTranscriptions; i++)
        {
            transcribeTasks.Add(Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var audioFile = await processingQueue.DequeueAsync(cts.Token);
                        Console.WriteLine($"[PROCESS-{Task.CurrentId}] Transcribing: {Path.GetFileName(audioFile)}...");

                        var text = await whisperService.TranscribeAsync(audioFile, cts.Token);

                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            var trimmedText = text.Trim();
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[TRANSCRIPTION-{Task.CurrentId}] {DateTime.Now:T} → {trimmedText}");
                            Console.ResetColor();

                            // Send to all connected web clients
                            try
                            {
                                await hubContext.Clients.All.SendAsync("ReceiveTranscription", trimmedText);
                                Console.WriteLine($"[SIGNALR] ✓ Sent to clients: {trimmedText}");
                            }
                            catch (Exception signalrEx)
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"[SIGNALR ERROR] Failed to send: {signalrEx.Message}");
                                Console.ResetColor();
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[PROCESS-{Task.CurrentId}] No text transcribed (empty result)");
                        }

                        if (File.Exists(audioFile))
                        {
                            File.Delete(audioFile);
                            Console.WriteLine($"[CLEANUP] Deleted: {Path.GetFileName(audioFile)}");
                        }
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex)
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine($"[PROCESS ERROR-{Task.CurrentId}] {ex.Message}");
                        Console.WriteLine($"[PROCESS ERROR-{Task.CurrentId}] {ex.StackTrace}");
                        Console.ResetColor();
                    }
                }
            }, cts.Token));
        }

        var allTasks = new List<Task> { captureTask };
        allTasks.AddRange(transcribeTasks);

        _ = app.RunAsync(cts.Token);
        await Task.WhenAll(allTasks);
    }
}