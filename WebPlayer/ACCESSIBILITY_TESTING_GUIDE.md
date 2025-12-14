# Accessibility Testing Guide
**INRT-55: ARIA Labels for Screen Reader Support**

## 📋 Implementation Summary

### ✅ Completed Tasks

#### INRT-719: ARIA Roles Implementation
- ✅ Added `role="button"` to CC toggle button
- ✅ Added `aria-pressed="true/false"` state to CC toggle button
- ✅ Added `role="button"` to Quality button
- ✅ Added `role="region"` to video player container
- ✅ Added `role="status"` to dynamic content areas
- ✅ Added `tabindex="0"` for keyboard accessibility

#### INRT-725: ARIA Labels Implementation
- ✅ CC Button: `aria-label="Toggle captions, currently off/on showing [track name]"`
- ✅ Quality Button: `aria-label="Change video quality, currently [quality level]"`
- ✅ Video Container: `aria-label="Live video player with caption and quality controls"`
- ✅ Subtitle Display: `aria-live="polite"` + `aria-atomic="true"`
- ✅ Connection Status: `aria-live="polite"` + `aria-atomic="true"`
- ✅ Dynamic label updates on state changes
- ✅ Status indicator hidden from screen readers with `aria-hidden="true"`

#### Additional Enhancements
- ✅ Enhanced focus visibility (3px blue outline)
- ✅ Console logging for debugging accessibility enhancements
- ✅ Proper semantic HTML structure
- ✅ WCAG 2.1 Level AA compliance target

---

## 🧪 Testing Phase (INRT-726)

### Step 1: Automated Testing with Lighthouse (5 minutes)

**Prerequisites:**
- Google Chrome browser
- Local development server running (`http://localhost:8000`)

**Instructions:**
1. Open Chrome and navigate to `http://localhost:8000`
2. Press `F12` to open DevTools
3. Click on the **"Lighthouse"** tab
4. Configure audit:
   - Uncheck all categories except **"Accessibility"**
   - Device: Desktop
   - Mode: Navigation
5. Click **"Analyze page load"**
6. Wait for the report (30-60 seconds)

**Expected Results:**
- **Target Score:** 95-100
- **Acceptable:** 90+
- **Critical Issues:** 0

**Take Screenshot:**
- Save the Lighthouse report screenshot
- Note the score and any issues found

---

### Step 2: Automated Testing with axe DevTools (5 minutes)

**Prerequisites:**
- Install axe DevTools extension from Chrome Web Store

