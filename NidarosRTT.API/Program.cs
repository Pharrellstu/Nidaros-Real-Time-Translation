using Microsoft.AspNetCore.SignalR;
using NidarosRTT.Infrastructure;
using NidarosRTT.API.Hubs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// Assume IMachineTranslationService and CTranslate2Service are available
public class Program
{
    public static async Task Main(string[] args)
    {
        // --- Configuration ---
        var streamUrl = Environment.GetEnvironmentVariable("STREAM_URL")
                        ?? "rtsp://192.168.1.96:1935/live/OBSstream";
        // FIX: Use localhost or internal Docker network address for the transcription API
        var whisperUrl = Environment.GetEnvironmentVariable("WHISPER_URL")
                         ?? "http://host.docker.internal:5001/transcribe";
        var ffmpegPath = Environment.GetEnvironmentVariable("FFMPEG_PATH")
                         ?? "C:/Users/xxxam/Downloads/ffmpeg-8.0-essentials_build/ffmpeg-8.0-essentials_build/bin/ffmpeg.exe"; // Corrected Windows path format

        // --- Translation Configuration ---
        var translationModelPath = Environment.GetEnvironmentVariable("CT2_MODEL_PATH")
                           ?? Path.Combine(AppContext.BaseDirectory, "m2m100_fa_ct2");
        var translationWorkers = 4; // Number of dedicated C++ worker threads

        var builder = WebApplication.CreateBuilder(args);

        // --- Dependency Injection Setup ---
        builder.Services.AddSingleton<AudioProcessingQueue>();
        builder.Services.AddSingleton<IWowzaAudioListener>(new WowzaAudioListener(streamUrl, ffmpegPath));
        builder.Services.AddSingleton<IWhisperService>(new WhisperService(whisperUrl));

        // Inject the CTranslate2 C# wrapper service
        builder.Services.AddSingleton<IMachineTranslationService>(
            new CTranslate2Service(translationModelPath, translationWorkers));

        builder.Services.AddSignalR();

        // 1. Add CORS services and define a policy
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.SetIsOriginAllowed(origin => true) // Not recommended for production
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });

        var app = builder.Build();

        // --- Middleware Pipeline ---
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.UseCors("AllowAll");
        app.MapHub<SubtitlesHub>("/subtitlesHub");
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

        // Retrieve the new translation service
        var translationService = app.Services.GetRequiredService<IMachineTranslationService>();

        Console.WriteLine("Starting Live STT Demo...");
        Console.WriteLine($"Listening to stream: {streamUrl}");
        Console.WriteLine("Press Ctrl+C to exit.");

        // Task 1: Capture audio from Wowza stream (Remains unchanged)
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
                        processingQueue.Enqueue(audioFile);
                        Console.WriteLine($"[CAPTURE] ✓ Queued: {Path.GetFileName(audioFile)}");
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

        // Task 2: Multiple transcription and translation workers (CORRECTED)
        var transcribeTasks = new List<Task>();
        int maxConcurrentTranscriptions = 6;
        for (int i = 0; i < maxConcurrentTranscriptions; i++)
        {
            transcribeTasks.Add(Task.Run(async () =>
            {
                while (!cts.Token.IsCancellationRequested)
                {
                    string audioFile = null; // Declare here to access in catch/finally
                    try
                    {
                        // --- STEP 1: DEQUEUE A FILE ---
                        audioFile = await processingQueue.DequeueAsync(cts.Token);
                        Console.WriteLine($"[PROCESS-{Task.CurrentId}] Transcribing: {Path.GetFileName(audioFile)}...");

                        // --- STEP 2: TRANSCRIBE (ENGLISH) ---
                        var englishText = await whisperService.TranscribeAsync(audioFile, cts.Token);

                        var trimmedEnglish = englishText?.Trim();

                        if (!string.IsNullOrWhiteSpace(trimmedEnglish))
                        {
                            // --- STEP 3: TRANSLATE (ENGLISH -> PERSIAN) ---
                            var persianText = await translationService.TranslateAsync(trimmedEnglish, "fa", cts.Token);

                            if (!string.IsNullOrWhiteSpace(persianText))
                            {
                                // --- STEP 4: BROADCAST PERSIAN ---
                                var finalSubtitle = persianText.Trim();

                                Console.ForegroundColor = ConsoleColor.Magenta;
                                Console.WriteLine($"[TRANSLATION-{Task.CurrentId}] {DateTime.Now:T} → {finalSubtitle}");
                                Console.ResetColor();

                                try
                                {
                                    await hubContext.Clients.All.SendAsync("ReceiveTranslation", finalSubtitle);
                                    Console.WriteLine($"[SIGNALR] ✓ Sent translated Persian subtitles to clients.");
                                }
                                catch (Exception signalrEx)
                                {
                                    Console.ForegroundColor = ConsoleColor.Yellow;
                                    Console.WriteLine($"[SIGNALR ERROR] Failed to send translation: {signalrEx.Message}");
                                    Console.ResetColor();
                                }
                            }
                            else
                            {
                                // --- STEP 4 (FALLBACK): BROADCAST ENGLISH ---
                                await hubContext.Clients.All.SendAsync("ReceiveTranscription", trimmedEnglish);
                                Console.WriteLine($"[PROCESS-{Task.CurrentId}] No translation result, sending original.");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"[PROCESS-{Task.CurrentId}] No usable text found after transcription.");
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
                    finally
                    {
                        // --- STEP 5: CLEANUP ---
                        // Use 'finally' to ensure cleanup even if translation/SignalR fails
                        if (File.Exists(audioFile))
                        {
                            try
                            {
                                File.Delete(audioFile);
                                Console.WriteLine($"[CLEANUP] Deleted: {Path.GetFileName(audioFile)}");
                            }
                            catch (Exception cleanupEx)
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"[CLEANUP ERROR] Failed to delete {Path.GetFileName(audioFile)}: {cleanupEx.Message}");
                                Console.ResetColor();
                            }
                        }
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