# How to Configure a New Wowza Application for Whisper Transcription

This guide explains how to manually configure a new Wowza Streaming Engine application to work with the Whisper transcription system.

## Prerequisites

*   The Wowza server and Whisper container must be running (via `docker-compose up`).
*   You have created a new application (e.g., `whisperdemo`) using the Wowza Engine Manager.

## Configuration Steps

You need to edit the `Application.xml` file for your specific application.

### 1. Locate the Configuration File

Navigate to the configuration folder for your new application. If you are running this project locally with the default Docker setup, the file will be located at:

`[Project Root]/wse/conf/[YourApplicationName]/Application.xml`

*Example:* `C:/Users/Pharrell/Desktop/Nidaros-Real-Time-Translation/wse/conf/whisperdemo/Application.xml`

### 2. Add the Custom Module

Open `Application.xml` in a text editor. Find the `<Modules>` section (usually near the bottom of the file) and add the following entry to the list:

```xml
<Module>
    <Name>whisperSpeechToText</Name>
    <Description>WhisperSpeechToText</Description>
    <Class>com.wowza.wms.plugin.captions.ModuleWhisperCaptions</Class>
</Module>
```

### 3. Add the Configuration Properties

Find the `<Properties>` section at the very bottom of the file (inside the closing `</Application>` tag). Add the following properties to configure the connection to the Whisper server:

```xml
<Properties>
    <!-- Enable the plugin for this app -->
    <Property>
        <Name>whisperCaptionsEnabled</Name>
        <Value>true</Value>
        <Type>Boolean</Type>
    </Property>
    
    <!-- Hostname of the whisper container (defined in docker-compose.yaml) -->
    <Property>
        <Name>whisperSocketHost</Name>
        <Value>whisper.server</Value>
    </Property>
    
    <!-- Port of the whisper container -->
    <Property>
        <Name>whisperSocketPort</Name>
        <Value>3000</Value>
        <Type>Integer</Type>
    </Property>
    
    <!-- Buffer delay to sync captions (10000 ms = 10 seconds) -->
    <Property>
        <Name>captionHandlerStreamDelay</Name>
        <Value>10000</Value>
        <Type>Integer</Type>
    </Property>
    
    <!-- Optional: Enable debug logging -->
    <Property>
        <Name>captionHandlerDebug</Name>
        <Value>false</Value>
        <Type>Boolean</Type>
    </Property>
</Properties>
```

### 4. Restart the Application

For the changes to take effect, you must restart the application.

1.  Go to the **Wowza Engine Manager** (http://localhost:8088).
2.  Navigate to **Applications** -> **[Your Application]**.
3.  Click **Restart** (or restart the entire server).

## Alternative: Using Wowza Engine Manager

You can also perform these steps via the web interface instead of editing the XML file directly.

1.  Go to **Applications** -> **[Your Application]** -> **Modules**.
2.  Click **Edit** -> **+ Add Module**.
    *   **Name**: `whisperSpeechToText`
    *   **Class**: `com.wowza.wms.plugin.captions.ModuleWhisperCaptions`
3.  Go to **Properties** -> **Custom** -> **Edit** -> **+ Add Custom Property**.
    *   Add the properties listed in step 3 above (Name, Value, and Type).
