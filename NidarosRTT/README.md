# NidarosRTT Console (Wyoming describe)

Minimal .NET 9 console app that connects to a Wyoming server (faster-whisper) and sends `describe`, printing a combined `info` JSON.

- Default host/port: 127.0.0.1:10300
- Override with env `WYOMING_HOST`, `WYOMING_PORT` or args: `dotnet run -- 192.168.1.10 10300`

## Run

Ensure the `faster-whisper` container is running and listening on port 10300, then from repo root:

```
dotnet run --project NidarosRTT
```

You should see a pretty-printed JSON document with available ASR models under the `data` property.
