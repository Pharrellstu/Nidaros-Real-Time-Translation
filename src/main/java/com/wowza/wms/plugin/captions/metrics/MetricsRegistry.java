package com.wowza.wms.plugin.captions.metrics;

import com.sun.net.httpserver.HttpServer;
import io.micrometer.core.instrument.Metrics;
import io.micrometer.prometheus.PrometheusConfig;
import io.micrometer.prometheus.PrometheusMeterRegistry;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.io.IOException;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Centralizes Micrometer/Prometheus integration for the captions module.
 */
public final class MetricsRegistry
{
    private static final Logger logger = LoggerFactory.getLogger(MetricsRegistry.class);
    private static final PrometheusMeterRegistry REGISTRY = new PrometheusMeterRegistry(PrometheusConfig.DEFAULT);
    private static final AtomicBoolean SERVER_STARTED = new AtomicBoolean(false);

    static
    {
        Metrics.addRegistry(REGISTRY);
    }

    private MetricsRegistry()
    {
    }

    public static PrometheusMeterRegistry registry()
    {
        return REGISTRY;
    }

    public static void ensureServerStarted()
    {
        if (SERVER_STARTED.get())
        {
            return;
        }

        int port = resolvePort();
        try
        {
            HttpServer server = HttpServer.create(new InetSocketAddress(port), 0);
            server.createContext("/metrics", exchange ->
            {
                byte[] response = REGISTRY.scrape().getBytes(StandardCharsets.UTF_8);
                exchange.getResponseHeaders().add("Content-Type", "text/plain; version=0.0.4");
                exchange.sendResponseHeaders(200, response.length);
                try (OutputStream os = exchange.getResponseBody())
                {
                    os.write(response);
                }
                finally
                {
                    exchange.close();
                }
            });
            server.setExecutor(Executors.newSingleThreadExecutor(r ->
            {
                Thread thread = new Thread(r, "captions-metrics-http");
                thread.setDaemon(true);
                return thread;
            }));
            server.start();
            SERVER_STARTED.set(true);
            logger.info("Captions metrics endpoint listening on port {} at /metrics", port);
        }
        catch (IOException e)
        {
            logger.error("Unable to start metrics endpoint on port {}", port, e);
        }
    }

    private static int resolvePort()
    {
        String configured = System.getProperty("METRICS_PORT");
        if (configured == null || configured.isBlank())
        {
            configured = System.getenv().getOrDefault("METRICS_PORT", "9101");
        }

        try
        {
            return Integer.parseInt(configured);
        }
        catch (NumberFormatException ex)
        {
            logger.warn("Invalid METRICS_PORT value '{}', falling back to 9101", configured);
            return 9101;
        }
    }
}

