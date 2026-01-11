package com.wowza.wms.plugin.captions.metrics;

import com.sun.net.httpserver.HttpServer;
import io.micrometer.core.instrument.Metrics;
import io.micrometer.prometheus.PrometheusConfig;
import io.micrometer.prometheus.PrometheusMeterRegistry;
import org.apache.logging.log4j.LogManager;
import org.apache.logging.log4j.Logger;

import java.io.IOException;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Centralizes Micrometer/Prometheus integration for the captions module.
 * Exposes metrics at /metrics endpoint on configurable port (default 9101).
 */
public final class MetricsRegistry {
    
    private static final Logger logger = LogManager.getLogger(MetricsRegistry.class);
    private static final PrometheusMeterRegistry REGISTRY = new PrometheusMeterRegistry(PrometheusConfig.DEFAULT);
    private static final AtomicBoolean SERVER_STARTED = new AtomicBoolean(false);
    private static final int DEFAULT_PORT = 9101;

    static {
        Metrics.addRegistry(REGISTRY);
    }

    private MetricsRegistry() {
    }

    public static PrometheusMeterRegistry registry() {
        return REGISTRY;
    }

    public static void ensureServerStarted() {
        if (SERVER_STARTED.get()) {
            return;
        }

        int port = resolvePort();
        try {
            HttpServer server = HttpServer.create(new InetSocketAddress(port), 0);
            server.createContext("/metrics", exchange -> {
                byte[] response = REGISTRY.scrape().getBytes(StandardCharsets.UTF_8);
                exchange.getResponseHeaders().add("Content-Type", "text/plain; version=0.0.4");
                exchange.sendResponseHeaders(200, response.length);
                try (OutputStream os = exchange.getResponseBody()) {
                    os.write(response);
                } finally {
                    exchange.close();
                }
            });
            server.setExecutor(Executors.newSingleThreadExecutor(r -> {
                Thread thread = new Thread(r, "captions-metrics-http");
                thread.setDaemon(true);
                return thread;
            }));
            server.start();
            SERVER_STARTED.set(true);
            logger.info("Captions metrics endpoint listening on port {} at /metrics", port);
        } catch (IOException e) {
            logger.error("Unable to start metrics endpoint on port {}", port, e);
        }
    }

    private static int resolvePort() {
        String configured = System.getProperty("METRICS_PORT");
        if (configured == null || configured.isBlank()) {
            configured = System.getenv().getOrDefault("METRICS_PORT", String.valueOf(DEFAULT_PORT));
        }

        try {
            return Integer.parseInt(configured);
        } catch (NumberFormatException ex) {
            logger.warn("Invalid METRICS_PORT value '{}', falling back to {}", configured, DEFAULT_PORT);
            return DEFAULT_PORT;
        }
    }
}
