# Nidaros Real-Time Translation

A real-time speech-to-text and translation system built on Wowza Streaming Engine. This project provides automatic caption generation and multi-language translation capabilities for live streaming media using either Azure Cognitive Services or Whisper AI models.

## Overview

This project implements a Wowza Streaming Engine plugin that captures audio from live streams, performs real-time speech-to-text transcription, and optionally translates captions into multiple languages. The system supports both Azure Cognitive Services Speech API and OpenAI's Whisper model for speech recognition.

### Key Features

- **Real-time Speech-to-Text**: Automatic caption generation from live audio streams
- **Multi-language Support**: Translation capabilities for multiple languages (English, French, Spanish, German, Japanese)
- **Dual Recognition Engines**: 
  - Azure Cognitive Services Speech API
  - Whisper AI (faster-whisper implementation)
- **Live Streaming Integration**: Seamless integration with Wowza Streaming Engine for HLS/WebRTC streams
- **Caption Formatting**: Automatic line breaking, text wrapping, and timing synchronization
- **Delayed Stream Management**: Configurable delay for caption synchronization

## Browser Support

The JW Player demo frontend is supported on the following browsers:

| Browser | Supported Versions |
|---------|-------------------|
| Chrome  | Latest 2 versions |
| Edge    | Latest 2 versions |
| Firefox | Latest 2 versions |
| Safari  | Latest 2 versions |

**Note**: For optimal performance and feature compatibility, we recommend keeping your browser up to date with the latest versions.

## Accessibility

The video player frontend is fully accessible and compliant with **WCAG 2.1 Level AA** standards, achieving a perfect **Lighthouse accessibility score of 100/100**.

### Testing Accessibility

To verify accessibility in your browser:

1. **Open the player**: Navigate to `http://localhost:8000`
2. **Open DevTools**: Press `F12` (Windows/Linux) or `Cmd+Option+I` (Mac)
3. **Run Lighthouse**:
   - Click the **Lighthouse** tab in DevTools
   - Select **"Accessibility"** category only
   - Click **"Analyze page load"**
   - Wait 30-60 seconds for results

### Understanding the Score

**Score: 100/100** 
- **Passed Audits (21)**: All automated accessibility checks passed
- **Manual Checks (10)**: Items requiring human verification (standard for all audits)
- **Not Applicable (38)**: Tests not relevant to this page type

**What This Means:**
- Screen readers can navigate all controls
- Keyboard navigation fully functional (Tab, Enter, Space keys work)
- All interactive elements have descriptive labels
- Focus indicators are visible
- Dynamic content updates are announced to assistive technologies

### Accessibility Features

The player implements comprehensive ARIA support:

- **ARIA Roles**: All buttons and regions properly labeled (`role="button"`, `role="region"`)
- **ARIA Labels**: Descriptive labels on all controls (e.g., "Toggle captions, currently off")
- **ARIA States**: Toggle button states tracked (`aria-pressed="true/false"`)
- **Live Regions**: Status updates announced via `aria-live` regions
- **Keyboard Support**: Full keyboard navigation with visible focus indicators
- **Semantic HTML**: Proper HTML5 structure with `<main>` landmark

### Screen Reader Compatibility

Tested and compatible with:
- **NVDA** (Windows) with Chrome/Firefox
- **JAWS** (Windows) with Chrome/Firefox/Edge
- **VoiceOver** (macOS/iOS) with Safari
- **TalkBack** (Android) with Chrome
- **Narrator** (Windows) with Edge

For detailed testing documentation, see `WebPlayer/ACCESSIBILITY_TESTING_GUIDE.md`.

## Architecture

The system consists of several components:

1. **Wowza Streaming Engine**: Media server handling live streams
2. **Caption Handlers Plugin**: Java-based plugin processing audio and managing captions
3. **Whisper Server**: Python-based speech recognition service (optional)
4. **LibreTranslate Server**: Translation service for multi-language support (optional)
5. **Wowza Manager**: Web-based management interface

## Prerequisites

- Docker and Docker Compose
- Wowza Streaming Engine License Key (trial or full license)
- Java 17 and Gradle (only required for local debugging and development)

