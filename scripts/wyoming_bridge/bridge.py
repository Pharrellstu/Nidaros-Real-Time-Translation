#!/usr/bin/env python3
"""
Wyoming bridge (library-based):
- Pulls audio from a Wowza HLS stream using ffmpeg and reads raw PCM from stdout.
- Uses the official Wyoming Python library to send STT events to Faster-Whisper (Wyoming server on :10300).
- Emits one JSON line per fixed-length segment with absolute/relative times and text.

Notes
- This avoids invoking `wyoming-cli` and does not require a virtualenv. Install the library system-wide or per-user.
- Install options (pick one):
  - pipx:     pipx install wyoming
  - user:     python3 -m pip install --user wyoming
  - system:   sudo python3 -m pip install wyoming

Usage example:
  python3 scripts/wyoming_bridge/bridge.py \
    --hls-url http://localhost/live/stream/playlist.m3u8 \
    --wyoming-addr localhost:10300 \
    --segment-sec 2.0 \
    --lang nl
"""

import argparse
import contextlib
import asyncio
import json
import signal
import sys
import time
from typing import Optional

try:
    from wyoming.client import AsyncClient
    from wyoming.audio import AudioStart, AudioChunk
    from wyoming.event import Event
except Exception as e:  # pragma: no cover
    sys.stderr.write(
        "Error: Python package 'wyoming' is not installed.\n"
        "Install it with one of:\n"
        "  pipx install wyoming\n"
        "  python3 -m pip install --user wyoming\n"
        "  sudo python3 -m pip install wyoming\n"
    )
    raise


RATE = 16000  # Hz
WIDTH = 2     # bytes (s16le)
CHANNELS = 1  # mono


async def run_ffmpeg_pcm(hls_url: str) -> asyncio.subprocess.Process:
    """Start ffmpeg that decodes HLS to raw s16le PCM on stdout."""
    cmd = [
        "ffmpeg",
        "-loglevel", "error",
        "-i", hls_url,
        "-vn",
        "-ac", str(CHANNELS),
        "-ar", str(RATE),
        "-f", "s16le",
        "pipe:1",
    ]
    proc = await asyncio.create_subprocess_exec(
        *cmd, stdout=asyncio.subprocess.PIPE
    )
    assert proc.stdout is not None
    return proc


def _tcp_uri(addr: str) -> str:
    """Convert host:port to tcp:// URI if needed."""
    if addr.startswith("tcp://") or addr.startswith("unix://"):
        return addr
    # assume host:port
    host, _, port = addr.partition(":")
    if not port:
        raise ValueError("--wyoming-addr must be host:port or tcp://host:port")
    return f"tcp://{host}:{port}"


async def transcribe_once(
    client_uri: str,
    pcm: bytes,
    language: Optional[str],
    segment_ms_base: int,
) -> str:
    """Send a single PCM segment to Wyoming and return the final transcript text.

    We send: transcribe -> audio-start -> one or more audio-chunk -> audio-stop,
    then read transcript(-chunk) until transcript-stop or EOF.
    """
    text_parts: list[str] = []
    final_text: str = ""

    async with AsyncClient.from_uri(client_uri) as client:
        # Request transcription
        data = {}
        if language:
            data["language"] = language
        await client.write_event(Event(type="transcribe", data=data))

        # Audio start
        await client.write_event(
            AudioStart(
                rate=RATE,
                width=WIDTH,
                channels=CHANNELS,
                timestamp=segment_ms_base,
            ).event()
        )

        # Stream audio in smaller chunks to avoid oversized frames
        bytes_per_second = RATE * WIDTH * CHANNELS
        chunk_bytes = max(bytes_per_second // 10, 3200)  # ~100ms or >=3200B
        for i in range(0, len(pcm), chunk_bytes):
            chunk = pcm[i : i + chunk_bytes]
            # Approximate timestamp progression
            ts = segment_ms_base + int((i / bytes_per_second) * 1000)
            await client.write_event(
                AudioChunk(
                    rate=RATE,
                    width=WIDTH,
                    channels=CHANNELS,
                    audio=chunk,
                    timestamp=ts,
                ).event()
            )

        # End of audio
        await client.write_event(Event(type="audio-stop", data={"timestamp": segment_ms_base + int((len(pcm) / bytes_per_second) * 1000)}))

        # Read responses until transcript-stop or EOF (with timeout)
        deadline = time.monotonic() + 2.0  # seconds after audio-stop
        while True:
            try:
                read_task = asyncio.create_task(client.read_event())
                timeout = max(0.0, deadline - time.monotonic())
                event = await asyncio.wait_for(read_task, timeout=timeout if timeout > 0 else 0.1)
            except asyncio.TimeoutError:
                break

            if event is None:
                break

            etype = event.type
            data = event.data or {}
            if etype == "transcript-chunk":
                part = data.get("text", "").strip()
                if part:
                    text_parts.append(part)
            elif etype == "transcript":
                final_text = data.get("text", final_text)
            elif etype == "transcript-stop":
                break

    combined = " ".join(text_parts).strip()
    return combined or (final_text.strip() if final_text else "")


async def main_async() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--hls-url", required=True, help="Wowza HLS playlist URL (e.g., http://localhost/live/stream/playlist.m3u8)")
    ap.add_argument("--wyoming-addr", default="localhost:10300", help="Faster-Whisper Wyoming server (host:port or tcp://host:port)")
    ap.add_argument("--segment-sec", type=float, default=2.0, help="Segment length in seconds")
    ap.add_argument("--lang", default="nl", help="Language code (e.g., nl)")
    args = ap.parse_args()

    client_uri = _tcp_uri(args.wyoming_addr)
    proc = await run_ffmpeg_pcm(args.hls_url)

    loop = asyncio.get_event_loop()
    stop_flag = asyncio.Event()

    def _signal_handler(*_):
        stop_flag.set()

    for sig in (signal.SIGINT, signal.SIGTERM):
        try:
            loop.add_signal_handler(sig, _signal_handler)
        except NotImplementedError:
            pass

    bytes_per_second = RATE * WIDTH * CHANNELS
    bytes_per_segment = max(1, int(args.segment_sec * bytes_per_second))
    assert proc.stdout is not None

    segment_index = 0
    abs_offset = 0.0

    try:
        while not stop_flag.is_set():
            # Read one segment of PCM (blocks until filled)
            try:
                pcm = await proc.stdout.readexactly(bytes_per_segment)
            except asyncio.IncompleteReadError as e:
                pcm = e.partial
                if not pcm:
                    break

            segment_ms_base = int(time.time() * 1000)
            text = await transcribe_once(client_uri, pcm, args.lang, segment_ms_base)

            # Without word timings from server, we approximate cue to the full segment window
            out = {
                "segment_index": segment_index,
                "rel_start": 0.0,
                "rel_end": round(args.segment_sec, 3),
                "abs_start": round(abs_offset, 3),
                "abs_end": round(abs_offset + args.segment_sec, 3),
                "text": text,
                "source": "faster-whisper/wyoming",
            }
            print(json.dumps(out, ensure_ascii=False), flush=True)

            segment_index += 1
            abs_offset += args.segment_sec
    finally:
        try:
            if proc.returncode is None:
                proc.terminate()
                with contextlib.suppress(Exception):
                    await asyncio.wait_for(proc.wait(), timeout=5)
        except Exception:
            pass


def main() -> None:
    try:
        asyncio.run(main_async())
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
