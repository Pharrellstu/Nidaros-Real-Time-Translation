# Testing Guide: Resilient Subtitle Reconnection (INRT-74)

## Overview

This guide helps you test the automatic reconnection feature for subtitle streaming. The implementation ensures that subtitle connections recover automatically from network interruptions without requiring manual restarts.

**Branch**: `Iustin-Resilient-Reconnects-Hiccups`  
**JIRA Ticket**: INRT-48 / INRT-74

---

## What Was Implemented

### Problem Solved
Previously, if the network connection between Wowza and the Whisper server was interrupted, subtitles would stop working permanently until the entire system was restarted manually.

### Solution Delivered
1. **Automatic Reconnection** - Backend detects connection loss and automatically reconnects (max 5 retry attempts with exponential backoff)
2. **Caption Deduplication** - Prevents duplicate captions from appearing after reconnection
3. **Visual Status Indicator** - Frontend shows connection health with colored indicator
4. **Memory Leak Prevention** - Automatic cleanup prevents memory buildup over time
5. **Stream Health Monitoring** - Frontend tracks stream health without false warnings

---

## Prerequisites

Before testing, ensure you have:
- ✅ Docker Desktop running
- ✅ OBS Studio installed (for streaming)
- ✅ Modern web browser (Chrome, Edge, Firefox, Safari)
- ✅ Access to PowerShell terminal

---

## Setup Instructions

### 1. Start the System

```powershell
# Navigate to project directory
cd "d:\Universitate\Project 2.1 Nidaros\Nidaros-Real-Time-Translation"

# Ensure you're on the correct branch
git branch --show-current
# Should show: Iustin-Resilient-Reconnects-Hiccups

# Start Docker containers
docker-compose up -d

# Wait 30 seconds for services to initialize
Start-Sleep -Seconds 30
```

### 2. Verify Containers Are Running

```powershell
docker ps
```

You should see these containers running:
- `wowzastreamingenginecaptionhandlers-wse-1` (Wowza)
- `wowzastreamingenginecaptionhandlers-whisper_server-1` (Whisper)
- `wowzastreamingenginecaptionhandlers-manager-1` (Manager)
- `webplayer` (Web Player)

### 3. Configure OBS Studio

Open OBS Studio and configure streaming:

**Settings → Stream:**
- Service: Custom
- Server: `rtmp://localhost:1935/whisper`
- Stream Key: `myStream`

**Settings → Output:**
- Output Mode: Advanced
- Encoder: Hardware (NVENC) or Software (x264)
- Bitrate: 2500-6000 Kbps
- Keyframe Interval: 2 seconds

**Audio Source:**
- Add any audio source (microphone, desktop audio, or media file)
- Audio must be active for captions to generate

### 4. Start Streaming

1. In OBS, click **"Start Streaming"**
2. Wait 5-10 seconds for stream to stabilize
3. You should see "Live" indicator in OBS

### 5. Open Web Player

Open your browser and navigate to:
```
http://localhost:8000
```

**What You Should See:**
- Video player with your OBS stream
- Subtitles appearing at the bottom (if audio is active)
- Green indicator (top-right) showing **"Captions active"**
- Status message: "✅ Stream loaded with 1 caption track(s). Click the CC button to enable."

---

## Testing Procedures

### Test 1: Normal Operation ✅
**Purpose**: Verify everything works without interruptions

**Steps:**
1. Stream audio from OBS (speak or play audio)
2. Watch captions appear in the web player
3. Toggle captions ON/OFF using CC button

**Expected Results:**
- ✅ Captions appear within 2-3 seconds of audio
- ✅ Green status indicator: "Captions active"
- ✅ No errors in browser console (press F12)
- ✅ Captions match spoken audio

**Success Criteria:**
- Captions display correctly
- No console errors or warnings

---

### Test 2: Short Network Interruption (< 5 seconds) ✅
**Purpose**: Verify automatic reconnection works quickly

**Steps:**
1. Ensure OBS is streaming and captions are visible
2. Open PowerShell and run:
   ```powershell
   # Disconnect Whisper server
   docker network disconnect wowzastreamingenginecaptionhandlers_default wowzastreamingenginecaptionhandlers-whisper_server-1
   
   # Wait 2 seconds
   Start-Sleep -Seconds 2
   
   # Reconnect
   docker network connect wowzastreamingenginecaptionhandlers_default wowzastreamingenginecaptionhandlers-whisper_server-1
   ```

**Expected Results:**
- ✅ During disconnect: No immediate change (captions may continue for ~5s due to buffering)
- ✅ After reconnect: Captions resume within 5 seconds
- ✅ Status indicator stays green or briefly shows orange "Reconnecting..."
- ✅ No duplicate captions appear

**Check Backend Logs:**
```powershell
docker logs wowzastreamingenginecaptionhandlers-wse-1 --tail 30
```
You should see:
```
WhisperSpeechToTextHandler.Socket.reconnect: Attempting to reconnect...
WhisperSpeechToTextHandler.Socket.reconnect: ✓ Successfully reconnected to whisper.server:3000
```

