package com.nidaros.wowza;

import com.wowza.wms.amf.*;
import com.wowza.wms.application.*;
import com.wowza.wms.logging.*;
import com.wowza.wms.stream.*;
import org.apache.http.client.methods.*;
import org.apache.http.impl.client.*;
import org.apache.http.util.EntityUtils;
import com.google.gson.*;
import java.util.*;

/**
 * Polls .NET API for WebVTT captions and injects them into the stream
 * INRT-603: Caption polling and injection
 */
public class CaptionInjector implements Runnable {
    
    private final String apiUrl;
    private final IApplicationInstance appInstance;
    private final WMSLogger logger;
    private final Gson gson;
    private int totalCaptionsInjected = 0;
    
    public CaptionInjector(String apiUrl, IApplicationInstance appInstance, WMSLogger logger) {
        this.apiUrl = apiUrl;
        this.appInstance = appInstance;
        this.logger = logger;
        this.gson = new Gson();
    }
    
    @Override
    public void run() {
        try {
            pollAndInjectCaptions();
        } catch (Exception e) {
            // Graceful degradation: log error but don't stop polling
            logger.error("[CAPTION-INJECTOR] Error in polling cycle: " + e.getMessage());
        }
    }
    
    private void pollAndInjectCaptions() {
        String endpoint = apiUrl + "/api/captions/webvtt";
        
        CloseableHttpClient httpClient = null;
        CloseableHttpResponse response = null;
        
        try {
            httpClient = HttpClients.createDefault();
            HttpGet request = new HttpGet(endpoint);
            
            response = httpClient.execute(request);
            int statusCode = response.getStatusLine().getStatusCode();
            
            if (statusCode == 200) {
                String jsonResponse = EntityUtils.toString(response.getEntity());
                
                // Parse JSON array of WebVTT cues
                WebVTTCue[] cues = gson.fromJson(jsonResponse, WebVTTCue[].class);
                
                if (cues != null && cues.length > 0) {
                    long pollTime = System.currentTimeMillis();
                    logger.info("[CAPTION-INJECTOR] [T=" + pollTime + "] Received " + cues.length + 
                               " caption(s) from API");
                    
                    for (WebVTTCue cue : cues) {
                        injectCaption(cue, pollTime);
                    }
                }
            } else if (statusCode != 404) {
                // 404 means no captions available (normal), other errors should be logged
                logger.warn("[CAPTION-INJECTOR] Unexpected status: HTTP " + statusCode);
            }
            
        } catch (Exception e) {
            logger.error("[CAPTION-INJECTOR] Error polling API: " + e.getMessage());
        } finally {
            try {
                if (response != null) response.close();
                if (httpClient != null) httpClient.close();
            } catch (Exception e) {
                // Ignore cleanup errors
            }
        }
    }
    
    private void injectCaption(WebVTTCue cue, long pollTime) {
        try {
            // Get all active streams in the application
            List<IMediaStream> streams = appInstance.getStreams().getStreams();
            
            if (streams.isEmpty()) {
                logger.warn("[CAPTION-INJECTOR] No active streams to inject caption into");
                return;
            }
            
            for (IMediaStream stream : streams) {
                // Create AMF data for onTextData event (per Wowza documentation)
                // Per https://www.wowza.com/docs/how-to-configure-closed-captioning-for-live-streaming
                // onTextData requires: text, lang, and optionally trackid
                AMFDataMixedArray data = new AMFDataMixedArray();
                
                // Add caption data - Wowza expects 'text', 'lang', and 'trackid'
                data.put("text", new AMFDataItem(cue.text));
                data.put("lang", new AMFDataItem("eng"));  // Three-letter language code
                data.put("trackid", new AMFDataItem(99));  // 99 is the special value for live streams per Wowza docs
                
                // Inject into stream as onTextData event
                stream.sendDirect("onTextData", data);
                
                totalCaptionsInjected++;
                
                long injectTime = System.currentTimeMillis();
                long latency = injectTime - pollTime;
                
                logger.info("[CAPTION-INJECTOR] [T=" + injectTime + "] ✓ Injected caption #" + 
                           totalCaptionsInjected + " into stream '" + stream.getName() + 
                           "': \"" + cue.text + "\" (injection latency: " + latency + "ms)");
            }
            
        } catch (Exception e) {
            logger.error("[CAPTION-INJECTOR] Error injecting caption: " + e.getMessage(), e);
        }
    }
}