**Instructions:**
1. Install extension: [axe DevTools](https://chrome.google.com/webstore/detail/axe-devtools-web-accessib/lhdoppojpmngadmnindnejefpokejbdd)
2. Open DevTools (`F12`)
3. Click on **"axe DevTools"** tab
4. Click **"Scan ALL of my page"**
5. Review results

**Expected Results:**
- **Critical violations:** 0
- **Serious violations:** 0
- **Moderate violations:** 0 (acceptable: 1-2)
- **Minor violations:** 0 (acceptable: 1-3)

**Documentation:**
- Take screenshot of results
- Note any violations with their descriptions

---

### Step 3: Keyboard Navigation Testing (10 minutes)

**Test Checklist:**

```
Test #1: Tab Navigation
□ Press Tab key repeatedly
□ Verify focus moves to video player controls
□ Verify CC button receives focus
□ Verify Quality button receives focus
□ Verify focus indicator is visible (blue outline)
□ Verify tab order is logical

Test #2: Reverse Navigation
□ Press Shift + Tab
□ Verify you can navigate backwards
□ Verify focus moves in reverse order

Test #3: Button Activation
□ Tab to CC button
□ Press Enter key → Should toggle captions
□ Press Space key → Should toggle captions
□ Tab to Quality button
□ Press Enter key → Should cycle quality
□ Press Space key → Should cycle quality

Test #4: Focus Trap Detection
□ Tab through all elements
□ Verify you can Tab away from the player
□ Verify focus doesn't get stuck anywhere

Test #5: Visual Focus
□ Verify focus outline is visible on all interactive elements
□ Verify outline color is #4A90E2 (blue)
□ Verify outline is 3px wide
□ Verify outline has 2px offset
```

**Results Template:**
```
✅ = Pass
❌ = Fail

Tab Navigation: ___
Reverse Navigation: ___
Button Activation (Enter): ___
Button Activation (Space): ___
No Focus Traps: ___
Visual Focus Indicators: ___

Overall: PASS / FAIL
```

---

### Step 4: ARIA Attribute Verification (5 minutes)

**Instructions:**
1. Open DevTools (`F12`)
2. Open Console tab
3. Look for accessibility enhancement logs:
   ```
   ✅ ARIA: CC button accessibility enhanced
   ✅ ARIA: Quality button accessibility enhanced
   🎯 ARIA Accessibility Status: {...}
   ```
4. Right-click on CC button → **Inspect**
5. Verify attributes in Elements panel:
   ```html
   role="button"
   aria-pressed="false"
   aria-label="Toggle captions, currently off"
   tabindex="0"
   ```
6. Click CC button to toggle captions
7. Verify `aria-pressed` changes to `"true"`
8. Verify `aria-label` updates with track name

**Manual Inspection Checklist:**
```
□ CC button has role="button"
□ CC button has aria-pressed attribute
□ CC button has aria-label
□ CC button has tabindex="0"
□ Quality button has role="button"
□ Quality button has aria-label
□ Quality button has tabindex="0"
□ Video container has role="region"
□ Video container has aria-label
□ Subtitle display has role="status"
□ Subtitle display has aria-live="polite"
□ Subtitle display has aria-atomic="true"
□ Connection status has role="status"
□ Connection status has aria-live="polite"
□ Status indicator has aria-hidden="true"
```

---

### Step 5: Browser Console Check (2 minutes)

**Instructions:**
1. Open Console (`F12` → Console tab)
2. Reload page
3. Verify no errors
4. Look for accessibility logs

**Expected Console Output:**
```
✅ ARIA: CC button accessibility enhanced
✅ ARIA: Quality button accessibility enhanced
🎯 ARIA Accessibility Status: {
  ccButtonAccessible: true,
  qualityButtonAccessible: true,
  ariaLiveRegions: 2,
  wcagCompliance: "WCAG 2.1 Level AA"
}
```

---

## 🎯 Optional: Screen Reader Testing (15 minutes)

**Only required if:**
- Automated tests show issues
- Final quality assurance needed
- Compliance verification required

### Using NVDA (Windows - Free)

**Installation:**
1. Download NVDA from https://www.nvaccess.org/download/
2. Install (5 minutes)
3. Start NVDA: `Ctrl + Alt + N`

**Testing Steps:**
1. Start NVDA
2. Navigate to `http://localhost:8000`
3. Press `Tab` to move to video player
4. Listen to announcements:
   - Should hear: "Live video player with caption and quality controls, region"
5. Tab to CC button
   - Should hear: "Toggle captions, currently off, button, not pressed"
6. Press `Space` to activate
   - Should hear: "Toggle captions, currently on showing [track name], button, pressed"
   - Should hear: "Captions ON: Showing [track name] track." (aria-live announcement)
7. Tab to Quality button
   - Should hear: "Change video quality, currently Auto, button"
8. Press `Space` to activate
   - Should hear: "Change video quality, currently [quality], button"
   - Should hear: "Quality set to: [quality]" (aria-live announcement)

**Expected Behavior:**
- ✅ All buttons announced as "button"
- ✅ CC button state announced (pressed/not pressed)
- ✅ Button labels are descriptive
- ✅ Dynamic content updates are announced
- ✅ Navigation is logical and intuitive

---

## 📊 Test Results Template

```markdown
## Accessibility Audit Results

**Date:** [Insert Date]
**Tester:** [Your Name]
**Branch:** Iustin-ARIA-Labeles
**Jira Tasks:** INRT-719, INRT-725, INRT-726

---

### 1. Lighthouse Accessibility Score

**Score:** ___/100

**Issues Found:**
- [List any issues or write "None"]

**Screenshot:** [Attach screenshot]

---

### 2. axe DevTools Results

**Violations Summary:**
- Critical: ___
- Serious: ___
- Moderate: ___
- Minor: ___

**Details:**
[List specific violations or write "No violations found"]

**Screenshot:** [Attach screenshot]

---

### 3. Keyboard Navigation

**Test Results:**
- Tab navigation: ✅/❌
- Focus indicators: ✅/❌
- Enter activation: ✅/❌
- Space activation: ✅/❌
- No focus traps: ✅/❌

**Issues Found:**
[List any issues or write "None"]

---

### 4. ARIA Attributes

**Verification Results:**
- All ARIA roles present: ✅/❌
- ARIA labels descriptive: ✅/❌
- aria-live regions working: ✅/❌
- Dynamic updates working: ✅/❌

**Console Logs:**
[Paste console output]

---

### 5. Screen Reader Testing (Optional)

**Tool Used:** [NVDA / JAWS / VoiceOver / Not tested]

**Results:**
[Describe screen reader experience or write "Not tested"]

---

### 6. Overall Assessment

**WCAG 2.1 Level AA Compliance:** ✅ PASS / ❌ FAIL

**Acceptance Criteria Met:**
- [ ] UI controls expose correct ARIA roles
- [ ] UI controls expose correct ARIA labels
- [ ] Accessibility audit passes with no critical issues

**Recommendations:**
[Any suggestions for improvement or write "None"]

**Ready for Production:** YES / NO
```

---

## 🔧 Troubleshooting

### Issue: Buttons not found in console
**Solution:** Check if JW Player loaded correctly. The timeout may need adjustment.

### Issue: Focus outline not visible
**Solution:** Check browser zoom level and CSS specificity.

### Issue: aria-live not announcing
**Solution:** Ensure screen reader is running and aria-live region exists before content changes.

### Issue: Lighthouse score below 90
**Solution:** Review individual issues in Lighthouse report and address each one.

---

## 📚 Resources

- [WCAG 2.1 Guidelines](https://www.w3.org/WAI/WCAG21/quickref/)
- [ARIA Authoring Practices](https://www.w3.org/WAI/ARIA/apg/)
- [axe DevTools Documentation](https://www.deque.com/axe/devtools/)
- [NVDA Screen Reader](https://www.nvaccess.org/)
- [JW Player Accessibility](https://docs.jwplayer.com/players/docs/players-web-player-accessibility)

---

## ✅ Sign-off

**Developer:** _______________  
**Date:** _______________

**QA Tester:** _______________  
**Date:** _______________

**Accessibility Compliance:** ✅ APPROVED / ❌ NEEDS WORK
```

**Next Steps:**
1. Review this document
2. Complete automated testing (Steps 1-5)
3. Fill out the test results template
4. Address any issues found
5. Re-test after fixes
6. Get final approval