## Project Structure

```
.
├── src/                          # Java source code
│   └── main/java/com/wowza/wms/plugin/captions/
│       ├── azure/                # Azure Speech Service integration
│       ├── whisper/              # Whisper AI integration
│       ├── audio/                # Audio processing handlers
│       ├── caption/              # Caption generation and formatting
│       ├── stream/               # Stream management and delayed streaming
│       └── transcoder/           # Audio resampling and transcoding
├── conf/                         # Configuration files
│   ├── azure/                    # Azure-specific application config
│   └── whisper/                  # Whisper-specific application config
├── lib/                          # External JAR dependencies
├── model_cache/                  # Cached Whisper models
├── wse/                          # Wowza Streaming Engine data
├── wowza_setup/                  # Docker setup scripts
├── docker-compose.yaml           # Main Docker Compose (Whisper only)
├── docker-compose-translate.yaml # Docker Compose with translation
└── build.gradle                  # Gradle build configuration
```

## Installation & Setup

### 1. Clone the Repository

```bash
git clone https://github.com/Pharrellstu/Nidaros-Real-Time-Translation.git
cd Nidaros-Real-Time-Translation
```

### 2. Configure Environment

Set your Wowza Streaming Engine license key. You can either create a `.env` file in the project root:

```bash
WSE_LICENSE_KEY=your-wowza-license-key
```

Or export it directly:

```bash
export WSE_LICENSE_KEY=your-wowza-license-key
```

## Running the Application

### Option 1: Whisper Only (No Translation)

Run with Whisper speech recognition only:

```bash
docker-compose up -d
```

This starts:
- Wowza Streaming Engine (port 1935, 8087)
- Whisper Server (port 3000)
- Wowza Manager (port 8088)

### Option 2: Whisper + Translation

Run with Whisper and LibreTranslate for multi-language translation:

```bash
docker-compose -f docker-compose-translate.yaml up -d
```

This starts:
- Wowza Streaming Engine (port 1935, 8087)
- Whisper Server (port 3000)
- LibreTranslate Server (port 5001)
- Wowza Manager (port 8088)

### 3. Access the Services

- **Wowza Streaming Engine REST API**: http://localhost:8087
- **Wowza Manager UI**: http://localhost:8088
  - Username: `admin`
  - Password: `password`
- **Whisper Server**: http://localhost:3000
- **LibreTranslate** (if enabled): http://localhost:5001
- **TestFrontend** : http://localhost:8000
- **Prometheus**: http://localhost:9090
- **Grafana**: http://localhost:3001 (credentials `admin` / `password`)

### Monitoring & Metrics

- **Application metrics** (latency, errors, drops, throughput) are exposed by the Wowza captions plugin through Micrometer and scraped by Prometheus (`wowza-captions` job). Grafana ships with the `RT Translator Overview` dashboard to visualize these KPIs, and the admin credentials are pre-provisioned via `GF_SECURITY_ADMIN_USER=admin` / `GF_SECURITY_ADMIN_PASSWORD=password` in `docker-compose.yaml`.
- **Audio buffer depth** (`rttranslator_audio_buffer_depth`): gauges how many audio frames are queued for Whisper. A rising value signals that STT is falling behind; the panel is already wired into the Grafana dashboard once you rebuild/restart Wowza.

Prometheus and Grafana still start automatically with `docker compose up -d`. Visit Grafana at `http://localhost:3001` to see the application dashboard.

## Configuration

### Whisper Configuration

Edit environment variables in `docker-compose.yaml` or `docker-compose-translate.yaml`:

```yaml
environment:
  - MODEL=tiny.en              # Model: tiny.en, base.en, small.en, medium.en, large-v3
  - LOG_LEVEL=INFO             # Logging level
  - MIN_CHUNK_SIZE=1           # Minimum audio chunk size
  - SOURCE_LANGUAGE=en         # Source language
  - REPORT_LANGUAGES=en,fr,es  # Languages for captions
  - TRANSLATE_HOST=libretranslate.server  # Translation service host
  - TRANSLATE_PORT=5000        # Translation service port
```

