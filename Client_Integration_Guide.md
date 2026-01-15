# Nidaros Real-Time Translation System - Integration Guide

## 1. Executive Summary

This document outlines the architecture, installation, and integration process for the Nidaros Real-Time Translation system. This solution is designed to add real-time Speech-to-Text (STT) and multi-language translation capabilities to an existing Wowza Streaming Engine (WSE) deployment.

The system captures audio from live streams, processes it through AI models (Whisper for transcription, LibreTranslate for translation), and injects the resulting captions back into the stream as WebVTT tracks for HLS playback.

## 2. Integration Strategy

For environments where Wowza Streaming Engine is already deployed on Azure, there are two primary integration paths.

**File Transfer Requirements**
Regardless of the chosen method, the entire project folder (excluding the `src` folder if preferred, but keeping `docker-compose` files, `conf`, `lib`, and cache folders) must be copied to the Azure server. The Docker containers rely on local volume mounts (e.g., `./model_cache`) to store AI models and configuration.

---

## 3. Installation Steps

### Option A: Full Rebuild (Recommended)
*Use this option to replace the existing Wowza installation with a fully pre-configured containerized environment. This method handles the build process automatically and is the most reliable way to ensure all components work together.*

#### Step 1: Prepare the Environment
1.  **Transfer Files**: Copy the entire project folder to the Azure VM.
2.  **License Key**: Set the Wowza License Key.
    ```bash
    export WSE_LICENSE_KEY=your-license-key-here
    ```

#### Step 2: Launch the Stack
Run the complete stack (Wowza + Whisper + LibreTranslate + Manager) using Docker Compose.

```bash
docker-compose -f docker-compose-translate.yaml up -d
```

This command will:
1.  Start a **Wowza Streaming Engine** container with the plugin pre-installed (mapped via volumes).
2.  Start the **Whisper** and **LibreTranslate** services.
3.  Start the **Wowza Manager**.

#### Step 3: Verify Access
*   **Wowza Manager**: `http://<server-ip>:8088` (Login: admin / password)
*   **Wowza Engine**: `http://<server-ip>:8087`

The `Application.xml` configuration is already handled via the volume mapping `./conf:/usr/local/WowzaStreamingEngine/conf.addon`.

---

### Option B: Sidecar Deployment (Alternative)
*Use this option to keep the existing Wowza installation and "attach" the translation capabilities to it.*

#### Step 1: Deploy AI Services
The Wowza plugin requires the backend AI services to be running. Deploy them using Docker.

1.  **Transfer Files**: Copy the project folder to the Azure VM.
2.  **Run the Services**:
    Navigate to the project folder and run the AI services. Use the `docker-compose-translate.yaml` file to target only the AI containers.
    ```bash
    docker-compose -f docker-compose-translate.yaml up -d whisper_server libretranslate_server
    ```
    *   *Note: Ensure ports 3000 (Whisper) and 5001 (LibreTranslate) are accessible from the Wowza server (localhost if on the same VM).*

#### Step 2: Install the Wowza Plugin
The plugin JAR file (`wse-plugin-caption-handlers-1.1.0.jar`) is required. There are two methods to obtain it:

**Method 1: Build Manually (Requires Wowza Libraries)**
1.  Open `gradle.properties` and update `wseLibDir` to point to the **actual Wowza installation's lib folder** on the server.
    *   **Linux Example**: `wseLibDir=/usr/local/WowzaStreamingEngine/lib`
    *   **Windows Example**: `wseLibDir=C:/Program Files (x86)/Wowza Media Systems/Wowza Streaming Engine 4.8.0/lib`
2.  Run the build command:
    ```bash
    ./gradlew clean build
    ```

**Method 2: Extract from Docker (If Manual Build Fails)**
If the manual build fails (often due to missing Wowza libraries on the local machine), use Docker to build the artifact:
1.  Run the Docker Compose command from Option A (Full Rebuild):
    ```bash
    docker-compose -f docker-compose-translate.yaml up -d
    ```
2.  Once the containers are running, the build process inside the container will have created the JAR file.
3.  Copy the JAR file from the project's `build/libs/` folder (which is mapped to the container).
4.  Stop the containers if the full stack is not needed: `docker-compose down`.

