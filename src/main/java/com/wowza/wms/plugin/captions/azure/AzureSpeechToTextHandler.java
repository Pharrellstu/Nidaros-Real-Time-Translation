/*
 * This code and all components (c) Copyright 2006 - 2025, Wowza Media Systems, LLC.  All rights reserved.
 * This code is licensed pursuant to the Wowza Public License version 1.0, available at www.wowza.com/legal.
 */

package com.wowza.wms.plugin.captions.azure;

import com.microsoft.cognitiveservices.speech.*;
import com.microsoft.cognitiveservices.speech.audio.*;
import com.microsoft.cognitiveservices.speech.translation.*;
import com.wowza.wms.application.*;
import com.wowza.wms.plugin.captions.audio.SpeechHandler;
import com.wowza.wms.plugin.captions.caption.Caption;
import com.wowza.wms.plugin.captions.caption.CaptionHandler;
import com.wowza.wms.plugin.captions.caption.CaptionHelper;
import com.wowza.wms.plugin.captions.caption.CaptionTiming;
import com.wowza.wms.timedtext.model.ITimedTextConstants;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.slf4j.MDC;

import java.time.*;
import java.time.format.DateTimeFormatter;
import java.util.*;
import java.util.concurrent.*;
import java.util.stream.Collectors;

import static com.wowza.wms.plugin.captions.ModuleAzureSpeechToTextCaptions.*;

public class AzureSpeechToTextHandler implements SpeechHandler
{
    private static final Class<AzureSpeechToTextHandler> CLASS = AzureSpeechToTextHandler.class;
    private static final String CLASS_NAME = CLASS.getSimpleName();

    // Logging / tracing keys (must match Logback JSON MDC fields)
    private static final String MDC_SERVICE_NAME = "serviceName";
    private static final String MDC_SESSION_ID = "sessionId";
    private static final String MDC_REQUEST_ID = "requestId";

    // Keep it stable across this Wowza module so logs are consistent
    private static final String SERVICE_NAME_VALUE = "wowza-captions";
    private static final String ENGINE_VALUE = "azure";

    public static final String DEFAULT_RECOGNITION_LANGUAGE = "en-US";

    private static final Logger logger = LoggerFactory.getLogger(AzureSpeechToTextHandler.class);

    private final CaptionHandler captionHandler;
    private final PushAudioInputStream audioStream = PushAudioInputStream.createPushStream();
    private final Semaphore semaphore = new Semaphore(0);
    private final SpeechConfig speechConfig;

    private final String recognitionLanguage;
    private final Map<String, String> languageMap;
    private final List<String> translationLanguages;
    private final List<String> phrases;

    private final boolean debugLog;
    private final int maxLineLength;
    private final int maxLines;
    private final String firstPassTerminators;
    private final int firstPassPercentage;

    // Trace fields available for this handler lifetime
    private final String requestId = UUID.randomUUID().toString();
    private volatile String azureSessionId;

