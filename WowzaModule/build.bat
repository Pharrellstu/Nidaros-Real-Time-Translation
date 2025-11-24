@echo off
REM Nidaros Wowza Module Build Script for Windows
REM This script builds the Java module and copies it to the wowza/lib directory

echo =========================================
echo Building Nidaros Wowza Audio Capture Module
echo =========================================

cd /d "%~dp0"

REM Clean previous builds
echo [1/3] Cleaning previous builds...
call gradle clean

REM Build the JAR
echo [2/3] Building JAR file...
call gradle build

REM Check if build was successful
if %ERRORLEVEL% EQU 0 (
    echo [3/3] Build successful!
    echo.
    echo JAR location: .\build\libs\nidaros-audio-capture-1.0.0.jar
    echo Copied to: ..\wowza\lib\nidaros-audio-capture-1.0.0.jar
    echo.
    echo Next steps:
    echo 1. Run: docker-compose down
    echo 2. Run: docker-compose up --build
    echo 3. Start streaming from OBS to rtmp://localhost:1935/live/OBSstream
    echo 4. Check Wowza logs for module initialization
    echo.
) else (
    echo Build failed! Check errors above.
    exit /b 1
)

pause
