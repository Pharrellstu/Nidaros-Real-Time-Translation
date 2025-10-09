# Audio Capture Tool

Quick audio grabber for live streams. Pulls 5-second chunks from RTMP for transcription. Works great!

## What This Does

✅ **Just works** - captures audio segments reliably
✅ **RTMP input** - direct from Wowza (HLS was problematic)
✅ **Multiple modes** - continuous, single, or test
✅ **Auto-cleanup** - keeps latest files only
✅ **Zero setup** - runs in Docker container

## Quick Start

### Run Audio Capture
```bash
# Continuous recording (what you need!)
docker run -v "$(pwd)/AudioCapture/output:/app/output" src-audio-capture --mode continuous --duration 5 --max-segments 10

# Single segments
docker run -v "$(pwd)/AudioCapture/output:/app/output" src-audio-capture --mode single --duration 10 --count 3

# Test mode
docker run -v "$(pwd)/AudioCapture/output:/app/output" src-audio-capture --mode test
```

| Flag | What it does | Example |
|------|-------------|---------|
| `--mode` | `continuous`, `single`, `test` | `--mode continuous` |
| `--duration` | How long each segment | `--duration 5` |
| `--count` | How many segments | `--count 3` |
| `--max-segments` | Keep latest N files | `--max-segments 10` |

## Your Working Setup

**Easy setup with .env file:**
```bash
# Copy and edit the .env file with your stream URLs
cp .env.example .env  # (if you make one)

# Or just edit .env directly:
RTMP_STREAM_URL=rtmp://YOUR_IP:1935/YOUR_APP/_definst_/YOUR_STREAM
HLS_STREAM_URL=http://YOUR_IP:1935/YOUR_APP/_definst_/YOUR_STREAM/playlist.m3u8
```

**Current working URLs:**
- **RTMP:** `rtmp://100.110.10.66:1935/Test01/_definst_/CPHTD` 
- **HLS:** `http://100.110.10.66:1935/Test01/_definst_/CPHTD/playlist.m3u8` (for web player)

## File Output

- `continuous_5s_YYYYMMDD_HHMMSS_seg1.wav` - Latest segments
- Auto-deletes old files when too many
- Mounts to `./AudioCapture/output/` on your machine

## Troubleshooting

### "FFmpeg failed"
- Stream not running? Check OBS → Wowza
- RTMP URL wrong? Test: `rtmp://100.110.10.66:1935/Test01/_definst_/CPHTD`

### "Permission denied"
- Windows folder permissions - run as admin or fix folder permissions

### "No audio in files"
- Check OBS audio levels while streaming
- Verify Wowza is receiving audio

## Technical Stuff

**FFmpeg setup:**
```csharp
FFMpegArguments
    .FromUrlInput(new Uri("rtmp://100.110.10.66:1935/Test01/_definst_/CPHTD"))
    .OutputToFile(file, options => options
        .WithDuration(TimeSpan.FromSeconds(5))
        .WithAudioCodec("pcm_s16le")
        .WithAudioSamplingRate(16000)
        .WithCustomArgument("-ac 1"))
```

**Container:** .NET 8 + FFmpeg, runs everything inside Docker

## Docker Commands

```bash
# Run your working capture
docker run -v "$(pwd)/AudioCapture/output:/app/output" src-audio-capture --mode continuous --duration 5 --max-segments 10

# Build if needed
docker-compose build

# Check logs
docker-compose logs

# Stop everything
docker-compose down
```

---
*Works with RTMP, HLS had duplicates. Use RTMP for reliable capture!*
