using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace NidarosRTT.Infrastructure.Captions
{
    /// <summary>
    /// Thread-safe queue for managing captions with timecode-based matching
    /// </summary>
    public class CaptionQueue
    {
        private readonly ConcurrentDictionary<uint, CaptionEntry> _captions;
        private readonly ILogger<CaptionQueue>? _logger;
        private readonly int _defaultToleranceMs;
        private readonly int _maxCaptionAgeMs;
        
        public CaptionQueue(ILogger<CaptionQueue>? logger = null, int defaultToleranceMs = 500, int maxCaptionAgeMs = 60000)
        {
            _captions = new ConcurrentDictionary<uint, CaptionEntry>();
            _logger = logger;
            _defaultToleranceMs = defaultToleranceMs;
            _maxCaptionAgeMs = maxCaptionAgeMs;
        }
        
        /// <summary>
        /// Queue a caption with its timecode
        /// </summary>
        /// <param name="text">Caption text</param>
        /// <param name="timecode">Timecode in milliseconds</param>
        public void QueueCaption(string text, uint timecode)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                _logger?.LogWarning("Attempted to queue empty caption at timecode {Timecode}ms", timecode);
                return;
            }
            
            var entry = new CaptionEntry
            {
                Text = text,
                Timecode = timecode,
                QueuedAt = DateTime.UtcNow,
                IsConsumed = false
            };
            
            _captions[timecode] = entry;
            
            _logger?.LogDebug("Queued caption at {Timecode}ms: '{Text}' (length: {Length})", 
                timecode, text.Substring(0, Math.Min(text.Length, 50)), text.Length);
            
            // Clean up old captions to prevent memory leaks
            CleanupOldCaptions();
        }
        
        /// <summary>
        /// Try to get a caption for the given timecode with tolerance window
        /// </summary>
        /// <param name="timecode">Target timecode in milliseconds</param>
        /// <param name="toleranceMs">Tolerance window in milliseconds (default from constructor)</param>
        /// <returns>Caption text if found within tolerance, null otherwise</returns>
        public string? TryGetCaptionForTimecode(uint timecode, int? toleranceMs = null)
        {
            int tolerance = toleranceMs ?? _defaultToleranceMs;
            
            // Search within tolerance window
            var matchingEntry = _captions
                .Where(kvp => !kvp.Value.IsConsumed)
                .Where(kvp => Math.Abs((long)kvp.Key - timecode) <= tolerance)
                .OrderBy(kvp => Math.Abs((long)kvp.Key - timecode))
                .FirstOrDefault();
            
            if (matchingEntry.Value != null)
            {
                // Mark as consumed
                matchingEntry.Value.IsConsumed = true;
                matchingEntry.Value.ConsumedAt = DateTime.UtcNow;
                
                _logger?.LogDebug("Found caption for timecode {Timecode}ms (matched: {MatchedTimecode}ms, delta: {Delta}ms): '{Text}'",
                    timecode, matchingEntry.Key, Math.Abs((long)matchingEntry.Key - timecode), 
                    matchingEntry.Value.Text.Substring(0, Math.Min(matchingEntry.Value.Text.Length, 50)));
                
                return matchingEntry.Value.Text;
            }
            
            return null;
        }
        
        /// <summary>
        /// Get caption exactly at the specified timecode (no tolerance)
        /// </summary>
        public string? GetCaptionExact(uint timecode)
        {
            if (_captions.TryGetValue(timecode, out var entry) && !entry.IsConsumed)
            {
                entry.IsConsumed = true;
                entry.ConsumedAt = DateTime.UtcNow;
                return entry.Text;
            }
            
            return null;
        }
        
        /// <summary>
        /// Check if a caption exists for the given timecode (within tolerance)
        /// </summary>
        public bool HasCaptionForTimecode(uint timecode, int? toleranceMs = null)
        {
            int tolerance = toleranceMs ?? _defaultToleranceMs;
            
            return _captions
                .Any(kvp => !kvp.Value.IsConsumed && 
                           Math.Abs((long)kvp.Key - timecode) <= tolerance);
        }
        
        /// <summary>
        /// Get all queued captions (for debugging)
        /// </summary>
        public IReadOnlyDictionary<uint, string> GetQueuedCaptions(bool includeConsumed = false)
        {
            return _captions
                .Where(kvp => includeConsumed || !kvp.Value.IsConsumed)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.Text);
        }
        
        /// <summary>
        /// Get queue statistics
        /// </summary>
        public CaptionQueueStats GetStats()
        {
            var allCaptions = _captions.Values.ToList();
            
            return new CaptionQueueStats
            {
                TotalQueued = allCaptions.Count,
                UnconsumedCount = allCaptions.Count(c => !c.IsConsumed),
                ConsumedCount = allCaptions.Count(c => c.IsConsumed),
                OldestTimecode = allCaptions.Any() ? allCaptions.Min(c => c.Timecode) : 0,
                NewestTimecode = allCaptions.Any() ? allCaptions.Max(c => c.Timecode) : 0,
                AverageLatencyMs = allCaptions
                    .Where(c => c.IsConsumed && c.ConsumedAt.HasValue)
                    .Select(c => (c.ConsumedAt!.Value - c.QueuedAt).TotalMilliseconds)
                    .DefaultIfEmpty(0)
                    .Average()
            };
        }
        
        /// <summary>
        /// Clear all captions from the queue
        /// </summary>
        public void Clear()
        {
            var count = _captions.Count;
            _captions.Clear();
            _logger?.LogInformation("Cleared {Count} captions from queue", count);
        }
        
        /// <summary>
        /// Remove captions older than maxCaptionAgeMs
        /// </summary>
        private void CleanupOldCaptions()
        {
            var cutoffTime = DateTime.UtcNow.AddMilliseconds(-_maxCaptionAgeMs);
            var toRemove = _captions
                .Where(kvp => kvp.Value.QueuedAt < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();
            
            foreach (var timecode in toRemove)
            {
                if (_captions.TryRemove(timecode, out var removed))
                {
                    _logger?.LogDebug("Removed old caption at {Timecode}ms (age: {Age}s, consumed: {Consumed})",
                        timecode, (DateTime.UtcNow - removed.QueuedAt).TotalSeconds, removed.IsConsumed);
                }
            }
        }
        
        /// <summary>
        /// Get the number of captions currently in the queue
        /// </summary>
        public int Count => _captions.Count;
        
        /// <summary>
        /// Get the number of unconsumed captions
        /// </summary>
        public int UnconsumedCount => _captions.Values.Count(c => !c.IsConsumed);
    }
    
    /// <summary>
    /// Internal caption entry with metadata
    /// </summary>
    internal class CaptionEntry
    {
        public required string Text { get; set; }
        public uint Timecode { get; set; }
        public DateTime QueuedAt { get; set; }
        public bool IsConsumed { get; set; }
        public DateTime? ConsumedAt { get; set; }
    }
    
    /// <summary>
    /// Statistics about the caption queue
    /// </summary>
    public class CaptionQueueStats
    {
        public int TotalQueued { get; set; }
        public int UnconsumedCount { get; set; }
        public int ConsumedCount { get; set; }
        public uint OldestTimecode { get; set; }
        public uint NewestTimecode { get; set; }
        public double AverageLatencyMs { get; set; }
    }
}
