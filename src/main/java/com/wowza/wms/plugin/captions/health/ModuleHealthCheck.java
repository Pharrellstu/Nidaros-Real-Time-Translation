/*
 * This code and all components (c) Copyright 2006 - 2025, Wowza Media Systems, LLC.  All rights reserved.
 * This code is licensed pursuant to the Wowza Public License version 1.0, available at www.wowza.com/legal.
 */

package com.wowza.wms.plugin.captions.health;

import com.wowza.wms.application.IApplicationInstance;
import com.wowza.wms.application.WMSProperties;
import com.wowza.wms.logging.WMSLogger;
import com.wowza.wms.logging.WMSLoggerFactory;
import com.wowza.wms.module.ModuleBase;
import com.wowza.wms.plugin.captions.metrics.ServiceMetrics;

/**
 * Wowza module that starts the health check HTTP server.
 * 
 * Configuration properties:
 * - healthCheckEnabled: Enable/disable health check endpoint (default: true)
 * - healthCheckPort: Port for the health check server (default: 8090)
 * - whisperHost: Whisper server hostname (default: whisper.server)
 * - whisperPort: Whisper server port (default: 3000)
 * - libreTranslateHost: LibreTranslate hostname (default: libretranslate.server)
 * - libreTranslatePort: LibreTranslate port (default: 5000)
 */
public class ModuleHealthCheck extends ModuleBase
{
    private static final String CLASS_NAME = ModuleHealthCheck.class.getSimpleName();

    public static final String PROP_HEALTH_CHECK_ENABLED = "healthCheckEnabled";
    public static final String PROP_HEALTH_CHECK_PORT = "healthCheckPort";
    public static final String PROP_WHISPER_HOST = "whisperSocketHost";
    public static final String PROP_WHISPER_PORT = "whisperSocketPort";
    public static final String PROP_LIBRETRANSLATE_HOST = "libreTranslateHost";
    public static final String PROP_LIBRETRANSLATE_PORT = "libreTranslatePort";

    private static final boolean DEFAULT_ENABLED = true;
    private static final int DEFAULT_HEALTH_CHECK_PORT = 8090;
    private static final String DEFAULT_WHISPER_HOST = "whisper.server";
    private static final int DEFAULT_WHISPER_PORT = 3000;
    private static final String DEFAULT_LIBRETRANSLATE_HOST = "libretranslate.server";
    private static final int DEFAULT_LIBRETRANSLATE_PORT = 5000;

    private WMSLogger logger;
    private HealthCheckServer healthCheckServer;
    private ServiceMetrics serviceMetrics;
    private boolean enabled;

    public void onAppCreate(IApplicationInstance appInstance)
    {
        logger = WMSLoggerFactory.getLoggerObj(appInstance);
        WMSProperties props = appInstance.getProperties();

        enabled = props.getPropertyBoolean(PROP_HEALTH_CHECK_ENABLED, DEFAULT_ENABLED);

        if (!enabled)
        {
            logger.info(CLASS_NAME + ".onAppCreate: Health check module disabled");
            return;
        }

        int port = props.getPropertyInt(PROP_HEALTH_CHECK_PORT, DEFAULT_HEALTH_CHECK_PORT);
        String whisperHost = props.getPropertyStr(PROP_WHISPER_HOST, DEFAULT_WHISPER_HOST);
        int whisperPort = props.getPropertyInt(PROP_WHISPER_PORT, DEFAULT_WHISPER_PORT);
        String libreTranslateHost = props.getPropertyStr(PROP_LIBRETRANSLATE_HOST, DEFAULT_LIBRETRANSLATE_HOST);
        int libreTranslatePort = props.getPropertyInt(PROP_LIBRETRANSLATE_PORT, DEFAULT_LIBRETRANSLATE_PORT);

        try
        {
            HealthCheckService healthCheckService = new HealthCheckService(
                    whisperHost, whisperPort,
                    libreTranslateHost, libreTranslatePort
            );
            healthCheckServer = new HealthCheckServer(port, healthCheckService, logger);
            healthCheckServer.start();
            logger.info(CLASS_NAME + ".onAppCreate: Health check endpoint available at http://localhost:" + port + "/health");

            // Start Prometheus metrics collection
            serviceMetrics = new ServiceMetrics(healthCheckService);
            serviceMetrics.start();
            logger.info(CLASS_NAME + ".onAppCreate: Prometheus metrics endpoint available at http://localhost:9101/metrics");
        }
        catch (Exception e)
        {
            logger.error(CLASS_NAME + ".onAppCreate: Failed to start health check server", e);
        }
    }

    public void onAppDestroy(IApplicationInstance appInstance)
    {
        if (serviceMetrics != null)
        {
            serviceMetrics.stop();
            logger.info(CLASS_NAME + ".onAppDestroy: Prometheus metrics stopped");
        }
        if (healthCheckServer != null)
        {
            healthCheckServer.stop();
            logger.info(CLASS_NAME + ".onAppDestroy: Health check server stopped");
        }
    }
}