#### Step 3: Deploy the JAR
1.  **Copy JARs**:
    *   Take the `wse-plugin-caption-handlers-1.1.0.jar` file.
    *   Copy it to the existing Wowza `lib` folder (e.g., `/usr/local/WowzaStreamingEngine/lib`).
    *   **Important**: Ensure any required dependencies (Jackson, etc.) are also present in the `lib` folder.

#### Step 4: Configure Wowza Application
Edit the `Application.xml` for the live application (e.g., `conf/live/Application.xml`) to enable the plugin.

1.  **Add the Module**:
    Inside the `<Modules>` section, add:
    ```xml
    <Module>
        <Name>whisperSpeechToText</Name>
        <Description>WhisperSpeechToText</Description>
        <Class>com.wowza.wms.plugin.captions.ModuleWhisperCaptions</Class>
    </Module>
    ```

2.  **Configure Properties**:
    Add the following to the `<Properties>` section at the bottom of `Application.xml`:
    ```xml
    <Property>
        <Name>whisperCaptionsEnabled</Name>
        <Value>true</Value>
    </Property>
    <Property>
        <Name>whisperSocketHost</Name>
        <Value>localhost</Value> <!-- IP of Whisper Container -->
    </Property>
    <Property>
        <Name>whisperSocketPort</Name>
        <Value>3000</Value>
    </Property>
    <Property>
        <Name>captionHandlerStreamDelay</Name>
        <Value>10000</Value> <!-- Delay in ms to sync captions -->
    </Property>
    ```

3.  **Timed Text Configuration**:
    In the `<TimedText>` section, configure the languages:
    ```xml
    <Properties>
        <Property>
            <Name>captionLiveIngestLanguages</Name>
            <Value>en,fr,es,de</Value> <!-- Comma-separated ISO codes -->
        </Property>
    </Properties>
    ```

#### Step 5: Restart Wowza
Restart the Wowza Streaming Engine service to load the new plugin and configuration.

---

## 4. Configuration & Tuning

### Whisper Service Settings
Modify the environment variables in the `docker-compose` file for the `whisper_server`:
*   `MODEL`: `tiny.en`, `base`, `small`, `medium`, `large-v3`. (Larger models = better accuracy but higher latency/CPU usage).
*   `SOURCE_LANGUAGE`: The language spoken in the stream (e.g., `en`).
*   `REPORT_LANGUAGES`: Target languages for translation (e.g., `en,fr,es`).
*   `USE_GPU`: Set to `True` if the Azure VM has an NVIDIA GPU (highly recommended for production).

### Latency Management
*   **Stream Delay**: The STT process takes time. To ensure captions appear in sync with the video, the video stream is often delayed.
*   Adjust `captionHandlerStreamDelay` in `Application.xml` (default is 10000ms or 10s).
*   **Playback URL**: Configure the video player to use the delayed stream URL:
    `http://server:1935/app/streamName_delayed/playlist.m3u8`.

## 5. Verification

1.  **Start a Stream**: Push an RTMP stream to Wowza (e.g., `rtmp://server:1935/live/myStream`).
2.  **Check Logs**:
    *   Wowza Logs: Look for `ModuleWhisperCaptions` initialization and connection success.
    *   Whisper Logs: `docker logs whisper_server` should show audio chunks being received and text being generated.
3.  **Playback**:
    *   Open a player (like JW Player or the provided WebPlayer).
    *   Load the HLS URL: `http://server:1935/live/myStream_delayed/playlist.m3u8`.
    *   Verify that CC options are available and text is appearing.

## 6. Troubleshooting

*   **Stream Link Not Working**:
    *   If `http://server:1935/...` fails, try replacing `server` with `localhost` if testing locally on the server (e.g., `http://localhost:1935/...`).
    *   Ensure port 1935 is open in the Azure Network Security Group (NSG).
*   **No Captions**:
    *   Check if `whisper_server` is reachable from Wowza.
    *   Verify `captionHandlerStreamDelay` is sufficient.
*   **High Latency**:
    *   Use a smaller Whisper model (e.g., `tiny` or `base`).
    *   Enable GPU acceleration.
*   **Translation Errors**:
    *   Ensure `LibreTranslate` is running and the `TRANSLATE_HOST` is correctly set in the Whisper container environment.

---
*Generated for Nidaros Real-Time Translation Project*
