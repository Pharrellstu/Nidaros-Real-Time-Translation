using Microsoft.AspNetCore.SignalR;
using NidarosRTT.Infrastructure;
using NidarosRTT.API.Hubs;
using NidarosRTT.Core.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Events;

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
        
        // --- Serilog Configuration ---
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Async(a => a.File(
                path: "logs/log-.txt",
                rollingInterval: RollingInterval.Day,
                restrictedToMinimumLevel: LogEventLevel.Information))
            .WriteTo.Async(a => a.Seq(
                serverUrl: builder.Configuration["Seq:ServerUrl"],
                apiKey: builder.Configuration["Seq:ApiKey"]))
            .CreateLogger();

        builder.Host.UseSerilog();
        
        Log.Information("Serilog file logging is working!");
        
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
                policy.SetIsOriginAllowed(origin => true)
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

        // Basic API route
        app.MapGet("/", () => "Live Subtitle Translation Service is running.");

        // HLS Proxy endpoints
        app.MapGet("/hls/{streamName}/playlist.m3u8", async (string streamName, HttpContext context, IHttpClientFactory httpClientFactory) =>
        {
            try
            {
                var httpClient = httpClientFactory.CreateClient();
                var wowzaUrl = $"http://wowza-trial:1935/live/{streamName}/playlist.m3u8";
                var response = await httpClient.GetAsync(wowzaUrl);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    content = content.Replace($"chunklist", $"/hls/{streamName}/chunklist");

                    context.Response.ContentType = "application/vnd.apple.mpegurl";
                    context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
                    await context.Response.WriteAsync(content);
                }
                else
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync("Stream not found");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[HLS PROXY ERROR] playlist.m3u8: {Message}", ex.Message);
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync($"Error: {ex.Message}");
            }
        });

        app.MapGet("/hls/{streamName}/{fileName}", async (string streamName, string fileName, HttpContext context, IHttpClientFactory httpClientFactory) =>
        {
            try
            {
                var httpClient = httpClientFactory.CreateClient();
                var wowzaUrl = $"http://wowza-trial:1935/live/{streamName}/{fileName}";
                var response = await httpClient.GetAsync(wowzaUrl);

                if (response.IsSuccessStatusCode)
                {
                    if (fileName.EndsWith(".m3u8"))
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        content = content.Replace($"media_", $"/hls/{streamName}/media_");

                        context.Response.ContentType = "application/vnd.apple.mpegurl";
                        context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
                        await context.Response.WriteAsync(content);
                    }
                    else
                    {
                        var content = await response.Content.ReadAsByteArrayAsync();
                        context.Response.ContentType = "video/MP2T";
                        context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
                        await context.Response.Body.WriteAsync(content);
                    }
                }
                else
                {
                    context.Response.StatusCode = 404;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[HLS PROXY ERROR] {Message}", ex.Message);
                context.Response.StatusCode = 500;
            }
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

        // Health status logging
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
                
                Log.Information("[HEALTH STATUS] Translation service is now: {Status}", statusMessage);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[STATUS BROADCAST ERROR] {Message}", ex.Message);
            }
        };

        Log.Information("Starting Live STT Demo...");
        Log.Information("Listening to stream: {StreamUrl}", streamUrl);
        Log.Information("Press Ctrl+C to exit.");

        // Task 1: Capture audio task
        var captureTask = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    Log.Information("[CAPTURE] Attempting to capture audio chunk...");
                    var audioFile = await audioListener.CaptureAudioChunkAsync(cts.Token);

                    if (audioFile != null)
                    {
                        processingQueue.Enqueue(audioFile);
                        Log.Information("[CAPTURE] ✓ Queued: {File}", Path.GetFileName(audioFile.FilePath));
                    }
                    else
                    {
                        Log.Warning("[CAPTURE] ✗ No audio captured. Retrying in 5s...");
                        await Task.Delay(5000, cts.Token);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[CAPTURE ERROR] {Message}", ex.Message);
                    await Task.Delay(5000, cts.Token);
                }
            }
        }, cts.Token);

        // Task 2: Transcription workers
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
                        Log.Information("[PROCESS-{Id}] Transcribing: {File}", Task.CurrentId, Path.GetFileName(chunk.FilePath));

                        var transcriptionResult = await whisperService.TranscribeAsync(chunk.FilePath, cts.Token);

                        if (transcriptionResult != null &&
                            (!string.IsNullOrWhiteSpace(transcriptionResult.OriginalText) ||
                             !string.IsNullOrWhiteSpace(transcriptionResult.TranslatedText)))
                        {
                            var originalText = (transcriptionResult.OriginalText ?? transcriptionResult.TranslatedText ?? "").Trim();
                            var translatedText = (transcriptionResult.TranslatedText ?? transcriptionResult.OriginalText ?? "").Trim();

                            Log.Information("[TRANSCRIPTION-{Id}] {Time} → {Text}",
                                Task.CurrentId, DateTime.Now.ToString("T"), translatedText);

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

                            try
                            {
                                await hubContext.Clients.All.SendAsync("ReceiveTranscription", dto);
                                Log.Information("[SIGNALR] ✓ Sent to clients: {Text}", dto.text);
                            }
                            catch (Exception signalrEx)
                            {
                                Log.Warning(signalrEx, "[SIGNALR ERROR] Failed to send message");
                            }
                        }
                        else
                        {
                            Log.Warning("[PROCESS-{Id}] No text transcribed", Task.CurrentId);
                        }

                        if (File.Exists(chunk.FilePath))
                        {
                            File.Delete(chunk.FilePath);
                            Log.Information("[CLEANUP] Deleted: {File}", Path.GetFileName(chunk.FilePath));
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[PROCESS ERROR-{Id}] {Message}", Task.CurrentId, ex.Message);
                    }
                }
            }, cts.Token));
        }

        var allTasks = new List<Task> { captureTask };
        allTasks.AddRange(transcribeTasks);

        // Logging health endpoint
        app.MapGet("/logging-health", () =>
        {
            Log.Information("Logging health check OK");
            return Results.Ok(new
            {
                status = "ok",
                message = "Logging system is working"
            });
        });

        _ = app.RunAsync(cts.Token);
        await Task.WhenAll(allTasks);
    }
}
