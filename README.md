# Nidaros Real-Time Translation

This is a tool for capturing audio from HLS streams and converting it to text using Whisper.

## What it does

- Captures audio from your stream in 5 second chunks
- Uses Whisper AI to make text from the audio
- Everything runs in Docker containers
- Old audio files get deleted automatically

## You need

- Docker Desktop (get it from docker.com)
- URL to your HLS stream

## How to start (minimum steps)

### 1. Make .env file

Create `.env` file in project folder:
```bash
HLS_STREAM_URL=http://your-stream-url-here/playlist.m3u8
RTMP_STREAM_URL=rtmp://your-stream-url-here/stream
```

### 2. Start everything

```powershell
cd d:\projects\Nidaros-Real-Time-Translation
docker-compose up --build -d
```

### 3. See the transcriptions

```powershell
docker-compose logs -f whisper
```

Done. You will see text appearing in console.

## What happens

1. audio-capture gets audio from your stream (5 second pieces)
2. whisper makes text from the audio
3. Text shows in console and audio file gets deleted

## Other commands

### See running containers
```powershell
docker ps
```

### Check audio capture logs
```powershell
docker-compose logs -f audio-capture
```

### Stop everything
```powershell
docker-compose down
```

### Restart after you change something
```powershell
docker-compose down
docker-compose up --build -d
```

### Delete everything including files
```powershell
docker-compose down -v
Remove-Item AudioCapture\output\* -Recurse -Force
```

## Where to see output

- Live transcriptions: `docker-compose logs -f whisper`

## Folder structure

```
Nidaros-Real-Time-Translation/
├── NidarosRTT.API/Services/
│   ├── AudioCapture/            # gets audio from stream
│   └── whisper-service/         # makes text from audio
├── AudioCapture/output/         # audio files go here (deleted after)
├── docker-compose.yml           # docker config
└── .env                         # settings
```

## Technical stuff

### The 2 services

1. Audio Capture
   - gets audio in 5 second chunks
   - saves as WAV files (16kHz mono)
   - keeps max 10 files then deletes old ones

2. Whisper Service
   - uses whisper base model (english)
   - makes text from WAV files
   - prints to console
   - deletes the audio file after

### How it works

```
Stream -> Get Audio (5sec) -> WAV File -> Whisper -> Text -> Console
                                             |
                                          Delete File
```

## If something broke

### No text showing up
```powershell
# check containers running
docker ps

# check audio capture logs
docker-compose logs -f audio-capture

# check if audio files exist
dir AudioCapture\output\*.wav
```

### Stream not working
- check if your stream URL works
- make sure stream is running and has audio
- check firewall maybe

### Docker problems
```powershell
# restart everything
docker-compose down
docker-compose up --build -d

# if still broken, clean everything and try again
docker-compose down -v
docker system prune -f
docker-compose up --build -d
```
