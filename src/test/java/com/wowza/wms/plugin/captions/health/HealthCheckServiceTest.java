/*
 * This code and all components (c) Copyright 2006 - 2025, Wowza Media Systems, LLC.  All rights reserved.
 * This code is licensed pursuant to the Wowza Public License version 1.0, available at www.wowza.com/legal.
 */

package com.wowza.wms.plugin.captions.health;

import com.wowza.wms.plugin.captions.health.model.HealthResponse;
import com.wowza.wms.plugin.captions.health.model.ServiceHealth;
import com.wowza.wms.plugin.captions.health.model.ServiceStatus;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Unit tests for HealthCheckService.
 */
class HealthCheckServiceTest
{
    private HealthCheckService healthCheckService;

    @BeforeEach
    void setUp()
    {
        // Use localhost with invalid ports to simulate down services
        healthCheckService = new HealthCheckService("localhost", 9999, "localhost", 9998);
    }

    @Test
    void testCheckHealth_ReturnsHealthResponse()
    {
        HealthResponse response = healthCheckService.checkHealth();

        assertNotNull(response);
        assertNotNull(response.getTimestamp());
        assertNotNull(response.getServices());
        assertEquals(3, response.getServices().size());
    }

    @Test
    void testCheckHealth_ContainsAllServices()
    {
        HealthResponse response = healthCheckService.checkHealth();

        assertTrue(response.getServices().containsKey("whisper"));
        assertTrue(response.getServices().containsKey("libretranslate"));
        assertTrue(response.getServices().containsKey("wse"));
    }

    @Test
    void testCheckHealth_WseAlwaysOk()
    {
        HealthResponse response = healthCheckService.checkHealth();

        ServiceHealth wseHealth = response.getServices().get("wse");
        assertNotNull(wseHealth);
        assertEquals(ServiceStatus.OK, wseHealth.getStatus());
    }

    @Test
    void testCheckHealth_DownServicesHaveDownStatus()
    {
        HealthResponse response = healthCheckService.checkHealth();

        ServiceHealth whisperHealth = response.getServices().get("whisper");
        assertNotNull(whisperHealth);
        assertEquals(ServiceStatus.DOWN, whisperHealth.getStatus());
        assertNotNull(whisperHealth.getMessage());

        ServiceHealth libreTranslateHealth = response.getServices().get("libretranslate");
        assertNotNull(libreTranslateHealth);
        assertEquals(ServiceStatus.DOWN, libreTranslateHealth.getStatus());
        assertNotNull(libreTranslateHealth.getMessage());
    }

    @Test
    void testCheckHealth_OverallStatusIsDown_WhenServicesAreDown()
    {
        HealthResponse response = healthCheckService.checkHealth();

        assertEquals(ServiceStatus.DOWN, response.getOverallStatus());
    }

    @Test
    void testCheckHealth_ResponseTimeIsSet()
    {
        HealthResponse response = healthCheckService.checkHealth();

        for (ServiceHealth health : response.getServices().values())
        {
            assertTrue(health.getResponseTimeMs() >= 0);
        }
    }

    @Test
    void testCheckHealth_UptimeIsSet()
    {
        HealthResponse response = healthCheckService.checkHealth();

        for (ServiceHealth health : response.getServices().values())
        {
            assertTrue(health.getUptimeSeconds() >= 0);
        }
    }
}
