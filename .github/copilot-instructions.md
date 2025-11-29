# Nidaros Real-Time Translation - AI Agent Instructions

## Architecture Overview

This is a **real-time live subtitle translation system** for RTMP/RTSP streams. It uses a multi-service architecture orchestrated via Docker Compose:

- **api-service** (.NET 9.0): Main orchestrator that captures audio, manages processing queue, and broadcasts subtitles via SignalR
- **whisper-service** (Python/FastAPI): Faster-Whisper transcription with optional translation delegation
- **translator-service** (Python/Flask): Argos Translate for offline EN→NL translation
- **wowza-trial**: Wowza Streaming Engine for RTMP/RTSP ingestion and HLS delivery
- **wowza-manager**: Web UI for Wowza configuration

### Data Flow Pipeline

1. **Capture**: `WowzaAudioListener` uses FFmpeg to extract 5-second audio chunks from RTSP stream (`rtsp://wowza-trial:1935/live/OBSstream`)
2. **Noise Reduction**: RNNoise processes each chunk before transcription (happens in both `WowzaAudioListener` and `WhisperService`)
3. **Queue**: `AudioProcessingQueue` (thread-safe `ConcurrentQueue` + `SemaphoreSlim`) buffers chunks for processing
4. **Transcription**: Multiple concurrent workers (default: 6) call Faster-Whisper service via HTTP multipart upload
5. **Translation**: Whisper service forwards text to translator service with failover handling
6. **Broadcast**: API broadcasts `SingleCaptionDto` to web clients via SignalR `SubtitlesHub`
7. **Display**: Browser receives subtitles and syncs them to HLS video using wall-clock timestamps

## Critical Timing Architecture

### Wall-Clock Timestamp System
- Audio chunks are tagged with `wallClockStartTS` and `wallClockEndTS` (Unix milliseconds) at capture time
- Subtitles carry these timestamps through the entire pipeline (`AudioChunk` → `SingleCaptionDto`)
- Browser calculates dynamic HLS delay by measuring buffer size and schedules subtitle display relative to capture time
- This ensures subtitles sync perfectly even if transcription/translation takes variable time

### Latency Configuration Points
- Audio chunk duration: `WowzaAudioListener.cs` line ~50: `-t 5` (5 seconds)
- Concurrent workers: `Program.cs` line ~172: `maxConcurrentTranscriptions = 6`
- Wowza HLS chunk size: Configure via web UI at `localhost:8088` → `cupertinoChunkDurationTarget=3000`
- Browser delay adjustment: `index.html` → `delayAdjustment = 3000` (milliseconds)

## Health Monitoring & Circuit Breaker Pattern

### TranslationHealthMonitor (`NidarosRTT.Core/Services/`)
Implements a circuit breaker to prevent cascading failures when translation service is down:

- **Health Checks**: Polls `http://translator-service:5000/health` every 15 seconds (configurable)
- **Failure Threshold**: 2 consecutive failures triggers "Unavailable" status (default)
- **Circuit Breaker**: Stops health checks for 60 seconds after circuit opens
- **Status Broadcast**: `StatusChanged` event broadcasts to all SignalR clients → orange warning banner appears
- **Automatic Fallback**: System falls back to English subtitles when translation unavailable

Configuration in `Program.cs` lines 30-37:
```csharp
new TranslationHealthMonitor(
    httpClientFactory, logger, translatorUrl,
    healthCheckIntervalSeconds: 15,
    healthCheckTimeoutSeconds: 10,
    failureThreshold: 2,
    circuitBreakerDurationSeconds: 60
)
```

## Key Conventions & Patterns

### DTO Naming Pattern
Use lowercase property names for JSON serialization compatibility with JavaScript (e.g., `SingleCaptionDto` has `text`, `originalText`, `wallClockStartTS`)

### Audio Processing
- Always use **16kHz mono WAV** format (standard for speech recognition)
- Clean audio with `NoiseReductionService.CleanAudioAsync()` before transcription (RNNoise subprocess call)
- Delete processed audio files in finally blocks to avoid disk filling

### Error Handling Philosophy
- **Graceful degradation**: Never crash on missing translation - fall back to original text
- **Retry with backoff**: `WowzaAudioListener` retries every 5s if stream down, `TranslationHealthMonitor` uses circuit breaker
- **Verbose logging**: All services log extensively with color-coded console output (Green=success, Yellow=warning, Red=error)

### Concurrent Processing
- Use multiple background workers (Tasks) for transcription to overlap I/O-bound operations
- `AudioProcessingQueue` decouples capture from processing with semaphore-based async dequeue
- Lock carefully: `TranslationHealthMonitor` uses `_statusLock` object for status changes

## Development Workflows

### Local Development (Without Docker)
```fish
# Terminal 1: Translator service
cd TranslatorService
pip install flask argostranslate
python translate_server.py  # Runs on :5000

# Terminal 2: Whisper service
cd FasterWhisperService
pip install -r requirements.txt
set -x TRANSLATION_SERVICE_URL http://localhost:5000/translate
uvicorn app.run:app --host 0.0.0.0 --port 5001

# Terminal 3: .NET API
cd NidarosRTT.API
set -x WHISPER_URL http://localhost:5001/transcribe
set -x TRANSLATOR_URL http://localhost:5000
set -x STREAM_URL rtsp://localhost:1935/live/OBSstream
set -x FFMPEG_PATH /usr/bin/ffmpeg  # Adjust for your OS
dotnet run
```

