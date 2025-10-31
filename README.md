# Nidaros Real-Time Translation

Live subtitle system with automatic translation for RTMP streams. Stream in English, get Dutch subtitles in real time—all running locally.

## What It Does

Captures audio from your OBS stream, transcribes it with AI (Faster Whisper), translates to Dutch (Argos Translate), and displays synchronized subtitles on a web player. Completely offline after initial setup.

## Key Features

* Fast AI transcription (2-3 seconds per chunk)
* Offline translation (English → Dutch)
* Audio noise reduction (RNNoise)
* Smart failover (falls back to English if translation fails)
* User controls: subtitle size, language toggle, on/off switch
* Real-time sync with video
* No cloud APIs, completely private

## Requirements

* **Docker Desktop** (for Windows/Mac/Linux)
* **OBS Studio** or any RTMP streaming software
* **4 GB RAM minimum** (8 GB recommended)
* **Wowza trial license** (free from wowza.com)

## How to Run

### 1. Clone the Repository

```bash
git clone https://github.com/Pharrellstu/Nidaros-Real-Time-Translation.git
cd Nidaros-Real-Time-Translation
```

### 2. Create Wowza Environment File

Create a new file named `.env` in the root of the project.

```ini
# .env
WSE_LICENSE_KEY=your-wowza-license-key-here
ADMIN_USERNAME=admin
ADMIN_PASSWORD=your-secure-password
```

### 3. Configure Your Streaming Software (OBS)

1. Open OBS Studio.
2. Go to **Settings > Stream**.
3. Set **Service** to `Custom...`.
4. Set **Server** to `rtmp://localhost:1935/live`.
5. Set **Stream Key** to `OBSstream`.

### 4. Build and Run the Containers

```bash
docker-compose up --build
```

Wait for all services to start. You may see some FFmpeg errors from `api-service-1`—this is normal. It just means it's waiting for your stream to start.

### 5. Configure Wowza Delay (Important)

1. Open `http://localhost:8088` and login with your credentials
2. Select **Applications** → **live** → **Properties**
3. Find `cupertinoChunkDurationTarget` and set to **3000**
4. Find `cupertinoPlaylistChunkCount` and set to **15**
5. Click **Save** and **Restart Stream**

### 6. Start Streaming & View Results

1. In OBS, click **Start Streaming**
2. Open `http://localhost:5032/index.html` in your browser
3. You should see your video with live Dutch subtitles!

**Subtitle controls:**
- Toggle subtitles on/off
- Adjust size (Small, Medium, Normal, Large)
- Switch language (Dutch/English)
### How It Works

1. **Health Monitoring:** The .NET API continuously monitors the translation service health endpoint every 15 seconds.
2. **Circuit Breaker:** After 2 consecutive failures (~30 seconds), the system triggers "unavailable" status.
3. **Automatic Fallback:** When translation fails, subtitles display in the original language (English) instead of crashing.
4. **User Notification:** A persistent orange warning banner appears at the top of the web page when translation is unavailable.
5. **Automatic Recovery:** When the translation service comes back online, the system automatically resumes translations and removes the warning.

### Testing the Failover

To test the failover mechanism:

```bash
# Stop the translation service
docker stop nidaros-real-time-translation-translator-service-1

# Wait ~30 seconds and observe:
# - Orange warning banner appears on webpage
# - Console shows: "Translation service is DOWN"
# - Subtitles appear in English (original language)

# Restart the translation service
docker start nidaros-real-time-translation-translator-service-1

# Wait ~30 seconds and observe:
# - Warning banner disappears
# - Console shows: "Translation service has RECOVERED"
# - Dutch translations resume
```

### Configuration

Health monitoring parameters can be adjusted in `NidarosRTT.API/Program.cs`:

* `healthCheckIntervalSeconds`: Time between health checks (default: 15s)
* `healthCheckTimeoutSeconds`: HTTP request timeout (default: 10s)
* `failureThreshold`: Number of failures before triggering unavailable status (default: 2)
* `circuitBreakerDurationSeconds`: Time to wait before retrying after circuit opens (default: 60s)

## Troubleshooting

* **Error: `Output file #0 does not contain any stream`**

    * This just means the `api-service` is running, but your RTMP stream from OBS is not active. **Start streaming from OBS** to fix this.

* **Warning: `Duration is out of bounds` in `wowza-trial-1` logs**

    * This means your **Keyframe Interval** in OBS is not set to `2` seconds. Stop your stream, fix the setting in **OBS > Settings > Output > Streaming**, and start streaming again.

* **Subtitles don't appear**

    * Check the `api-service-1` logs. Are transcriptions appearing there?
    * Check the `whisper-service-1` logs. Are there any errors?
    * Open your browser's Developer Console (F12) on `http://localhost:5032`. Look for any SignalR connection errors. Ensure you fixed the `index.html` URL to point to `subtitlesHub` (lowercase 's') as noted in the setup.

* **Translation warning banner appears**

    * This is normal if the `translator-service` is down or unavailable.
    * Check if the translator service is running: `docker ps | grep translator`
    * Check translator service logs: `docker logs nidaros-real-time-translation-translator-service-1`
    * The system will automatically fall back to showing English subtitles.
    * Translations will resume automatically when the service recovers.

