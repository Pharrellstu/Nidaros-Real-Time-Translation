# Nidaros-Real-Time-Translation

# NidarosRTT

Live speech-to-text (Dutch) from a Wowza HLS/RTSP stream, using .NET 9, SignalR, Whisper.NET (in-process, cross-platform), and FFmpeg.

## Structure
- **NidarosRTT.API** – Entry point for real-time translation via REST or WebSocket (SignalR).
- **NidarosRTT.Core** – Business logic and data models.
- **NidarosRTT.Infrastructure** – External integrations (speech-to-text, translation APIs).
- **NidarosRTT.Tests** – Unit tests for core logic.

## Prerequisites
- .NET 9 SDK
- A running stream:
	- HLS: http://localhost:1935/live/testStream/playlist.m3u8 (from Wowza compose below)
	- or RTSP: rtsp://HOST:1935/live/OBSstream

## Install & build
```bash
dotnet restore
dotnet build
```

## Whisper model (GGML, CPU)
Place a GGML model in `NidarosRTT.API/models` and ensure the filename matches `Program.cs` (default: `ggml-tiny.bin`). Examples:

```bash
# Tiny multilingual (fastest; adequate for demo)
curl -L -o NidarosRTT.API/models/ggml-tiny.bin \
	https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-tiny.bin

# Or base.en (English only) — not for Dutch
# curl -L -o NidarosRTT.API/models/ggml-base.en.bin \
#   https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin
```

The project copies `models/**` to the output automatically on build.

## Run (Linux/Fish shell)
```fish
# Bind Kestrel to a fixed port
set -x ASPNETCORE_URLS http://localhost:5032
dotnet run --project NidarosRTT.API
```

Open the test UI in your browser:
- http://localhost:5032/

The page connects to `/subtitlesHub` and displays live transcriptions. The video player loads HLS from `http://<your-host>:1935/live/testStream/playlist.m3u8`.

## CPU tuning and language
- Language: set to Dutch ("nl") in `Program.cs` when constructing `WhisperService`.
- Threads: reduce via `new WhisperService(modelPath, language: "nl", threads: 1)`.
- Concurrency: adjust `maxConcurrentTranscriptions` (start with 1–2).
- Model size: `ggml-tiny.bin` keeps CPU low; larger models increase quality but use more CPU.

## Wowza docker setup (HLS demo)

1. Create a `.env` based on `.env.example` (license/user/pass).
2. Start Wowza:
	 ```bash
	 docker compose up -d
	 ```
3. Verify HLS in browser: http://localhost:1935/live/testStream/playlist.m3u8
4. If using RTSP, set the URL in `Program.cs` and ensure network reachability.

## How it works
- `WowzaAudioListener` pulls 10s audio chunks from HLS/RTSP using FFmpeg (auto-detects system ffmpeg or downloads portable binaries).
- `AudioProcessingQueue` buffers chunk paths.
- `WhisperService` (Whisper.NET) transcribes WAV to text in-process (no external EXE).
- SignalR Hub (`/subtitlesHub`) broadcasts transcriptions to web clients.

## Troubleshooting
- Video says “media could not be loaded”:
	- Click the big play button (autoplay policies); ensure HLS URL http://<host>:1935/live/testStream/playlist.m3u8 is reachable.
	- Check DevTools Console for CORS or Mixed Content (serve UI over HTTP if HLS is HTTP).
- No transcriptions in UI:
	- Server should log “[CAPTURE] ✓ Queued …”, “[TRANSCRIPTION-] …”, “[SIGNALR] ✓ Sent …”.
	- Ensure a GGML model file exists at `NidarosRTT.API/models/ggml-tiny.bin` (or update filename in `Program.cs`).
	- Language: Dutch specified as `nl`; use a multilingual model (not `.en`).
- High CPU:
	- Lower `threads` in `WhisperService` and `maxConcurrentTranscriptions`.
	- Use a smaller model (tiny/base).

## Development notes
- Configuration is currently hardcoded in `Program.cs` for simplicity. Future work: move to `appsettings.json`/env.
- FFmpeg is managed via Xabe.FFmpeg and its downloader, preferring system ffmpeg on Linux.
