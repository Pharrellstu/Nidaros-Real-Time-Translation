/*
 * This code and all components (c) Copyright 2006 - 2025, Wowza Media Systems, LLC.  All rights reserved.
 * This code is licensed pursuant to the Wowza Public License version 1.0, available at www.wowza.com/legal.
 */

package com.wowza.wms.plugin.captions.metrics;

import com.wowza.wms.plugin.captions.health.HealthCheckService;
import com.wowza.wms.plugin.captions.health.model.HealthResponse;
import com.wowza.wms.plugin.captions.health.model.ServiceHealth;
import com.wowza.wms.plugin.captions.health.model.ServiceStatus;
import io.micrometer.core.instrument.Gauge;
import io.micrometer.core.instrument.Tags;
import org.apache.logging.log4j.LogManager;
import org.apache.logging.log4j.Logger;

import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.Executors;
import java.util.concurrent.ScheduledExecutorService;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

/**
 * Exposes health check metrics to Prometheus via Micrometer.
 * Periodically polls the HealthCheckService and updates gauges.
 */
public class ServiceMetrics {

    private static final Logger logger = LogManager.getLogger(ServiceMetrics.class);
    private static final long POLL_INTERVAL_SECONDS = 15;

    private final HealthCheckService healthCheckService;
    private final ScheduledExecutorService scheduler;
    private final Map<String, AtomicReference<Double>> statusGauges = new ConcurrentHashMap<>();
    private final Map<String, AtomicReference<Double>> responseTimeGauges = new ConcurrentHashMap<>();
    private final Map<String, AtomicReference<Double>> uptimeGauges = new ConcurrentHashMap<>();

    public ServiceMetrics(HealthCheckService healthCheckService) {
        this.healthCheckService = healthCheckService;
        this.scheduler = Executors.newSingleThreadScheduledExecutor(r -> {
            Thread thread = new Thread(r, "service-metrics-poller");
            thread.setDaemon(true);
            return thread;
        });
    }

    /**
     * Starts the metrics collection and registers gauges with Prometheus.
     */
    public void start() {
        MetricsRegistry.ensureServerStarted();
        
        // Initialize gauges for known services
        initializeServiceGauges("whisper");
        initializeServiceGauges("libretranslate");
        initializeServiceGauges("wse");

        // Schedule periodic health check polling
        scheduler.scheduleAtFixedRate(this::updateMetrics, 0, POLL_INTERVAL_SECONDS, TimeUnit.SECONDS);
        logger.info("ServiceMetrics started, polling every {} seconds", POLL_INTERVAL_SECONDS);
    }

    /**
     * Stops the metrics collection.
     */
    public void stop() {
        scheduler.shutdown();
        try {
            if (!scheduler.awaitTermination(5, TimeUnit.SECONDS)) {
                scheduler.shutdownNow();
            }
        } catch (InterruptedException e) {
            scheduler.shutdownNow();
            Thread.currentThread().interrupt();
        }
        logger.info("ServiceMetrics stopped");
    }

    private void initializeServiceGauges(String serviceName) {
        AtomicReference<Double> statusRef = new AtomicReference<>(0.0);
        AtomicReference<Double> responseTimeRef = new AtomicReference<>(0.0);
        AtomicReference<Double> uptimeRef = new AtomicReference<>(0.0);

        statusGauges.put(serviceName, statusRef);
        responseTimeGauges.put(serviceName, responseTimeRef);
        uptimeGauges.put(serviceName, uptimeRef);

        Tags tags = Tags.of("service", serviceName);

        // Status: 1 = OK, 0.5 = DEGRADED, 0 = DOWN
        Gauge.builder("nidaros_service_status", statusRef, AtomicReference::get)
                .tags(tags)
                .description("Service health status (1=OK, 0.5=DEGRADED, 0=DOWN)")
                .register(MetricsRegistry.registry());

        // Response time in milliseconds
        Gauge.builder("nidaros_service_response_time_ms", responseTimeRef, AtomicReference::get)
                .tags(tags)
                .description("Service response time in milliseconds")
                .register(MetricsRegistry.registry());

        // Uptime in seconds
        Gauge.builder("nidaros_service_uptime_seconds", uptimeRef, AtomicReference::get)
                .tags(tags)
                .description("Service uptime in seconds")
                .register(MetricsRegistry.registry());
    }

    private void updateMetrics() {
        try {
            HealthResponse response = healthCheckService.checkHealth();
            Map<String, ServiceHealth> services = response.getServices();

            for (Map.Entry<String, ServiceHealth> entry : services.entrySet()) {
                String serviceName = entry.getKey();
                ServiceHealth health = entry.getValue();

                AtomicReference<Double> statusRef = statusGauges.get(serviceName);
                AtomicReference<Double> responseTimeRef = responseTimeGauges.get(serviceName);
                AtomicReference<Double> uptimeRef = uptimeGauges.get(serviceName);

                if (statusRef != null) {
                    statusRef.set(statusToDouble(health.getStatus()));
                }
                if (responseTimeRef != null) {
                    responseTimeRef.set((double) health.getResponseTimeMs());
                }
                if (uptimeRef != null) {
                    uptimeRef.set((double) health.getUptimeSeconds());
                }
            }
        } catch (Exception e) {
            logger.error("Error updating service metrics", e);
        }
    }

    private double statusToDouble(ServiceStatus status) {
        switch (status) {
            case OK:
                return 1.0;
            case DEGRADED:
                return 0.5;
            case DOWN:
            default:
                return 0.0;
        }
    }
}
