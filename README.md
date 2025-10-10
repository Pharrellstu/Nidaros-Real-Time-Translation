# Nidaros Real-Time Translation

A Docker-based tool for capturing and processing audio from live streams (Wowza Streaming Engine) for real-time translation.

## Project Structure

- **NidarosRTT.API** - Web API and audio processing service
- **NidarosRTT.Core** - Core application logic and business rules
- **NidarosRTT.Infrastructure** - External service integrations
- **NidarosRTT.Tests** - Test suite for the application
- **AudioCapture** - Docker-based audio capture service with FFmpeg

## Prerequisites

### Required Software

1. **Docker Desktop**
   - **Windows**: Download from [Docker Desktop for Windows](https://www.docker.com/products/docker-desktop/)
   - **Installation**: Run installer as Administrator, restart when prompted
   - **Verification**: Open PowerShell and run `docker --version`

2. **Docker Compose** (included with Docker Desktop)
   - Verify with: `docker-compose --version`

3. **.NET 8.0 SDK** (for development)
   - Download from [Microsoft's website](https://dotnet.microsoft.com/download/dotnet/8.0)

### System Requirements
- **Windows 10/11** with WSL2 enabled
- **Minimum 8GB RAM** (16GB recommended)
- **10GB free disk space** for Docker images and audio files

## Docker Environment Setup

### 1. Install Docker Desktop

```powershell
# Download and install Docker Desktop
# Enable WSL2 integration during installation
# Restart your computer after installation
```

### 2. Verify Docker Installation

```powershell
# Check Docker version
docker --version

# Check Docker Compose version
docker-compose --version

# Test Docker installation
docker run hello-world
```

### 3. Configure Docker for Audio Capture

The project uses Docker with **host networking** to access your Wowza Streaming Engine:

```yaml
# docker-compose.yml configuration
audio-capture:
  build:
    context: ./NidarosRTT.API/Services/AudioCapture
    dockerfile: Dockerfile
  volumes:
    - ./AudioCapture/output:/app/output  # Audio files saved here
  environment:
    - HLS_STREAM_URL=http://172.20.160.1:1935/Test01/_definst_/myStream/playlist.m3u8
  network_mode: "host"  # Direct access to host network
  command: ["--mode", "continuous", "--duration", "5", "--max-segments", "10"]
```

## Getting Started

### 1. Clone and Setup

```powershell
# Clone the repository
git clone [your-repository-url]
cd Nidaros-Real-Time-Translation

# Create output directory for audio files
mkdir AudioCapture\output
```

### 2. Configure Stream URLs

Set your Wowza stream URL as an environment variable:

```powershell
# Set HLS stream URL (replace with your Wowza server IP)
$env:HLS_STREAM_URL="http://172.20.160.1:1935/Test01/_definst_/myStream/playlist.m3u8"

# Optional: Set RTMP URL for other services
$env:RTMP_STREAM_URL="rtmp://172.20.160.1:1935/Test01/_definst_/myStream"
```

### 3. Build and Run

```powershell
# Build the Docker image (first time or after code changes)
docker-compose build --no-cache audio-capture

# Start the audio capture service
docker-compose up audio-capture

# Run in background (detached mode)
docker-compose up -d audio-capture
```

### 4. Verify Audio Capture

Check that WAV files are being created:

```powershell
# List audio files (should update every 5 seconds)
dir AudioCapture\output\*.wav

# Watch files being created in real-time
Get-ChildItem AudioCapture\output\*.wav | Sort-Object LastWriteTime -Descending
```

## Docker Commands Reference

### Container Management

```powershell
# View running containers
docker ps

# View all containers (including stopped)
docker ps -a

# Stop the audio capture service
docker-compose down

# Stop and remove all containers, networks, and volumes
docker-compose down --volumes --remove-orphans

# Restart a specific service
docker-compose restart audio-capture
```

### Image Management

```powershell
# List Docker images
docker images

# Remove unused images and free disk space
docker system prune -f

# Remove specific image
docker rmi nidaros-real-time-translation-audio-capture

# Rebuild from scratch (no cache)
docker-compose build --no-cache audio-capture
```

### Debugging and Logs

```powershell
# View container logs
docker-compose logs audio-capture

# Follow logs in real-time
docker-compose logs -f audio-capture

# Execute commands inside running container
docker exec -it nidaros-real-time-translation-audio-capture-1 bash

# Test FFmpeg directly in container
docker run --rm --network host --entrypoint ffmpeg nidaros-real-time-translation-audio-capture -i "http://172.20.160.1:1935/Test01/_definst_/myStream/playlist.m3u8" -t 5 -vn -acodec pcm_s16le -ar 16000 -ac 1 /tmp/test.wav
```

## Application Features

### Audio Capture Service
- **Continuous Recording**: Captures 5-second audio segments every 5 seconds
- **Rolling Buffer**: Maintains maximum 10 segments (auto-cleanup)
- **WAV Format**: 16kHz mono PCM audio suitable for speech processing
- **Docker Integration**: Fully containerized with FFmpeg

### File Output
- **Location**: `AudioCapture/output/` directory
- **Naming**: `continuous_5s_YYYYMMDD_HHMMSS_segN.wav`
- **Size**: ~156KB per 5-second segment
- **Format**: WAV, 16kHz, mono, PCM 16-bit

## Troubleshooting

### Docker Issues

```powershell
# Docker Desktop not starting
# 1. Restart Docker Desktop as Administrator
# 2. Enable WSL2 integration in Docker settings
# 3. Restart Windows

# Container fails to start
docker-compose down
docker system prune -f
docker-compose build --no-cache audio-capture
docker-compose up audio-capture
```

### Network Connectivity

```powershell
# Test connection to Wowza from Docker
docker run --rm --network host alpine ping -c 3 172.20.160.1

# Test HLS stream accessibility
curl "http://172.20.160.1:1935/Test01/_definst_/myStream/playlist.m3u8"

# Verify Docker host networking
docker run --rm --network host alpine ip addr show
```

### Audio Capture Issues

```powershell
# No WAV files created
# 1. Check Wowza stream is active and accessible
# 2. Verify correct HLS_STREAM_URL environment variable
# 3. Ensure Docker has host network access

# Files created but empty
# 1. Check Wowza stream has audio track
# 2. Verify stream format compatibility
# 3. Check Docker container logs for FFmpeg errors
```

### Common Error Solutions

| Error | Solution |
|-------|----------|
| `No route to host` | Use `network_mode: "host"` in docker-compose.yml |
| `HTTPS connection failed` | Ensure HLS_STREAM_URL uses `http://` not `https://` |
| `Stream not found` | Verify Wowza stream name and application path |
| `Permission denied` | Run Docker Desktop as Administrator |
| `Port already in use` | Stop conflicting services or change ports |

## Environment Variables

```bash
# Required
HLS_STREAM_URL=http://172.20.160.1:1935/Test01/_definst_/myStream/playlist.m3u8

# Optional
RTMP_STREAM_URL=rtmp://172.20.160.1:1935/Test01/_definst_/myStream
ASPNETCORE_ENVIRONMENT=Development
```

## Development Setup

For local development without Docker:

```powershell
# Install .NET 8.0 SDK
# Install FFmpeg for Windows

# Restore packages
dotnet restore

# Run audio capture service locally
cd NidarosRTT.API/Services/AudioCapture
dotnet run -- --mode continuous --duration 5 --max-segments 10
```

## Performance Optimization

### Docker Settings
- **Memory**: Allocate at least 4GB to Docker Desktop
- **CPU**: Use at least 2 CPU cores
- **Disk**: Enable fast SSD storage for Docker data

### Audio Processing
- **Segment Duration**: 5 seconds (optimal for real-time processing)
- **Sample Rate**: 16kHz (sufficient for speech recognition)
- **Format**: Mono WAV (reduces file size and processing time)

## Security Considerations

- **Network Access**: Host networking mode gives container full network access
- **File Permissions**: Audio files are created with host user permissions
- **Stream Security**: Use HTTPS streams when available for production
