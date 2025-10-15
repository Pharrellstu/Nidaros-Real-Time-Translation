# NidarosRTT Console (Wyoming describe + transcribe)

Minimal .NET 9 console app that connects to a Wyoming server (faster-whisper) and:
- `describe`: prints the `info` JSON
- `transcribe <pcm>`: streams a 16kHz mono s16le PCM file and prints the transcript
- `live <hls-url> [segmentSeconds]`: pulls live audio via ffmpeg, segments, transcribes, prints transcripts

Default host/port: 127.0.0.1:10300
- Override with env `WYOMING_HOST`, `WYOMING_PORT` or args at the end of commands.

## Run

Ensure the `faster-whisper` container is running and listening on port 10300.

Describe:

```
dotnet run --project NidarosRTT
```

Transcribe a PCM file (16kHz mono s16le):

```
# Example: convert to PCM first
ffmpeg -hide_banner -y -i testMaterial/example.webm -t 5 -ac 1 -ar 16000 -f s16le testMaterial/snippet.pcm

# Then run
dotnet run --project NidarosRTT -- transcribe testMaterial/snippet.pcm
```

Live from Wowza HLS (example Wowza URL in docker-compose):

```
dotnet run --project NidarosRTT -- live http://localhost:1935/live/testStream/playlist.m3u8 8
```

- The second argument is optional segment length in seconds (default 8). Each segment is sent as a separate transcription request; final transcript per segment is printed as it completes.
- Stop with Ctrl+C.
