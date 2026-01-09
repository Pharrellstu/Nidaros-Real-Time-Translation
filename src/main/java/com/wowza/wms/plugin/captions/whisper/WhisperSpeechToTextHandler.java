/*
 * This code and all components (c) Copyright 2006 - 2025, Wowza Media Systems, LLC.  All rights reserved.
 * This code is licensed pursuant to the Wowza Public License version 1.0, available at www.wowza.com/legal.
 */

package com.wowza.wms.plugin.captions.whisper;

import com.fasterxml.jackson.core.JsonFactory;
import com.fasterxml.jackson.core.JsonParser;
import com.fasterxml.jackson.core.JsonToken;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.wowza.wms.plugin.captions.audio.SpeechHandler;
import com.wowza.wms.plugin.captions.caption.Caption;
import com.wowza.wms.plugin.captions.caption.CaptionHandler;
import com.wowza.wms.plugin.captions.caption.CaptionHelper;
import com.wowza.util.StringUtils;
import com.wowza.wms.application.IApplicationInstance;
import com.wowza.wms.application.WMSProperties;
import com.wowza.wms.logging.WMSLogger;
import com.wowza.wms.logging.WMSLoggerFactory;
import com.wowza.wms.plugin.captions.whisper.model.*;
import com.wowza.wms.timedtext.model.ITimedTextConstants;

import java.io.IOException;
import java.io.InputStream;
import java.net.Socket;
import java.net.SocketException;
import java.nio.ByteBuffer;
import java.time.Duration;
import java.time.Instant;
import java.util.*;
import java.util.concurrent.*;
import java.util.stream.Collectors;

import static com.wowza.wms.plugin.captions.ModuleAzureSpeechToTextCaptions.PROP_DEFAULT_CAPTION_LANGUAGES;
import static com.wowza.wms.plugin.captions.ModuleCaptionsBase.*;
import static com.wowza.wms.plugin.captions.stream.DelayedStream.DEFAULT_START_DELAY;

public class WhisperSpeechToTextHandler implements SpeechHandler
{
    private static final Class<WhisperSpeechToTextHandler> CLASS = WhisperSpeechToTextHandler.class;
    private static final String CLASS_NAME = CLASS.getSimpleName();

    private final LinkedBlockingQueue<ByteBuffer> audioBuffer = new LinkedBlockingQueue<>();
    private final Map<String, LinkedList<CaptionLine>> captionLines = new ConcurrentHashMap<>();
    private final Set<String> processedCaptionIds = ConcurrentHashMap.newKeySet();

    private final WMSLogger logger;
    private final CaptionHandler captionHandler;
    private Socket socket;
    private SocketListener socketListener;

    private final Map<String, String> languageMap;
    private final boolean debugLog;
    private final int maxLineLength;
    private final int maxLineCount;
    private final String socketHost;
    private final int socketPort;

    private final IApplicationInstance appInstance;
    private final int newLineThreshold;
    private final long delay;

    private volatile boolean doQuit = false;
    private volatile boolean outputRunning = false;
    private volatile boolean isConnected = false;

    private int retryCount = 0;
    private static final int MAX_RETRIES = 5;
    private static final long MAX_CAPTION_AGE_MS = 300000; // 5 minutes

