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
        var streamUrl = "rtsp://172.20.10.8:1935/live/OBSstream";
        var whisperCliPath = @"C:\Users\Pharrell\whisper.cpp\build\bin\whisper-cli.exe";
        var modelPath = @"C:\Users\Pharrell\whisper.cpp\models\ggml-tiny.en.bin";
        var ffmpegPath = @"C:\ffmpeg\bin";

        var builder = WebApplication.CreateBuilder(args);

        // --- Dependency Injection Setup ---
        builder.Services.AddSingleton<AudioProcessingQueue>();
        builder.Services.AddSingleton<IWowzaAudioListener>(new WowzaAudioListener(streamUrl, ffmpegPath));
        builder.Services.AddSingleton<IWhisperService>(new WhisperService(whisperCliPath, modelPath));
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

        // Capture Task
        var captureTask = Task.Run(async () => { /* ... existing capture logic ... */ }, cts.Token);

        // Transcription Tasks
        var transcribeTasks = new List<Task>();
        int maxConcurrentTranscriptions = 6;
        for (int i = 0; i < maxConcurrentTranscriptions; i++)
        {
            transcribeTasks.Add(Task.Run(async () => {
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var audioFile = await processingQueue.DequeueAsync(cts.Token);
                        var text = await whisperService.TranscribeAsync(audioFile, cts.Token);
                        if (!string.IsNullOrWhiteSpace(text))
                        {
                            var trimmedText = text.Trim();
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[TRANSCRIPTION-{Task.CurrentId}] → {trimmedText}");
                            Console.ResetColor();
                            await hubContext.Clients.All.SendAsync("ReceiveTranscription", trimmedText);
                        }
                        if (File.Exists(audioFile)) File.Delete(audioFile);
                    }
                    catch (OperationCanceledException) { }
                    catch (Exception ex) { Console.WriteLine($"[PROCESS ERROR-{Task.CurrentId}] {ex.Message}"); }
                }
            }, cts.Token));
        }

        var allTasks = new List<Task> { captureTask };
        allTasks.AddRange(transcribeTasks);
        _ = app.RunAsync(cts.Token);
        await Task.WhenAll(allTasks);
    }
}