* **Subtitles appear in English instead of Dutch**

    * Check if the translation service is running: `docker ps | grep translator`
    * Look for `[TRANSLATION] Translated:` messages in `whisper-service-1` logs.
    * Check if translation status shows `"service_unavailable"` in browser console.
    * The orange warning banner should appear if translation is down.

## Performance Optimization

### For Better Transcription Speed

1. **Use GPU acceleration** (if you have NVIDIA GPU):
   - Edit `FasterWhisperService/app/run.py`
   - Change `DEVICE = "cpu"` to `DEVICE = "cuda"`
   - Change `COMPUTE_TYPE = "int8"` to `COMPUTE_TYPE = "float16"`
   - Rebuild: `docker-compose up --build whisper-service`

2. **Use smaller Whisper model:**
   - Edit `FasterWhisperService/app/run.py`
   - Change `MODEL_NAME = "small.en"` to `MODEL_NAME = "tiny.en"`
   - Trade-off: Faster but less accurate

3. **Reduce concurrent workers:**
   - Edit `NidarosRTT.API/Program.cs`
   - Change `maxConcurrentTranscriptions = 6` to `= 3`
   - Reduces CPU load but may increase subtitle delay

### For Lower Latency

1. **Reduce audio chunk size:**
   - Edit `NidarosRTT.Infrastructure/WowzaAudioListener.cs`
   - Change `-t 5` (5 seconds) to `-t 3` (3 seconds)
   - Note: Shorter chunks = less context for transcription

2. **Adjust Wowza HLS settings:**
   - Reduce `cupertinoChunkDurationTarget` to 2000ms
   - Reduce `cupertinoPlaylistChunkCount` to 10
   - Trade-off: Lower delay but more chance of buffering

### For Better Audio Quality

1. **Increase FFmpeg sample rate:**
   - Edit `WowzaAudioListener.cs`
   - Change `-ar 16000` to `-ar 44100`
   - Trade-off: Larger files, slightly slower processing

2. **Disable RNNoise** (if your audio is already clean):
   - Comment out noise reduction calls in `WowzaAudioListener.cs` and `WhisperService.cs`
   - Saves ~0.5-1 second per chunk

## Advanced Configuration

### Environment Variables

You can customize behavior with environment variables in `docker-compose.yml`:

```yaml
environment:
  - WHISPER_URL=http://whisper-service:5001/transcribe
  - TRANSLATOR_URL=http://translator-service:5000
  - STREAM_URL=rtsp://wowza-trial:1935/live/OBSstream
  - ASPNETCORE_URLS=http://+:5032
  - FFMPEG_PATH=/usr/bin/ffmpeg
```

### Health Check Tuning

For production deployments, you might want to adjust health monitoring:

**In `Program.cs`:**
```csharp
healthCheckIntervalSeconds: 15,      // How often to check (seconds)
healthCheckTimeoutSeconds: 10,       // How long to wait for response
failureThreshold: 2,                 // Failures before marking unavailable  
circuitBreakerDurationSeconds: 60    // How long to wait before retry
```

## Technical Details

### Audio Processing Pipeline

1. **Capture (FFmpeg):**
   - 5-second WAV chunks at 16kHz, mono
   - Uses RTSP TCP transport for reliability
   - Includes timing metadata for synchronization

2. **Noise Reduction (RNNoise):**
   - Xiph.org's recurrent neural network denoiser
   - Removes stationary noise (fans, hum, etc.)
   - Compiled from source during Docker build

3. **Transcription (Faster Whisper):**
   - OpenAI Whisper optimized with CTranslate2
   - Uses beam search with size 5 for accuracy
   - VAD filtering removes silent segments

4. **Translation (Argos Translate):**
   - LibreTranslate's offline models
   - Neural machine translation
   - No external API calls required

### SignalR Communication

The system uses SignalR for real-time communication:

**Hub Events:**
- `ReceiveTranscription`: Sends subtitle DTO to clients
- `TranslationServiceStatusChanged`: Broadcasts service health updates

**DTO Structure:**
```json
{
  "text": "Dutch translation here",
  "originalText": "English original here",
  "translationAvailable": true,
  "translationStatus": "success",
  "sourceLanguage": "en",
  "targetLanguage": "nl",
  "wallClockStartTS": 1698765432000,
  "wallClockEndTS": 1698765437000
}
```

### Subtitle Timing

The system calculates video delay dynamically:
1. Measures HLS buffer size in browser
2. Averages last 10 measurements
3. Adds `delayAdjustment` offset (default: 3000ms)
4. Schedules subtitle display relative to capture timestamp

This ensures subtitles sync perfectly even if HLS latency varies.

### Development Setup

**Running locally without Docker:**

1. Install .NET 9.0 SDK
2. Install Python 3.11+
3. Install FFmpeg and RNNoise
4. Run each service separately:
   ```bash
   # Terminal 1: Translator
   cd TranslatorService
   pip install -r requirements.txt
   python translate_server.py

   # Terminal 2: Whisper
   cd FasterWhisperService
   pip install -r requirements.txt
   python -m uvicorn app.run:app --host 0.0.0.0 --port 5001

   # Terminal 3: API
   cd NidarosRTT.API
   dotnet run
   ```