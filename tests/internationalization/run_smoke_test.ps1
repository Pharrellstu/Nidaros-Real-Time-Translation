# 🔥 Internationalization Smoke Test
# Quick 5-minute validation of multi-language caption functionality

Write-Host "=" -ForegroundColor Cyan -NoNewline; Write-Host ("=" * 59) -ForegroundColor Cyan
Write-Host "🔥 I18n Smoke Test - Quick Validation" -ForegroundColor Cyan
Write-Host "=" -ForegroundColor Cyan -NoNewline; Write-Host ("=" * 59) -ForegroundColor Cyan
Write-Host ""

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
Set-Location $projectRoot

# Step 1: Check Docker is running
Write-Host "Step 1: Checking Docker..." -ForegroundColor Yellow
try {
    docker ps | Out-Null
    Write-Host "  ✅ Docker is running" -ForegroundColor Green
} catch {
    Write-Host "  ❌ Docker is not running. Please start Docker Desktop." -ForegroundColor Red
    exit 1
}

# Step 2: Start services
Write-Host "`nStep 2: Starting translation services..." -ForegroundColor Yellow
Write-Host "  (This may take 30-60 seconds on first run)" -ForegroundColor Gray

docker-compose -f docker-compose-translate.yaml up -d 2>&1 | Out-Null

Write-Host "  ⏳ Waiting for services to initialize (30s)..." -ForegroundColor Gray
Start-Sleep -Seconds 30

# Step 3: Verify services
Write-Host "`nStep 3: Verifying services..." -ForegroundColor Yellow

$services = @("wse.docker", "whisper.server", "libretranslate.server", "webplayer")
$allRunning = $true

foreach ($service in $services) {
    $status = docker ps --filter "name=$service" --format "{{.Status}}" 2>$null
    if ($status -like "*Up*") {
        Write-Host "  ✅ $service" -ForegroundColor Green
    } else {
        Write-Host "  ❌ $service (not running)" -ForegroundColor Red
        $allRunning = $false
    }
}

if (-not $allRunning) {
    Write-Host "`n❌ Some services failed to start. Check logs:" -ForegroundColor Red
    Write-Host "   docker-compose -f docker-compose-translate.yaml logs" -ForegroundColor Gray
    exit 1
}

# Step 4: Check LibreTranslate languages
Write-Host "`nStep 4: Checking LibreTranslate configuration..." -ForegroundColor Yellow

try {
    $response = Invoke-RestMethod -Uri "http://localhost:5001/languages" -TimeoutSec 10
    $langCodes = $response | ForEach-Object { $_.code }
    
    $expectedLangs = @("nl", "de", "en", "uk", "ar")
    $foundLangs = @()
    
    foreach ($lang in $expectedLangs) {
        if ($langCodes -contains $lang) {
            Write-Host "  ✅ $lang" -ForegroundColor Green
            $foundLangs += $lang
        } else {
            Write-Host "  ⚠️  $lang (not found)" -ForegroundColor Yellow
        }
    }
    
    if ($foundLangs.Count -ge 3) {
        Write-Host "  ✅ Found $($foundLangs.Count)/5 expected languages" -ForegroundColor Green
    } else {
        Write-Host "  ⚠️  Only $($foundLangs.Count)/5 languages available" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  ⚠️  LibreTranslate not responding (may still be loading models)" -ForegroundColor Yellow
}

# Step 5: Check frontend
Write-Host "`nStep 5: Checking frontend..." -ForegroundColor Yellow

try {
    $response = Invoke-WebRequest -Uri "http://localhost:8000" -UseBasicParsing -TimeoutSec 5
    if ($response.Content -like "*jwplayer*") {
        Write-Host "  ✅ Frontend accessible with JW Player" -ForegroundColor Green
    } else {
        Write-Host "  ⚠️  Frontend accessible but player not detected" -ForegroundColor Yellow
    }
} catch {
    Write-Host "  ❌ Frontend not accessible at http://localhost:8000" -ForegroundColor Red
}

# Final instructions
Write-Host "`n" -NoNewline
Write-Host "=" -ForegroundColor Cyan -NoNewline; Write-Host ("=" * 59) -ForegroundColor Cyan
Write-Host "✅ Smoke Test Setup Complete!" -ForegroundColor Green
Write-Host "=" -ForegroundColor Cyan -NoNewline; Write-Host ("=" * 59) -ForegroundColor Cyan
Write-Host ""

Write-Host "📝 Next Steps (Manual Test):" -ForegroundColor Yellow
Write-Host ""
Write-Host "1. Open OBS Studio" -ForegroundColor White
Write-Host "   Settings → Stream:" -ForegroundColor Gray
Write-Host "   - Server: rtmp://localhost:1935/whisper" -ForegroundColor Gray
Write-Host "   - Stream Key: testStream" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Add audio source (microphone or media file)" -ForegroundColor White
Write-Host ""
Write-Host "3. Start Streaming in OBS" -ForegroundColor White
Write-Host ""
Write-Host "4. Open browser: http://localhost:8000" -ForegroundColor White
Write-Host ""
Write-Host "5. Click CC button and verify multiple language tracks appear" -ForegroundColor White
Write-Host ""
Write-Host "Expected: Off, Dutch (nl), German (de), English (en), Ukrainian (uk), Arabic (ar)" -ForegroundColor Gray
Write-Host ""

Write-Host "🔍 Troubleshooting:" -ForegroundColor Yellow
Write-Host "  - If no tracks appear: docker logs whisper.server --tail=50" -ForegroundColor Gray
Write-Host "  - If player doesn't load: docker logs wse.docker --tail=100" -ForegroundColor Gray
Write-Host "  - Full logs: docker-compose -f docker-compose-translate.yaml logs" -ForegroundColor Gray
Write-Host ""

Write-Host "⏱️  Total time: ~5-10 minutes for full test" -ForegroundColor Cyan
Write-Host ""
