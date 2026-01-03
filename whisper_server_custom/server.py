import socket
import threading
import json
import numpy as np
from faster_whisper import WhisperModel
import os
import time
import queue

# Configuration
HOST = '0.0.0.0'
PORT = 3000
MODEL_SIZE = os.environ.get("MODEL", "tiny.en")
MIN_CHUNK_SIZE = float(os.environ.get("MIN_CHUNK_SIZE", "1.0"))

print(f"Loading model {MODEL_SIZE}...")
# Run on CPU for now as per docker-compose
# compute_type="int8" is faster on CPU
model = WhisperModel(MODEL_SIZE, device="cpu", compute_type="int8")
print("Model loaded.")

def transcriber_thread(conn, audio_queue, stop_event):
    audio_buffer = np.array([], dtype=np.float32)
    buffer_offset_seconds = 0.0

    while not stop_event.is_set():
        try:
            # Get new audio data
            new_data = []
            while True:
                try:
                    chunk = audio_queue.get_nowait()
                    new_data.append(chunk)
                except queue.Empty:
                    break

            if new_data:
                new_audio = np.concatenate(new_data)
                audio_buffer = np.concatenate((audio_buffer, new_audio))

            # If buffer is too short, wait
            current_duration = len(audio_buffer) / 16000.0
            if current_duration < MIN_CHUNK_SIZE:
                time.sleep(0.1)
                continue

            # Transcribe
            # We process the buffer and clear it.
            # Note: In a production system, you'd want a sliding window (VAD)
            # to avoid cutting words in half.

            segments, info = model.transcribe(audio_buffer, beam_size=1, language="en")

            for segment in segments:
                response = {
                    "language": "en",
                    "text": segment.text,
                    "start": buffer_offset_seconds + segment.start,
                    "end": buffer_offset_seconds + segment.end
                }
                try:
                    # Send JSON object
                    conn.sendall(json.dumps(response).encode('utf-8'))
                    # Optional: Send a newline if the client needs it,
                    # but Jackson usually handles concatenated JSONs.
                    # conn.sendall(b'\n')
                except OSError:
                    return

            # Update offset
            buffer_offset_seconds += current_duration
            audio_buffer = np.array([], dtype=np.float32)

        except Exception as e:
            print(f"Transcriber error: {e}")
            break

        time.sleep(0.1)

def handle_client(conn, addr):
    print(f"Connected: {addr}")
    audio_queue = queue.Queue()
    stop_event = threading.Event()

    t = threading.Thread(target=transcriber_thread, args=(conn, audio_queue, stop_event))
    t.start()

    try:
        while True:
            data = conn.recv(4096)
            if not data:
                break

            # Convert PCM 16-bit LE to float32
            # np.frombuffer is fast
            # 16-bit PCM is signed (-32768 to 32767)
            audio_chunk = np.frombuffer(data, dtype=np.int16).astype(np.float32) / 32768.0
            audio_queue.put(audio_chunk)

    except Exception as e:
        print(f"Connection error: {e}")
    finally:
        stop_event.set()
        t.join()
        conn.close()
        print(f"Disconnected: {addr}")

def start_server():
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as s:
        s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        s.bind((HOST, PORT))
        s.listen()
        print(f"Listening on {HOST}:{PORT}")

        while True:
            conn, addr = s.accept()
            t = threading.Thread(target=handle_client, args=(conn, addr))
            t.start()

if __name__ == "__main__":
    start_server()
