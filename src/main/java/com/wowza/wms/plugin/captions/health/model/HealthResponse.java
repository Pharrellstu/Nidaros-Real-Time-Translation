/*
 * This code and all components (c) Copyright 2006 - 2025, Wowza Media Systems, LLC.  All rights reserved.
 * This code is licensed pursuant to the Wowza Public License version 1.0, available at www.wowza.com/legal.
 */

package com.wowza.wms.plugin.captions.health.model;

import java.time.Instant;
import java.util.HashMap;
import java.util.Map;

/**
 * Represents the overall health response containing status of all services.
 */
public class HealthResponse
{
    private ServiceStatus overallStatus;
    private String timestamp;
    private Map<String, ServiceHealth> services;

    public HealthResponse()
    {
        this.services = new HashMap<>();
        this.timestamp = Instant.now().toString();
        this.overallStatus = ServiceStatus.OK;
    }

    public ServiceStatus getOverallStatus()
    {
        return overallStatus;
    }

    public void setOverallStatus(ServiceStatus overallStatus)
    {
        this.overallStatus = overallStatus;
    }

    public String getTimestamp()
    {
        return timestamp;
    }

    public void setTimestamp(String timestamp)
    {
        this.timestamp = timestamp;
    }

    public Map<String, ServiceHealth> getServices()
    {
        return services;
    }

    public void setServices(Map<String, ServiceHealth> services)
    {
        this.services = services;
    }

    public void addService(String key, ServiceHealth serviceHealth)
    {
        this.services.put(key, serviceHealth);
        updateOverallStatus(serviceHealth.getStatus());
    }

    private void updateOverallStatus(ServiceStatus serviceStatus)
    {
        if (serviceStatus == ServiceStatus.DOWN)
        {
            this.overallStatus = ServiceStatus.DOWN;
        }
        else if (serviceStatus == ServiceStatus.DEGRADED && this.overallStatus != ServiceStatus.DOWN)
        {
            this.overallStatus = ServiceStatus.DEGRADED;
        }
    }
}