    public AzureSpeechToTextHandler(IApplicationInstance appInstance, CaptionHandler captionHandler, String subscriptionKey,
                                    String serviceRegion)
    {
        WMSProperties props = appInstance.getProperties();
        debugLog = props.getPropertyBoolean(PROP_CAPTIONS_DEBUG_LOG, false);
        firstPassTerminators = props.getPropertyStr(PROP_LINE_TERMINATORS, DEFAULT_FIRST_PASS_TERMINATORS);
        firstPassPercentage = props.getPropertyInt(PROP_FIRST_PASS_PERCENTAGE, DEFAULT_FIRST_PASS_PERCENTAGE);
        maxLineLength = props.getPropertyInt(PROP_MAX_CAPTION_LINE_LENGTH, CaptionHelper.defaultMaxLineLengthSBCS);
        maxLines = props.getPropertyInt(PROP_MAX_CAPTION_LINE_COUNT, 2);

        this.captionHandler = captionHandler;

        recognitionLanguage = toLocale(props.getPropertyStr(PROP_RECOGNITION_LANGUAGE, DEFAULT_RECOGNITION_LANGUAGE))
                .toLanguageTag();
        if (!isBCP47WithRegion(recognitionLanguage))
            throw new RuntimeException("Invalid recognition language: " + recognitionLanguage);

        String languagesStr = appInstance.getTimedTextProperties().getPropertyStr(
                PROP_DEFAULT_CAPTION_LANGUAGES,
                ITimedTextConstants.LANGUAGE_ID_ENGLISH
        );

        languageMap = Arrays.stream(languagesStr.split(","))
                .map(String::trim)
                .filter(s -> !s.isBlank())
                .collect(Collectors.toMap(
                        s -> toLocale(s).getLanguage(),
                        s -> s,
                        (existing, replacement) -> existing));

        translationLanguages = languageMap.keySet().stream()
                .filter(lang -> !lang.equals(Locale.forLanguageTag(recognitionLanguage).getLanguage()))
                .collect(Collectors.toList());

        String phraseStr = props.getPropertyStr(PROP_PHRASE_LIST, "");
        phrases = Arrays.stream(phraseStr.split(";"))
                .map(String::trim)
                .filter(s -> !s.isBlank())
                .collect(Collectors.toList());

        speechConfig = translationLanguages.isEmpty()
                ? SpeechConfig.fromSubscription(subscriptionKey, serviceRegion)
                : SpeechTranslationConfig.fromSubscription(subscriptionKey, serviceRegion);

        speechConfig.setSpeechRecognitionLanguage(recognitionLanguage);

        ProfanityOption profanityOption = ProfanityOption.Masked;
        try
        {
            profanityOption = ProfanityOption.valueOf(props.getPropertyStr(PROP_PROFANITY_MASK_OPTION, "Masked"));
        }
        catch (IllegalArgumentException ignored)
        {
            // keep default
        }
        speechConfig.setProfanity(profanityOption);

        // Minimal construction log (avoid leaking keys/regions etc.)
        logger.info("{}::{} created (engine={} recognitionLanguage={} targets={})",
                MODULE_NAME, CLASS_NAME, ENGINE_VALUE, recognitionLanguage, translationLanguages);
    }

