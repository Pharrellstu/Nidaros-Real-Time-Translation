FROM python:3.11-slim

# Install runtime dependencies
RUN apt-get update && \
    apt-get install -y ffmpeg wget && \
    rm -rf /var/lib/apt/lists/*

# Create working directory
WORKDIR /opt/whisper

# Copy requirements file
COPY requirements.txt .

# Install Python dependencies
RUN pip install --no-cache-dir -r requirements.txt

# Create models directory
RUN mkdir -p /opt/whisper/models

# Download the small.en model at build time for faster startup
# The model will be cached by faster-whisper
RUN python -c "from faster_whisper import WhisperModel; model = WhisperModel('small.en', device='cpu', compute_type='int8', download_root='/opt/whisper/models')"

# Copy application code
COPY app/ /opt/whisper/app/

# Set environment variables
ENV MODEL_NAME=small.en
ENV DEVICE=cpu
ENV COMPUTE_TYPE=int8
ENV MODEL_DIR=/opt/whisper/models
ENV PORT=5001

# Expose the application port
EXPOSE 5001

# Run the FastAPI application
CMD ["python", "/opt/whisper/app/run.py"]