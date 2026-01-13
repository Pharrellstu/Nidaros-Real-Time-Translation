# 🚀 Quick Start: Internationalization Testing

**Estimated Time:** 1 day (6-8 hours)

## ✅ Prerequisites Check

Before starting, verify:

```powershell
# 1. Docker services running
docker ps | Select-String "whisper|libretranslate"

# 2. Python installed
python --version  # Should be 3.8+

# 3. Node.js installed
node --version    # Should be 16+
```

---

## 📦 Step 1: Install Dependencies (5 minutes)

```powershell
# Navigate to test directory
cd tests\internationalization

# Install Python packages
pip install gTTS pydub

# Install Node packages
npm install

# Install Playwright browsers
npx playwright install
```

---

## 🎤 Step 2: Generate Test Audio (2 minutes)

```powershell
# Generate all test audio files
python generate_test_audio.py
```

**Expected Output:**
```
🌍 Internationalization Test Audio Generator
============================================================
🎤 Generating Dutch + Arabic test audio...
  ├─ Generating Dutch intro...
  ├─ Generating Arabic prayer...
  ├─ Generating Dutch outro...
  ✅ Created: audio/test_mixed_dutch_arabic.mp3 (15.3s)
...
✅ Test Audio Generation Complete!
```

**Files Created:**
- ✅ `audio/test_mixed_dutch_arabic.mp3` (15s)
- ✅ `audio/test_arabic_only.mp3` (8s)
- ✅ `audio/test_arabic_english_names.mp3` (7s)
- ✅ `audio/test_encoding_stress.mp3` (18s)

---

## 🐳 Step 3: Start Translation Stack (3 minutes)

```powershell
# Navigate to project root
cd ..\..

# Stop existing services
docker-compose down

# Start translation stack
docker-compose -f docker-compose-translate.yaml up -d

# Verify services
docker ps
```

**Expected Services:**
```
wse.docker              - Wowza Streaming Engine
whisper.server          - Whisper STT (model: small)
libretranslate.server   - Translation (nl,de,en,uk,ar)
wsem.docker             - Wowza Manager
webplayer               - Frontend (port 8000)
```

### Validate Services

```powershell
# Check LibreTranslate
curl http://localhost:5001/languages

# Check Wowza API
curl http://localhost:8087/v2/servers/_defaultServer_/status

# Check Frontend
curl http://localhost:8000
```

---

## 🎥 Step 4: Start Test Stream (2 minutes)

**Option A: Using FFmpeg (Recommended)**

```powershell
# Install FFmpeg if not present
# Download from: https://ffmpeg.org/download.html

# Stream Dutch + Arabic test file
ffmpeg -re -stream_loop -1 `
       -i tests\internationalization\audio\test_mixed_dutch_arabic.mp3 `
       -ar 16000 -ac 1 -c:a pcm_s16le `
       -f flv rtmp://localhost:1935/whisper/testStream
```

**Option B: Using OBS Studio**

1. Open OBS Studio
2. Add Source → **Media Source**
3. Select file: `tests\internationalization\audio\test_mixed_dutch_arabic.mp3`
4. ✅ Check "Loop"
5. Settings → Stream:
   - Server: `rtmp://localhost:1935/whisper`
   - Stream Key: `testStream`
6. **Start Streaming**

---

## 🧪 Step 5: Run Automated Tests (30 minutes)

### 5a. Quick Validation (5 minutes)

```powershell
cd tests\internationalization

# Test single browser (Chromium)
npm run test:chromium
```

### 5b. Full Test Suite (30 minutes)

```powershell
# Run all tests across all browsers
npm test
```

**Test Execution:**
```
Running 5 tests using 1 worker
[chromium] › INTL-01: Capture Dutch caption rendering (PASS) ✅
[chromium] › INTL-02: Capture Arabic RTL caption rendering (PASS) ✅
[chromium] › INTL-03: Enumerate all caption tracks (PASS) ✅
[firefox] › INTL-01: Capture Dutch caption rendering (PASS) ✅
[firefox] › INTL-02: Capture Arabic RTL caption rendering (PASS) ✅
...
5 passed (2m 15s)
```

---

## 📸 Step 6: Review Results (10 minutes)

### View Screenshots

```powershell
# Open screenshots folder
explorer screenshots
```

**Files:**
- `dutch-captions-chromium.png`
- `arabic-rtl-chromium.png`
- `dutch-captions-firefox.png`
- `arabic-rtl-firefox.png`
- `encoding-stress-chromium.png`

### Check Test Report

```powershell
# Open HTML report
npx playwright show-report
```

---

## ✅ Step 7: Manual Validation (20 minutes)

### 7a. Verify Multi-Track Captions

