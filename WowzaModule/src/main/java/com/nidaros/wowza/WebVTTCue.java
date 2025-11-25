package com.nidaros.wowza;

/**
 * DTO matching C# WebVTTCueDto for JSON deserialization
 */
public class WebVTTCue {
    public String webvttCue;
    public long startTimestamp;
    public long endTimestamp;
    public String text;
    public String originalText;
}
