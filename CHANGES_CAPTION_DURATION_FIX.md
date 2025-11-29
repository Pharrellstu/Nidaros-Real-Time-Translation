# Caption Duration Fix - Minimum 2 Second Display

**Date**: November 29, 2025  
**Status**: ✅ COMPLETED - Ready to rebuild

## Problem Statement

**User observation**: "I always get a burst of 2 short subtitle segments at the same time which quickly disappear"

### Root Cause Analysis

1. **Flawed timing calculation** in `CreateCaptionFromLines`:
   ```csharp
   // OLD (BROKEN):
   var captionStart = start + (duration * captionIndex / totalWords);
   var captionEnd = captionStart + (duration / (totalWords / maxLinesPerCaption));
   ```
   - Divided by **word count** instead of **caption count**
   - Created 0.5-0.8 second durations (impossible to read)

2. **No minimum duration enforcement**:
   - Captions could be as short as 0.4 seconds
   - WebVTT best practice: **2-6 seconds minimum**

3. **Example from logs** (BEFORE FIX):
   ```
   00:05:18.769 → 00:05:19.609  (0.84s) ❌ Too short!
   00:05:24.040 → 00:05:24.611  (0.57s) ❌ Too short!
   00:05:34.465 → 00:05:34.948  (0.48s) ❌ Unreadable!
   ```

## Solution Implemented

### 1. Added Minimum Duration Parameter
```csharp
private readonly double _minCaptionDurationSeconds;

public CaptionFormatter(
    int maxLineLength = 37, 
    int maxLinesPerCaption = 2, 
    double minCaptionDurationSeconds = 2.0  // ← NEW
)
```

### 2. Refactored Caption Generation
**Two-phase approach**:

**Phase 1**: Collect all caption texts first
```csharp
// Build list of caption texts without timing
var captionTexts = new List<string>();
// ... (line breaking logic unchanged)
captionTexts.Add(string.Join("\n", lines));
```

**Phase 2**: Assign timing with proper duration
```csharp
for (int i = 0; i < captionTexts.Count; i++)
{
    var caption = CreateCaptionFromLines(
        captionTexts[i], 
        i,                      // caption index
        totalCaptionCount,      // ← FIX: use caption count, not word count!
        start, 
        end, 
        language
    );
    captions.Add(caption);
}
```

### 3. Fixed Timing Calculation
```csharp
private Caption CreateCaptionFromLines(
    string text,              // ← Changed from List<string> lines
    int captionIndex, 
    int totalCaptionCount,    // ← Changed from totalWords
    TimeSpan start, 
    TimeSpan end, 
    string language
)
{
    var totalDuration = end - start;

    // Single caption: use full duration
    if (totalCaptionCount <= 1)
    {
        var duration = totalDuration;
        if (duration.TotalSeconds < _minCaptionDurationSeconds)
        {
            duration = TimeSpan.FromSeconds(_minCaptionDurationSeconds);
        }
        return new Caption(start, start + duration, text, language);
    }

    // Multiple captions: distribute evenly
    var durationPerCaption = TimeSpan.FromSeconds(
        totalDuration.TotalSeconds / totalCaptionCount
    );
    
    // Enforce minimum
    if (durationPerCaption.TotalSeconds < _minCaptionDurationSeconds)
    {
        durationPerCaption = TimeSpan.FromSeconds(_minCaptionDurationSeconds);
    }

    var captionStart = start + (durationPerCaption * captionIndex);
    var captionEnd = captionStart + durationPerCaption;

    return new Caption(captionStart, captionEnd, text, language);
}
```

## Expected Results

### BEFORE Fix
```
Caption 1: 00:00:15.000 → 00:00:15.500 (0.5s) "standard library that contains"
Caption 2: 00:00:15.250 → 00:00:15.750 (0.5s) "modules to handle I.O."
```
- ❌ Durations too short (< 1 second)
- ❌ Overlapping timestamps
- ❌ Both appear simultaneously and vanish

### AFTER Fix
```
Caption 1: 00:00:15.000 → 00:00:17.500 (2.5s) "standard library that contains"
Caption 2: 00:00:17.500 → 00:00:20.000 (2.5s) "modules to handle I.O."
```
- ✅ Minimum 2 seconds per caption
- ✅ Sequential display (no overlap)
- ✅ Time to read comfortably

### Example Scenario
**Input**: "One, two, three, four, five. Six, seven, eight, nine, ten."  
**Whisper timing**: 0.0s → 5.0s (5 second chunk)

**Before**: 2 captions × 0.6s each = unreadable bursts  
**After**: 2 captions × 2.5s each = readable sequential display

## Testing Instructions

1. **Rebuild the API service** (only .NET changes, no Docker rebuild needed):
   ```fish
   docker compose restart api-service
   ```

2. **Watch logs** for new timing pattern:
   ```fish
   docker logs -f nidaros-real-time-translation-api-service-1 | grep "VTT-"
   ```
   
   Look for:
   - Durations ≥ 2.0 seconds
   - No overlapping timestamps
   - Sequential caption display

3. **Test with MPV**:
   ```fish
   ./test-mpv.fish
   ```
   
   Observe:
   - Captions stay on screen long enough to read
   - No more "burst of 2 segments" appearing together
   - Smooth sequential display

4. **Verify VTT file**:
   ```fish
   docker exec nidaros-real-time-translation-api-service-1 \
     tail -n 30 /shared_content/vtt/OBSstream.vtt
   ```
   
   Check timestamps:
   ```
   00:01:23.456 --> 00:01:25.456   ← 2.0s minimum ✓
   Caption text here
   
   00:01:25.456 --> 00:01:27.956   ← 2.5s (no overlap) ✓
   Next caption text
   ```

## Performance Impact

- ✅ **No latency change** - only affects display duration
- ✅ **No CPU impact** - same line breaking logic
- ✅ **Better UX** - captions actually readable

## Edge Cases Handled

1. **Single short caption**: Extended to 2.0 seconds minimum
2. **Multiple captions in 5s chunk**: Each gets ≥2.0s (may extend beyond chunk boundary)
3. **Very long text**: Still breaks at 37 chars and punctuation, but with proper timing

## Future Improvements (Not in this PR)

- Task 3: Fix audio capture gaps (eliminates burst pattern entirely)
- Task 7: Caption deduplication (prevent duplicate text)
- Task 6: VAD filtering (skip silent chunks)

## Files Modified

- ✅ `NidarosRTT.Infrastructure/CaptionFormatter.cs`
  - Added `_minCaptionDurationSeconds` parameter (default 2.0)
  - Refactored `FormatSegment` to two-phase approach
  - Fixed `CreateCaptionFromLines` timing calculation
  - Changed parameter from `totalWords` to `totalCaptionCount`

## Validation Checklist

- [x] Code compiles without errors
- [x] Logic reviewed for timing accuracy
- [ ] Manual testing with MPV player (user to verify)
- [ ] VTT file timestamps validated (user to verify)
- [ ] No more "burst of 2 segments" issue (user to verify)

---

**Ready for testing!** Restart `api-service` and test with MPV player.
