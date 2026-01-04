/*
 * This code and all components (c) Copyright 2006 - 2025, Wowza Media Systems, LLC.  All rights reserved.
 * This code is licensed pursuant to the Wowza Public License version 1.0, available at www.wowza.com/legal.
 */

package com.wowza.wms.plugin.captions;

import com.wowza.wms.application.IApplicationInstance;
import com.wowza.wms.module.ModuleBase;
import com.wowza.wms.timedtext.model.ITimedTextConstants;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public class ModuleCaptionsBase extends ModuleBase
{
    public static Class CLASS = ModuleCaptionsBase.class;
    public static String MODULE_NAME = CLASS.getSimpleName();
    public static final String MODULE_VERSION = ReleaseInfo.getVersion();
    public static final String DELAYED_STREAM_SUFFIX = "_delayed";
    public static final String RESAMPLED_STREAM_SUFFIX = "_resampled";
    public static final boolean DEFAULT_CAPTIONS_ENABLED = false;
    public static final String PROP_CAPTIONS_STREAM_DELAY = "captionHandlerStreamDelay";
    public static final String PROP_CAPTIONS_DEBUG_LOG = "captionHandlerDebug";
    public static final String PROP_DELAYED_STREAM_DEBUG_LOG = "captionHandlerDelayedStreamDebugLog";
    public static final String PROP_MAX_CAPTION_LINE_LENGTH = "captionHandlerMaxLineLength";
    public static final String PROP_MAX_CAPTION_LINE_COUNT = "captionHandlerMaxLines";
    public static final String PROP_LINE_TERMINATORS = "captionHandlerFirstPassTerminators";
    public static final String DEFAULT_FIRST_PASS_TERMINATORS = ". |?|!|,|;";
    public static final String PROP_FIRST_PASS_PERCENTAGE = "captionHandlerFirstPassPercentage";
    public static final int DEFAULT_FIRST_PASS_PERCENTAGE = 60;
    public static final String PROP_SPEAKER_CHANGE_INDICATOR = "captionHandlerSpeakerChangeIndicator";
    public static final String DEFAULT_SPEAKER_CHANGE_INDICATOR = ">>";
    public static final String PROP_NEW_LINE_THRESHOLD = "captionHandlerNewLineThreshold";
    public static final int DEFAULT_NEW_LINE_THRESHOLD = 250;
    protected static final Logger logger = LoggerFactory.getLogger(ModuleCaptionsBase.class);

    public void onAppCreate(IApplicationInstance appInstance)
    {
        logger.info("Initializing {} version {}", MODULE_NAME, MODULE_VERSION);

        // Read a few key properties so startup logs help debugging in Docker/local.
        boolean debug = appInstance.getProperties().getPropertyBoolean(PROP_CAPTIONS_DEBUG_LOG, false);
        boolean delayedDebug = appInstance.getProperties().getPropertyBoolean(PROP_DELAYED_STREAM_DEBUG_LOG, false);
        int streamDelayMs = appInstance.getProperties().getPropertyInt(PROP_CAPTIONS_STREAM_DELAY, 0);
        int maxLineLength = appInstance.getProperties().getPropertyInt(PROP_MAX_CAPTION_LINE_LENGTH, 0);
        int maxLines = appInstance.getProperties().getPropertyInt(PROP_MAX_CAPTION_LINE_COUNT, 0);
        String terminators = appInstance.getProperties().getPropertyStr(PROP_LINE_TERMINATORS);
        if (terminators == null || terminators.isBlank())
            terminators = DEFAULT_FIRST_PASS_TERMINATORS;
        int firstPassPct = appInstance.getProperties().getPropertyInt(PROP_FIRST_PASS_PERCENTAGE, DEFAULT_FIRST_PASS_PERCENTAGE);
        String speakerIndicator = appInstance.getProperties().getPropertyStr(PROP_SPEAKER_CHANGE_INDICATOR);
        if (speakerIndicator == null || speakerIndicator.isBlank())
            speakerIndicator = DEFAULT_SPEAKER_CHANGE_INDICATOR;
        int newLineThresholdMs = appInstance.getProperties().getPropertyInt(PROP_NEW_LINE_THRESHOLD, DEFAULT_NEW_LINE_THRESHOLD);
        logger.info(
                "Captions config: debug={} delayedDebug={} streamDelayMs={} maxLineLength={} maxLines={} firstPassPct={} newLineThresholdMs={} speakerIndicator='{}' terminators='{}'",
                debug,
                delayedDebug,
                streamDelayMs,
                maxLineLength,
                maxLines,
                firstPassPct,
                newLineThresholdMs,
                speakerIndicator,
                terminators
        );

        // Ensure DVR recorder control suffixes contain our delayed/resampled suffixes.
        String suffixes = appInstance.getProperties().getPropertyStr("dvrRecorderControlSuffixes");
        if (suffixes != null && !suffixes.isBlank())
        {
            boolean hasDelayed = suffixes.contains(DELAYED_STREAM_SUFFIX);
            boolean hasResampled = suffixes.contains(RESAMPLED_STREAM_SUFFIX);

            if (!hasDelayed || !hasResampled)
            {
                StringBuilder newSuffixes = new StringBuilder(suffixes);
                if (!hasDelayed)
                    newSuffixes.append(",").append(DELAYED_STREAM_SUFFIX);
                if (!hasResampled)
                    newSuffixes.append(",").append(RESAMPLED_STREAM_SUFFIX);

                appInstance.getProperties().setProperty("dvrRecorderControlSuffixes", newSuffixes.toString());
                logger.info("Updated dvrRecorderControlSuffixes with delayed/resampled stream suffixes");
            }
            else
            {
                logger.debug("dvrRecorderControlSuffixes already contains delayed/resampled stream suffixes");
            }
        }
        else
        {
            logger.warn("dvrRecorderControlSuffixes property not found or empty; delayed/resampled suffixes not appended");
        }
    }
}