    public WhisperSpeechToTextHandler(IApplicationInstance appInstance, CaptionHandler captionHandler)
    {
        this.appInstance = appInstance;
        this.logger = WMSLoggerFactory.getLoggerObj(appInstance);
        this.captionHandler = captionHandler;
        WMSProperties props = appInstance.getProperties();
        this.debugLog = props.getPropertyBoolean(PROP_CAPTIONS_DEBUG_LOG, false);
        this.maxLineLength = props.getPropertyInt(PROP_MAX_CAPTION_LINE_LENGTH, CaptionHelper.defaultMaxLineLengthSBCS);
        this.maxLineCount = props.getPropertyInt(PROP_MAX_CAPTION_LINE_COUNT, 2);
        this.newLineThreshold = props.getPropertyInt(PROP_NEW_LINE_THRESHOLD, DEFAULT_NEW_LINE_THRESHOLD);
        this.delay = props.getPropertyLong(PROP_CAPTIONS_STREAM_DELAY, DEFAULT_START_DELAY);

        String languagesStr = appInstance.getTimedTextProperties().getPropertyStr(PROP_DEFAULT_CAPTION_LANGUAGES, ITimedTextConstants.LANGUAGE_ID_ENGLISH);
        languageMap = Arrays.stream(languagesStr.split(","))
                .map(String::trim)
                .filter(s -> !s.isBlank())
                .collect(Collectors.toMap(
                        s -> toLocale(s).getLanguage(),
                        s -> s,
                        (existing, replacement) -> existing));

        this.socketHost = props.getPropertyStr("whisperSocketHost", "localhost");
        this.socketPort = props.getPropertyInt("whisperSocketPort", 3000);
        try
        {
            this.socket = new Socket(socketHost, socketPort);
            this.socketListener = new SocketListener();
            new Thread(socketListener, CLASS_NAME + ".SocketListener").start();
            this.isConnected = true;
        }
        catch (IOException e)
        {
            socket = null;
            isConnected = false;
            logger.error(CLASS_NAME + " error creating Whisper socket: " + e, e);
        }
    }

    private void addExponentialDelayWithJitter()
    {
        long jitter = (long) (Math.random() * 1000); // Add up to 1 second of random jitter
        long baseDelay = 500;
        long delay = baseDelay * (1L << retryCount) + jitter;
        try {
            Thread.sleep(delay);
        } catch (InterruptedException ie) {
            Thread.currentThread().interrupt();
            throw new RuntimeException(ie);
        }
    }

    private void reconnect()
    {
        isConnected = false;
        
        // Close old socket and listener properly
        try
        {
            if (socket != null && !socket.isClosed())
            {
                socket.close();
            }
        }
        catch (Exception e)
        {
            logger.warn(CLASS_NAME + ".Socket.reconnect: Error closing old socket", e);
        }
        
        clearStaleCaptions();
        
        while (retryCount < MAX_RETRIES && !doQuit)
        {
            try
            {
                logger.info(CLASS_NAME + ".Socket.reconnect: Attempting to reconnect (attempt " + (retryCount + 1) + "/" + MAX_RETRIES + ")...");
                
                socket = new Socket(socketHost, socketPort);
                socketListener = new SocketListener();
                new Thread(socketListener, CLASS_NAME + ".SocketListener").start();
                
                // Reset retry count on successful reconnection
                retryCount = 0;
                isConnected = true;
                logger.info(CLASS_NAME + ".Socket.reconnect: ✓ Successfully reconnected to " + socketHost + ":" + socketPort + " after " + (retryCount > 0 ? retryCount + " retry attempt(s)" : "first attempt"));
                logger.debug(CLASS_NAME + ".Socket.reconnect: Connection restored - caption deduplication active, stale captions cleared");
                break;
            }
            catch (Exception e)
            {
                socket = null;
                retryCount++;
                if (retryCount >= MAX_RETRIES) {
                    logger.error(CLASS_NAME + ".Socket.reconnect: Failed to reconnect after " + MAX_RETRIES + " attempts", e);
                    break;
                }
                logger.warn(CLASS_NAME + ".Socket.reconnect: Retry " + retryCount + " failed", e);
                addExponentialDelayWithJitter();
            }
        }
    }

    @Override
    public void run()
    {
        while (!doQuit)
        {
            try
            {
                if (!outputRunning)
                {
                    outputRunning = true;
                    appInstance.getVHost().getThreadPool().execute(this::processPendingCaptions);
                }
                ByteBuffer frame = audioBuffer.poll(100, TimeUnit.MILLISECONDS);
                if (frame == null)
                    continue;
                if (socket != null && socket.isConnected())
                {
                    socket.getOutputStream().write(frame.array());
                    socket.getOutputStream().flush();
                }
                else
                {
                    if (isConnected)
                    {
                        logger.error(CLASS_NAME + ".run(): Socket is not connected");
                    }
                    reconnect();
                }
            }
            catch (Exception e)
            {
                if (doQuit)
                    break;
                reconnect();
                if (socket == null)
                    break;
            }
        }
    }

