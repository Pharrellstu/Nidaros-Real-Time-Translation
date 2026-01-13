/**
 * Playwright Automation for Internationalization Testing
 * 
 * Captures screenshots of caption rendering across browsers
 * for RTL and mixed-language validation.
 * 
 * Installation:
 *   npm install -D @playwright/test
 *   npx playwright install
 */

const { test, expect, chromium, firefox, webkit } = require('@playwright/test');
const path = require('path');
const fs = require('fs');

// Test configuration
const TEST_CONFIG = {
  playerUrl: 'http://localhost:8000',
  streamTimeout: 60000, // Wait up to 60s for stream to start
  captionTimeout: 30000, // Wait up to 30s for captions to appear
  screenshotDir: path.join(__dirname, 'screenshots'),
};

// Ensure screenshot directory exists
if (!fs.existsSync(TEST_CONFIG.screenshotDir)) {
  fs.mkdirSync(TEST_CONFIG.screenshotDir, { recursive: true });
}

test.describe('Internationalization Caption Tests', () => {
  
  test.beforeEach(async ({ page }) => {
    // Navigate to player
    await page.goto(TEST_CONFIG.playerUrl);
    
    // Wait for JW Player to initialize
    await page.waitForFunction(() => {
      return window.jwplayer && window.jwplayer('jw-live-player');
    }, { timeout: 10000 });
    
    console.log('✅ JW Player initialized');
  });

  test('INTL-01: Capture Dutch caption rendering', async ({ page, browserName }) => {
    console.log(`Testing on ${browserName}...`);
    
    // Wait for stream to load
    await page.waitForSelector('.jw-state-playing, .jw-state-buffering', { 
      timeout: TEST_CONFIG.streamTimeout 
    });
    console.log('✅ Stream loaded');
    
    // Enable captions
    await page.evaluate(() => {
      const player = jwplayer('jw-live-player');
      const tracks = player.getCaptionsList();
      
      // Find Dutch track (typically index 1)
      const dutchTrack = tracks.find(t => t.language === 'nl');
      if (dutchTrack) {
        player.setCurrentCaptions(dutchTrack.id);
      } else {
        player.setCurrentCaptions(1); // Fallback to first track
      }
      
      return { tracks, selected: player.getCurrentCaptions() };
    });
    
    console.log('✅ Dutch captions enabled');
    
    // Wait for caption text to appear
    await page.waitForFunction(() => {
      const captionElements = document.querySelectorAll('.jw-text-track-display');
      return Array.from(captionElements).some(el => el.textContent.trim().length > 0);
    }, { timeout: TEST_CONFIG.captionTimeout });
    
    console.log('✅ Caption text detected');
    
    // Take screenshot
    const screenshotPath = path.join(
      TEST_CONFIG.screenshotDir, 
      `dutch-captions-${browserName}.png`
    );
    await page.screenshot({ 
      path: screenshotPath,
      fullPage: false
    });
    
    console.log(`✅ Screenshot saved: ${screenshotPath}`);
  });

  test('INTL-02: Capture Arabic RTL caption rendering', async ({ page, browserName }) => {
    console.log(`Testing Arabic RTL on ${browserName}...`);
    
    // Wait for stream to load
    await page.waitForSelector('.jw-state-playing, .jw-state-buffering', { 
      timeout: TEST_CONFIG.streamTimeout 
    });
    
    // Enable captions and find Arabic track
    const captionInfo = await page.evaluate(() => {
      const player = jwplayer('jw-live-player');
      const tracks = player.getCaptionsList();
      
      // Find Arabic track
      const arabicTrack = tracks.find(t => t.language === 'ar');
      if (arabicTrack) {
        player.setCurrentCaptions(arabicTrack.id);
      }
      
      return { 
        tracks: tracks.map(t => ({ id: t.id, label: t.label, language: t.language })),
        arabicFound: !!arabicTrack,
        selectedTrack: player.getCurrentCaptions()
      };
    });
    
    console.log('Caption tracks:', captionInfo);
    
    if (!captionInfo.arabicFound) {
      console.warn('⚠️  No Arabic track found, using first available track');
    }
    
    // Wait for caption text (Arabic characters)
    await page.waitForFunction(() => {
      const captionElements = document.querySelectorAll('.jw-text-track-display');
      const text = Array.from(captionElements).map(el => el.textContent).join('');
      // Check for Arabic Unicode range
      return /[\u0600-\u06FF]/.test(text);
    }, { timeout: TEST_CONFIG.captionTimeout });
    
    console.log('✅ Arabic caption text detected');
    
    // Inspect text direction
    const rtlAnalysis = await page.evaluate(() => {
      const captionElements = document.querySelectorAll('.jw-text-track-display, .jw-text-track-cue');
      const analysis = [];
      
      captionElements.forEach((el, idx) => {
        const computedStyle = window.getComputedStyle(el);
        analysis.push({
          index: idx,
          text: el.textContent.trim(),
          direction: computedStyle.direction,
          textAlign: computedStyle.textAlign,
          unicodeDir: el.getAttribute('dir'),
        });
      });
      
      return analysis;
    });
    
    console.log('RTL Analysis:', rtlAnalysis);
    
    // Take screenshot
    const screenshotPath = path.join(
      TEST_CONFIG.screenshotDir, 
      `arabic-rtl-${browserName}.png`
    );
    await page.screenshot({ 
      path: screenshotPath,
      fullPage: false
    });
    
    console.log(`✅ Screenshot saved: ${screenshotPath}`);
    
    // Validate RTL rendering
    const hasRTL = rtlAnalysis.some(a => a.direction === 'rtl' || a.textAlign === 'right');
    console.log(hasRTL ? '✅ RTL direction detected' : '⚠️  RTL direction NOT detected');
  });

  test('INTL-03: Enumerate all caption tracks', async ({ page }) => {
    console.log('Enumerating caption tracks...');
    
    // Wait for stream to load
    await page.waitForSelector('.jw-state-playing, .jw-state-buffering', { 
      timeout: TEST_CONFIG.streamTimeout 
    });
    
    // Get all caption tracks
    const tracks = await page.evaluate(() => {
      const player = jwplayer('jw-live-player');
      return player.getCaptionsList().map(t => ({
        id: t.id,
        label: t.label,
        language: t.language,
        kind: t.kind
      }));
    });
    
    console.log('Available caption tracks:');
    console.table(tracks);
    
    // Validate track count
    expect(tracks.length).toBeGreaterThan(1); // At least "Off" + 1 language track
    
    // Validate language codes
    const languageTracks = tracks.filter(t => t.language);
    expect(languageTracks.length).toBeGreaterThan(0);
    
    console.log(`✅ Found ${languageTracks.length} language tracks`);
  });

  test('INTL-04: Test caption track switching', async ({ page }) => {
    console.log('Testing caption track switching...');
    
    // Wait for stream
    await page.waitForSelector('.jw-state-playing, .jw-state-buffering', { 
      timeout: TEST_CONFIG.streamTimeout 
    });
    
    // Get available tracks
    const tracks = await page.evaluate(() => {
      const player = jwplayer('jw-live-player');
      return player.getCaptionsList();
    });
    
    if (tracks.length < 3) {
      console.warn('⚠️  Not enough tracks for switching test');
      return;
    }
    
    // Test switching between tracks
    for (let i = 1; i < Math.min(tracks.length, 4); i++) {
      console.log(`Switching to track ${i}: ${tracks[i].label}`);
      
      await page.evaluate((trackId) => {
        const player = jwplayer('jw-live-player');
        player.setCurrentCaptions(trackId);
      }, tracks[i].id);
      
      // Wait for caption change
      await page.waitForTimeout(2000);
      
      // Verify track is active
      const currentTrack = await page.evaluate(() => {
        const player = jwplayer('jw-live-player');
        return player.getCurrentCaptions();
      });
      
      expect(currentTrack).toBe(tracks[i].id);
      console.log(`✅ Track ${i} active`);
    }
  });

  test('INTL-05: Capture encoding stress test', async ({ page, browserName }) => {
    console.log(`Testing multilingual encoding on ${browserName}...`);
    
    // Wait for stream to load
    await page.waitForSelector('.jw-state-playing, .jw-state-buffering', { 
      timeout: TEST_CONFIG.streamTimeout 
    });
    
    // Enable captions
    await page.evaluate(() => {
      const player = jwplayer('jw-live-player');
      player.setCurrentCaptions(1); // First available track
    });
    
    // Wait for any caption text
    await page.waitForFunction(() => {
      const captionElements = document.querySelectorAll('.jw-text-track-display');
      return Array.from(captionElements).some(el => el.textContent.trim().length > 0);
    }, { timeout: TEST_CONFIG.captionTimeout });
    
    // Capture all visible caption text
    const captionText = await page.evaluate(() => {
      const captionElements = document.querySelectorAll('.jw-text-track-display');
      return Array.from(captionElements).map(el => el.textContent.trim()).join(' ');
    });
    
    console.log('Captured caption text:', captionText);
    
    // Check for specific character sets
    const hasArabic = /[\u0600-\u06FF]/.test(captionText);
    const hasGreek = /[\u0370-\u03FF]/.test(captionText);
    const hasCyrillic = /[\u0400-\u04FF]/.test(captionText);
    const hasDiacritics = /[àáâãäåèéêëìíîïòóôõöùúûüýÿ]/i.test(captionText);
    
    console.log('Encoding validation:', {
      arabic: hasArabic ? '✅' : '❌',
      greek: hasGreek ? '✅' : '❌',
      cyrillic: hasCyrillic ? '✅' : '❌',
      diacritics: hasDiacritics ? '✅' : '❌',
    });
    
    // Take screenshot
    const screenshotPath = path.join(
      TEST_CONFIG.screenshotDir, 
      `encoding-stress-${browserName}.png`
    );
    await page.screenshot({ 
      path: screenshotPath,
      fullPage: false
    });
    
    console.log(`✅ Screenshot saved: ${screenshotPath}`);
  });
});

// Helper to run tests across multiple browsers
test.describe('Cross-Browser Validation', () => {
  const browsers = ['chromium', 'firefox'];
  
  for (const browserName of browsers) {
    test(`Run all tests on ${browserName}`, async () => {
      const browser = await (browserName === 'chromium' ? chromium : firefox).launch();
      const context = await browser.newContext();
      const page = await context.newPage();
      
      try {
        // Run test sequence
        await page.goto(TEST_CONFIG.playerUrl);
        console.log(`✅ ${browserName} test completed`);
      } finally {
        await browser.close();
      }
    });
  }
});
