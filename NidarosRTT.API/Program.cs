using NidarosRTT.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var streamUrl = "http://172.20.10.8:1935/live/OBSstream/playlist.m3u8";
var whisperExe = @"C:\Users\Pharrell\whisper.cpp\build\bin\whisper-cli.exe";
var modelPath = @"C:\Users\Pharrell\whisper.cpp\models\ggml-base.en.bin";

var listener = new WowzaAudioListener(streamUrl);
var whisper = new WhisperService(whisperExe, modelPath);
var queue = new AudioProcessingQueue();
var cts = new CancellationTokenSource();

app.MapGet("/", () => "Local Whisper STT demo running...");

_ = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            var audioFile = await listener.CaptureAudioChunkAsync();
            queue.Enqueue(audioFile);
            Console.WriteLine($"Captured and queued: {Path.GetFileName(audioFile)}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Capture error: {ex.Message}");
        }

        await Task.Delay(3000, cts.Token); // slight overlap for continuity
    }
}, cts.Token);

_ = Task.Run(async () =>
{
    while (!cts.Token.IsCancellationRequested)
    {
        try
        {
            var audioFile = await queue.DequeueAsync(cts.Token);
            Console.WriteLine($"Processing: {Path.GetFileName(audioFile)}...");

            var text = await whisper.TranscribeAsync(audioFile);

            if (!string.IsNullOrWhiteSpace(text))
                Console.WriteLine($"{DateTime.Now:T} → {text.Trim()}");
            else
                Console.WriteLine("Empty transcription result.");

            File.Delete(audioFile);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Processing error: {ex.Message}");
        }
    }
}, cts.Token);

app.Run();
