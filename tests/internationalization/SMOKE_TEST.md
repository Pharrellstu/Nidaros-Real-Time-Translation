# 🔥 5-Minute Smoke Test - Internationalization

**Goal:** Quick validation that multi-language captions are working

**Time:** 5-10 minutes

---

## ✅ Step 1: Start Services (2 minutes)

```powershell
# From project root
docker-compose -f docker-compose-translate.yaml up -d

# Wait 30 seconds for services to initialize
Start-Sleep -Seconds 30

# Check services are running
docker ps --format "table {{.Names}}\t{{.Status}}"
```

**Expected Output:**
```
NAMES                   STATUS
wse.docker              Up
whisper.server          Up
libretranslate.server   Up
webplayer               Up
wsem.docker             Up
```

---

## ✅ Step 2: Verify Configuration (30 seconds)

```powershell
# Check LibreTranslate has our languages
curl http://localhost:5001/languages | Select-String "nl|ar|de|en|uk"

# Check frontend is accessible
curl http://localhost:8000 -UseBasicParsing | Select-String "JW Player"
```

**Expected:**
- ✅ Languages: Dutch (nl), Arabic (ar), German (de), English (en), Ukrainian (uk)
- ✅ Frontend returns HTML with "JW Player"

---

## ✅ Step 3: Quick Manual Test (3 minutes)

### Option A: Test with Microphone (Easiest)

1. Open OBS Studio
2. **Settings → Stream:**
   - Server: `rtmp://localhost:1935/whisper`
   - Stream Key: `testStream`
3. **Add Source → Audio Input Capture** (your microphone)
4. **Start Streaming**
5. **Speak in English:** "Hello, this is a test"
6. Wait 5 seconds
7. **Speak different language or wait** for translation

### Option B: Test with Pre-existing Audio File

1. OBS Studio → **Add Source → Media Source**
2. Select any MP3/MP4 file with speech
3. ✅ Check "Loop"
4. **Settings → Stream** (same as above)
5. **Start Streaming**

---

## ✅ Step 4: Verify Captions (2 minutes)

1. Open browser: **http://localhost:8000**
2. Wait for stream to load (green status indicator)
3. **Click CC button** in player
4. **Check:** Do you see multiple tracks listed?

**Expected:**
```
Off
Dutch (nl)
German (de)
English (en)
Ukrainian (uk)
Arabic (ar)
```

**If YES → ✅ Test PASSED**
**If NO → See troubleshooting below**

---

## 🎯 Success Criteria

Minimum working validation:

- ✅ At least 2-3 language tracks appear in caption list
- ✅ Track names show language codes (nl, ar, en, etc.)
- ✅ Can switch between tracks without errors
- ✅ Text appears in subtitle area (even if wrong language)

**That's it!** If these work, your multi-language setup is functional.

---

## 🐛 Troubleshooting (If Test Fails)

### Only "Off" track appears (No caption tracks)

```powershell
# Check Whisper is processing audio
docker logs whisper.server --tail=50

# Look for lines like:
# {"text": "...", "language": "en", "start": 0.0, "end": 2.5}
```

**Fix:** If no logs appear, audio stream isn't reaching Whisper
- Verify OBS is streaming to correct RTMP URL
- Check stream name matches (testStream vs myStream)

### Tracks appear but no text shows

```powershell
# Check delayed stream is created
curl http://localhost:1935/whisper/testStream_delayed/playlist.m3u8

# Check Wowza logs
docker logs wse.docker --tail=100 | Select-String "caption"
```

**Fix:** 
- Wait 10-30 seconds (caption delay is configured)
- Refresh browser page

### Services won't start

```powershell
# Check if ports are in use
netstat -ano | Select-String "1935|3000|5001|8000"

# Stop any conflicting services
docker-compose down
```

---

## 📊 What This Test Validates

✅ **Docker setup** - Translation stack running
✅ **Whisper STT** - Receiving and processing audio  
✅ **LibreTranslate** - Translation service available
✅ **Java plugin** - Dynamic track ID assignment working
✅ **WebVTT output** - Multiple caption tracks generated
✅ **Frontend** - JW Player displaying multiple tracks

## ⚠️ What This Test Does NOT Validate

❌ RTL rendering (requires visual inspection)
❌ BiDi text handling (requires specific Arabic+English test)
❌ Cross-browser compatibility
❌ Encoding for all character sets
❌ Track synchronization timing

---

## 🚀 If Smoke Test Passes

Your changes are working! Next steps:

1. **Document in JIRA:**
   - "Multi-language caption tracks now generate with unique track IDs"
   - "Tested languages: nl, de, en, uk, ar"
   - "Feature status: ✅ Basic functionality verified"

2. **Commit Java changes:**
   ```powershell
   git add src/main/java/com/wowza/wms/plugin/captions/whisper/WhisperSpeechToTextHandler.java
   git commit -m "Fix: Assign dynamic track IDs per language (INTL-01)"
   git push
   ```

3. **Optional:** Run full test suite later for comprehensive validation

---

## 📝 Quick Test Report Template

```markdown
## Smoke Test Results - Multi-Language Captions

**Date:** 2026-01-13
**Tester:** [Your Name]
**Duration:** 5 minutes

### Setup
- Docker Compose: docker-compose-translate.yaml
- Languages: nl, de, en, uk, ar

### Test Execution
- [x] Services started successfully
- [x] LibreTranslate languages verified
- [x] OBS streaming to Whisper
- [x] Caption tracks detected in player

### Results
- **Tracks Found:** 5 (nl, de, en, uk, ar)
- **Track Switching:** ✅ Works
- **Text Display:** ✅ Visible
- **Status:** ✅ PASS

### Issues
- None (basic functionality working)

### Recommendation
✅ Feature ready for basic use
⚠️  RTL rendering requires additional validation
```

---

**Total Time:** ~5-10 minutes

Just need to verify:
1. Services start
2. Caption tracks appear
3. Basic functionality works

That's it! 🚀
