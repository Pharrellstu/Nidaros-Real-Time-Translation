using System;
using System.Linq;
using Xunit;
using NidarosRTT.Infrastructure.Captions;

namespace NidarosRTT.Tests.Captions
{
    public class FlvVideoTagModifierTests
    {
        private readonly FlvVideoTagModifier _modifier;
        private readonly Cea608Encoder _encoder;

        public FlvVideoTagModifierTests()
        {
            _modifier = new FlvVideoTagModifier();
            _encoder = new Cea608Encoder();
        }

        [Fact]
        public void InjectCaption_KeyframeWithNalu_InjectsSeI()
        {
            // Arrange
            var videoTag = CreateMockAvcNaluKeyframe();
            var cea608Data = _encoder.EncodeCaptionText("Test");
            
            // Act
            var result = _modifier.InjectCaption(videoTag, cea608Data, injectOnKeyframesOnly: true);
            
            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > videoTag.Length, "Modified tag should be larger");
            
            // Header should remain the same (first 5 bytes)
            Assert.Equal(videoTag[0], result[0]);
            Assert.Equal(videoTag[1], result[1]);
            Assert.Equal(videoTag[2], result[2]);
            Assert.Equal(videoTag[3], result[3]);
            Assert.Equal(videoTag[4], result[4]);
        }

        [Fact]
        public void InjectCaption_InterFrame_SkipsInjectionWhenKeyframeOnly()
        {
            // Arrange
            var videoTag = CreateMockAvcNaluInterFrame();
            var cea608Data = _encoder.EncodeCaptionText("Test");
            
            // Act
            var result = _modifier.InjectCaption(videoTag, cea608Data, injectOnKeyframesOnly: true);
            
            // Assert - should return original unchanged
            Assert.Equal(videoTag.Length, result.Length);
            Assert.Equal(videoTag, result);
        }

        [Fact]
        public void InjectCaption_InterFrame_InjectsWhenKeyframeOnlyIsFalse()
        {
            // Arrange
            var videoTag = CreateMockAvcNaluInterFrame();
            var cea608Data = _encoder.EncodeCaptionText("Test");
            
            // Act
            var result = _modifier.InjectCaption(videoTag, cea608Data, injectOnKeyframesOnly: false);
            
            // Assert
            Assert.True(result.Length > videoTag.Length, "Modified tag should be larger");
        }

        [Fact]
        public void InjectCaption_NullVideoTag_ReturnsOriginal()
        {
            // Arrange
            var cea608Data = _encoder.EncodeCaptionText("Test");
            
            // Act
            var result = _modifier.InjectCaption(null!, cea608Data);
            
            // Assert
            Assert.Null(result);
        }

        [Fact]
        public void InjectCaption_NullCea608Data_ReturnsOriginal()
        {
            // Arrange
            var videoTag = CreateMockAvcNaluKeyframe();
            
            // Act
            var result = _modifier.InjectCaption(videoTag, null!);
            
            // Assert
            Assert.Equal(videoTag.Length, result.Length);
            Assert.Equal(videoTag, result);
        }

        [Fact]
        public void InjectCaption_SequenceHeader_SkipsInjection()
        {
            // Arrange
            var videoTag = CreateMockAvcSequenceHeader();
            var cea608Data = _encoder.EncodeCaptionText("Test");
            
            // Act
            var result = _modifier.InjectCaption(videoTag, cea608Data);
            
            // Assert - should return original unchanged
            Assert.Equal(videoTag.Length, result.Length);
            Assert.Equal(videoTag, result);
        }

