/*
 * This code and all components (c) Copyright 2006 - 2025, Wowza Media Systems, LLC.  All rights reserved.
 * This code is licensed pursuant to the Wowza Public License version 1.0, available at www.wowza.com/legal.
 */

package com.wowza.wms.plugin.captions.caption;

import com.wowza.wms.amf.*;
import com.wowza.wms.application.IApplicationInstance;
import com.wowza.wms.plugin.captions.stream.DelayedStream;
import com.wowza.wms.vhost.IVHost;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

import java.time.Instant;

import static com.wowza.wms.plugin.captions.ModuleCaptionsBase.PROP_CAPTIONS_DEBUG_LOG;
import static com.wowza.wms.plugin.captions.caption.CaptionHelper.dotNetEpoch;

public class DelayedStreamCaptionHandler implements CaptionHandler
{
    private static final Class<DelayedStreamCaptionHandler> CLASS = DelayedStreamCaptionHandler.class;
    private static final String CLASS_NAME = CLASS.getSimpleName();
    private static final int DEFAULT_WORDS_PER_MINUTE = 150;

    private static final Logger logger = LoggerFactory.getLogger(DelayedStreamCaptionHandler.class);

    private final DelayedStream delayedStream;
    private final boolean debugLog;

    private int wordsPerMinute = DEFAULT_WORDS_PER_MINUTE;

    public DelayedStreamCaptionHandler(IApplicationInstance appInstance, DelayedStream delayedStream)
    {
        this.delayedStream = delayedStream;
        debugLog = appInstance.getProperties().getPropertyBoolean(PROP_CAPTIONS_DEBUG_LOG, false);
    }

    @Override
    public void handleCaption(Caption caption)
    {
        if (caption == null)
            return;

        if (debugLog)
            logger.debug("{}.handleCaption: caption={}", CLASS_NAME, caption);

        if (delayedStream == null)
        {
            logger.warn("pipeline event=caption_drop reason=delayedStream_null lang={} trackId={}",
                    safeStr(caption.getLanguage()),
                    caption.getTrackId());
            return;
        }

        AMFDataObj amfData = new AMFDataObj();
        amfData.put("text", new AMFDataItem(caption.getText()));
        amfData.put("language", new AMFDataItem(caption.getLanguage()));
        amfData.put("trackid", new AMFDataItem(caption.getTrackId()));

        AMFDataList dataList = new AMFDataList();
        dataList.add(new AMFDataItem("onTextData"));
        dataList.add(amfData);

        byte[] data = dataList.serialize();

        long startOffset = delayedStream.getStartOffset();
        long captionOffset = caption.getBegin();

        AMFPacket packet = new AMFPacket(IVHost.CONTENTTYPE_DATA, 0, data);
        packet.setAbsTimecode(startOffset + captionOffset);

        if (debugLog)
            logger.debug("{}.handleCaption: packet={}", CLASS_NAME, packet);

        delayedStream.writePacket(packet);
        logCaptionSent(caption);
    }

    @Override
    public int getWordsPerMinute()
    {
        return wordsPerMinute;
    }

    @Override
    public void setWordsPerMinute(int wordsPerMinute)
    {
        this.wordsPerMinute = wordsPerMinute;
    }

    @Override
    public CaptionTiming getCaptionTiming()
    {
        long startOffset = delayedStream.getStartOffset();
        long firstTC = delayedStream.getFirstPacketTimecode();
        long lastTC = delayedStream.getLastPacketTimecode();

        Instant start = dotNetEpoch.plusMillis(firstTC - startOffset);
        Instant end = dotNetEpoch.plusMillis(lastTC - startOffset);

        return new CaptionTiming(start, end);
    }

    private String safeStr(String value)
    {
        return value == null ? "unknown" : value;
    }
}