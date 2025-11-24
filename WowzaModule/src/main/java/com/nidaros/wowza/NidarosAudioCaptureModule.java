package com.nidaros.wowza;

import com.wowza.wms.module.ModuleBase;
import com.wowza.wms.application.*;
import com.wowza.wms.stream.*;
import com.wowza.wms.logging.*;
import java.util.concurrent.*;

/**
 * Main Wowza module for capturing audio from RTMP streams and sending to .NET API
 * INRT-600: Minimal module with lifecycle logging
 */
public class NidarosAudioCaptureModule extends ModuleBase {
    
    private AudioPacketListener audioListener;
    private String dotnetApiUrl;
    private ScheduledExecutorService captionPoller;
    private WMSLogger logger;
    
    public void onAppStart(IApplicationInstance appInstance) {
        logger = WMSLoggerFactory.getLoggerObj(appInstance);
        String fullname = appInstance.getApplication().getName() + "/" + appInstance.getName();
        long timestamp = System.currentTimeMillis();
        
        logger.info("========================================");
        logger.info("[NIDAROS] [T=" + timestamp + "] Module starting for application: " + fullname);
        logger.info("========================================");
        
        // Get .NET API URL from environment or properties
        dotnetApiUrl = System.getenv("DOTNET_API_URL");
        if (dotnetApiUrl == null || dotnetApiUrl.isEmpty()) {
            dotnetApiUrl = appInstance.getProperties().getPropertyStr("dotnetApiUrl", "http://api-service:5032");
        }
        
        logger.info("[NIDAROS] .NET API URL: " + dotnetApiUrl);
        
        // Create audio packet listener for INRT-601
        audioListener = new AudioPacketListener(dotnetApiUrl, logger);
        appInstance.addMediaStreamListener(audioListener);
        
        logger.info("[NIDAROS] Audio packet listener registered");
        
        // Start caption polling service for INRT-603
        captionPoller = Executors.newScheduledThreadPool(1);
        CaptionInjector injector = new CaptionInjector(dotnetApiUrl, appInstance, logger);
        captionPoller.scheduleAtFixedRate(injector, 0, 500, TimeUnit.MILLISECONDS);
        
        logger.info("[NIDAROS] Caption polling started (500ms interval)");
        logger.info("[NIDAROS] Module initialization complete");
    }
    
    public void onAppStop(IApplicationInstance appInstance) {
        long timestamp = System.currentTimeMillis();
        logger.info("[NIDAROS] [T=" + timestamp + "] Module stopping...");
        
        // Stop caption polling
        if (captionPoller != null) {
            captionPoller.shutdown();
            try {
                if (!captionPoller.awaitTermination(5, TimeUnit.SECONDS)) {
                    captionPoller.shutdownNow();
                }
                logger.info("[NIDAROS] Caption poller stopped");
            } catch (InterruptedException e) {
                captionPoller.shutdownNow();
            }
        }
        
        // Remove audio listener
        if (audioListener != null) {
            appInstance.removeMediaStreamListener(audioListener);
            audioListener.shutdown();
            logger.info("[NIDAROS] Audio listener stopped");
        }
        
        logger.info("[NIDAROS] Module stopped successfully");
    }
    
    public void onStreamCreate(IMediaStream stream) {
        long timestamp = System.currentTimeMillis();
        logger.info("[NIDAROS] [T=" + timestamp + "] Stream created: " + stream.getName() + 
                        " (Type: " + stream.getStreamType() + ")");
    }
    
    public void onStreamDestroy(IMediaStream stream) {
        long timestamp = System.currentTimeMillis();
        logger.info("[NIDAROS] [T=" + timestamp + "] Stream destroyed: " + stream.getName());
    }
}
