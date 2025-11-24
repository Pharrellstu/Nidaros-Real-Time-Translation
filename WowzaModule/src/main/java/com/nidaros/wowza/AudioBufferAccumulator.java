package com.nidaros.wowza;

import com.wowza.wms.logging.*;
import java.io.*;
import java.util.*;
import java.util.concurrent.*;

/**
 * Accumulates audio packets into 2-second WAV chunks
 * INRT-601, INRT-602: Audio buffering and 2-second chunk creation
 */
public class AudioBufferAccumulator {
    
    private final String streamName;
    private final String dotnetApiUrl;
    private final int chunkDurationMs; // 2000ms
    private final WMSLogger logger;
    private final ExecutorService executor;
    
    private final ByteArrayOutputStream audioBuffer;
    private long chunkStartTimestamp;
    private long lastPacketTimestamp;
    private boolean isFirstPacket = true;
    private int chunkCounter = 0;
    
    // Audio format constants (AAC from RTMP will be converted to PCM)
    private static final int SAMPLE_RATE = 16000; // 16kHz for Whisper
    private static final int CHANNELS = 1; // Mono
    private static final int BITS_PER_SAMPLE = 16;
    
    public AudioBufferAccumulator(String streamName, String dotnetApiUrl, 
                                   int chunkDurationMs, WMSLogger logger, 
                                   ExecutorService executor) {
        this.streamName = streamName;
        this.dotnetApiUrl = dotnetApiUrl;
        this.chunkDurationMs = chunkDurationMs;
        this.logger = logger;
        this.executor = executor;
        this.audioBuffer = new ByteArrayOutputStream();
        
        logger.info("[ACCUMULATOR-" + streamName + "] Initialized with " + chunkDurationMs + "ms chunks");
    }
    
    public synchronized void addAudioPacket(byte[] audioData, long timestamp) {
        try {
            // Initialize on first packet
            if (isFirstPacket) {
                chunkStartTimestamp = timestamp;
                lastPacketTimestamp = timestamp;
                isFirstPacket = false;
                logger.info("[ACCUMULATOR-" + streamName + "] [T=" + timestamp + 
                           "] Started chunk #" + chunkCounter + " at timestamp: " + timestamp);
            }
            
            // Strip AAC/ADTS headers and add raw audio to buffer
            byte[] rawAudio = stripAACHeaders(audioData);
            audioBuffer.write(rawAudio);
            lastPacketTimestamp = timestamp;
            
            // Check if we've accumulated enough for 2 seconds
            long elapsedMs = lastPacketTimestamp - chunkStartTimestamp;
            if (elapsedMs >= chunkDurationMs) {
                flush();
            }
            
        } catch (IOException e) {
            logger.error("[ACCUMULATOR-" + streamName + "] Error adding audio packet: " + 
                        e.getMessage(), e);
        }
    }
    
    public synchronized void flush() {
        if (audioBuffer.size() == 0) {
            return; // Nothing to send
        }
        
        try {
            long flushStartTime = System.currentTimeMillis();
            
            // Create WAV file in memory
            byte[] wavData = createWAVFile(audioBuffer.toByteArray());
            
            // Prepare metadata
            final long startTs = chunkStartTimestamp;
            final long endTs = lastPacketTimestamp;
            final int currentChunk = chunkCounter;
            
            logger.info("[ACCUMULATOR-" + streamName + "] [T=" + flushStartTime + 
                       "] Flushing chunk #" + currentChunk + ": " + wavData.length + 
                       " bytes (timestamps: " + startTs + " - " + endTs + ")");
            
            // Send to .NET API asynchronously (don't block audio capture)
            executor.submit(() -> {
                HttpAudioSender.sendAudio(dotnetApiUrl, streamName, wavData, startTs, endTs, logger);
            });
            
            // Reset buffer for next chunk
            audioBuffer.reset();
            isFirstPacket = true;
            chunkCounter++;
            
        } catch (Exception e) {
            logger.error("[ACCUMULATOR-" + streamName + "] Error flushing buffer: " + 
                        e.getMessage(), e);
        }
    }
    
    /**
     * Strip AAC ADTS header (typically 7-9 bytes)
     * Simple heuristic: if first byte is 0xFF (sync word), skip 7 bytes
     */
    private byte[] stripAACHeaders(byte[] aacData) {
        if (aacData.length > 7 && (aacData[0] & 0xFF) == 0xFF && (aacData[1] & 0xF0) == 0xF0) {
            // This looks like ADTS header
            return Arrays.copyOfRange(aacData, 7, aacData.length);
        }
        // No header detected, return as-is
        return aacData;
    }
    
    /**
     * Create WAV file with proper header
     * Format: PCM, 16kHz, mono, 16-bit
     */
    private byte[] createWAVFile(byte[] audioData) throws IOException {
        ByteArrayOutputStream wavStream = new ByteArrayOutputStream();
        
        // Calculate sizes
        int dataSize = audioData.length;
        int fileSize = 36 + dataSize; // 44 byte header - 8 bytes + data
        
        // RIFF header
        wavStream.write("RIFF".getBytes());
        writeInt(wavStream, fileSize);
        wavStream.write("WAVE".getBytes());
        
        // fmt chunk
        wavStream.write("fmt ".getBytes());
        writeInt(wavStream, 16); // fmt chunk size
        writeShort(wavStream, 1); // Audio format (1 = PCM)
        writeShort(wavStream, CHANNELS);
        writeInt(wavStream, SAMPLE_RATE);
        writeInt(wavStream, SAMPLE_RATE * CHANNELS * BITS_PER_SAMPLE / 8); // Byte rate
        writeShort(wavStream, CHANNELS * BITS_PER_SAMPLE / 8); // Block align
        writeShort(wavStream, BITS_PER_SAMPLE);
        
        // data chunk
        wavStream.write("data".getBytes());
        writeInt(wavStream, dataSize);
        wavStream.write(audioData);
        
        return wavStream.toByteArray();
    }
    
    private void writeInt(OutputStream out, int value) throws IOException {
        out.write(value & 0xFF);
        out.write((value >> 8) & 0xFF);
        out.write((value >> 16) & 0xFF);
        out.write((value >> 24) & 0xFF);
    }
    
    private void writeShort(OutputStream out, int value) throws IOException {
        out.write(value & 0xFF);
        out.write((value >> 8) & 0xFF);
    }
}