    private void processPendingCaptions()
    {
        try
        {
            List<Caption> captions = new ArrayList<>();
            for (Map.Entry<String, LinkedList<CaptionLine>> entry : captionLines.entrySet())
            {
                String language = entry.getKey();
                LinkedList<CaptionLine> lines = entry.getValue();

                synchronized (lines)
                {
                    Instant start = null;
                    Instant end = null;
                    List<String> textList = new ArrayList<>();
                    if (doQuit || lines.size() > maxLineCount || (!lines.isEmpty() && lines.peekLast().getTimeAdded() < System.currentTimeMillis() - delay / 2))
                    {
                        while (textList.size() < maxLineCount && !lines.isEmpty())
                        {
                            CaptionLine line = lines.removeFirst();
                            if (start == null)
                                start = line.getStart();
                            end = line.getEnd();
                            textList.add(line.getText());
                        }
                    }

                    if (!textList.isEmpty())
                    {
                        // todo: make trackid dynamic
                        Caption caption = new Caption(language, start, end, String.join("\n", textList), 99);
                        captions.add(caption);
                    }
                }
            }
            captions.forEach(captionHandler::handleCaption);
        }
        catch (Exception e)
        {
            logger.error(CLASS_NAME + ".processPendingCaptions: Error processing pending captions: " + captionLines, e);
        }
        finally
        {
            outputRunning = false;
        }
    }

    @Override
    public void addAudioFrame(byte[] frame)
    {
        audioBuffer.add(ByteBuffer.wrap(frame));
    }

    public boolean isConnected()
    {
        return isConnected && socket != null && socket.isConnected() && !socket.isClosed();
    }

    @Override
    public void close()
    {
        logger.info(CLASS_NAME + ".close()");
        doQuit = true;
        isConnected = false;
        if (socket != null)
        {
            try
            {
                socket.shutdownOutput();
            }
            catch (IOException e)
            {
                logger.error(CLASS_NAME + ".close: Error closing socket: " + e, e);
            }
        }
        captionLines.clear();
        processedCaptionIds.clear();
    }

    private void clearStaleCaptions()
    {
        logger.info(CLASS_NAME + ".clearStaleCaptions: Clearing stale captions during reconnection");
        captionLines.clear();
        
        // Clean up old processed caption IDs to prevent memory buildup
        long currentTime = System.currentTimeMillis();
        processedCaptionIds.removeIf(id -> {
            try {
                long timestamp = Long.parseLong(id.split("_")[0]);
                return (currentTime - timestamp) > MAX_CAPTION_AGE_MS;
            } catch (Exception e) {
                return false;
            }
        });
    }

    private String generateCaptionId(WhisperResponse response)
    {
        return System.currentTimeMillis() + "_" + response.getLanguage() + "_" + 
               (long)(response.getStart() * 1000) + "_" + (long)(response.getEnd() * 1000) + "_" + 
               response.getText().hashCode();
    }