**Success Criteria:**
- Connection restored automatically
- Captions resume without manual intervention
- No duplicate captions

---

### Test 3: Extended Network Outage (10-30 seconds) ✅
**Purpose**: Verify exponential backoff retry mechanism

**Steps:**
1. Ensure OBS is streaming and captions are visible
2. Run:
   ```powershell
   # Disconnect
   docker network disconnect wowzastreamingenginecaptionhandlers_default wowzastreamingenginecaptionhandlers-whisper_server-1
   
   # Wait 15 seconds
   Start-Sleep -Seconds 15
   
   # Reconnect
   docker network connect wowzastreamingenginecaptionhandlers_default wowzastreamingenginecaptionhandlers-whisper_server-1
   ```

**Expected Results:**
- ✅ Backend retries connection multiple times (visible in logs)
- ✅ After reconnect: Captions resume within 10 seconds
- ✅ Status indicator may show orange "Reconnecting..." during outage
- ✅ Returns to green "Captions active" after recovery

**Check Retry Attempts in Logs:**
```powershell
docker logs wowzastreamingenginecaptionhandlers-wse-1 --tail 50
```
You should see multiple retry attempts:
```
WhisperSpeechToTextHandler.Socket.reconnect: Attempting to reconnect (attempt 1/5)...
WhisperSpeechToTextHandler.Socket.reconnect: Attempting to reconnect (attempt 2/5)...
...
WhisperSpeechToTextHandler.Socket.reconnect: ✓ Successfully reconnected...
```

**Success Criteria:**
- System retries automatically (no manual restart needed)
- Captions resume after network restored
- No crashes or permanent failures

---

### Test 4: Server Crash Recovery ✅
**Purpose**: Verify recovery when Whisper service crashes/restarts

**Steps:**
1. Ensure OBS is streaming and captions are visible
2. Run:
   ```powershell
   # Stop Whisper server (simulates crash)
   docker stop wowzastreamingenginecaptionhandlers-whisper_server-1
   
   # Wait 10 seconds
   Start-Sleep -Seconds 10
   
   # Restart server
   docker start wowzastreamingenginecaptionhandlers-whisper_server-1
   
   # Wait 5 seconds for initialization
   Start-Sleep -Seconds 5
   ```

**Expected Results:**
- ✅ Backend logs show retry attempts while server is down
- ✅ After restart: Captions resume within 15 seconds
- ✅ Whisper model reloads automatically
- ✅ No manual configuration needed

**Check Whisper Server Logs:**
```powershell
docker logs wowzastreamingenginecaptionhandlers-whisper_server-1 --tail 20
```
You should see:
```
Loading Whisper tiny.en model...
done. It took X.XX seconds.
Whisper is warmed up.
Connected on ('172.19.0.2', XXXXX)
```

**Success Criteria:**
- System recovers automatically after server restart
- Captions resume without manual intervention

---

### Test 5: Memory Leak Check ✅
**Purpose**: Verify no memory leaks after multiple reconnections

**Steps:**
1. Run Tests 2-3 five times (5 reconnection cycles)
2. Keep browser DevTools open (F12 → Console tab)
3. After 10 minutes of streaming, check memory:
   ```javascript
   // Run in browser console
   window.streamDebug.getSize()
   ```

**Expected Results:**
- ✅ Subtitle tracking size ≤ 100 items
- ✅ Console shows periodic cleanup logs:
  ```
  Cleaned up old subtitle tracking data
  ```
- ✅ No memory growth over time
- ✅ Browser tab doesn't slow down or crash

**Success Criteria:**
- Memory stays bounded (≤ 100 subtitle items tracked)
- Periodic cleanup logs visible
- No performance degradation

---

### Test 6: Caption Deduplication ✅
**Purpose**: Verify no duplicate captions after reconnection

**Steps:**
1. Ensure OBS is streaming with active audio/speech
2. Watch captions appearing normally
3. Perform a quick reconnection:
   ```powershell
   docker network disconnect wowzastreamingenginecaptionhandlers_default wowzastreamingenginecaptionhandlers-whisper_server-1
   Start-Sleep -Seconds 3
   docker network connect wowzastreamingenginecaptionhandlers_default wowzastreamingenginecaptionhandlers-whisper_server-1
   ```
4. Watch captions resume
5. Open browser console (F12) and look for:
   ```
   Duplicate caption detected, skipping: [text]
   ```

**Expected Results:**
- ✅ If duplicate captions are sent by backend, frontend filters them
- ✅ Users see each caption only once
- ✅ Console may log duplicate detection (this is normal and expected)

**Success Criteria:**
- No visible duplicate captions to end users
- Backend and frontend deduplication working

---

## Visual Status Indicators

The web player shows connection status in the top-right corner:

