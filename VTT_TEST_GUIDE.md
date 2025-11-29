# VTT Generation Test Guide

## What Was Added

The following components have been integrated into the existing pipeline:

### New Services
1. **VttWriter** - Writes WebVTT format subtitle files
2. **CaptionFormatter** - Formats text with proper line breaks (max 37 chars, 2 lines)
3. **Caption Model** - Data structure for subtitle timing and text
4. **TranscriptionResult** - Extended to include `start_seconds` and `end_seconds` from Whisper

### Modified Files
- `Program.cs` - Added VTT writing alongside SignalR broadcast
- `docker-compose.yml` - Added `VTT_OUTPUT_PATH` and `STREAM_NAME` environment variables
- `FasterWhisperService/app/run.py` - Returns segment timing data
- `WhisperService.cs` - Parses timing data from Whisper responses

## Testing Steps

### 1. Build and Start Services

```fish
cd /home/pop/Documents/gdrive/nhlStenden/Y2-P2/Project/Nidaros-Real-Time-Translation
docker-compose up --build
```

### 2. Check VTT Writer Initialization

In the `api-service` logs, you should see:
```
[VTT WRITER] Initialized VTT file for stream: OBSstream
[VTT WRITER] Output path: /app/vtt_output/OBSstream.vtt
```

### 3. Start OBS Streaming

Configure OBS as per documentation:
- **Settings → Stream → Custom**
- **URL**: `rtmp://localhost:1935/live`
- **Stream Key**: `OBSstream`
- **Output → Keyframe Interval**: 2 seconds

Start streaming with audio/speech.

### 4. Monitor VTT Generation

Watch for cyan-colored output in `api-service` logs:
```
[VTT-1] 00:00:04.500 → 00:00:06.000: Hello world, this is a test.
[VTT-2] 00:00:06.000 → 00:00:08.500: How are you doing today?
```

### 5. Verify VTT File Contents

**Inside Docker Container:**
```fish
docker exec -it nidaros-real-time-translation-api-service-1 cat /app/vtt_output/OBSstream.vtt
```

**Expected Format:**
```
WEBVTT

00:00:04.500 --> 00:00:06.000
Hello world, this is a test.

00:00:06.000 --> 00:00:08.500
How are you doing today?
```

### 6. Copy VTT File to Host (Optional)

```fish
docker cp nidaros-real-time-translation-api-service-1:/app/vtt_output/OBSstream.vtt ./test_output.vtt
```

Then open with a text editor to verify formatting.

## Expected Behavior

### VTT File Features
✅ **WEBVTT header** at the top
✅ **Timestamps** in `HH:MM:SS.mmm` format
✅ **Line breaking** at punctuation when possible
✅ **Max 37 characters** per line
✅ **Max 2 lines** per caption block
✅ **Empty line** between caption blocks

### Console Output
You should see both:
1. **Green text**: `[TRANSCRIPTION-X]` - Shows transcribed text
2. **Cyan text**: `[VTT-X]` - Shows VTT caption timing and text
3. **Existing SignalR**: Still broadcasts to browser (legacy path)

## Troubleshooting

### No VTT file created
- Check if `/app/vtt_output` directory exists in container
- Verify `VTT_OUTPUT_PATH` environment variable is set
- Check for initialization message in logs

### Empty VTT file
- Verify Whisper service is returning `start_seconds` and `end_seconds`
- Check for `[WHISPER RESPONSE]` logs showing timing data
- Ensure audio chunks have speech content

### Incorrect timing
- Whisper returns timing relative to audio chunk start (usually 0-5 seconds)
- For proper stream timing, we'll need the StreamBuffer service (next phase)

### Line breaking issues
- Check if text has punctuation (., ?, !, ,, ;)
- CaptionFormatter breaks at punctuation when possible
- Lines exceeding 37 chars will be split at word boundaries

## What's Working vs. What's Missing

### ✅ Currently Working
- Audio capture from RTSP stream
- Whisper transcription with timing
- Translation (when service available)
- VTT file generation with proper formatting
- Line breaking and caption formatting
- Both SignalR (legacy) and VTT output work in parallel

### ⚠️ Limitations (To Be Fixed Next)
- **No 30-second stream buffering** - Captions appear in real-time, not delayed
- **No stream re-ingestion** - Original stream not republished to Wowza
- **Timing relative to chunk** - Not synchronized to full stream timeline
- **No shared volume with Wowza yet** - VTT file not accessible to Wowza
- **Console.WriteLine debugging** - Should be replaced with proper logging

## Next Steps

After verifying VTT generation works:

1. **Add shared volume** to `docker-compose.yml` for Wowza access
2. **Build StreamBuffer** for 30-second delay and proper timing
3. **Build StreamPublisher** to re-ingest delayed stream
4. **Remove SignalR** legacy code once VTT approach is confirmed working
5. **Add structured logging** to replace Console.WriteLine

## Success Criteria

You can consider this phase successful if:
- [ ] VTT file is created at `/app/vtt_output/OBSstream.vtt`
- [ ] File contains `WEBVTT` header
- [ ] Captions appear with proper timing format
- [ ] Text is broken into lines (max 37 chars)
- [ ] Captions are separated by empty lines
- [ ] Console shows cyan `[VTT-X]` messages
- [ ] Existing SignalR browser display still works
