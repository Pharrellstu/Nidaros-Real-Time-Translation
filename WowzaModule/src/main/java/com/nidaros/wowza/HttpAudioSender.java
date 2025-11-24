package com.nidaros.wowza;

import com.wowza.wms.logging.*;
import org.apache.http.client.methods.*;
import org.apache.http.entity.mime.*;
import org.apache.http.entity.mime.content.*;
import org.apache.http.entity.ContentType;
import org.apache.http.impl.client.*;
import org.apache.http.HttpEntity;
import java.io.*;

/**
 * Sends audio chunks to .NET API via HTTP POST
 * INRT-601: Audio transmission to API
 */
public class HttpAudioSender {
    
    public static void sendAudio(String apiUrl, String streamName, byte[] wavData,
                                  long startTs, long endTs, WMSLogger logger) {
        String endpoint = apiUrl + "/api/audio/process";
        
        CloseableHttpClient httpClient = null;
        CloseableHttpResponse response = null;
        
        try {
            httpClient = HttpClients.createDefault();
            
            HttpPost uploadFile = new HttpPost(endpoint);
            
            // Create multipart request with audio file and metadata
            MultipartEntityBuilder builder = MultipartEntityBuilder.create();
            builder.addBinaryBody("file", wavData, 
                ContentType.create("audio/wav"), "audio.wav");
            builder.addTextBody("streamName", streamName);
            builder.addTextBody("startTimestamp", String.valueOf(startTs));
            builder.addTextBody("endTimestamp", String.valueOf(endTs));
            
            HttpEntity multipart = builder.build();
            uploadFile.setEntity(multipart);
            
            // Send request with timestamp logging (INRT-604)
            long sendStartTime = System.currentTimeMillis();
            logger.info("[HTTP-SENDER] [T=" + sendStartTime + "] Sending " + wavData.length + 
                       " bytes to " + endpoint);
            
            response = httpClient.execute(uploadFile);
            
            long sendEndTime = System.currentTimeMillis();
            int statusCode = response.getStatusLine().getStatusCode();
            long latency = sendEndTime - sendStartTime;
            
            if (statusCode == 200 || statusCode == 202) {
                logger.info("[HTTP-SENDER] [T=" + sendEndTime + "] ✓ Successfully sent audio " +
                           "for stream '" + streamName + "' (HTTP " + statusCode + ", latency: " + 
                           latency + "ms)");
            } else {
                logger.warn("[HTTP-SENDER] Unexpected status code: HTTP " + statusCode + 
                           " for stream '" + streamName + "'");
            }
            
        } catch (IOException e) {
            // Graceful degradation (INRT-605): Log error but don't crash
            logger.error("[HTTP-SENDER] Failed to send audio for stream '" + streamName + "': " + 
                        e.getMessage());
            logger.info("[HTTP-SENDER] Stream will continue without captions (graceful degradation)");
        } finally {
            // Clean up resources
            try {
                if (response != null) {
                    response.close();
                }
                if (httpClient != null) {
                    httpClient.close();
                }
            } catch (IOException e) {
                logger.warn("[HTTP-SENDER] Error closing HTTP client: " + e.getMessage());
            }
        }
    }
}
