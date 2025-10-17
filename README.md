# NidarosRTT – Real-Time Subtitles Demo

End-to-end demo that ingests an RTMP stream from OBS into Wowza, extracts audio chunks with FFmpeg, transcribes via a Wyoming faster-whisper server, and pushes subtitles to the browser over SignalR.

## Structure
- `NidarosRTT.API` – ASP.NET Core entry point (serves a small UI and SignalR hub `/subtitlesHub`).
- `NidarosRTT.Core` – Core types and logic.
- `NidarosRTT.Infrastructure` – Wowza audio capture, transcription client, and processing queue.
- `NidarosRTT.Tests` – Unit tests.
- `docker-compose.yml` – Wowza Streaming Engine (trial), Wowza Manager, API, and faster-whisper services.

Key ports (host):
- API/UI: http://localhost:5032
- SignalR Hub: http://localhost:5032/subtitlesHub
- Wowza RTMP ingest: rtmp://localhost:1935/live
- Wowza HLS playback: http://localhost:1935/live/OBSstream/playlist.m3u8
- Wowza Manager UI: http://localhost:8088
- faster-whisper (Wyoming protocol): tcp://localhost:10300

## Prerequisites
- Docker and Docker Compose
- OBS Studio (or another RTMP encoder)

## 1) Configure environment variables
This project uses a `.env` file for Wowza credentials used by `docker-compose.yml`.

1. Copy the example file and edit your values:

```fish
cp .env.example .env
```

Windows (PowerShell):

```powershell
Copy-Item .env.example .env
```

2. Set these variables in `.env`:

- `WSE_LICENSE_KEY` – Wowza Streaming Engine trial license key
- `ADMIN_USERNAME` – Wowza admin username
- `ADMIN_PASSWORD` – Wowza admin password

The API and faster-whisper containers have sensible defaults already defined in `docker-compose.yml`. You can override if needed:

- API
	- `STREAM_URL` (default: `rtsp://wse.docker:1935/live/OBSstream`)
	- `ASPNETCORE_URLS` (default: `http://+:5032`)
	- `FFMPEG_PATH` (default in container: `/usr/bin/ffmpeg`)
	- `WYOMING_HOST` (default: `faster-whisper` inside the compose network)
	- `WYOMING_PORT` (default: `10300`)
- faster-whisper
	- `WHISPER_MODEL` (e.g., `base`, `small`, `large-v3`)
	- `WHISPER_LANG` (e.g., `nl`)
	- `WHISPER_BEAM` (optional beam size)

## 2) Start the stack
Bring up Wowza, faster-whisper, and the API using Docker Compose:

```fish
docker compose up -d
```

Verify containers are healthy:

```fish
docker compose ps
```

## 3) Configure OBS and start streaming
In OBS Studio:

1. Settings → Stream
2. Service: Custom
3. Server: `rtmp://localhost:1935/live`
4. Stream Key: `OBSstream`
5. Start Streaming

This publishes a stream named `OBSstream` to Wowza.

## 4) Open the demo UI and subtitles
Open the UI served by the API:

- http://localhost:5032/index.html

Notes:
- The demo page (`NidarosRTT.API/wwwroot/index.html`) contains a `<source>` with a sample Wowza URL. You’ll likely want to change it to your local Wowza HLS URL:
	- `http://localhost:1935/live/OBSstream/playlist.m3u8`
- The SignalR hub is available at `/subtitlesHub` on port `5032`.

When OBS is streaming, the API will:
- Capture 5s audio chunks from `STREAM_URL` using FFmpeg
- Send PCM audio to the faster-whisper Wyoming server
- Broadcast recognized text to all connected browsers as live subtitles

## 5) Optional: Wowza Manager
You can inspect and manage Wowza via the Manager UI:

- URL: http://localhost:8088
- Login: the `ADMIN_USERNAME` and `ADMIN_PASSWORD` you set in `.env`

From Manager you can verify the live application, incoming stream, and playback URLs.

## Local development (without Docker)
If you prefer to run the API locally:

```fish
dotnet restore
dotnet run --project NidarosRTT.API
```

Then open http://localhost:5032.

## Troubleshooting
- No subtitles?
	- Confirm OBS is streaming to `rtmp://localhost:1935/live` with key `OBSstream`.
	- Check the API logs for FFmpeg errors (container `api-service`).
	- Ensure faster-whisper is reachable on `10300` and the model is loaded.
	- Verify `STREAM_URL` is correct and reachable from the API container. Defaults to `rtsp://wse.docker:1935/live/OBSstream` inside the compose network.
- Video doesn’t play in the browser?
	- Ensure the `<source>` in `wwwroot/index.html` points to the local Wowza HLS URL.
	- Some browsers block mixed content; keep everything on HTTP locally or configure HTTPS.
- Permissions/Ports
	- Ensure ports 5032, 1935, 8088, and 10300 are not occupied by other services.

## Notes
- GPU acceleration for faster-whisper is supported by the image; see the commented `runtime/deploy` section in `docker-compose.yml` and enable NVIDIA runtime on your host.
- Paths and URLs are case-insensitive in ASP.NET routing, but we recommend using `/subtitlesHub` (lowercase) to match the mapping in code.
