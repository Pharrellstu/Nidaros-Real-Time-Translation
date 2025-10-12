# Nidaros-Real-Time-Translation

Backend scaffold for live subtitle generation integrated with a Wowza Streaming Engine setup. Current scope (M0) provides the solution structure and a minimal API you can run locally. Speech-to-text and packaging will be added next.

## Prerequisites
- .NET SDK 9.x
- Optional for streaming: Docker + Docker Compose (to run the Wowza trial containers)

## Project structure
- `NidarosRTT.API` – Minimal Web API (Swagger in Development), health and info endpoints.
- `NidarosRTT.Core` – Domain models and core abstractions.
- `NidarosRTT.Infrastructure` – Integrations (to be implemented: ffmpeg, Vosk, packagers).
- `NidarosRTT.Tests` – xUnit tests.

## Quick start (API)
1) Restore and build
	- dotnet restore
	- dotnet build
2) Run the API
	- dotnet run --project NidarosRTT.API
3) Try endpoints
	- Health: GET http://localhost:5000/health
	- Info: GET http://localhost:5000/api/info
	- Swagger (Development): http://localhost:5000/swagger

Note: Default ports above assume Kestrel development defaults; your port may differ. The terminal output will show the bound URLs.

## Tests
- Run all tests
  - dotnet test

## Wowza (optional, for stream input)
1) Create a `.env` file based on `.env.example` and set values:
	- `WSE_LICENSE_KEY`
	- `ADMIN_USERNAME`
	- `ADMIN_PASSWORD`
2) Start Wowza containers
	- docker compose up -d
3) Ports (from `docker-compose.yaml`)
	- Manager UI: host port 8088 → container 8080
	- Engine REST: host port 8087
	- RTMP: 1935, HTTP(S): 80/443

At this stage (M0), the API doesn’t ingest or process streams yet. Next milestones will add real-time transcription (Vosk) and WebVTT/HLS subtitle packaging.

## Development notes
- Target framework: net9.0
- Minimal endpoints are provided for smoke testing.
- See `TODO.txt` for the milestone plan and current progress.
