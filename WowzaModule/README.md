# Nidaros Wowza Audio Capture Module

This Java module integrates with Wowza Streaming Engine to capture audio from RTMP streams, send it to the .NET API for transcription, and inject WebVTT captions back into the HLS stream.

## Features

- **INRT-600**: Lifecycle logging for streams
- **INRT-601**: Real-time audio packet capture from RTMP
- **INRT-602**: 2-second audio chunk buffering
- **INRT-603**: WebVTT caption injection via onTextData
- **INRT-604**: Comprehensive latency measurement
- **INRT-605**: Graceful degradation on errors

## Building

### Prerequisites
- Java 11 or higher
- Gradle 7.x+
- Wowza Streaming Engine (for SDK JARs)

### Build Commands

```bash
# Build the JAR
gradle clean build

# JAR will automatically be copied to ../wowza/lib/
```

## Deployment

The module is deployed via Docker volume mount:

```yaml
volumes:
  - ./wowza/lib:/usr/local/WowzaStreamingEngine/lib
```

## Configuration

Set environment variable in docker-compose.yml:
```yaml
environment:
  - DOTNET_API_URL=http://api-service:5032
```

## How It Works

1. **Audio Capture**: Listens to RTMP stream packets, filters for audio
2. **Buffering**: Accumulates 2 seconds of audio, creates WAV file
3. **HTTP POST**: Sends WAV to .NET API `/api/audio/process`
4. **Caption Polling**: Polls `/api/captions/webvtt` every 500ms
5. **Injection**: Injects captions as `onTextData` events into stream

## Logging

All operations are logged with timestamps for latency measurement:
- `[NIDAROS]` - Module lifecycle
- `[AUDIO-LISTENER]` - Stream attachment
- `[PACKET-LISTENER]` - Packet capture
- `[ACCUMULATOR]` - Audio buffering
- `[HTTP-SENDER]` - API communication
- `[CAPTION-INJECTOR]` - Caption injection