| Color | Status | Meaning |
|-------|--------|---------|
| 🟢 Green | "Captions active" | Stream healthy, captions working normally |
| 🟢 Green | "Waiting for captions..." | Stream active, waiting for first caption to appear |
| 🟠 Orange | "Reconnecting..." | Stream has no updates for 30+ seconds |
| 🔴 Red | "Connection Error" | Player error, stream failed to load |

**Normal Operation**: You should see green during active streaming.

---

## Troubleshooting

### Problem: Captions Don't Appear
**Possible Causes:**
1. OBS not streaming
2. No audio source in OBS
3. Whisper server not running

**Solutions:**
```powershell
# Check OBS streaming status (should show "Live")
# Check audio levels in OBS (should show green bars)

# Verify Whisper server is running
docker logs wowzastreamingenginecaptionhandlers-whisper_server-1 --tail 20

# Restart containers if needed
docker-compose restart
```

---

### Problem: Orange "Reconnecting..." Status Persists
**Possible Causes:**
1. OBS stopped streaming
2. Network connectivity issues
3. Whisper server crashed

**Solutions:**
```powershell
# Check OBS streaming status
# Check container health
docker ps

# Check Wowza logs for errors
docker logs wowzastreamingenginecaptionhandlers-wse-1 --tail 50

# Restart specific container if crashed
docker restart wowzastreamingenginecaptionhandlers-whisper_server-1
```

---

### Problem: Console Shows Errors
**Check Browser Console (F12):**

**Common Errors:**
- `404` for favicon.ico → **Ignore** (non-critical)
- `net::ERR_FAILED` → Check network/Docker containers
- JavaScript errors → Check if code changes were saved

**Solutions:**
1. Refresh browser page (Ctrl+R)
2. Clear browser cache (Ctrl+Shift+Del)
3. Restart Docker containers

---

## Debugging Commands

### View Real-Time Logs
```powershell
# Wowza logs
docker logs wowzastreamingenginecaptionhandlers-wse-1 -f --tail 50

# Whisper logs
docker logs wowzastreamingenginecaptionhandlers-whisper_server-1 -f --tail 50

# Web player logs
docker logs webplayer -f --tail 50
```

### Check Network Connectivity
```powershell
# Verify containers can communicate
docker exec wowzastreamingenginecaptionhandlers-wse-1 ping whisper.server -c 4
```

### Browser Debug Console
Open browser console (F12) and run:
```javascript
// Check stream health
window.streamDebug.lastStreamUpdate()

// Check subtitle tracking
window.streamDebug.getSize()

// Check if caption tracks are available
window.streamDebug.captionTracksAvailable()
```

---

## Expected Test Results Summary

| Test | Duration | Expected Outcome |
|------|----------|------------------|
| **Normal Operation** | 2-5 min | ✅ Captions display, green indicator |
| **Short Interruption** | 10 sec | ✅ Auto-reconnect within 5s |
| **Extended Outage** | 30 sec | ✅ Multiple retries, recovers after reconnect |
| **Server Crash** | 2 min | ✅ Recovers after restart |
| **Memory Leak** | 10 min | ✅ Memory bounded, cleanup logs visible |
| **Deduplication** | 1 min | ✅ No duplicate captions visible |

---

## Files Modified (For Code Review)

### Backend
- `src/main/java/com/wowza/wms/plugin/captions/whisper/WhisperSpeechToTextHandler.java`
  - Lines 45-46: Connection state tracking
  - Lines 64-70: Caption deduplication setup
  - Lines 124-157: Reconnection logic with retry mechanism
  - Lines 253-277: Stale caption cleanup
  - Lines 390-459: Socket listener improvements

### Frontend
- `WebPlayer/index.html`
  - Lines 72-176: Connection status indicator CSS
  - Lines 179-192: Stream health monitoring variables
  - Lines 231-254: Connection monitoring logic
  - Lines 294-328: Event handlers for stream health tracking

### Documentation
- `SUBTITLE_RECONNECTION.md` - Technical documentation with test results

---

## Acceptance Criteria Verification

Before marking the JIRA ticket as complete, verify:

- [ ] ✅ The subtitle connection recovers automatically within 5 seconds
- [ ] ✅ The subtitle connection does not cause memory leaks after reconnect
- [ ] ✅ The subtitle connection does not cause duplicate cues after reconnect
- [ ] ✅ Visual status indicator shows connection state accurately
- [ ] ✅ No false warnings during normal operation

---

## Support & Questions

If you encounter issues during testing:

1. **Check logs first** (see Debugging Commands section)
2. **Verify all containers are running**: `docker ps`
3. **Review browser console** for JavaScript errors (F12)
4. **Check OBS streaming status** (should show "Live")

For technical questions about the implementation, refer to `SUBTITLE_RECONNECTION.md` for detailed technical documentation.

---

**Testing completed by:** [Your Name]  
**Testing date:** [Date]  
**Branch tested:** Iustin-Resilient-Reconnects-Hiccups  
**Result:** [ ] PASS / [ ] FAIL  
**Notes:** _[Add any observations or issues encountered]_
