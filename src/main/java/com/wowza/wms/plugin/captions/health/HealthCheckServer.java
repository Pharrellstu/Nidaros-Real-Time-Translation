/*
 * This code and all components (c) Copyright 2006 - 2025, Wowza Media Systems, LLC.  All rights reserved.
 * This code is licensed pursuant to the Wowza Public License version 1.0, available at www.wowza.com/legal.
 */

package com.wowza.wms.plugin.captions.health;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.SerializationFeature;
import com.sun.net.httpserver.HttpExchange;
import com.sun.net.httpserver.HttpHandler;
import com.sun.net.httpserver.HttpServer;
import com.wowza.wms.logging.WMSLogger;
import com.wowza.wms.plugin.captions.health.model.HealthResponse;
import com.wowza.wms.plugin.captions.health.model.ServiceStatus;

import java.io.IOException;
import java.io.OutputStream;
import java.net.InetSocketAddress;
import java.nio.charset.StandardCharsets;

/**
 * HTTP server that exposes the /health endpoint for monitoring.
 */
public class HealthCheckServer
{
    private static final String CLASS_NAME = HealthCheckServer.class.getSimpleName();

    private final HttpServer server;
    private final HealthCheckService healthCheckService;
    private final ObjectMapper objectMapper;
    private final WMSLogger logger;

    public HealthCheckServer(int port, HealthCheckService healthCheckService, WMSLogger logger) throws IOException
    {
        this.healthCheckService = healthCheckService;
        this.logger = logger;
        this.objectMapper = new ObjectMapper();
        this.objectMapper.enable(SerializationFeature.INDENT_OUTPUT);

        this.server = HttpServer.create(new InetSocketAddress(port), 0);
        this.server.createContext("/health", new HealthHandler());
        this.server.setExecutor(null);
    }

    /**
     * Starts the health check HTTP server.
     */
    public void start()
    {
        server.start();
        logger.info(CLASS_NAME + ".start: Health check server started on port " + server.getAddress().getPort());
    }

    /**
     * Stops the health check HTTP server.
     */
    public void stop()
    {
        server.stop(0);
        logger.info(CLASS_NAME + ".stop: Health check server stopped");
    }

    /**
     * Handler for the /health endpoint.
     */
    private class HealthHandler implements HttpHandler
    {
        @Override
        public void handle(HttpExchange exchange) throws IOException
        {
            if (!"GET".equals(exchange.getRequestMethod()))
            {
                exchange.sendResponseHeaders(405, -1);
                return;
            }

            try
            {
                HealthResponse healthResponse = healthCheckService.checkHealth();
                String jsonResponse = objectMapper.writeValueAsString(healthResponse);
                byte[] responseBytes = jsonResponse.getBytes(StandardCharsets.UTF_8);

                int httpStatus = getHttpStatusCode(healthResponse.getOverallStatus());

                exchange.getResponseHeaders().set("Content-Type", "application/json");
                exchange.sendResponseHeaders(httpStatus, responseBytes.length);

                try (OutputStream os = exchange.getResponseBody())
                {
                    os.write(responseBytes);
                }
            }
            catch (Exception e)
            {
                logger.error(CLASS_NAME + ".HealthHandler.handle: Error processing health check", e);
                String errorResponse = "{\"error\": \"Internal server error\"}";
                byte[] errorBytes = errorResponse.getBytes(StandardCharsets.UTF_8);
                exchange.sendResponseHeaders(500, errorBytes.length);
                try (OutputStream os = exchange.getResponseBody())
                {
                    os.write(errorBytes);
                }
            }
        }

        private int getHttpStatusCode(ServiceStatus status)
        {
            return switch (status)
            {
                case OK -> 200;
                case DEGRADED -> 200;
                case DOWN -> 503;
            };
        }
    }
}
