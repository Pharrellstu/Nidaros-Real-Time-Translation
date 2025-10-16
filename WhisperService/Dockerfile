# Stage 1: Build Whisper
FROM ubuntu:22.04 AS build

# Install build dependencies
RUN apt-get update && \
    apt-get install -y git cmake build-essential && \
    rm -rf /var/lib/apt/lists/*

# Clone Whisper.cpp
RUN git clone https://github.com/ggerganov/whisper.cpp /opt/whisper
WORKDIR /opt/whisper

# Build whisper-cli (shared lib)
RUN cmake -B build -DWHISPER_BUILD_SHARED=ON && \
    cmake --build build --config Release

# Stage 2: Runtime
FROM python:3.11-slim

# Install runtime dependencies
RUN apt-get update && \
    apt-get install -y ffmpeg && \
    rm -rf /var/lib/apt/lists/*

# Copy whisper-cli binary and its shared library
COPY --from=build /opt/whisper/build/bin/whisper-cli /usr/local/bin/whisper-cli
COPY --from=build /opt/whisper/build/libwhisper.so* /usr/local/lib/

# Add this line to tell the linker where to find the library
ENV LD_LIBRARY_PATH /usr/local/lib

RUN ldconfig
RUN chmod +x /usr/local/bin/whisper-cli

# Copy models
COPY models /opt/whisper/models

# Copy FastAPI app
COPY app/run.py /opt/whisper/run.py

# Install Python dependencies
RUN pip install --no-cache-dir fastapi uvicorn python-multipart

# Environment variables
ENV MODEL_PATH=/opt/whisper/models/ggml-base.en.bin
ENV PORT=5001

# Expose port
EXPOSE 5001

ENTRYPOINT ["python", "/opt/whisper/run.py"]
