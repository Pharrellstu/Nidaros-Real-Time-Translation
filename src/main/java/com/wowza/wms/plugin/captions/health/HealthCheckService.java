/*
 * This code and all components (c) Copyright 2006 - 2025, Wowza Media Systems, LLC.  All rights reserved.
 * This code is licensed pursuant to the Wowza Public License version 1.0, available at www.wowza.com/legal.
 */

package com.wowza.wms.plugin.captions.health;

import com.wowza.wms.plugin.captions.health.model.HealthResponse;
import com.wowza.wms.plugin.captions.health.model.ServiceHealth;
import com.wowza.wms.plugin.captions.health.model.ServiceStatus;

import java.io.IOException;
import java.net.HttpURLConnection;
import java.net.Socket;
import java.net.URL;
import java.time.Instant;

/**
 * Service responsible for checking the health of all system components.
 */
public class HealthCheckService
{
    private static final int CONNECTION_TIMEOUT_MS = 5000;
    private static final int READ_TIMEOUT_MS = 5000;
    private static final long DEGRADED_THRESHOLD_MS = 2000;

    private final String whisperHost;
    private final int whisperPort;
    private final String libreTranslateHost;
    private final int libreTranslatePort;
    private final Instant startTime;

    public HealthCheckService(String whisperHost, int whisperPort, String libreTranslateHost, int libreTranslatePort)
    {
        this.whisperHost = whisperHost;
        this.whisperPort = whisperPort;
        this.libreTranslateHost = libreTranslateHost;
        this.libreTranslatePort = libreTranslatePort;
        this.startTime = Instant.now();
    }

    /**
     * Performs health checks on all services and returns the aggregated response.
     *
     * @return HealthResponse containing status of all services
     */
    public HealthResponse checkHealth()
    {
        HealthResponse response = new HealthResponse();

        response.addService("whisper", checkWhisperHealth());
        response.addService("libretranslate", checkLibreTranslateHealth());
        response.addService("wse", checkWseHealth());

        return response;
    }

    /**
     * Checks the health of the Whisper speech-to-text service.
     */
    private ServiceHealth checkWhisperHealth()
    {
        ServiceHealth health = new ServiceHealth("Whisper Server", ServiceStatus.DOWN);
        health.setUptimeSeconds(getUptimeSeconds());

        long startTime = System.currentTimeMillis();
        try (Socket socket = new Socket())
        {
            socket.connect(new java.net.InetSocketAddress(whisperHost, whisperPort), CONNECTION_TIMEOUT_MS);
            long responseTime = System.currentTimeMillis() - startTime;
            health.setResponseTimeMs(responseTime);

            if (responseTime > DEGRADED_THRESHOLD_MS)
            {
                health.setStatus(ServiceStatus.DEGRADED);
                health.setMessage("High latency detected");
            }
            else
            {
                health.setStatus(ServiceStatus.OK);
                health.setMessage("Service is healthy");
            }
        }
        catch (IOException e)
        {
            health.setResponseTimeMs(System.currentTimeMillis() - startTime);
            health.setStatus(ServiceStatus.DOWN);
            health.setMessage("Unable to connect: " + e.getMessage());
        }

        return health;
    }

    /**
     * Checks the health of the LibreTranslate service.
     */
    private ServiceHealth checkLibreTranslateHealth()
    {
        ServiceHealth health = new ServiceHealth("LibreTranslate Server", ServiceStatus.DOWN);
        health.setUptimeSeconds(getUptimeSeconds());

        long startTime = System.currentTimeMillis();
        try
        {
            URL url = new URL("http://" + libreTranslateHost + ":" + libreTranslatePort + "/languages");
            HttpURLConnection connection = (HttpURLConnection) url.openConnection();
            connection.setRequestMethod("GET");
            connection.setConnectTimeout(CONNECTION_TIMEOUT_MS);
            connection.setReadTimeout(READ_TIMEOUT_MS);

            int responseCode = connection.getResponseCode();
            long responseTime = System.currentTimeMillis() - startTime;
            health.setResponseTimeMs(responseTime);

            if (responseCode == 200)
            {
                if (responseTime > DEGRADED_THRESHOLD_MS)
                {
                    health.setStatus(ServiceStatus.DEGRADED);
                    health.setMessage("High latency detected");
                }
                else
                {
                    health.setStatus(ServiceStatus.OK);
                    health.setMessage("Service is healthy");
                }
            }
            else
            {
                health.setStatus(ServiceStatus.DEGRADED);
                health.setMessage("Unexpected response code: " + responseCode);
            }

            connection.disconnect();
        }
        catch (IOException e)
        {
            health.setResponseTimeMs(System.currentTimeMillis() - startTime);
            health.setStatus(ServiceStatus.DOWN);
            health.setMessage("Unable to connect: " + e.getMessage());
        }

        return health;
    }

    /**
     * Checks the health of the Wowza Streaming Engine.
     * Since this code runs within WSE, we consider it healthy if we're executing.
     */
    private ServiceHealth checkWseHealth()
    {
        ServiceHealth health = new ServiceHealth("Wowza Streaming Engine", ServiceStatus.OK);
        health.setUptimeSeconds(getUptimeSeconds());
        health.setResponseTimeMs(0);
        health.setMessage("Service is running");
        return health;
    }

    private long getUptimeSeconds()
    {
        return java.time.Duration.between(startTime, Instant.now()).getSeconds();
    }
}
