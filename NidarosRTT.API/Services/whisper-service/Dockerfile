FROM ubuntu:22.04

# Install dependencies
RUN apt-get update && apt-get install -y \
    build-essential \
    cmake \
    ffmpeg \
    python3 \
    python3-pip \
    git \
    wget \
    curl \
    && rm -rf /var/lib/apt/lists/*

# Clone whisper.cpp
WORKDIR /app
RUN git clone https://github.com/ggerganov/whisper.cpp.git
WORKDIR /app/whisper.cpp

# Build whisper.cpp
RUN make

# Download a model (using base.en as it's smaller for testing)
RUN apt-get update && apt-get install -y wget && \
    ./models/download-ggml-model.sh base.en

# Create a script to process audio
RUN echo '#!/bin/bash\n\
echo "Whisper transcription service starting..."\n\
echo "Watching /data for .wav files"\n\
mkdir -p /data/processed\n\
while true; do\n\
  for audio_file in /data/*.wav; do\n\
    if [ -f "$audio_file" ]; then\n\
      filename=$(basename "$audio_file")\n\
      if [ ! -f "/data/processed/$filename.done" ]; then\n\
        echo ""\n\
        echo "========================================"\n\
        echo "Processing: $filename"\n\
        echo "========================================"\n\
        ffmpeg -i "$audio_file" -ar 16000 -ac 1 -c:a pcm_s16le "/tmp/temp_16k.wav" -y 2>/dev/null\n\
        echo ""\n\
        echo "Transcription:"\n\
        /app/whisper.cpp/build/bin/whisper-cli -m /app/whisper.cpp/models/ggml-base.en.bin -f /tmp/temp_16k.wav -nt -np 2>&1\n\
        echo ""\n\
        echo "========================================"\n\
        rm -f /tmp/temp_16k.wav\n\
        touch "/data/processed/$filename.done"\n\
        rm -f "$audio_file"\n\
        echo "Deleted: $filename"\n\
      fi\n\
    fi\n\
  done\n\
  sleep 1\n\
done' > /app/process.sh && chmod +x /app/process.sh

WORKDIR /app/whisper.cpp
CMD ["/app/process.sh"]