    private void handleWhisperResponse(WhisperResponse response)
    {
        if (debugLog)
            logger.info(CLASS_NAME + ".handleWhisperResponse: response: " + response);
        
        // Deduplication: Check if caption already processed
        String captionId = generateCaptionId(response);
        if (!processedCaptionIds.add(captionId))
        {
            if (debugLog)
                logger.info(CLASS_NAME + ".handleWhisperResponse: Duplicate caption detected, skipping: " + captionId);
            return;
        }
        
        String language = languageMap.getOrDefault(response.getLanguage(), response.getLanguage());
        LinkedList<CaptionLine> lines = captionLines.computeIfAbsent(language, k -> new LinkedList<>());
        synchronized (lines)
        {
            String text = response.getText();
            if (!StringUtils.isEmpty(text))
            {
                Instant start = CaptionHelper.epochInstantFromMillis((long) (response.getStart() * 1000));
                Instant end = CaptionHelper.epochInstantFromMillis((long) (response.getEnd() * 1000));

                StringBuilder sb = new StringBuilder();
                CaptionLine line = lines.peekLast();
                if (line != null && Duration.between(line.getEnd(), start).toMillis() < newLineThreshold)
                {
                    sb.append(line.getText());
                }
                else
                {
                    line = new CaptionLine(language);
                    line.setStart(start);
                    lines.add(line);
                }

                List<String> items = Arrays.stream(text.split("\\s+")).toList();
                if (debugLog)
                    logger.info(CLASS_NAME + ".handleCaptionMessage: items: " + items);
                float duration = response.getEnd() - response.getStart();
                float perWordDuration = duration / items.size();
                for (int i = 0; i < items.size(); i++)
                {
                    String item = items.get(i);
                    float itemStart = response.getStart() + (i * perWordDuration);
                    float itemEnd = itemStart + perWordDuration;
                    int length = sb.length();
                    if (length > 0)
                    {
                        // check if the text length + preceding space will exceed the max line length. If so, create a new line
                        if (length + 1 + item.length() > maxLineLength)
                        {
                            line.setEnd(end);
                            line.setText(sb.toString());
                            sb.setLength(0);
                            if (debugLog)
                                logger.info(CLASS_NAME + ".handleCaptionMessage(maxLineLength): start: " + line.getStart() + ", end: " + line.getEnd() + ", text: " + line.getText());
                            line = new CaptionLine(language);
                            line.setStart(CaptionHelper.epochInstantFromMillis((long) (itemStart * 1000)));
                            lines.add(line);
                        }
                        else
                            sb.append(" ");
                    }
                    sb.append(item);
                    end = CaptionHelper.epochInstantFromMillis((long) (itemEnd * 1000));
                }
                text = sb.toString();
                if (!text.isEmpty())
                {
                    line.setEnd(end);
                    line.setText(text);
                    if (debugLog)
                        logger.info(CLASS_NAME + ".handleCaptionMessage(end): start: " + line.getStart() + ", end: " + line.getEnd() + ", text: " + line.getText());
                }
            }
        }
    }

    private class SocketListener implements Runnable
    {
        @Override
        public void run()
        {
            Socket localSocket = socket;
            if (localSocket == null || localSocket.isClosed())
            {
                logger.warn(CLASS_NAME + ".SocketListener.run: Socket is null or closed, exiting");
                return;
            }
            
            try (InputStream inputStream = localSocket.getInputStream())
            {
                parseJsonStream(inputStream);
                if (!doQuit)
                {
                    logger.info(CLASS_NAME + ".SocketListener.run: Stream ended, triggering reconnection");
                    isConnected = false;
                }
                processPendingCaptions();
            }
            catch (SocketException s)
            {
                if (!doQuit)
                {
                    logger.info(CLASS_NAME + ".SocketListener.run: SocketException: " + s + ", connection lost");
                    isConnected = false;
                }
            }
            catch (IOException e)
            {
                if (!doQuit)
                {
                    logger.error(CLASS_NAME + ".SocketListener.run exception", e);
                    isConnected = false;
                }
            }
        }

        private void parseJsonStream(InputStream inputStream) throws IOException
        {
            JsonFactory factory = new JsonFactory();
            ObjectMapper objectMapper = new ObjectMapper();
            JsonParser parser = factory.createParser(inputStream);

            while (!parser.isClosed() && !doQuit)
            {
                JsonToken token = parser.nextToken();
                if (token == JsonToken.START_OBJECT)
                {
                    // Deserialize the JSON object into a POJO
                    WhisperResponse response = objectMapper.readValue(parser, WhisperResponse.class);
                    handleWhisperResponse(response);
                }
                else if (token == null)
                {
                    break;
                }
            }
            logger.info(CLASS_NAME + ".parseJsonStream: end");
        }
    }
}
