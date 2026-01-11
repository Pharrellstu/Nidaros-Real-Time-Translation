/*
 * This code and all components (c) Copyright 2006 - 2025, Wowza Media Systems, LLC.  All rights reserved.
 * This code is licensed pursuant to the Wowza Public License version 1.0, available at www.wowza.com/legal.
 */

package com.wowza.wms.plugin.captions.health.model;

/**
 * Enum representing the status of a service.
 */
public enum ServiceStatus
{
    OK("ok"),
    DEGRADED("degraded"),
    DOWN("down");

    private final String value;

    ServiceStatus(String value)
    {
        this.value = value;
    }

    public String getValue()
    {
        return value;
    }
}
