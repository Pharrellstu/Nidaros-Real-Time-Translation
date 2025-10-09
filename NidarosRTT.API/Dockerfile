# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY ["NidarosRTT.API/NidarosRTT.API.csproj", "NidarosRTT.API/"]
COPY ["NidarosRTT.Core/NidarosRTT.Core.csproj", "NidarosRTT.Core/"]
COPY ["NidarosRTT.Infrastructure/NidarosRTT.Infrastructure.csproj", "NidarosRTT.Infrastructure/"]

# Create a solution file with only the required projects
RUN dotnet new sln -n NidarosRTT
RUN dotnet sln NidarosRTT.sln add NidarosRTT.API/NidarosRTT.API.csproj \
    NidarosRTT.Core/NidarosRTT.Core.csproj \
    NidarosRTT.Infrastructure/NidarosRTT.Infrastructure.csproj

# Restore packages for the API project (this will restore dependencies for all projects)
WORKDIR "/src/NidarosRTT.API"
RUN dotnet restore

# Copy everything else
WORKDIR "/src"
COPY . .

# Build the app
WORKDIR "/src/NidarosRTT.API"
RUN dotnet build -c Release -o /app/build

# Publish the app
RUN dotnet publish -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app

# Install FFmpeg
RUN apt-get update && \
    apt-get install -y --no-install-recommends ffmpeg && \
    rm -rf /var/lib/apt/lists/*

# Copy the published app
COPY --from=build /app/publish .

# Set the entry point
ENTRYPOINT ["dotnet", "NidarosRTT.API.dll"]