    @Override
    public void run()
    {
        // Ensure core MDC values are present for all logs in this handler
        MDC.put(MDC_SERVICE_NAME, SERVICE_NAME_VALUE);
        MDC.put(MDC_REQUEST_ID, requestId);

        long startTs = System.currentTimeMillis();
        logger.info("pipeline event=stt_start engine={} requestId={}", ENGINE_VALUE, requestId);

        try (speechConfig; audioStream; AudioConfig audioConfig = AudioConfig.fromStreamInput(audioStream))
        {
            Recognizer recognizer;
            if (speechConfig instanceof SpeechTranslationConfig)
            {
                translationLanguages.forEach(lang -> ((SpeechTranslationConfig) speechConfig).addTargetLanguage(lang));
                recognizer = new TranslationRecognizer((SpeechTranslationConfig) speechConfig, audioConfig);
            }
            else
            {
                recognizer = new SpeechRecognizer(speechConfig, audioConfig);
            }

            try (recognizer)
            {
                String recognizerName = recognizer.getClass().getSimpleName();

                if (!phrases.isEmpty())
                {
                    PhraseListGrammar grammar = PhraseListGrammar.fromRecognizer(recognizer);
                    phrases.forEach(grammar::addPhrase);
                }

                // Event listeners: keep them structured and consistent
                recognizer.sessionStarted.addEventListener((s, e) -> {
                    azureSessionId = e.getSessionId();
                    MDC.put(MDC_SESSION_ID, azureSessionId);
                    logger.info("pipeline event=stt_session_started engine={} recognizer={} sessionId={}",
                            ENGINE_VALUE, recognizerName, azureSessionId);
                });

                recognizer.sessionStopped.addEventListener((s, e) -> {
                    MDC.put(MDC_SESSION_ID, e.getSessionId());
                    logger.info("pipeline event=stt_session_stopped engine={} recognizer={} sessionId={}",
                            ENGINE_VALUE, recognizerName, e.getSessionId());
                });

                recognizer.speechStartDetected.addEventListener((s, e) -> {
                    MDC.put(MDC_SESSION_ID, e.getSessionId());
                    logger.debug("pipeline event=speech_start_detected engine={} sessionId={}", ENGINE_VALUE, e.getSessionId());
                });

                recognizer.speechEndDetected.addEventListener((s, e) -> {
                    MDC.put(MDC_SESSION_ID, e.getSessionId());
                    logger.debug("pipeline event=speech_end_detected engine={} sessionId={}", ENGINE_VALUE, e.getSessionId());
                });

                if (recognizer instanceof TranslationRecognizer)
                {
                    ((TranslationRecognizer) recognizer).recognizing.addEventListener((s, e) ->
                            handleRecognizingEvent(e.getSessionId(), e.getResult()));

                    ((TranslationRecognizer) recognizer).recognized.addEventListener((s, e) ->
                            handleRecognizedEvent(e.getSessionId(), e.getResult()));

                    ((TranslationRecognizer) recognizer).canceled.addEventListener((s, e) ->
                            handleCancelledEvent(e.getSessionId(), e.getReason(), e.getErrorCode(), e.getErrorDetails()));

                    ((TranslationRecognizer) recognizer).startContinuousRecognitionAsync().get();
                    semaphore.acquire();
                    ((TranslationRecognizer) recognizer).stopContinuousRecognitionAsync().get();
                }
                else
                {
                    ((SpeechRecognizer) recognizer).recognizing.addEventListener((s, e) ->
                            handleRecognizingEvent(e.getSessionId(), e.getResult()));

                    ((SpeechRecognizer) recognizer).recognized.addEventListener((s, e) ->
                            handleRecognizedEvent(e.getSessionId(), e.getResult()));

                    ((SpeechRecognizer) recognizer).canceled.addEventListener((s, e) ->
                            handleCancelledEvent(e.getSessionId(), e.getReason(), e.getErrorCode(), e.getErrorDetails()));

                    ((SpeechRecognizer) recognizer).startContinuousRecognitionAsync().get();
                    semaphore.acquire();
                    ((SpeechRecognizer) recognizer).stopContinuousRecognitionAsync().get();
                }
            }
            catch (InterruptedException ignored)
            {
                // normal shutdown path sometimes
                Thread.currentThread().interrupt();
                logger.warn("pipeline event=stt_interrupted engine={} requestId={}", ENGINE_VALUE, requestId);
            }

            long durationMs = System.currentTimeMillis() - startTs;
            logger.info("pipeline event=stt_success engine={} durationMs={} requestId={}", ENGINE_VALUE, durationMs, requestId);
        }
        catch (Exception e)
        {
            long durationMs = System.currentTimeMillis() - startTs;
            logger.error("pipeline event=stt_fail engine={} durationMs={} requestId={}", ENGINE_VALUE, durationMs, requestId, e);
        }
        finally
        {
            // Clear MDC for this handler thread
            MDC.remove(MDC_SESSION_ID);
            MDC.remove(MDC_REQUEST_ID);
            MDC.remove(MDC_SERVICE_NAME);
        }
    }

    private void handleRecognizingEvent(String sessionId, RecognitionResult result)
    {
        MDC.put(MDC_SESSION_ID, sessionId);

        if (debugLog)
        {
            Instant start = CaptionHelper.epochInstantFromTicks(result.getOffset());
            Instant end = CaptionHelper.epochInstantFromTicks(result.getOffset().add(result.getDuration()));

            String latencyStr = result.getProperties().getProperty(PropertyId.SpeechServiceResponse_RecognitionLatencyMs);
            long latency = safeLong(latencyStr, -1);

            // JSON can be large; keep it for debug only
            String json = result.getProperties().getProperty(PropertyId.SpeechServiceResponse_JsonResult);

            logger.debug("stt recognizing engine={} sessionId={} timing={} latencyMs={} json={}",
                    ENGINE_VALUE, sessionId, getTimestamp(start, end), latency, json);
        }
    }