### Application Configuration

Application-specific settings are in `conf/whisper/Application.xml` or `conf/azure/Application.xml`:

```xml
<Property>
  <Name>whisperCaptionsEnabled</Name>
  <Value>true</Value>
</Property>
<Property>
  <Name>captionHandlerStreamDelay</Name>
  <Value>30000</Value> <!-- Delay in milliseconds -->
</Property>
```

## Usage

### Publishing a Stream

#### Using OBS Studio

Configure your OBS streaming settings:

1. Go to **Settings → Stream**
2. Set the following values:
   - **Server**: `rtmp://localhost:1935/whisper`
   - **Stream Key**: `myStream` (or any name you choose)

#### Using FFmpeg

```bash
ffmpeg -re -i input.mp4 -c:v libx264 -c:a aac -f flv rtmp://localhost:1935/whisper/myStream
```

#### Using Direct RTMP URL

```bash
rtmp://localhost:1935/whisper/myStream
```

### Viewing Captions

Access the HLS stream with WebVTT captions:

```
http://localhost:1935/whisper/myStream_delayed/playlist.m3u8
```

The `_delayed` suffix indicates the stream includes synchronized captions with the configured delay.

## Development

### Local Debugging

For local development and debugging, you can build the plugin from source:

#### Prerequisites for Local Development
- Java 17
- Gradle (included via wrapper)
- Local Wowza Streaming Engine installation

#### Building from Source

1. Point the build to your local Wowza Streaming Engine `lib` directory by either setting the `WSE_LIB_DIR` environment variable or passing `-PwseLibDir=/path/to/lib` when running Gradle. Example (PowerShell):

```powershell
$env:WSE_LIB_DIR="C:\Program Files\Wowza Media Systems\Wowza Streaming Engine 4.9.6+3\lib"
```

2. Build the plugin:

```bash
# Clean build
./gradlew clean build

# Build and copy dependencies
./gradlew copyDeps

# Run tests
./gradlew test
```

The compiled JAR will be located at `build/libs/wse-plugin-caption-handlers-1.1.0.jar`.

### Project Dependencies

- Wowza Streaming Engine SDK

## GPU Support (Optional)

To enable GPU acceleration for Whisper:

1. Uncomment GPU-related sections in `docker-compose.yaml` or `docker-compose-translate.yaml`
2. Ensure NVIDIA Container Toolkit is installed
3. Set environment variables:

```yaml
environment:
  - USE_GPU=True
  - FP16=true
```

## Troubleshooting

### Common Issues

1. **License Key Error**: Ensure `WSE_LICENSE_KEY` is set correctly
2. **Connection Refused**: Check if all services are running with `docker-compose ps`

### Logs

View logs:

```bash
# All services
docker-compose logs -f

# Specific service
docker-compose logs -f wse
docker-compose logs -f whisper_server

# Wowza logs (if persisted)
tail -f wse/logs/wowzastreamingengine_error.log
```

## License

This code is licensed pursuant to the Wowza Public License version 1.0, available at www.wowza.com/legal.

Copyright © 2006 - 2025, Wowza Media Systems, LLC. All rights reserved.

## Contributing

We follow a structured branch naming convention: `Name/type/task`

### Branch Naming Format

```
Name/type/task-description
```

**Examples:**
- `Peter/feature/Update-README.md`
- `John/bugfix/Fix-Caption-Timing`
- `Sarah/docs/Add-API-Documentation`

### Contribution Steps

1. Create a branch following the naming convention:
   ```bash
   git checkout -b YourName/type/task-description
   ```
   Where `type` can be: `feature`, `bugfix`, `docs`, `refactor`, etc.
2. Commit your changes with clear messages
3. Push to your branch:
   ```bash
   git push
   ```
4. Create a Pull Request with a clear description of your changes

## Support

For issues and questions:
- Open an issue on GitHub
- Refer to [Wowza Documentation](https://www.wowza.com/docs)

## Acknowledgments

- Wowza Media Systems for the Streaming Engine
- OpenAI for Whisper
- LibreTranslate for translation services