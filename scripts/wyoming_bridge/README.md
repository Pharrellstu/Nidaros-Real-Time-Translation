Wyoming Bridge (POC)

Purpose
- Pull audio from Wowza HLS and send it to the Faster-Whisper (Wyoming) server using the official Python library (no CLI).
- Emit JSON lines with transcript segments and timestamps.

Prereqs
- ffmpeg installed on host
- Python 3.10+
- Faster-Whisper container reachable on port 10300
- Python package: wyoming (install without a venv)
  - pipx:     pipx install wyoming
  - user:     python3 -m pip install --user wyoming
  - system:   sudo python3 -m pip install wyoming

Run
  python3 scripts/wyoming_bridge/bridge.py \
    --hls-url http://localhost/live/stream/playlist.m3u8 \
    --wyoming-addr localhost:10300 \
    --segment-sec 2.0 \
    --lang nl

Output
- JSON lines printed to stdout, e.g.:
  {"segment_index":0,"rel_start":0.0,"rel_end":2.0,"abs_start":0.0,"abs_end":2.0,"text":"We gaan het vandaag even helemaal anders doen.","source":"faster-whisper/wyoming"}
