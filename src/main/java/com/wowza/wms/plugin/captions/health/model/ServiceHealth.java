/*
 * This code and all components (c) Copyright 2006 - 2025, Wowza Media Systems, LLC.  All rights reserved.
 * This code is licensed pursuant to the Wowza Public License version 1.0, available at www.wowza.com/legal.
 */

package com.wowza.wms.plugin.captions.health.model;

/**
 * Represents the health status of a single service including key performance metrics.
 */
public class ServiceHealth
{
    private String name;
    private ServiceStatus status;
    private long responseTimeMs;
    private long uptimeSeconds;
    private String message;

    public ServiceHealth()
    {
    }

    public ServiceHealth(String name, ServiceStatus status)
    {
        this.name = name;
        this.status = status;
    }

    public String getName()
    {
        return name;
    }

    public void setName(String name)
    {
        this.name = name;
    }

    public ServiceStatus getStatus()
    {
        return status;
    }

    public void setStatus(ServiceStatus status)
    {
        this.status = status;
    }

    public long getResponseTimeMs()
    {
        return responseTimeMs;
    }

    public void setResponseTimeMs(long responseTimeMs)
    {
        this.responseTimeMs = responseTimeMs;
    }

    public long getUptimeSeconds()
    {
        return uptimeSeconds;
    }

    public void setUptimeSeconds(long uptimeSeconds)
    {
        this.uptimeSeconds = uptimeSeconds;
    }

    public String getMessage()
    {
        return message;
    }

    public void setMessage(String message)
    {
        this.message = message;
    }
}
