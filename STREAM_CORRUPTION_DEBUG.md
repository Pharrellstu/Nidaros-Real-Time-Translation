# Stream Corruption Debug Notes

## Problem
The delayed stream (`OBSstream_delayed`) shows corruption errors:
```
[h264 @ 0x...] Invalid NAL unit size (23330847 > ...)
[h264 @ 0x...] missing picture in access unit with size ...
```

## Investigation

### Symptoms
1. Original stream (`OBSstream`) works perfectly
2. Delayed stream shows constant "Invalid NAL unit size" errors
3. The magic number `23330847` (0x01644E1F) appears repeatedly
4. Corruption happens **even without caption injection**

### Data Flow Analysis

**What we're doing:**
```
StreamPuller (FFmpeg) 
  ↓ reads FLV from RTMP
StreamBuffer (stores packets)
  ↓ provides MediaPacket { Data = FLV tag payload }
StreamPublisher (writes FLV)
  ↓ manually constructs FLV tags
  ↓ pipes to FFmpeg stdin
FFmpeg (-c copy)
  ↓ converts FLV → RTMP
Wowza
```

### Debug Output (Video Packet #1)
```
Data size: 16092 bytes
First 20 bytes: 17-01-00-00-43-00-00-00-1C-67-64-00-1F-AC-D9-40-50-05-BB-01
  Frame type: 0x1 (keyframe)
  Codec ID: 0x7 (AVC/H.264)
  AVC packet type: 0x1 (NALU)
```

**Byte breakdown:**
- `17` = Frame type (1) + Codec ID (7)
- `01` = AVC packet type (NALU)
- `00-00-43` = Composition time offset (67ms)
- `00-00-00-1C` = AVCC NAL length (28 bytes) ← **CORRECT!**
- `67` = NAL type 7 (SPS)
- `64-00-1F` = SPS data starts...

### The Smoking Gun
The bytes `01-64-4E-1F` when misread would give `0x01644E1F` = 23330847!

But looking at the actual data: `17-01-00-00-43-00-00-00-1C-67-64-00-1F...`

If FFmpeg reads from position 5 and treats it as a 4-byte big-endian length:
- Position 5-8: `00-00-00-1C` = 28 ← **CORRECT**

But if it reads from position 6:
- Position 6-9: `00-00-1C-67` = 7271 ← Wrong but not our magic number

If it reads from position 7:
- Position 7-10: `00-1C-67-64` = 1861476 ← Still not it

If it reads from a shifted position:
- Some offset: `01-64-4E-1F` = 23330847 ← **THERE IT IS!**

### Root Cause Theory

**Hypothesis 1**: FFmpeg's FLV→RTMP with `-c copy` doesn't handle piped FLV correctly
- The FLV tag structure we're creating is correct
- But FFmpeg might be buffering incorrectly or missing bytes
- This causes it to read NAL lengths from wrong offsets

**Hypothesis 2**: We have a byte alignment issue
- Maybe we're not flushing at the right time?
- Maybe there's a race condition in writing header/data/prevtagsize?

**Hypothesis 3**: AVCC vs Annex B confusion
- Input uses AVCC (length-prefixed)
- Output needs Annex B (start codes)?
- But this doesn't explain why OBS→Wowza direct works

### Potential Solutions

1. **Use FFmpeg's FLV muxer properly** instead of manual construction:
   ```
   ffmpeg -f h264 -i pipe:0 -f aac -i pipe:1 -c copy -f flv -
   ```

2. **Write to a file then stream it** (confirm our FLV is valid):
   ```csharp
   // Write to temp FLV file
   await File.WriteAllBytesAsync("/tmp/test.flv", flvData);
   // Then use FFmpeg to stream it
   ffmpeg -re -i /tmp/test.flv -c copy -f flv rtmp://...
   ```

3. **Use librtmp or FlvLib directly** instead of piping through FFmpeg

4. **Don't reconstruct FLV - use raw stream copy**:
   Instead of: FLV → MediaPacket → FLV
   Do: FLV → passthrough → FLV

## Next Steps

1. **Test if our generated FLV is valid** by writing to file and playing it
2. **Compare byte-by-byte** our FLV output vs OBS's FLV output  
3. **Try different FFmpeg command** without stdin piping
4. **Consider using FFmpeg's concat protocol** or FLV file output

## Files to Check
- `StreamPublisher.cs` - WritePacketAsync() method
- `FlvParser.cs` - Tag reconstruction
- FFmpeg command line in StreamPublisher
