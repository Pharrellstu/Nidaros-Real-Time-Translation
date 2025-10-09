# Nidaros-Real-Time-Translation

# NidarosRTT

A new .NET Web API project initialized with proper solution structure.

## Structure
- **NidarosRTT.API** – Entry point for real-time translation via REST or WebSocket (SignalR).
- **NidarosRTT.Core** – Business logic and data models.
- **NidarosRTT.Infrastructure** – External integrations (speech-to-text, translation APIs).
- **NidarosRTT.Tests** – Unit tests for core logic.

## Setup
1. Clone the repository
2. Run `dotnet restore`
3. Start the API: `dotnet run --project NidarosRTT.API`

## Wowza docker container Setup
1. Create a .env file based on the provided .env.example file with your own values
2. Run `docker-compose up -d` to start the container
