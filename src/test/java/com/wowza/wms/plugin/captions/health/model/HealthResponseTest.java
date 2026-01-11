/*
 * This code and all components (c) Copyright 2006 - 2025, Wowza Media Systems, LLC.  All rights reserved.
 * This code is licensed pursuant to the Wowza Public License version 1.0, available at www.wowza.com/legal.
 */

package com.wowza.wms.plugin.captions.health.model;

import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

import static org.junit.jupiter.api.Assertions.*;

/**
 * Unit tests for HealthResponse model.
 */
class HealthResponseTest
{
    private HealthResponse healthResponse;

    @BeforeEach
    void setUp()
    {
        healthResponse = new HealthResponse();
    }

    @Test
    void testInitialOverallStatus_IsOk()
    {
        assertEquals(ServiceStatus.OK, healthResponse.getOverallStatus());
    }

    @Test
    void testTimestamp_IsSet()
    {
        assertNotNull(healthResponse.getTimestamp());
    }

    @Test
    void testAddService_OkService_KeepsOverallOk()
    {
        ServiceHealth okService = new ServiceHealth("test", ServiceStatus.OK);
        healthResponse.addService("test", okService);

        assertEquals(ServiceStatus.OK, healthResponse.getOverallStatus());
    }

    @Test
    void testAddService_DegradedService_SetsOverallDegraded()
    {
        ServiceHealth degradedService = new ServiceHealth("test", ServiceStatus.DEGRADED);
        healthResponse.addService("test", degradedService);

        assertEquals(ServiceStatus.DEGRADED, healthResponse.getOverallStatus());
    }

    @Test
    void testAddService_DownService_SetsOverallDown()
    {
        ServiceHealth downService = new ServiceHealth("test", ServiceStatus.DOWN);
        healthResponse.addService("test", downService);

        assertEquals(ServiceStatus.DOWN, healthResponse.getOverallStatus());
    }

    @Test
    void testAddService_DownOverridesDegraded()
    {
        ServiceHealth degradedService = new ServiceHealth("degraded", ServiceStatus.DEGRADED);
        ServiceHealth downService = new ServiceHealth("down", ServiceStatus.DOWN);

        healthResponse.addService("degraded", degradedService);
        healthResponse.addService("down", downService);

        assertEquals(ServiceStatus.DOWN, healthResponse.getOverallStatus());
    }

    @Test
    void testAddService_DegradedDoesNotOverrideDown()
    {
        ServiceHealth downService = new ServiceHealth("down", ServiceStatus.DOWN);
        ServiceHealth degradedService = new ServiceHealth("degraded", ServiceStatus.DEGRADED);

        healthResponse.addService("down", downService);
        healthResponse.addService("degraded", degradedService);

        assertEquals(ServiceStatus.DOWN, healthResponse.getOverallStatus());
    }

    @Test
    void testServices_AreAddedToMap()
    {
        ServiceHealth service1 = new ServiceHealth("service1", ServiceStatus.OK);
        ServiceHealth service2 = new ServiceHealth("service2", ServiceStatus.OK);

        healthResponse.addService("s1", service1);
        healthResponse.addService("s2", service2);

        assertEquals(2, healthResponse.getServices().size());
        assertTrue(healthResponse.getServices().containsKey("s1"));
        assertTrue(healthResponse.getServices().containsKey("s2"));
    }
}
