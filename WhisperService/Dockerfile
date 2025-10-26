# Stage 1: Build Whisper from source
FROM ubuntu:22.04 AS build

# Install build dependencies
RUN apt-get update && \
    apt-get install -y build-essential cmake git wget && \
    rm -rf /var/lib/apt/lists/*

# Set working directory and clone the repository
WORKDIR /opt/whisper
RUN git clone https://github.com/ggerganov/whisper.cpp.git .
# Build the shared library and the command-line interface
RUN cmake -B build -DWHISPER_BUILD_SHARED=ON && \
    cmake --build build --config Release

# -----------------------------------------------------------------------------

# Stage 2: Create the final Python runtime image
FROM python:3.11-slim

# Install runtime dependencies (ffmpeg and wget for downloading the model)
RUN apt-get update && \
    apt-get install -y ffmpeg wget && \
    rm -rf /var/lib/apt/lists/*

# Copy the compiled binary and shared libraries from the build stage
COPY --from=build /opt/whisper/build/bin/whisper-cli /usr/local/bin/whisper-cli
COPY --from=build /opt/whisper/build/src/libwhisper.so* /usr/local/lib/
COPY --from=build /opt/whisper/build/ggml/src/libggml*.so* /usr/local/lib/

# Make the binary executable and configure dynamic library links.
# ldconfig creates the necessary links and cache for the system to find the .so files.
# This replaces the complex and unnecessary for-loop from the original file.
RUN chmod +x /usr/local/bin/whisper-cli && \
    ldconfig

# Set the library path environment variable
ENV LD_LIBRARY_PATH=/usr/local/lib

# --- START CHANGES ---

# Create the models directory
RUN mkdir -p /opt/whisper/models

# Download the model directly into the image
# This replaces the old "COPY models /opt/whisper/models" line
RUN wget -O /opt/whisper/models/ggml-base.en.bin https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.en.bin

# Copy your application code
COPY app/run.py /opt/whisper/run.py

# --- END CHANGES ---

# Install Python dependencies
RUN pip install --no-cache-dir fastapi uvicorn python-multipart requests

# Set environment variables for the application
ENV MODEL_PATH=/opt/whisper/models/ggml-base.en.bin
ENV PORT=5001

# Expose the application port
EXPOSE 5001

# Define the entrypoint to run the FastAPI app
ENTRYPOINT ["python", "/opt/whisper/run.py"]