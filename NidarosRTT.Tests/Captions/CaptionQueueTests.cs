using System;
using System.Threading.Tasks;
using System.Linq;
using Xunit;
using NidarosRTT.Infrastructure.Captions;

namespace NidarosRTT.Tests.Captions
{
    public class CaptionQueueTests
    {
        [Fact]
        public void QueueCaption_ValidCaption_AddsToQueue()
        {
            // Arrange
            var queue = new CaptionQueue();
            var text = "Test caption";
            uint timecode = 1000;
            
            // Act
            queue.QueueCaption(text, timecode);
            
            // Assert
            Assert.Equal(1, queue.Count);
            var caption = queue.GetCaptionExact(timecode);
            Assert.Equal(text, caption);
        }

        [Fact]
        public void QueueCaption_EmptyText_DoesNotAddToQueue()
        {
            // Arrange
            var queue = new CaptionQueue();
            
            // Act
            queue.QueueCaption("", 1000);
            queue.QueueCaption("   ", 2000);
            
            // Assert
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void QueueCaption_SameTimecode_OverwritesPrevious()
        {
            // Arrange
            var queue = new CaptionQueue();
            uint timecode = 1000;
            
            // Act
            queue.QueueCaption("First caption", timecode);
            queue.QueueCaption("Second caption", timecode);
            
            // Assert
            Assert.Equal(1, queue.Count);
            var caption = queue.GetCaptionExact(timecode);
            Assert.Equal("Second caption", caption);
        }

        [Fact]
        public void TryGetCaptionForTimecode_ExactMatch_ReturnsCaption()
        {
            // Arrange
            var queue = new CaptionQueue();
            queue.QueueCaption("Test", 1000);
            
            // Act
            var result = queue.TryGetCaptionForTimecode(1000);
            
            // Assert
            Assert.Equal("Test", result);
        }

        [Fact]
        public void TryGetCaptionForTimecode_WithinTolerance_ReturnsCaption()
        {
            // Arrange
            var queue = new CaptionQueue(defaultToleranceMs: 500);
            queue.QueueCaption("Test", 1000);
            
            // Act - search for timecode 1200ms (within 500ms tolerance)
            var result = queue.TryGetCaptionForTimecode(1200);
            
            // Assert
            Assert.Equal("Test", result);
        }

        [Fact]
        public void TryGetCaptionForTimecode_OutsideTolerance_ReturnsNull()
        {
            // Arrange
            var queue = new CaptionQueue(defaultToleranceMs: 500);
            queue.QueueCaption("Test", 1000);
            
            // Act - search for timecode 2000ms (outside 500ms tolerance)
            var result = queue.TryGetCaptionForTimecode(2000);
            
            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void TryGetCaptionForTimecode_MultipleMatches_ReturnsClosest()
        {
            // Arrange
            var queue = new CaptionQueue(defaultToleranceMs: 1000);
            queue.QueueCaption("Caption 1", 1000);
            queue.QueueCaption("Caption 2", 1500);
            queue.QueueCaption("Caption 3", 2000);
            
            // Act - search for 1400ms (closest to 1500ms)
            var result = queue.TryGetCaptionForTimecode(1400);
            
            // Assert
            Assert.Equal("Caption 2", result);
        }

        [Fact]
        public void TryGetCaptionForTimecode_MarksAsConsumed()
        {
            // Arrange
            var queue = new CaptionQueue();
            queue.QueueCaption("Test", 1000);
            
            // Act
            var first = queue.TryGetCaptionForTimecode(1000);
            var second = queue.TryGetCaptionForTimecode(1000);
            
            // Assert
            Assert.Equal("Test", first);
            Assert.Null(second); // Should be consumed
        }

        [Fact]
        public void GetCaptionExact_NoMatch_ReturnsNull()
        {
            // Arrange
            var queue = new CaptionQueue();
            queue.QueueCaption("Test", 1000);
            
            // Act
            var result = queue.GetCaptionExact(2000);
            
            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void HasCaptionForTimecode_CaptionExists_ReturnsTrue()
        {
            // Arrange
            var queue = new CaptionQueue(defaultToleranceMs: 500);
            queue.QueueCaption("Test", 1000);
            
            // Act
            var result = queue.HasCaptionForTimecode(1200);
            
            // Assert
            Assert.True(result);
        }

        [Fact]
        public void HasCaptionForTimecode_NoCaption_ReturnsFalse()
        {
            // Arrange
            var queue = new CaptionQueue(defaultToleranceMs: 500);
            queue.QueueCaption("Test", 1000);
            
            // Act
            var result = queue.HasCaptionForTimecode(2000);
            
            // Assert
            Assert.False(result);
        }

        [Fact]
        public void GetQueuedCaptions_ReturnsAllUnconsumed()
        {
            // Arrange
            var queue = new CaptionQueue();
            queue.QueueCaption("Caption 1", 1000);
            queue.QueueCaption("Caption 2", 2000);
            queue.QueueCaption("Caption 3", 3000);
            
            // Consume one
            queue.GetCaptionExact(2000);
            
            // Act
            var result = queue.GetQueuedCaptions(includeConsumed: false);
            
            // Assert
            Assert.Equal(2, result.Count);
            Assert.True(result.ContainsKey(1000));
            Assert.True(result.ContainsKey(3000));
            Assert.False(result.ContainsKey(2000));
        }

        [Fact]
        public void GetQueuedCaptions_IncludeConsumed_ReturnsAll()
        {
            // Arrange
            var queue = new CaptionQueue();
            queue.QueueCaption("Caption 1", 1000);
            queue.QueueCaption("Caption 2", 2000);
            queue.GetCaptionExact(1000); // Consume one
            
            // Act
            var result = queue.GetQueuedCaptions(includeConsumed: true);
            
            // Assert
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void GetStats_EmptyQueue_ReturnsZeros()
        {
            // Arrange
            var queue = new CaptionQueue();
            
            // Act
            var stats = queue.GetStats();
            
            // Assert
            Assert.Equal(0, stats.TotalQueued);
            Assert.Equal(0, stats.UnconsumedCount);
            Assert.Equal(0, stats.ConsumedCount);
        }

        [Fact]
        public void GetStats_WithCaptions_ReturnsCorrectStats()
        {
            // Arrange
            var queue = new CaptionQueue();
            queue.QueueCaption("Caption 1", 1000);
            queue.QueueCaption("Caption 2", 2000);
            queue.QueueCaption("Caption 3", 3000);
            queue.GetCaptionExact(2000); // Consume one
            
            // Act
            var stats = queue.GetStats();
            
            // Assert
            Assert.Equal(3, stats.TotalQueued);
            Assert.Equal(2, stats.UnconsumedCount);
            Assert.Equal(1, stats.ConsumedCount);
            Assert.Equal(1000u, stats.OldestTimecode);
            Assert.Equal(3000u, stats.NewestTimecode);
        }

        [Fact]
        public void Clear_RemovesAllCaptions()
        {
            // Arrange
            var queue = new CaptionQueue();
            queue.QueueCaption("Caption 1", 1000);
            queue.QueueCaption("Caption 2", 2000);
            queue.QueueCaption("Caption 3", 3000);
            
            // Act
            queue.Clear();
            
            // Assert
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void Count_ReflectsQueueSize()
        {
            // Arrange
            var queue = new CaptionQueue();
            
            // Act & Assert
            Assert.Equal(0, queue.Count);
            
            queue.QueueCaption("Caption 1", 1000);
            Assert.Equal(1, queue.Count);
            
            queue.QueueCaption("Caption 2", 2000);
            Assert.Equal(2, queue.Count);
            
            queue.Clear();
            Assert.Equal(0, queue.Count);
        }

        [Fact]
        public void UnconsumedCount_TracksUnconsumedCaptions()
        {
            // Arrange
            var queue = new CaptionQueue();
            queue.QueueCaption("Caption 1", 1000);
            queue.QueueCaption("Caption 2", 2000);
            queue.QueueCaption("Caption 3", 3000);
            
            // Act & Assert
            Assert.Equal(3, queue.UnconsumedCount);
            
            queue.GetCaptionExact(1000);
            Assert.Equal(2, queue.UnconsumedCount);
            
            queue.GetCaptionExact(2000);
            Assert.Equal(1, queue.UnconsumedCount);
        }

        [Fact]
        public async Task ConcurrentAccess_ThreadSafe()
        {
            // Arrange
            var queue = new CaptionQueue();
            var tasks = new Task[10];
            
            // Act - multiple threads queueing captions
            for (int i = 0; i < 10; i++)
            {
                var timecode = (uint)(i * 1000);
                var text = $"Caption {i}";
                tasks[i] = Task.Run(() => queue.QueueCaption(text, timecode));
            }
            
            await Task.WhenAll(tasks);
            
            // Assert
            Assert.Equal(10, queue.Count);
        }

        [Fact]
        public async Task ConcurrentReadWrite_ThreadSafe()
        {
            // Arrange
            var queue = new CaptionQueue();
            for (int i = 0; i < 100; i++)
            {
                queue.QueueCaption($"Caption {i}", (uint)(i * 100));
            }
            
            // Act - concurrent reads and writes
            var writeTasks = Enumerable.Range(100, 50)
                .Select(i => Task.Run(() => queue.QueueCaption($"New Caption {i}", (uint)(i * 100))))
                .ToArray();
            
            var readTasks = Enumerable.Range(0, 50)
                .Select(i => Task.Run(() => queue.TryGetCaptionForTimecode((uint)(i * 100))))
                .ToArray();
            
            await Task.WhenAll(writeTasks.Concat(readTasks));
            
            // Assert - no exceptions thrown, queue is consistent
            Assert.True(queue.Count > 0);
        }

        [Fact]
        public void TryGetCaptionForTimecode_CustomTolerance_UsesProvidedValue()
        {
            // Arrange
            var queue = new CaptionQueue(defaultToleranceMs: 500);
            queue.QueueCaption("Test", 1000);
            
            // Act - use custom tolerance of 1500ms
            var result = queue.TryGetCaptionForTimecode(2000, toleranceMs: 1500);
            
            // Assert
            Assert.Equal("Test", result);
        }

        [Fact]
        public void TryGetCaptionForTimecode_CustomTolerance_OverridesDefault()
        {
            // Arrange
            var queue = new CaptionQueue(defaultToleranceMs: 2000); // High default
            queue.QueueCaption("Test", 1000);
            
            // Act - use custom low tolerance of 100ms
            var result = queue.TryGetCaptionForTimecode(1500, toleranceMs: 100);
            
            // Assert - should not find because custom tolerance is too small
            Assert.Null(result);
        }
    }
}