        [Fact]
        public void IsKeyframe_KeyframeTag_ReturnsTrue()
        {
            // Arrange
            var videoTag = CreateMockAvcNaluKeyframe();
            
            // Act
            var result = _modifier.IsKeyframe(videoTag);
            
            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsKeyframe_InterFrameTag_ReturnsFalse()
        {
            // Arrange
            var videoTag = CreateMockAvcNaluInterFrame();
            
            // Act
            var result = _modifier.IsKeyframe(videoTag);
            
            // Assert
            Assert.False(result);
        }

        [Fact]
        public void IsAvcNaluPacket_ValidAvcNalu_ReturnsTrue()
        {
            // Arrange
            var videoTag = CreateMockAvcNaluKeyframe();
            
            // Act
            var result = _modifier.IsAvcNaluPacket(videoTag);
            
            // Assert
            Assert.True(result);
        }

        [Fact]
        public void IsAvcNaluPacket_SequenceHeader_ReturnsFalse()
        {
            // Arrange
            var videoTag = CreateMockAvcSequenceHeader();
            
            // Act
            var result = _modifier.IsAvcNaluPacket(videoTag);
            
            // Assert
            Assert.False(result);
        }

        [Fact]
        public void GetFrameType_Keyframe_ReturnsKeyframe()
        {
            // Arrange
            var videoTag = CreateMockAvcNaluKeyframe();
            
            // Act
            var result = _modifier.GetFrameType(videoTag);
            
            // Assert
            Assert.Equal(VideoFrameType.Keyframe, result);
        }

        [Fact]
        public void GetFrameType_InterFrame_ReturnsInterFrame()
        {
            // Arrange
            var videoTag = CreateMockAvcNaluInterFrame();
            
            // Act
            var result = _modifier.GetFrameType(videoTag);
            
            // Assert
            Assert.Equal(VideoFrameType.InterFrame, result);
        }

        [Fact]
        public void GetCompositionTime_ZeroCompositionTime_ReturnsZero()
        {
            // Arrange
            var videoTag = CreateMockAvcNaluKeyframe();
            
            // Act
            var result = _modifier.GetCompositionTime(videoTag);
            
            // Assert
            Assert.Equal(0, result);
        }

        [Fact]
        public void GetCompositionTime_PositiveCompositionTime_ReturnsCorrectValue()
        {
            // Arrange
            var videoTag = CreateMockAvcNaluKeyframe();
            // Set composition time to 1000 (0x0003E8)
            videoTag[2] = 0x00;
            videoTag[3] = 0x03;
            videoTag[4] = 0xE8;
            
            // Act
            var result = _modifier.GetCompositionTime(videoTag);
            
            // Assert
            Assert.Equal(1000, result);
        }

        [Fact]
        public void ParseNalUnits_SingleNalu_ParsesCorrectly()
        {
            // Arrange
            var videoTag = CreateMockAvcNaluKeyframe();
            
            // Act
            var result = _modifier.ParseNalUnits(videoTag);
            
            // Assert
            Assert.NotNull(result);
            Assert.Single(result);
            Assert.Equal(NalUnitType.CodedSliceIdr, result[0].Type);
        }

        [Fact]
        public void ParseNalUnits_MultipleNalus_ParsesAll()
        {
            // Arrange
            var videoTag = CreateMockAvcNaluWithMultipleNalus();
            
            // Act
            var result = _modifier.ParseNalUnits(videoTag);
            
            // Assert
            Assert.NotNull(result);
            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void ParseNalUnits_EmptyTag_ReturnsEmptyList()
        {
            // Arrange
            var videoTag = new byte[5]; // Only header, no NALUs
            
            // Act
            var result = _modifier.ParseNalUnits(videoTag);
            
            // Assert
            Assert.Empty(result);
        }

        [Fact]
        public void InjectCaption_PreservesOriginalNalUnits()
        {
            // Arrange
            var videoTag = CreateMockAvcNaluKeyframe();
            var originalNalUnits = _modifier.ParseNalUnits(videoTag);
            var cea608Data = _encoder.EncodeCaptionText("Test");
            
            // Act
            var result = _modifier.InjectCaption(videoTag, cea608Data);
            var modifiedNalUnits = _modifier.ParseNalUnits(result);
            
            // Assert
            // Should have one more NAL unit (the SEI)
            Assert.Equal(originalNalUnits.Count + 1, modifiedNalUnits.Count);
            
            // First NAL should be SEI
            Assert.Equal(NalUnitType.SEI, modifiedNalUnits[0].Type);
            
            // Remaining NALs should match original
            for (int i = 0; i < originalNalUnits.Count; i++)
            {
                Assert.Equal(originalNalUnits[i].Type, modifiedNalUnits[i + 1].Type);
            }
        }

        // Helper methods to create mock FLV video tags

        private byte[] CreateMockAvcNaluKeyframe()
        {
            var tag = new List<byte>
            {
                // Byte 0: FrameType (0x1 = keyframe) | CodecID (0x7 = AVC)
                0x17,
                
                // Byte 1: AVCPacketType (0x01 = NALU)
                0x01,
                
                // Bytes 2-4: CompositionTime (0x000000)
                0x00, 0x00, 0x00,
                
                // Mock AVCC NAL unit: 4-byte length + NAL data
                // Length: 10 bytes
                0x00, 0x00, 0x00, 0x0A,
                
                // NAL unit header: type 5 (IDR slice)
                0x65, // 0b01100101: ref_idc=3, type=5
                
                // Mock NAL data (9 bytes)
                0x88, 0x84, 0x00, 0x00, 0x03, 0x00, 0x00, 0x03, 0x00
            };
            
            return tag.ToArray();
        }

        private byte[] CreateMockAvcNaluInterFrame()
        {
            var tag = new List<byte>
            {
                // Byte 0: FrameType (0x2 = inter frame) | CodecID (0x7 = AVC)
                0x27,
                
                // Byte 1: AVCPacketType (0x01 = NALU)
                0x01,
                
                // Bytes 2-4: CompositionTime
                0x00, 0x00, 0x00,
                
                // Mock AVCC NAL unit
                0x00, 0x00, 0x00, 0x0A,
                
                // NAL unit header: type 1 (non-IDR slice)
                0x41, // 0b01000001: ref_idc=2, type=1
                
                // Mock NAL data
                0x88, 0x84, 0x00, 0x00, 0x03, 0x00, 0x00, 0x03, 0x00
            };
            
            return tag.ToArray();
        }

        private byte[] CreateMockAvcSequenceHeader()
        {
            var tag = new List<byte>
            {
                // Byte 0: FrameType (0x1 = keyframe) | CodecID (0x7 = AVC)
                0x17,
                
                // Byte 1: AVCPacketType (0x00 = sequence header)
                0x00,
                
                // Bytes 2-4: CompositionTime
                0x00, 0x00, 0x00,
                
                // Mock AVCDecoderConfigurationRecord
                0x01, 0x64, 0x00, 0x1F, 0xFF, 0xE1, 0x00, 0x19
            };
            
            return tag.ToArray();
        }

        private byte[] CreateMockAvcNaluWithMultipleNalus()
        {
            var tag = new List<byte>
            {
                // Header
                0x17, 0x01, 0x00, 0x00, 0x00,
                
                // First NAL unit (IDR slice)
                0x00, 0x00, 0x00, 0x05,
                0x65, 0x88, 0x84, 0x00, 0x03,
                
                // Second NAL unit (non-IDR slice)
                0x00, 0x00, 0x00, 0x05,
                0x41, 0x88, 0x84, 0x00, 0x03
            };
            
            return tag.ToArray();
        }
    }
}
