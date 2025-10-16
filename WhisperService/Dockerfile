# Stage 1: Build Whisper
FROM ubuntu:22.04 AS build

# Install dependencies for building Whisper
RUN apt-get update && \
    apt-get install -y build-essential cmake git wget && \
    rm -rf /var/lib/apt/lists/*

# Set working directory
WORKDIR /opt/whisper

# Clone whisper.cpp (or copy it if it's in your context)
RUN git clone https://github.com/ggerganov/whisper.cpp.git .

# Build the shared library and CLI
RUN cmake -B build -DWHISPER_BUILD_SHARED=ON && \
    cmake --build build --config Release && \
    echo "=== Build output ===" && \
    ls -la build/ && \
    echo "=== Build/bin output ===" && \
    ls -la build/bin/ && \
    echo "=== Looking for libwhisper ===" && \
    find build/ -name "libwhisper*"

# Stage 2: Runtime
FROM python:3.11-slim

# Install runtime dependencies
RUN apt-get update && \
    apt-get install -y ffmpeg && \
    rm -rf /var/lib/apt/lists/*

# Copy whisper binary and shared libraries
COPY --from=build /opt/whisper/build/bin/whisper-cli /usr/local/bin/whisper-cli
COPY --from=build /opt/whisper/build/src/libwhisper.so* /usr/local/lib/
COPY --from=build /opt/whisper/build/ggml/src/libggml*.so* /usr/local/lib/

# Debug: Show what was copied
RUN echo "=== Files copied to /usr/local/lib ===" && \
    ls -la /usr/local/lib/libwhisper* || echo "No libwhisper files found!"

# Create symlinks for any version-specific shared libraries
RUN cd /usr/local/lib && \
    echo "=== Creating symlinks ===" && \
    for lib in libwhisper.so* libggml*.so*; do \
        echo "Processing: $lib"; \
        if [ -f "$lib" ]; then \
            base=$(echo "$lib" | sed 's/\.so.*/\.so/'); \
            if [ "$lib" != "$base" ] && [ "$lib" != "${base}.1" ]; then \
                echo "Creating symlink: $lib -> ${base}.1"; \
                ln -sf "$lib" "${base}.1"; \
                echo "Creating symlink: $lib -> $base"; \
                ln -sf "$lib" "$base"; \
            fi; \
        fi; \
    done && \
    echo "=== After symlink creation ===" && \
    ls -la /usr/local/lib/lib{whisper,ggml}* || echo "No libraries found!"

# Ensure the shared library path is recognized
ENV LD_LIBRARY_PATH=/usr/local/lib
RUN ldconfig && chmod +x /usr/local/bin/whisper-cli

# Verification - show what libraries are available and what whisper-cli needs
RUN echo "=== Library dependencies of whisper-cli ===" && \
    ldd /usr/local/bin/whisper-cli

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

# Run the FastAPI app
ENTRYPOINT ["python", "/opt/whisper/run.py"]