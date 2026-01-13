# Internationalization Testing Suite

Automated testing suite for validating multi-language caption support in the Nidaros Real-Time Translation project.

## 📋 Test Coverage

### INTL-01: Mixed-Language Stream Detection
- **Objective:** Verify Whisper detects language switches in real-time
- **Test Data:** Dutch sermon with embedded Arabic prayers
- **Validation:** Separate caption tracks generated per language

### INTL-02: RTL Text Rendering
- **Objective:** Validate Right-to-Left rendering for Arabic/Hebrew
- **Test Cases:** Pure Arabic, Arabic+Numbers, Arabic+English names
- **Browsers:** Chrome, Firefox, Safari (if available)

### INTL-03: Font & Encoding End-to-End
- **Objective:** Verify UTF-8 preservation through entire pipeline
- **Test Data:** Multilingual text with diacritics (Greek, Arabic, Cyrillic, French)
- **Validation Points:** STT → Translation → WebVTT → Browser

## 🚀 Quick Start

### Prerequisites

1. **Python 3.8+** (for TTS audio generation)
2. **Node.js 16+** (for Playwright tests)
3. **Docker services running:**
   ```bash
   cd ../..
   docker-compose -f docker-compose-translate.yaml up -d
   ```

### Step 1: Install Dependencies

```bash
# Python dependencies for audio generation
pip install gTTS pydub

# Node dependencies for Playwright tests
npm install
npx playwright install
```

### Step 2: Generate Test Audio

```bash
python generate_test_audio.py
```

**Output:**
- `audio/test_mixed_dutch_arabic.mp3` - Mixed-language test
- `audio/test_arabic_only.mp3` - Pure RTL test
- `audio/test_arabic_english_names.mp3` - BiDi test
- `audio/test_encoding_stress.mp3` - Encoding validation

### Step 3: Start Test Stream

**Option A: Using FFmpeg**
```bash
# Stream Dutch + Arabic test file
ffmpeg -re -stream_loop -1 -i audio/test_mixed_dutch_arabic.mp3 \
       -ar 16000 -ac 1 -c:a pcm_s16le \
       -f flv rtmp://localhost:1935/whisper/testStream

# In another terminal, stream Arabic-only file
ffmpeg -re -stream_loop -1 -i audio/test_arabic_only.mp3 \
       -ar 16000 -ac 1 -c:a pcm_s16le \
       -f flv rtmp://localhost:1935/whisper/arabicTest
```

**Option B: Using OBS Studio**
1. Add Media Source → Select `audio/test_mixed_dutch_arabic.mp3`
2. Settings → Stream → Server: `rtmp://localhost:1935/whisper`
3. Stream Key: `testStream`
4. Start Streaming

### Step 4: Run Automated Tests

```bash
# Run all tests
npm test

# Run with visible browser
npm run test:headed

# Run specific browser
npm run test:chromium
npm run test:firefox

# Debug mode
npm run test:debug
```

## 📊 Test Execution

### Manual Validation Steps

1. **Verify Docker Services:**
   ```bash
   docker ps | grep -E "whisper|libretranslate"
   ```

2. **Check LibreTranslate Languages:**
   ```bash
   curl http://localhost:5001/languages
   ```
   Expected: `nl`, `de`, `en`, `uk`, `ar`

3. **Monitor Wowza Logs:**
   ```bash
   docker exec wse.docker tail -f /usr/local/WowzaStreamingEngine/logs/wowzastreamingengine_access.log
   ```

4. **Inspect WebVTT Output:**
   ```bash
   # Check for multi-track manifest
   curl http://localhost:1935/whisper/testStream_delayed/playlist.m3u8
   ```

5. **Open Player:**
   - Navigate to http://localhost:8000
   - Click CC button
   - Verify multiple language tracks available

### Expected Results

#### ✅ Pass Criteria

- **INTL-01:**
  - [ ] Dutch and Arabic tracks appear separately in caption list
  - [ ] Track IDs are unique (trackId=1 for nl, trackId=2 for ar, etc.)
  - [ ] Language metadata preserved: `language: "nl"` vs `language: "ar"`
  - [ ] No mixed-language text in single track

- **INTL-02:**
  - [ ] Arabic text renders right-to-left
  - [ ] Numbers displayed correctly in Arabic context
  - [ ] English names embedded without breaking RTL flow
  - [ ] Punctuation not mirrored (?, !)

