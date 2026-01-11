# Grafana Metrics Documentation

## Overview

The Nidaros Real-Time Translation system includes Prometheus metrics collection and Grafana dashboards for monitoring service health and performance.

## Architecture

```
┌─────────────────┐     ┌─────────────────┐     ┌─────────────────┐
│   WSE Plugin    │────▶│   Prometheus    │────▶│    Grafana      │
│  (port 9101)    │     │   (port 9090)   │     │   (port 3001)   │
└─────────────────┘     └─────────────────┘     └─────────────────┘
        │
        ▼
┌─────────────────┐
│  Health Check   │
│  (port 8090)    │
└─────────────────┘
```

## Endpoints

| Service | Port | Path | Description |
|---------|------|------|-------------|
| Prometheus Metrics | 9101 | `/metrics` | Prometheus scrape endpoint |
| Health Check | 8090 | `/health` | JSON health status |
| Prometheus UI | 9090 | `/` | Prometheus query interface |
| Grafana | 3001 | `/` | Grafana dashboards |

## Metrics Exposed

### Service Status
- **`nidaros_service_status`** - Service health status gauge
  - Labels: `service` (whisper, libretranslate, wse)
  - Values: `1` = OK, `0.5` = DEGRADED, `0` = DOWN

### Response Time
- **`nidaros_service_response_time_ms`** - Service response time in milliseconds
  - Labels: `service` (whisper, libretranslate, wse)

### Uptime
- **`nidaros_service_uptime_seconds`** - Service uptime in seconds
  - Labels: `service` (whisper, libretranslate, wse)

## Configuration

### Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| `METRICS_PORT` | 9101 | Port for Prometheus metrics endpoint |

### Wowza Module Properties

Configure in your Wowza application properties:

```xml
<Property>
    <Name>healthCheckEnabled</Name>
    <Value>true</Value>
</Property>
<Property>
    <Name>healthCheckPort</Name>
    <Value>8090</Value>
</Property>
```

## Docker Compose

The following services are included in `docker-compose.yaml`:

```yaml
prometheus:
    image: prom/prometheus:latest
    ports:
        - 9090:9090
    volumes:
        - ./prometheus.yml:/etc/prometheus/prometheus.yml:ro

grafana:
    image: grafana/grafana:latest
    environment:
        - GF_SECURITY_ADMIN_USER=admin
        - GF_SECURITY_ADMIN_PASSWORD=password
    ports:
        - 3001:3000
```

## Accessing Grafana

1. Start the Docker Compose stack: `docker-compose up -d`
2. Open Grafana: http://localhost:3001
3. Login with:
   - Username: `admin`
   - Password: `password`
4. Navigate to Dashboards → "Nidaros RTT Health Overview"

## Dashboard Panels

The pre-configured dashboard includes:

1. **Service Status Indicators** - Real-time status for Whisper, LibreTranslate, and WSE
2. **Response Time Graph** - Historical response times with threshold lines
3. **Uptime Graph** - Service uptime tracking
4. **Health History** - Timeline of service health changes

## Prometheus Queries

Example PromQL queries:

```promql
# Current service status
nidaros_service_status{service="whisper"}

# Average response time over 5 minutes
avg_over_time(nidaros_service_response_time_ms{service="whisper"}[5m])

# Services that are down
nidaros_service_status == 0

# Alert: Service degraded or down
nidaros_service_status < 1
```

## Troubleshooting

### Metrics not appearing in Prometheus

1. Check if the metrics endpoint is accessible:
   ```bash
   curl http://localhost:9101/metrics
   ```

2. Verify Prometheus can reach the target:
   - Open Prometheus UI: http://localhost:9090
   - Go to Status → Targets
   - Check if `wowza-captions` target is UP

### Grafana shows "No Data"

1. Verify Prometheus datasource is configured correctly
2. Check if metrics are being scraped in Prometheus UI
3. Ensure the WSE application has started and metrics are being collected

## Files Structure

```
├── prometheus.yml                          # Prometheus configuration
├── docker-compose.yaml                     # Docker services (includes prometheus/grafana)
├── grafana/
│   └── provisioning/
│       ├── datasources/
│       │   └── prometheus.yaml             # Prometheus datasource config
│       └── dashboards/
│           ├── dashboards.yaml             # Dashboard provisioning config
│           └── json/
│               └── nidaros-health-overview.json  # Pre-built dashboard
└── src/main/java/com/wowza/wms/plugin/captions/metrics/
    ├── MetricsRegistry.java                # Prometheus registry & HTTP server
    └── ServiceMetrics.java                 # Health metrics collection
```
