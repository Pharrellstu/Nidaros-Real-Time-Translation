/*
 * This code and all components (c) Copyright 2006 - 2025, Wowza Media Systems, LLC.  All rights reserved.
 * This code is licensed pursuant to the Wowza Public License version 1.0, available at www.wowza.com/legal.
 */

package com.wowza.wms.plugin.captions.caption;

import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.slf4j.MDC;

public interface CaptionHandler
{
    String MDC_SERVICE_NAME = "serviceName";
    String SERVICE_NAME_VALUE = "wowza-captions";

    Logger logger = LoggerFactory.getLogger(CaptionHandler.class);

    void handleCaption(Caption caption);

    default void logCaptionSent(Caption caption)
    {
        if (caption == null)
            return;

        try
        {
            MDC.put(MDC_SERVICE_NAME, SERVICE_NAME_VALUE);

            // Keep this light — one log per caption delivery
            logger.info("pipeline event=caption_sent lang={} trackId={} start={} end={}",
                    safeStr(caption.getLanguage()),
                    caption.getTrackId(),
                    caption.getStart(),
                    caption.getEnd());
        }
        finally
        {
            MDC.remove(MDC_SERVICE_NAME);
        }
    }

    default String safeStr(String value)
    {
        return value == null ? "unknown" : value;
    }

    int getWordsPerMinute();

    void setWordsPerMinute(int wordsPerMinute);

    CaptionTiming getCaptionTiming();
}