1. Open browser: http://localhost:8000
2. Wait for stream to load (status indicator turns green)
3. Click **CC button** in player
4. **Verify:** Multiple tracks listed (Dutch, German, English, Ukrainian, Arabic)

### 7b. Test Track Switching

1. Select **Arabic** track
2. **Observe:** Text direction (should be RTL)
3. Switch to **Dutch** track
4. **Observe:** Text direction changes to LTR

### 7c. Inspect WebVTT Output

```powershell
# Check HLS manifest
curl http://localhost:1935/whisper/testStream_delayed/playlist.m3u8

# Look for subtitle tracks
curl http://localhost:1935/whisper/testStream_delayed/media_w123456789_1.m3u8
```

**Expected:** Multiple `#EXT-X-MEDIA:TYPE=SUBTITLES` entries

---

## 📋 Step 8: Document Findings (30 minutes)

### 8a. Create Test Report

Copy template from `README.md` and fill in:

```markdown
# I18n Test Execution Report
**Date:** 2026-01-13
**Tester:** [Your Name]

## Test Results

### INTL-01: Mixed-Language Detection
- Status: ✅ PASS
- Dutch track detected: ✅
- Arabic track detected: ✅
- Track IDs unique: ✅ (nl=1, ar=2)
- Evidence: screenshots/dutch-arabic-chromium.png
```

### 8b. Collect Evidence

Create folder:
```
test-results-2026-01-13/
├── screenshots/
├── playwright-report/
├── docker-logs.txt
├── webvtt-sample.vtt
└── test-report.md
```

### 8c. Export Docker Logs

```powershell
docker logs whisper.server > docker-logs-whisper.txt
docker logs wse.docker > docker-logs-wowza.txt
```

---

## 🐛 Troubleshooting

### Test Audio Won't Generate

```powershell
# Error: "No module named 'gtts'"
pip install --upgrade gTTS pydub

# Error: "Connection error"
# gTTS requires internet to access Google TTS API
# Check internet connection
```

### No Caption Tracks in Player

```powershell
# Check Whisper is receiving audio
docker logs whisper.server --tail=50

# Expected output:
# "text": "Welkom allemaal", "language": "nl"
```

### Playwright Tests Fail

```powershell
# Check player is accessible
curl http://localhost:8000

# Run in headed mode to see what's happening
npm run test:headed

# Increase timeout
# Edit playwright.config.js: timeout: 180000
```

### Arabic Text Renders LTR

This is a **KNOWN ISSUE**. The current implementation does not insert BiDi markers.

**Workaround for testing:**
1. Open browser DevTools (F12)
2. Inspect caption element
3. Manually add: `<div dir="rtl">Arabic text</div>`
4. Take screenshot as "expected behavior"

---

## ⏱️ Time Estimates

| Phase | Task | Time |
|-------|------|------|
| **Setup** | Install dependencies | 5 min |
| | Generate test audio | 2 min |
| | Start Docker services | 3 min |
| **Execution** | Start test stream | 2 min |
| | Run automated tests | 30 min |
| | Manual validation | 20 min |
| **Documentation** | Review screenshots | 10 min |
| | Write test report | 30 min |
| | Collect evidence | 10 min |
| **Total** | | **~2 hours active + 6 hours for comprehensive testing**

---

## 📊 Success Criteria

### Minimum (Quick Validation)

- ✅ Docker services running
- ✅ Test audio streams successfully
- ✅ At least 2 caption tracks detected (nl, ar)
- ✅ Screenshots captured for Chrome
- ✅ No major errors in console logs

### Comprehensive (Full Test Suite)

- ✅ All 5 automated tests pass
- ✅ Screenshots for 3+ browsers
- ✅ RTL rendering validated visually
- ✅ Encoding stress test shows all character sets
- ✅ Manual track switching works
- ✅ Complete test report with evidence

---

## 🎯 Next Steps

After completing quick validation:

### If Tests PASS
1. Document configuration in JIRA
2. Merge dynamic track ID fix to main branch
3. Plan Phase 2: RTL improvements

### If Tests FAIL
1. Document specific failures
2. Collect debug logs
3. Review with development team
4. Adjust test scope or fix bugs

---

## 📞 Need Help?

**Common Issues:**
- Services won't start → Check `.env` file for Wowza license
- No audio processing → Verify Whisper model downloaded
- Translation fails → Check LibreTranslate languages configured

**Debug Commands:**
```powershell
# Check all services
docker-compose -f docker-compose-translate.yaml ps

# View logs
docker-compose -f docker-compose-translate.yaml logs -f

# Restart specific service
docker-compose -f docker-compose-translate.yaml restart whisper_server
```

---

**You're ready to start testing! 🚀**

Run through Step 1-5 first for quick validation, then proceed to comprehensive testing if needed.
