package com.nidaros.wowza;

import com.wowza.wms.logging.*;
import com.wowza.wms.stream.*;
import java.util.concurrent.*;

/**
 * Listens to media stream events and attaches packet listeners to capture audio
 * INRT-601: Audio packet capture
 */
public class AudioPacketListener implements IMediaStreamNotify {
    
    private final String dotnetApiUrl;
    private final WMSLogger logger;
    private final ConcurrentHashMap<String, AudioBufferAccumulator> accumulators;
    private final ExecutorService executorService;
    
    public AudioPacketListener(String dotnetApiUrl, WMSLogger logger) {
        this.dotnetApiUrl = dotnetApiUrl;
        this.logger = logger;
        this.accumulators = new ConcurrentHashMap<>();
        this.executorService = Executors.newFixedThreadPool(4);
        
        logger.info("[AUDIO-LISTENER] Initialized with API URL: " + dotnetApiUrl);
    }
    
    @Override
    public void onMediaStreamCreate(IMediaStream stream) {
        String streamName = stream.getName();
        long timestamp = System.currentTimeMillis();
        
        logger.info("[AUDIO-LISTENER] [T=" + timestamp + "] Attaching to stream: " + streamName);
        
        // Create accumulator for this stream (2-second chunks - INRT-602)
        AudioBufferAccumulator accumulator = new AudioBufferAccumulator(
            streamName,
            dotnetApiUrl,
            2000, // 2-second chunks for lower latency
            logger,
            executorService
        );
        
        accumulators.put(streamName, accumulator);
        
        // Attach packet listener to capture audio packets
        LivePacketListener packetListener = new LivePacketListener(accumulator, logger, streamName);
        stream.addLivePacketListener(packetListener);
        
        logger.info("[AUDIO-LISTENER] Successfully attached to stream: " + streamName);
    }
    
    @Override
    public void onMediaStreamDestroy(IMediaStream stream) {
        String streamName = stream.getName();
        long timestamp = System.currentTimeMillis();
        
        logger.info("[AUDIO-LISTENER] [T=" + timestamp + "] Detaching from stream: " + streamName);
        
        // Flush any remaining audio in the buffer
        AudioBufferAccumulator accumulator = accumulators.remove(streamName);
        if (accumulator != null) {
            accumulator.flush();
            logger.info("[AUDIO-LISTENER] Flushed remaining audio for stream: " + streamName);
        }
    }
    
    public void shutdown() {
        logger.info("[AUDIO-LISTENER] Shutting down...");
        
        // Flush all accumulators
        for (AudioBufferAccumulator accumulator : accumulators.values()) {
            accumulator.flush();
        }
        accumulators.clear();
        
        // Shutdown executor
        executorService.shutdown();
        try {
            if (!executorService.awaitTermination(5, TimeUnit.SECONDS)) {
                executorService.shutdownNow();
            }
        } catch (InterruptedException e) {
            executorService.shutdownNow();
        }
        
        logger.info("[AUDIO-LISTENER] Shutdown complete");
    }
}
