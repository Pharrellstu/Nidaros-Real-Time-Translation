using Microsoft.AspNetCore.SignalR;
using NidarosRTT.Infrastructure;
using NidarosRTT.Infrastructure.StreamBuffering;
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
        var vttOutputPath = Environment.GetEnvironmentVariable("VTT_OUTPUT_PATH") ?? "./vtt_output";
        var streamName = Environment.GetEnvironmentVariable("STREAM_NAME") ?? "OBSstream";
        
        // Stream buffering configuration
        var delayedStreamUrl = Environment.GetEnvironmentVariable("DELAYED_STREAM_URL")
                              ?? "rtmp://wowza-trial:1935/live/OBSstream_delayed";
        var bufferDelayMs = int.Parse(Environment.GetEnvironmentVariable("BUFFER_DELAY_MS") ?? "30000");
        var enableStreamBuffering = bool.Parse(Environment.GetEnvironmentVariable("ENABLE_STREAM_BUFFERING") ?? "true");

        var builder = WebApplication.CreateBuilder(args);

        // --- Dependency Injection Setup ---
        builder.Services.AddSingleton<AudioProcessingQueue>();
        builder.Services.AddSingleton<IWowzaAudioListener>(new WowzaAudioListener(streamUrl, ffmpegPath));
        builder.Services.AddSingleton<IWhisperService>(new WhisperService(whisperUrl));
        builder.Services.AddSingleton<ICaptionFormatter>(new CaptionFormatter(maxLineLength: 37, maxLinesPerCaption: 2));
        builder.Services.AddSingleton<IVttWriter>(new VttWriter(vttOutputPath));
        builder.Services.AddSingleton<IStreamTimingTracker, StreamTimingTracker>();
        builder.Services.AddSignalR();

        // Stream Buffering Services (for delayed stream)
        if (enableStreamBuffering)
        {
            builder.Services.AddSingleton<IStreamBuffer>(sp => new StreamBuffer(bufferDelayMs));
            builder.Services.AddSingleton<IStreamPuller>(sp => 
            {
                var buffer = sp.GetRequiredService<IStreamBuffer>();
                return new StreamPuller(streamUrl, buffer, ffmpegPath);
            });
            
            // Create shared caption queue for transcription → StreamPublisher communication
            var captionQueue = new NidarosRTT.Infrastructure.Captions.CaptionQueue();
            builder.Services.AddSingleton(captionQueue);
            
            builder.Services.AddSingleton<IStreamPublisher>(sp =>
            {
                var buffer = sp.GetRequiredService<IStreamBuffer>();
                var queue = sp.GetRequiredService<NidarosRTT.Infrastructure.Captions.CaptionQueue>();
                return new StreamPublisher(delayedStreamUrl, ffmpegPath, buffer, queue);
            });
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"[STREAM BUFFERING] Enabled with {bufferDelayMs}ms delay");
            Console.WriteLine($"[STREAM BUFFERING] Source: {streamUrl}");
            Console.WriteLine($"[STREAM BUFFERING] Delayed: {delayedStreamUrl}");
            Console.WriteLine($"[CEA-608 CAPTIONS] Enabled - captions will be embedded in delayed stream");
            Console.ResetColor();
        }

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

        // VTT File serving endpoint
        app.MapGet("/vtt/{streamName}.vtt", async (string streamName, HttpContext context) =>
        {
            try
            {
                var vttPath = Path.Combine(vttOutputPath, $"{streamName}.vtt");
                
                if (!File.Exists(vttPath))
                {
                    context.Response.StatusCode = 404;
                    await context.Response.WriteAsync($"VTT file not found: {streamName}.vtt");
                    return;
                }

                var content = await File.ReadAllTextAsync(vttPath);
                context.Response.ContentType = "text/vtt; charset=utf-8";
                context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
                await context.Response.WriteAsync(content);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[VTT SERVE ERROR] {ex.Message}");
                context.Response.StatusCode = 500;
                await context.Response.WriteAsync($"Error: {ex.Message}");
            }
        });

        // HLS Proxy endpoints to serve video through port 5032
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
                    // Rewrite URLs to point to our proxy
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
                Console.WriteLine($"[HLS PROXY ERROR] playlist.m3u8: {ex.Message}");
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
                    // Check if it's a manifest or segment
                    if (fileName.EndsWith(".m3u8"))
                    {
                        // It's a chunklist - rewrite segment URLs
                        var content = await response.Content.ReadAsStringAsync();
                        content = content.Replace($"media_", $"/hls/{streamName}/media_");

                        context.Response.ContentType = "application/vnd.apple.mpegurl";
                        context.Response.Headers.Append("Access-Control-Allow-Origin", "*");
                        await context.Response.WriteAsync(content);
                    }
                    else
                    {
                        // It's a media segment (.ts file)
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
                Console.WriteLine($"[HLS PROXY ERROR] {fileName}: {ex.Message}");
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
        var captionFormatter = app.Services.GetRequiredService<ICaptionFormatter>();
        var vttWriter = app.Services.GetRequiredService<IVttWriter>();
        var streamTimingTracker = app.Services.GetRequiredService<IStreamTimingTracker>();

        // Initialize VTT file for this stream
        vttWriter.Initialize(streamName);
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"[VTT WRITER] Initialized VTT file for stream: {streamName}");
        Console.WriteLine($"[VTT WRITER] Output path: {vttOutputPath}/{streamName}.vtt");
        Console.WriteLine($"[STREAM TIMING] Timing tracker initialized");
        Console.ResetColor();

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

        // --- Start Stream Buffering Tasks (if enabled) ---
        Task? streamPullerTask = null;
        Task? streamPublisherTask = null;
        IStreamPublisher? streamPublisher = null;  // Shared reference for caption queueing
        
        if (enableStreamBuffering)
        {
            var streamPuller = app.Services.GetRequiredService<IStreamPuller>();
            streamPublisher = app.Services.GetRequiredService<IStreamPublisher>();
            
            // Start pulling stream into buffer
            streamPullerTask = Task.Run(async () =>
            {
                try
                {
                    await streamPuller.StartAsync(cts.Token);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[STREAM PULLER TASK] Fatal error: {ex.Message}");
                    Console.ResetColor();
                }
            }, cts.Token);
            
            // Start publishing delayed stream (with caption injection)
            var publisher = streamPublisher;  // Capture for Task.Run
            streamPublisherTask = Task.Run(async () =>
            {
                try
                {
                    await publisher.StartAsync(cts.Token);
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[STREAM PUBLISHER TASK] Fatal error: {ex.Message}");
                    Console.ResetColor();
                }
            }, cts.Token);
            
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[STREAM BUFFERING] Puller and Publisher tasks started");
            Console.ResetColor();
        }

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
                            // USING ENGLISH-ONLY MODE (translation disabled)
                            // Both original and translated should be the same English text
                            var englishText = (transcriptionResult.OriginalText ?? transcriptionResult.TranslatedText ?? "").Trim();

                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[TRANSCRIPTION-{Task.CurrentId}] {DateTime.Now:T} → {englishText}");
                            Console.ResetColor();

                            // ===== CEA-608 CAPTION QUEUEING =====
                            if (streamPublisher != null)
                            {
                                try
                                {
                                    // Calculate stream offset from chunk's wall-clock timestamp
                                    var streamOffset = streamTimingTracker.GetStreamTimeOffset(chunk);
                                    
                                    // Get timing from Whisper result (relative to chunk)
                                    var whisperStart = transcriptionResult.GetStartTime();
                                    
                                    // Calculate absolute stream time for this caption
                                    var absoluteStreamTime = streamOffset + whisperStart;
                                    var timecodeMs = (uint)absoluteStreamTime.TotalMilliseconds;
                                    
                                    // Queue caption for injection into delayed stream
                                    streamPublisher.QueueCaption(englishText, timecodeMs);
                                    
                                    Console.ForegroundColor = ConsoleColor.Cyan;
                                    Console.WriteLine($"[CEA-608 QUEUE] Timecode: {timecodeMs}ms ({absoluteStreamTime:hh\\:mm\\:ss\\.fff}), Text: '{englishText.Substring(0, Math.Min(englishText.Length, 50))}'");
                                    Console.ResetColor();
                                }
                                catch (Exception captionEx)
                                {
                                    Console.ForegroundColor = ConsoleColor.Yellow;
                                    Console.WriteLine($"[CEA-608 ERROR] Failed to queue caption: {captionEx.Message}");
                                    Console.ResetColor();
                                }
                            }
                            // ===== END CEA-608 QUEUEING =====

                            // ===== VTT FILE GENERATION WITH STREAM TIMING =====
                            try
                            {
                                // Calculate stream offset from chunk's wall-clock timestamp
                                var streamOffset = streamTimingTracker.GetStreamTimeOffset(chunk);

                                // Get timing from Whisper result (relative to chunk)
                                var whisperStart = transcriptionResult.GetStartTime();
                                var whisperEnd = transcriptionResult.GetEndTime();

                                // Format text into captions with absolute stream timing
                                var captions = captionFormatter.FormatWithOffset(
                                    englishText,  // Using English text directly (no translation)
                                    whisperStart,
                                    whisperEnd,
                                    streamOffset,  // ← This adds cumulative time!
                                    language: "en"  // Changed from "nl" to "en"
                                );

                                // Write all captions to VTT file
                                foreach (var caption in captions)
                                {
                                    await vttWriter.AppendCaptionAsync(caption);
                                    Console.ForegroundColor = ConsoleColor.Cyan;
                                    Console.WriteLine($"[VTT-{Task.CurrentId}] Stream@{streamOffset:hh\\:mm\\:ss} + Whisper {whisperStart.TotalSeconds:F1}s = {caption.Start:hh\\:mm\\:ss\\.fff} → {caption.End:hh\\:mm\\:ss\\.fff}: {caption.Text.Replace("\n", " | ")}");
                                    Console.ResetColor();
                                }
                            }
                            catch (Exception vttEx)
                            {
                                Console.ForegroundColor = ConsoleColor.Yellow;
                                Console.WriteLine($"[VTT ERROR-{Task.CurrentId}] Failed to write VTT: {vttEx.Message}");
                                Console.ResetColor();
                            }
                            // ===== END VTT GENERATION =====

                            // Create DTO with translation status (translation disabled)
                            var dto = new SingleCaptionDto(
                                text: englishText,
                                originalText: englishText,  // Same as text since no translation
                                translationAvailable: false,  // Translation disabled
                                translationStatus: "disabled",
                                sourceLanguage: "en",
                                targetLanguage: "en",  // Changed from "nl"
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
        
        // Add stream buffering tasks if enabled
        if (streamPullerTask != null)
            allTasks.Add(streamPullerTask);
        if (streamPublisherTask != null)
            allTasks.Add(streamPublisherTask);

        _ = app.RunAsync(cts.Token);
        await Task.WhenAll(allTasks);
    }
}