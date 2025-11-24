using Microsoft.AspNetCore.SignalR;
using NidarosRTT.Infrastructure;
using NidarosRTT.API.Hubs;
using NidarosRTT.Core.Services;
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
                        ?? "rtsp://localhost:1935/live/OBSstream";
        var whisperUrl = Environment.GetEnvironmentVariable("WHISPER_URL")
                         ?? "http://localhost:5001/transcribe";
        var ffmpegPath = Environment.GetEnvironmentVariable("FFMPEG_PATH") ?? "C:/Users/xxxam/Downloads/ffmpeg-8.0-essentials_build/ffmpeg-8.0-essentials_build/bin/ffmpeg.exe";

        var builder = WebApplication.CreateBuilder(args);

        // --- Dependency Injection Setup ---
        builder.Services.AddSingleton<AudioProcessingQueue>();
        builder.Services.AddSingleton<IWowzaAudioListener>(new WowzaAudioListener(streamUrl, ffmpegPath));
        builder.Services.AddSingleton<IWhisperService>(new WhisperService(whisperUrl));
        builder.Services.AddSignalR();

        // Add HttpClient for HLS proxy and health monitoring
        builder.Services.AddHttpClient();

        // Translation Health Monitor Configuration
        var translatorUrl = Environment.GetEnvironmentVariable("TRANSLATOR_URL") ?? "http://localhost:5000";
        builder.Services.AddSingleton<ITranslationHealthMonitor>(serviceProvider =>
        {
            var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
            var logger = serviceProvider.GetRequiredService<ILogger<TranslationHealthMonitor>>();
            return new TranslationHealthMonitor(
                httpClientFactory,
                logger,
                translatorUrl,
                healthCheckIntervalSeconds: 15,
                healthCheckTimeoutSeconds: 10,
                failureThreshold: 2,
                circuitBreakerDurationSeconds: 60
            );
        });
        builder.Services.AddHostedService(provider => (TranslationHealthMonitor)provider.GetRequiredService<ITranslationHealthMonitor>());

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

        // HLS Proxy endpoints to serve video through port 5032
        // app.MapGet("/hls/{streamName}/playlist.m3u8", async (string streamName, HttpContext context, IHttpClientFactory httpClientFactory) =>
        // {
        //     try
        //     {
        //         var httpClient = httpClientFactory.CreateClient();
        //         var wowzaUrl = $"http://wowza-trial:1935/live/{streamName}/playlist.m3u8";
        //         var response = await httpClient.GetAsync(wowzaUrl);

        //         if (response.IsSuccessStatusCode)
        //         {
        //             var content = await response.Content.ReadAsStringAsync();
        //             // Rewrite URLs to point to our proxy
        //             content = content.Replace($"chunklist", $"/hls/{streamName}/chunklist");

        //             context.Response.ContentType = "application/vnd.apple.mpegurl";
        //             context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
        //             await context.Response.WriteAsync(content);
        //         }
        //         else
        //         {
        //             context.Response.StatusCode = 404;
        //             await context.Response.WriteAsync("Stream not found");
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         Console.WriteLine($"[HLS PROXY ERROR] playlist.m3u8: {ex.Message}");
        //         context.Response.StatusCode = 500;
        //         await context.Response.WriteAsync($"Error: {ex.Message}");
        //     }
        // });

        // app.MapGet("/hls/{streamName}/{fileName}", async (string streamName, string fileName, HttpContext context, IHttpClientFactory httpClientFactory) =>
        // {
        //     try
        //     {
        //         var httpClient = httpClientFactory.CreateClient();
        //         var wowzaUrl = $"http://wowza-trial:1935/live/{streamName}/{fileName}";
        //         var response = await httpClient.GetAsync(wowzaUrl);

        //         if (response.IsSuccessStatusCode)
        //         {
        //             // Check if it's a manifest or segment
        //             if (fileName.EndsWith(".m3u8"))
        //             {
        //                 // It's a chunklist - rewrite segment URLs
        //                 var content = await response.Content.ReadAsStringAsync();
        //                 content = content.Replace($"media_", $"/hls/{streamName}/media_");

        //                 context.Response.ContentType = "application/vnd.apple.mpegurl";
        //                 context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
        //                 await context.Response.WriteAsync(content);
        //             }
        //             else
        //             {
        //                 // It's a media segment (.ts file)
        //                 var content = await response.Content.ReadAsByteArrayAsync();
        //                 context.Response.ContentType = "video/MP2T";
        //                 context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
        //                 await context.Response.Body.WriteAsync(content);
        //             }
        //         }
        //         else
        //         {
        //             context.Response.StatusCode = 404;
        //         }
        //     }
        //     catch (Exception ex)
        //     {
        //         Console.WriteLine($"[HLS PROXY ERROR] {fileName}: {ex.Message}");
        //         context.Response.StatusCode = 500;
        //     }
        // });

        app.MapGet("/test-captions", () => {
            Console.WriteLine($"Called /test-captions {DateTime.Now}");
            
            return @"WEBVTT

        00:00:00.000 --> 00:10:00.000
        TEST CAPTION: Hello World!";
        });

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
        var healthMonitor = app.Services.GetRequiredService<ITranslationHealthMonitor>();

        // Subscribe to health status changes and broadcast to clients
        healthMonitor.StatusChanged += async (sender, e) =>
        {
            var statusMessage = e.Status == TranslationServiceStatus.Available ? "available" : "unavailable";
            try
            {
                await hubContext.Clients.All.SendAsync("TranslationServiceStatusChanged", new
                {
                    status = statusMessage,
                    message = e.Message,
                    timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                });
                Console.ForegroundColor = e.Status == TranslationServiceStatus.Available ? ConsoleColor.Green : ConsoleColor.Yellow;
                Console.WriteLine($"[HEALTH STATUS] Translation service is now: {statusMessage}");
                Console.ResetColor();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[STATUS BROADCAST ERROR] {ex.Message}");
            }
        };

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
                        processingQueue.Enqueue(audioFile);
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
                        var chunk = await processingQueue.DequeueAsync(cts.Token);
                        Console.WriteLine($"[PROCESS-{Task.CurrentId}] Transcribing: {Path.GetFileName(chunk.FilePath)}...");

                        // Get the TranscriptionResult object
                        var transcriptionResult = await whisperService.TranscribeAsync(chunk.FilePath, cts.Token);

                        // Check the result object
                        if (transcriptionResult != null && (!string.IsNullOrWhiteSpace(transcriptionResult.OriginalText) || !string.IsNullOrWhiteSpace(transcriptionResult.TranslatedText)))
                        {
                            // Ensure we have fallbacks
                            var originalText = (transcriptionResult.OriginalText ?? transcriptionResult.TranslatedText ?? "").Trim();
                            var translatedText = (transcriptionResult.TranslatedText ?? transcriptionResult.OriginalText ?? "").Trim();

                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[TRANSCRIPTION-{Task.CurrentId}] {DateTime.Now:T} → {translatedText}");
                            Console.ResetColor();

                            // Create DTO with translation status
                            var dto = new SingleCaptionDto(
                                text: translatedText,
                                originalText: originalText,
                                translationAvailable: !string.IsNullOrEmpty(transcriptionResult.TranslatedText),
                                translationStatus: transcriptionResult.TranslationStatus ?? "success",
                                sourceLanguage: transcriptionResult.SourceLanguage ?? "en",
                                targetLanguage: transcriptionResult.TargetLanguage ?? "nl",
                                wallClockStartTS: chunk.wallClockStartTS,
                                wallClockEndTS: chunk.wallClockEndTS
                            );

                            // Send to all connected web clients
                            try
                            {
                                await hubContext.Clients.All.SendAsync("ReceiveTranscription", dto);
                                Console.WriteLine($"[SIGNALR] ✓ Sent to clients: {dto.text}");
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

                        if (File.Exists(chunk.FilePath))
                        {
                            File.Delete(chunk.FilePath);
                            Console.WriteLine($"[CLEANUP] Deleted: {Path.GetFileName(chunk.FilePath)}");
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