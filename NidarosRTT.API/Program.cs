using NidarosRTT.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var streamUrl = "http://172.20.10.8:1935/live/OBSstream/playlist.m3u8";
var whisperExe = @"C:\Users\Pharrell\whisper.cpp\build\bin\whisper-cli.exe";
var modelPath = @"C:\Users\Pharrell\whisper.cpp\models\ggml-base.en.bin";

var listener = new WowzaAudioListener(streamUrl);
var whisper = new WhisperService(whisperExe, modelPath);

app.MapGet("/", () => "Local Whisper STT demo running...");

_ = Task.Run(async () =>
{
    Console.WriteLine("Starting local Whisper Speech-to-Text demo...");

    while (true)
    {
        try
        {
            var audioFile = await listener.CaptureAudioChunkAsync();
            Console.WriteLine($"Captured: {Path.GetFileName(audioFile)} — transcribing...");

            var text = await whisper.TranscribeAsync(audioFile);

            if (!string.IsNullOrWhiteSpace(text))
                Console.WriteLine($"{DateTime.Now:T} → {text}");
            else
                Console.WriteLine("No transcription returned.");

            File.Delete(audioFile);
        }
        catch (Exception ex)
        {
            Console.WriteLine($" Error: {ex.Message}");
        }

        await Task.Delay(2000);
    }
});

app.Run();