### Docker Compose Workflow
```fish
# Build and start all services
docker-compose up --build

# View logs for specific service
docker logs -f nidaros-real-time-translation-api-service-1

# Restart single service after code change
docker-compose up --build api-service

# Test translation failover
docker stop nidaros-real-time-translation-translator-service-1
# Wait 30s, observe orange banner at localhost:5032
docker start nidaros-real-time-translation-translator-service-1
```

### Testing the Pipeline
1. Configure OBS: **Settings → Stream → Custom → rtmp://localhost:1935/live → Stream Key: OBSstream**
2. Set OBS **Output → Keyframe Interval = 2 seconds** (critical for HLS)
3. Configure Wowza at `localhost:8088`: Set `cupertinoChunkDurationTarget=3000` and `cupertinoPlaylistChunkCount=15`
4. Start streaming from OBS
5. Open `localhost:5032/index.html` - subtitles should appear within 8-12 seconds

### Debugging Common Issues
- **"Output file #0 does not contain any stream"**: Normal - means OBS isn't streaming yet
- **No subtitles appear**: Check `api-service-1` logs for transcription output. Verify SignalR connection in browser console (F12)
- **Translation unavailable**: Check `docker ps | grep translator` and `docker logs translator-service-1`. Orange warning banner should appear automatically
- **Subtitles out of sync**: Check browser console for measured HLS delay. Adjust `delayAdjustment` in `index.html` if needed

## Project Structure Conventions

### Solution Organization (C#)
- **NidarosRTT.Core**: Domain services (health monitoring, interfaces)
- **NidarosRTT.Infrastructure**: External integrations (Whisper API, FFmpeg, audio queue, DTOs)
- **NidarosRTT.API**: Web host (SignalR hub, HLS proxy, background workers)
- **NidarosRTT.Tests**: xUnit tests (currently minimal)

### Python Services
- `FasterWhisperService/app/run.py`: FastAPI endpoint at `/transcribe`, handles translation delegation
- `TranslatorService/translate_server.py`: Flask endpoint at `/translate` and `/health`, auto-installs Argos models on first use

### Configuration Precedence
1. Environment variables in `docker-compose.yml`
2. Hardcoded defaults in `Program.cs` (fallback URLs for local development)
3. Wowza settings require manual web UI configuration (not persisted in code)

## External Dependencies

### Required Binaries
- **FFmpeg**: Audio extraction from RTSP streams. Must be on PATH or specified via `FFMPEG_PATH` env var
- **RNNoise**: Noise reduction library, compiled during Docker build (`FasterWhisperService/Dockerfile` and `NidarosRTT.API/Dockerfile`)

### Python Packages
- `faster-whisper`: CTranslate2-optimized Whisper implementation (much faster than OpenAI's)
- `argostranslate`: Offline neural translation (downloads models to `/app/argospm_downloads`)
- `uvicorn`: ASGI server for FastAPI
- `flask`: Web server for translation service

### .NET Packages
- `Microsoft.AspNetCore.SignalR`: Real-time communication for subtitle broadcast
- `Newtonsoft.Json`: JSON serialization (used in some services)

### Wowza Configuration
Requires manual setup via web UI (`localhost:8088`) after first launch:
- Set HLS chunk duration to 3000ms (reduces latency from default 10s)
- Set playlist chunk count to 15 (balance between buffer size and delay)
- License key must be in `.env` file (get free trial from wowza.com)

## Performance Tuning

### GPU Acceleration (NVIDIA Only)
Edit `FasterWhisperService/app/run.py` lines 16-17:
```python
DEVICE = "cuda"  # Change from "cpu"
COMPUTE_TYPE = "float16"  # Change from "int8"
```
Rebuild: `docker-compose up --build whisper-service`

### Model Size vs Speed Trade-off
`FasterWhisperService/app/run.py` line 15:
- `tiny.en`: Fastest, least accurate (~1s per 5s chunk)
- `small.en`: Default, good balance (~2-3s per chunk)
- `medium.en`: More accurate, slower (~5-7s per chunk)

### Concurrency Tuning
`Program.cs` line 172: Adjust `maxConcurrentTranscriptions` (default: 6)
- More workers = faster catchup if queue builds up, but higher CPU usage
- Fewer workers = lower CPU, but may lag if transcription is slow

## Common Modifications

### Changing Language Pair
1. Update `FasterWhisperService/app/run.py` line 51: Change `language="en"` to target source language
2. Update model: Change `MODEL_NAME = "small.en"` to multilingual model like `"small"` (line 15)
3. Update `TRANSLATION_SERVICE_URL` parameters in transcribe endpoint (line 64): Change `source_lang` and `target_lang`
4. Ensure Argos Translate has the language pair installed (run `install_en_nl.py` equivalent for your pair)

### Adding New SignalR Events
1. Define method in `SubtitlesHub.cs`
2. Broadcast via `hubContext.Clients.All.SendAsync("EventName", data)` in `Program.cs`
3. Handle in JavaScript: `connection.on("EventName", (data) => { ... })` in `index.html`

### HLS Proxy Modifications
The API serves HLS content via proxy endpoints (lines 71-134 in `Program.cs`):
- `/hls/{streamName}/playlist.m3u8`: Main playlist with URL rewriting
- `/hls/{streamName}/{fileName}`: Proxies chunklists and `.ts` segments
- Required because browsers can't access Wowza directly (CORS and port access)

When modifying, preserve URL rewriting logic to point segments back through proxy.
