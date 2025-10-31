# Stage 1 - build
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and projects
COPY ./*.sln ./
COPY NidarosRTT.API/*.csproj NidarosRTT.API/
COPY NidarosRTT.Core/*.csproj NidarosRTT.Core/
COPY NidarosRTT.Infrastructure/*.csproj NidarosRTT.Infrastructure/

# Restore only the projects referenced
WORKDIR /src/NidarosRTT.API
RUN dotnet restore "NidarosRTT.API.csproj"

# Copy everything and build
WORKDIR /src
COPY . .

# Publish API project
WORKDIR /src/NidarosRTT.API
RUN dotnet publish "NidarosRTT.API.csproj" -c Release -o /app/publish

# Stage 2 - runtime
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install FFmpeg and RNNoise build dependencies
RUN apt-get update && apt-get install -y \
    ffmpeg \
    git \
    autoconf \
    automake \
    libtool \
    pkg-config \
    libsndfile1-dev \
    build-essential \
    wget && \
    rm -rf /var/lib/apt/lists/*

# Build RNNoise from source
RUN git clone --depth=1 https://github.com/xiph/rnnoise.git /tmp/rnnoise && \
    cd /tmp/rnnoise && \
    ./autogen.sh && \
    ./configure && \
    make && \
    cp examples/rnnoise_demo /app/rnnoise_demo && \
    chmod +x /app/rnnoise_demo && \
    rm -rf /tmp/rnnoise

# Create non-root user (optional)
RUN useradd -m appuser
USER appuser

COPY --from=build /app/publish ./

# Expose app port
EXPOSE 5032

# Environment variables
ENV ASPNETCORE_URLS=http://+:5032 \
    STREAM_URL=rtsp://127.0.0.1/live/OBSstream \
    WHISPER_URL=http://whisper-service:5001/transcribe \
    MODEL_PATH=/opt/whisper/models/ggml-base.en.bin \
    FFMPEG_PATH=/usr/bin/ffmpeg

ENTRYPOINT ["dotnet", "NidarosRTT.API.dll"]