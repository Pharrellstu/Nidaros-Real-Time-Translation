package com.nidaros.wowza;

import com.wowza.wms.amf.*;
import com.wowza.wms.logging.*;
import com.wowza.wms.stream.*;

/**
 * Listens to live packets from stream and captures audio data
 * INRT-601: Real-time audio packet capture
 */
public class LivePacketListener implements IMediaStreamLivePacketNotify {
    
    private final AudioBufferAccumulator accumulator;
    private final WMSLogger logger;
    private final String streamName;
    private int audioPacketCount = 0;
    private int nonAudioPacketCount = 0;
    
    public LivePacketListener(AudioBufferAccumulator accumulator, WMSLogger logger, String streamName) {
        this.accumulator = accumulator;
        this.logger = logger;
        this.streamName = streamName;
    }
    
    @Override
    public void onLivePacket(IMediaStream stream, AMFPacket packet) {
        try {
            // Only process audio packets (type 8 = audio in AMF)
            if (packet.getType() == 8) {
                audioPacketCount++;
                
                // Get audio data and timestamp
                byte[] audioData = packet.getData();
                long timestamp = packet.getAbsTimecode();
                
                // Log first packet for debugging
                if (audioPacketCount == 1) {
                    logger.info("[PACKET-LISTENER-" + streamName + "] First audio packet received: " + 
                               audioData.length + " bytes, timestamp: " + timestamp);
                }
                
                // Add to accumulator for buffering
                accumulator.addAudioPacket(audioData, timestamp);
                
                // Periodic logging every 100 packets
                if (audioPacketCount % 100 == 0) {
                    logger.info("[PACKET-LISTENER-" + streamName + "] Processed " + audioPacketCount + 
                               " audio packets (skipped " + nonAudioPacketCount + " non-audio)");
                }
                
            } else {
                nonAudioPacketCount++;
            }
            
        } catch (Exception e) {
            logger.error("[PACKET-LISTENER-" + streamName + "] Error processing packet: " + 
                        e.getMessage(), e);
        }
    }
}