- **INTL-03:**
  - [ ] Diacritics preserved: `Café` not `Cafe`
  - [ ] Arabic characters not transliterated
  - [ ] Greek characters display correctly (α, γ, π)
  - [ ] Cyrillic text readable (Здравствуйте)

#### ⚠️ Known Issues

Current implementation gaps (as of code analysis):

1. **No RTL direction markers** in `CaptionHelper.java`
   - Arabic may render LTR instead of RTL
   - BiDi markers (U+200F, U+200E) not inserted

2. **HTML lang attribute static** in `index.html`
   - Always `<html lang="en">`
   - Screen readers use wrong pronunciation

3. **No Arabic punctuation handling**
   - Arabic-specific terminators not recognized
   - Falls back to default Western punctuation

## 📸 Screenshot Evidence

After running tests, screenshots are saved to:
```
screenshots/
├── dutch-captions-chromium.png
├── dutch-captions-firefox.png
├── arabic-rtl-chromium.png
├── arabic-rtl-firefox.png
├── encoding-stress-chromium.png
└── encoding-stress-firefox.png
```

## 🐛 Troubleshooting

### Audio Generation Fails

```bash
# Install system dependencies (Windows)
# gTTS requires internet connection to Google TTS API

# Alternative: Use online TTS services
# https://ttsmp3.com/
```

### No Caption Tracks Detected

```bash
# Check Whisper server logs
docker logs whisper.server

# Verify LibreTranslate is running
curl http://localhost:5001/translate \
  -d "q=Hello&source=en&target=nl"
```

### Playwright Tests Timeout

```bash
# Increase timeout in playwright.config.js
timeout: 180000  # 3 minutes

# Check player is accessible
curl http://localhost:8000
```

### Stream Not Found

```bash
# Verify stream name matches
# FFmpeg publishes to: rtmp://localhost:1935/whisper/testStream
# Player expects: http://localhost:1935/whisper/testStream_delayed/playlist.m3u8
```

## 📝 Test Report Template

After running tests, document results using this template:

```markdown
# I18n Test Execution Report
**Date:** YYYY-MM-DD
**Tester:** Your Name
**Build:** Commit SHA

## Environment
- Docker Compose: `docker-compose-translate.yaml`
- Whisper Model: small
- Languages: nl, de, en, uk, ar

## Test Results

### INTL-01: Mixed-Language Detection
- Status: ✅ PASS / ❌ FAIL
- Evidence: screenshots/dutch-arabic-test.png
- Notes: [Add observations]

### INTL-02: RTL Rendering
- Status: ✅ PASS / ⚠️ PARTIAL / ❌ FAIL
- Chrome: ✅ RTL detected
- Firefox: ❌ Rendered LTR
- Evidence: screenshots/arabic-rtl-*.png
- Notes: [Add observations]

### INTL-03: Encoding Validation
- Status: ✅ PASS / ❌ FAIL
- Diacritics: ✅ Preserved
- Arabic: ✅ No transliteration
- Greek: ✅ Correct rendering
- Cyrillic: ⚠️ Font fallback
- Evidence: screenshots/encoding-stress-*.png

## Issues Found
1. [Issue description]
2. [Issue description]

## Recommendations
1. [Recommendation]
2. [Recommendation]
```

## 🔧 Maintenance

### Update Test Audio

Modify `generate_test_audio.py` and regenerate:
```bash
python generate_test_audio.py
```

### Add New Test Cases

Edit `playwright_tests.js` and add new `test()` blocks:
```javascript
test('INTL-XX: Your test name', async ({ page }) => {
  // Your test logic
});
```

### Update Browser Matrix

Edit `playwright.config.js` to add/remove browsers:
```javascript
projects: [
  { name: 'chromium', use: { ...devices['Desktop Chrome'] } },
  { name: 'mobile-safari', use: { ...devices['iPhone 13'] } },
]
```

## 📚 References

- [WebVTT Specification](https://www.w3.org/TR/webvtt1/)
- [Unicode BiDi Algorithm](https://unicode.org/reports/tr9/)
- [JW Player Caption API](https://docs.jwplayer.com/players/docs/jw8-captions-support)
- [Playwright Documentation](https://playwright.dev/)

---

**Questions?** See main project README or contact project maintainer.
