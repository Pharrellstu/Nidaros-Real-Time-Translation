# Nidaros Real-Time Translation

# NidarosRTT

A new .NET Web API project initialized with proper solution structure.

## Structure
- **NidarosRTT.API** – Entry point for real-time translation via REST or WebSocket (SignalR).
- **NidarosRTT.Core** – Business logic and data models.
- **NidarosRTT.Infrastructure** – External integrations (speech-to-text, translation APIs).
- **NidarosRTT.Tests** – Unit tests for core logic.

## You need

- Docker Desktop (get it from docker.com)
- URL to your HLS stream

## How to start (minimum steps)

### 1. Set up Wowza
Make sure you are not running Wowza on your computer, if you are stop it following these instructions:
https://www.wowza.com/docs/how-to-start-and-stop-wowza-streaming-engine-software

Log in to Wowza on localost:8088
Create a new Application, select LIVE single server or origin
Name it wowzaApp
In Setup, in Playback Types, tick Apple HLS and Adobe RTMP
In source security access the  Source Authentication page via the link in the description and over there add a source name and source password
Click Test Playback..., fill in a Stream name with CPHTD and copy the RTMP and HLS URLs
Paste them in the .env file
Paste the RTMP url (should look like rtmp://wse-trial.wowza.com:1935/wowzaApp) in OBS and make sure it you have followed the instructions given by the client and that the stream key is CPHTD, Use authentication is ticked and the username and password from the Source Authentication page are filled in

Add the HLS link to NidarosRTT.API\wwwroot\hls_player.html

Access the player at http://localhost:5000/hls_player.html to check that the stream works

### 2. Make .env file

Create `.env` file in the root folder by copying the .env.example and filling in all of the constants

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
