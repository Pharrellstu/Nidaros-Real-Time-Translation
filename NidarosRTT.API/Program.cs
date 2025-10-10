using LiveSubtitleTranslation.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var streamUrl = "http://10.8.5.53:1935/live/OBSstream/playlist.m3u8";
var listener = new WowzaAudioListener(streamUrl);

app.MapGet("/", () => "Audio capture test running...");

_ = Task.Run(async () =>
{
    Console.WriteLine("Starting Wowza audio capture test...");

    while (true)
    {
        try
        {
            var audioFile = await listener.CaptureAudioChunkAsync();
            Console.WriteLine($"✅ Captured: {audioFile}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"⚠️ Error: {ex.Message}");
        }

        await Task.Delay(8000); // wait before next capture
    }
});

app.Run();
