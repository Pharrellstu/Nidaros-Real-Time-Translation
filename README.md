# Nidaros Real-Time Translation

A tool for capturing and processing audio from live streams for real-time translation.

## Project Structure

- **NidarosRTT.API** - Web API and audio processing service
- **NidarosRTT.Core** - Core application logic and business rules
- **NidarosRTT.Infrastructure** - External service integrations
- **NidarosRTT.Tests** - Test suite for the application

## Prerequisites

1. **Docker**
   - Required for containerized deployment
   - Download from [Docker's website](https://www.docker.com/products/docker-desktop/)

2. **.NET 9.0 SDK**
   - Required for development
   - Download from [Microsoft's website](https://dotnet.microsoft.com/download)

## Getting Started

1. Clone the repository:
   ```bash
   git clone [your-repository-url]
   cd Nidaros-Real-Time-Translation
   ```

2. Configure the application:
   - Copy the example configuration file:
     ```bash
     copy .env.example .env
     ```
   - Edit `.env` and add your stream URLs:
     ```
     RTMP_STREAM_URL=rtmp://your-stream-url
     HLS_STREAM_URL=http://your-hls-url/playlist.m3u8
     ```

3. Start the audio capture service:
   ```bash
   docker-compose up --build
   ```
   This will start capturing audio from the RTMP stream and save it as HLS segments.

4. Open the HLS player:
   - Navigate to `NidarosRTT.API/wwwroot/hls_player.html` in your file explorer
   - Double-click to open it directly in your browser
   - The player will automatically connect to the HLS stream

## Application Features

- Automatic stream playback on page load
- Audio capture in 5-second segments
- Maintains a rolling buffer of the 5 most recent segments in the `output` directory

## Troubleshooting

### Stream Connection Issues
- Verify the stream URLs in `.env` are correct
- Ensure the source stream is active and accessible
- Check Docker logs for connection errors

### Docker Container Issues
- Restart the containers:
  ```bash
  docker-compose down
  docker-compose up --build
  ```
- Check container status: `docker ps`
- View container logs: `docker logs [container_id]`

## Development Setup

To set up a development environment:

1. Install .NET 9.0 SDK
2. Run `dotnet restore`
3. Start the API: `dotnet run --project NidarosRTT.API`

## Configuration

Environment variables:
- `RTMP_STREAM_URL`: RTMP stream source URL
- `HLS_STREAM_URL`: HLS output stream URL
- `ASPNETCORE_ENVIRONMENT`: Set to "Development" for development mode
