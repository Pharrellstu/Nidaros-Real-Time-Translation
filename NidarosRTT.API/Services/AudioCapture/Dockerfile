# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY AudioCapture.csproj .
RUN dotnet restore

# Copy source code and build
COPY . .
RUN dotnet publish -c Release -o /app/publish AudioCapture.csproj

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install FFmpeg and required tools
RUN apt-get update && \
    apt-get install -y ffmpeg curl && \
    rm -rf /var/lib/apt/lists/*

# Copy published app
COPY --from=build /app/publish .

# Create output directory for WAV files
RUN mkdir -p /app/output

ENTRYPOINT ["dotnet", "AudioCapture.dll"]
