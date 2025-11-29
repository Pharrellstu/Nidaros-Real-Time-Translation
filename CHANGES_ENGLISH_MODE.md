# English-Only Mode Changes

**Date**: November 29, 2025  
**Status**: ✅ COMPLETED - Ready to rebuild

## Summary
Disabled translation service to use English-only transcription mode. This simplifies debugging and improves accuracy by avoiding translation confusion.

## Changes Made

### 1. FasterWhisperService (Python)
**File**: `FasterWhisperService/app/run.py`

- ✅ Disabled translation service calls in `/transcribe` endpoint
- ✅ Returns English text as both `original_text` and `translated_text`
- ✅ Sets `translation_status` to `"disabled"`
- ✅ Sets `target_language` to `"en"` instead of `"nl"`
- 💾 Translation code kept intact (commented) for future re-enablement

**Key Change**:
```python
# TRANSLATION DISABLED FOR NOW - Using English-only mode
# To re-enable: uncomment the lines below
# translated_text, translation_status = translate_text(text)

return {
    "original_text": text,
    "translated_text": text,  # Using English text directly
    "translation_status": "disabled",
    "target_language": "en",  # Changed from "nl"
}
```

### 2. API Service (C#)
**File**: `NidarosRTT.API/Program.cs`

- ✅ Updated transcription worker to use single `englishText` variable
- ✅ Changed VTT caption language from `"nl"` to `"en"`
- ✅ Updated SignalR DTO to reflect translation disabled state
- ✅ Console output now shows "TRANSCRIPTION" instead of "TRANSLATION"

**Key Changes**:
```csharp
// USING ENGLISH-ONLY MODE (translation disabled)
var englishText = (transcriptionResult.OriginalText ?? "").Trim();

// VTT generation uses English text
var captions = captionFormatter.FormatWithOffset(
    englishText,  // Using English text directly
    whisperStart,
    whisperEnd,
    streamOffset,
    language: "en"  // Changed from "nl"
);

// DTO for SignalR (translation disabled)
var dto = new SingleCaptionDto(
    text: englishText,
    originalText: englishText,
    translationAvailable: false,
    translationStatus: "disabled",
    targetLanguage: "en"  // Changed from "nl"
);
```

### 3. Docker Compose Configuration
**File**: `docker-compose.yml`

- ✅ Commented out `translator-service` (kept for future use)
- ✅ Removed `depends_on` translator from other services
- ✅ Added explicit Whisper model configuration: `MODEL_NAME=small.en`
- ✅ Added GPU/device configuration comments for future optimization

**Key Changes**:
```yaml
# translator-service: DISABLED - commented out entire section

whisper-service:
  environment:
    - MODEL_NAME=small.en  # English-only model
    - DEVICE=cpu
    - COMPUTE_TYPE=int8
    # Translation URL removed

api-service:
  depends_on:
    - whisper-service
    # translator-service removed from dependencies
```

## What This Fixes

### ✅ Before (With Translation)
- Mixed language confusion: "Nächtin, Zwint!" (German/Dutch mix)
- Translation latency: +1-2 seconds per caption
- Model confusion: Whisper tries to guess language, translator adds errors
- Console logs: `[TRANSLATION] Translated: Ik weet niet...`

### ✅ After (English Only)
- Clean English transcription directly from Whisper
- No translation latency
- Better Whisper accuracy (language-specific model optimized for English)
- Console logs: `[TRANSCRIPTION] English: <text>`

## Testing Instructions

1. **Rebuild services** (required for Python changes):
   ```fish
   docker compose down
   docker compose up --build -d
   ```
   ⚠️ **Note**: Run this manually in your terminal

2. **Start OBS streaming** to `rtmp://localhost:1935/live` with key `OBSstream`

3. **Watch API logs** to see English-only transcription:
   ```fish
   docker logs -f nidaros-real-time-translation-api-service-1
   ```
   
   You should see:
   ```
   [TRANSCRIPTION-X] HH:MM:SS → One, two, three, four...
   [VTT-X] Stream@00:00:15 + Whisper 0.0s = 00:00:15.123 → ...
   ```

4. **Test with MPV player**:
   ```fish
   ./test-mpv.fish
   ```
   
   Subtitles should now show pure English text.

5. **Verify VTT file**:
   ```fish
   docker exec nidaros-real-time-translation-api-service-1 tail -n 20 /shared_content/vtt/OBSstream.vtt
   ```

## Expected Improvements

1. **Accuracy**: English-specific Whisper model is more accurate than multilingual
2. **Latency**: Removed 1-2 second translation step
3. **Simplicity**: Easier to debug when only one language is involved
4. **Stability**: One less service to fail (translator-service not running)

## Re-enabling Translation (Future)

To re-enable Dutch translation:

1. Uncomment translator-service in `docker-compose.yml`
2. Uncomment translation code in `FasterWhisperService/app/run.py`
3. Update `Program.cs` to use `translatedText` variable again
4. Rebuild: `docker compose up --build -d`

## Files Modified

- ✅ `FasterWhisperService/app/run.py`
- ✅ `NidarosRTT.API/Program.cs`
- ✅ `docker-compose.yml`
- ✅ No changes to `WhisperService.cs` (already handles both modes)

## Next Steps

After testing English-only mode:
- **Task 2**: Fix audio capture timing (eliminate gaps)
- **Task 3**: Re-evaluate timing calculation
- **Task 5**: Add VAD and hallucination filtering