    private void handleRecognizedEvent(String sessionId, RecognitionResult result)
    {
        MDC.put(MDC_SESSION_ID, sessionId);

        if (result.getReason() == ResultReason.NoMatch)
        {
            if (debugLog)
                logger.info("pipeline event=stt_nomatch engine={} sessionId={}", ENGINE_VALUE, sessionId);
            return;
        }

        Instant start = CaptionHelper.epochInstantFromTicks(result.getOffset());
        Instant end = CaptionHelper.epochInstantFromTicks(result.getOffset().add(result.getDuration()));

        String latencyStr = result.getProperties().getProperty(PropertyId.SpeechServiceResponse_RecognitionLatencyMs);
        long latency = safeLong(latencyStr, -1);

        if (debugLog)
        {
            String json = result.getProperties().getProperty(PropertyId.SpeechServiceResponse_JsonResult);
            logger.debug("stt recognized engine={} sessionId={} timing={} latencyMs={} json={}",
                    ENGINE_VALUE, sessionId, getTimestamp(start, end), latency, json);
        }
        else
        {
            // Keep a small “result received” signal even when debug is off
            logger.info("pipeline event=stt_result_received engine={} sessionId={} latencyMs={}",
                    ENGINE_VALUE, sessionId, latency);
        }

        handleResult(result, start, end);
    }

    private void handleResult(RecognitionResult result, Instant start, Instant end)
    {
        // Captions generated/sent are part of the pipeline visibility
        CaptionTiming captionTiming = new CaptionTiming(start, end);

        List<Caption> sourceCaptions = CaptionHelper.getCaptions(
                languageMap.get(Locale.forLanguageTag(recognitionLanguage).getLanguage()),
                maxLineLength,
                maxLines,
                firstPassTerminators,
                firstPassPercentage,
                captionTiming,
                result.getText()
        );

        if (!sourceCaptions.isEmpty())
        {
            logger.info("pipeline event=caption_generated engine={} count={} lang={}",
                    ENGINE_VALUE, sourceCaptions.size(), recognitionLanguage);
        }

        sourceCaptions.forEach(captionHandler::handleCaption);

        if (result instanceof TranslationRecognitionResult)
        {
            ((TranslationRecognitionResult) result).getTranslations().forEach((language, translation) -> {
                List<Caption> translatedCaptions = CaptionHelper.getCaptions(
                        languageMap.get(language),
                        maxLineLength,
                        maxLines,
                        firstPassTerminators,
                        firstPassPercentage,
                        captionTiming,
                        translation
                );

                if (!translatedCaptions.isEmpty())
                {
                    logger.info("pipeline event=caption_generated engine={} count={} lang={}",
                            ENGINE_VALUE, translatedCaptions.size(), language);
                }

                translatedCaptions.forEach(captionHandler::handleCaption);
            });
        }
    }

    private void handleCancelledEvent(String sessionId, CancellationReason reason, CancellationErrorCode errorCode, String errorDetails)
    {
        MDC.put(MDC_SESSION_ID, sessionId);

        logger.warn("pipeline event=stt_cancelled engine={} sessionId={} reason={}",
                ENGINE_VALUE, sessionId, reason);

        if (reason == CancellationReason.Error)
        {
            logger.error("pipeline event=stt_cancelled_error engine={} sessionId={} errorCode={}",
                    ENGINE_VALUE, sessionId, errorCode);

            // Error details may include sensitive info; keep it as error but not too verbose
            logger.error("pipeline event=stt_cancelled_error_details engine={} sessionId={} details={}",
                    ENGINE_VALUE, sessionId, errorDetails);
        }

        semaphore.release();
    }

    @Override
    public void addAudioFrame(byte[] frame)
    {
        audioStream.write(frame);
    }

    @Override
    public void close()
    {
        logger.info("pipeline event=stt_close engine={} requestId={}", ENGINE_VALUE, requestId);
        audioStream.close();
        semaphore.release();
    }

    private String getTimestamp(Instant startTime, Instant endTime)
    {
        var format = "HH:mm:ss.SSS";
        // Set the timezone to UTC so the time is not adjusted for our local time zone.
        var formatter = DateTimeFormatter.ofPattern(format).withZone(ZoneId.from(ZoneOffset.UTC));
        return String.format("%s --> %s", formatter.format(startTime), formatter.format(endTime));
    }

    private long safeLong(String value, long fallback)
    {
        if (value == null || value.isBlank())
            return fallback;
        try
        {
            return Long.parseLong(value);
        }
        catch (Exception ignored)
        {
            return fallback;
        }
    }
}