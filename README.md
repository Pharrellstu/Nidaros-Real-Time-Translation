

# Nidaros Real-Time Translation (RTT) Service

This project provides a complete, end-to-end system for generating live subtitles from any RTMP video stream. It captures an RTMP stream (e.g., from OBS), uses Wowza Streaming Engine to re-stream it, captures audio chunks with a .NET service, transcribes them using a local Whisper.cpp service, and broadcasts the subtitles to a web client via SignalR.

## Features

* **Wowza Streaming Engine:** Ingests the RTMP stream and makes it available via HLS/RTSP.
* **.NET API (`api-service`):**
    * Captures audio chunks from the stream using FFmpeg.
    * Manages a job queue for transcription tasks.
    * Serves a simple HTML/JS web client.
    * Hosts a SignalR hub to broadcast transcriptions.
* **Whisper (`whisper-service`):** A lightweight Python/FastAPI service that wraps the `whisper.cpp` binary for fast, local transcription.
* **Web Client:** A simple `index.html` page that plays the HLS stream and displays the live subtitles.

## Project Structure

The entire application is orchestrated using Docker Compose.

* `api-service`: The main .NET application.
* `whisper-service`: The Python-based Whisper transcription service.
* `wowza-trial`: The Wowza Streaming Engine.
* `wowza-manager`: The web-based manager for Wowza.

## Prerequisites

* [Docker Desktop](https://www.docker.com/products/docker-desktop/)
* [Git](https://git-scm.com/)
* A streaming tool, such as [OBS Studio](https://obsproject.com/) (Recommended)

## How to Run

### 1. Clone the Repository

```bash
git clone https://github.com/Pharrellstu/Nidaros-Real-Time-Translation.git
cd Nidaros-Real-Time-Translation
````

### 2\. Create Wowza Environment File

This project uses an `.env` file to supply credentials to the Wowza Streaming Engine.

1.  Create a new file named `.env` in the root of the project.


2. Add your Wowza license key and desired admin credentials:

    ```ini
    # .env
    WSE_LICENSE_KEY=your-wowza-license-key-here
    ADMIN_USERNAME=admin
    ADMIN_PASSWORD=your-secure-password
    ```

### 3\. Configure Your Streaming Software (OBS)

You must send an RTMP stream to the Wowza container.

1.  Open OBS Studio.

2.  Go to **Settings \> Stream**.

3.  Set **Service** to `Custom...`.

4.  Set **Server** to `rtmp://localhost:1935/live`.

5.  Set **Stream Key** to `OBSstream`. (This must match the `STREAM_URL` and Wowza setup).

6.  Go to **Settings \> Output**.

7.  Set **Output Mode** to `Advanced`.

8.  On the **Streaming** tab: (THIS STEP IS OPTIONAL)

    * Set **Keyframe Interval** to `2` (This is critical for HLS and fixes Wowza warnings).
    * Set **Rate Control** to `CBR`.

### 4\. Build and Run the Containers

Run the following command from the root of the project. This will build the .NET and Whisper images (including downloading the Whisper model) and start all services.

```bash
docker-compose up --build
```

Wait for all services to start. You may see some FFmpeg errors from `api-service-1`—this is normal. It just means it's waiting for your stream to start.

### 6\. Inject Wowza artificial delay
(makes sure that the video/audio stream are displayred with a delay, so our subtitles have time to get generated)
1. Log in to `http://localhost:8080` in your browser.
2. Select the default "live" application from the Applications menu.
3. On the overview of the app (NOT on the Monitoring tab) select properties
4. Search for "cupertinoChunkDurationTarget", it is in the Cupertino Streaming Packetizer section and modify it to have the value of 3000
5. Search for "cupertinoPlaylistChunkCount", it is in the Cupertino Streaming Packetizer section and modify it to have the value of 15
6. Make sure you save and you click the Restart Stream button on the top of the webpage


### 5\. Start Streaming

Once the containers are running, go to OBS and click **"Start Streaming"**.

You should see the logs in your `api-service-1` terminal change from errors to success messages like:
[cite\_start]`[FFMPEG SUCCESS] Captured 160096 bytes of audio` [cite: 78]
[cite\_start]`[CAPTURE] ✓ Queued: chunk_...` [cite: 79]
`[TRANSCRIPTION-...] ... → This is a test transcription.`

### 6\. View the Result

* **Live Subtitles:** Open `http://localhost:5032/index.html` in your browser. You should see the video stream playing with live subtitles appearing below.
* **Wowza Manager:** You can manage your Wowza server by visiting `http://localhost:8088`. (Log in with the `ADMIN_USERNAME` and `ADMIN_PASSWORD` you set in your `.env` file).

## Troubleshooting

* **Error: `Output file #0 does not contain any stream`**

    * This just means the `api-service` is running, but your RTMP stream from OBS is not active. **Start streaming from OBS** to fix this.

* **Warning: `Duration is out of bounds` in `wowza-trial-1` logs**

    * This means your **Keyframe Interval** in OBS is not set to `2` seconds. Stop your stream, fix the setting in **OBS \> Settings \> Output \> Streaming**, and start streaming again.

* **Subtitles don't appear**

    * Check the `api-service-1` logs. Are transcriptions appearing there?
    * Check the `whisper-service-1` logs. Are there any errors?
    * Open your browser's Developer Console (F12) on `http://localhost:5032`. Look for any SignalR connection errors. Ensure you fixed the `index.html` URL to point to `subtitlesHub` (lowercase 's') as noted in the setup.

<!-- end list -->

```
```