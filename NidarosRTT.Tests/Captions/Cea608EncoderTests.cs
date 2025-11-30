using System;
using System.Linq;
using Xunit;
using NidarosRTT.Infrastructure.Captions;

namespace NidarosRTT.Tests.Captions
{
    public class Cea608EncoderTests
    {
        private readonly Cea608Encoder _encoder;

        public Cea608EncoderTests()
        {
            _encoder = new Cea608Encoder();
        }

        [Fact]
        public void EncodeCaptionText_SimpleText_ReturnsValidCea608Packets()
        {
            // Arrange
            var text = "Hello";
            
            // Act
            var result = _encoder.EncodeCaptionText(text);
            
            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Should contain: EDM (3 bytes) + PAC (3 bytes) + RCL (3 bytes) + Text packets + EOC (3 bytes)
            // "Hello" = 5 chars = 3 packets (3 bytes each) = 9 bytes
            // Total: 12 + 9 = 21 bytes minimum
            Assert.True(result.Length >= 21);
            
            // Verify all packets start with 0xFC (cc_valid=1, cc_type=0)
            for (int i = 0; i < result.Length; i += 3)
            {
                Assert.Equal(0xFC, result[i]);
            }
        }

        [Fact]
        public void EncodeCaptionText_EmptyString_ReturnsControlCodesOnly()
        {
            // Arrange
            var text = "";
            
            // Act
            var result = _encoder.EncodeCaptionText(text);
            
            // Assert
            Assert.NotNull(result);
            // Should only have control codes: EDM + PAC + RCL + EOC = 12 bytes
            Assert.Equal(12, result.Length);
        }

        [Fact]
        public void EncodeCaptionText_LongText_TruncatesToMaxLength()
        {
            // Arrange
            var text = "This is a very long text that exceeds the maximum character limit for a single line";
            
            // Act
            var result = _encoder.EncodeCaptionText(text);
            
            // Assert
            Assert.NotNull(result);
            // Max 32 characters = 16 packets * 3 bytes = 48 bytes for text
            // Plus control codes: 12 bytes
            // Total: <= 60 bytes
            Assert.True(result.Length <= 60);
        }

        [Fact]
        public void EncodeCaptionText_SpecialCharacters_SanitizesCorrectly()
        {
            // Arrange
            var text = "Test™ €100 🎉"; // Contains non-ASCII
            
            // Act
            var result = _encoder.EncodeCaptionText(text);
            
            // Assert
            Assert.NotNull(result);
            // Should only contain ASCII characters "Test 100 "
            // Non-ASCII characters should be removed
            Assert.True(result.Length > 12); // Has some text
        }

        [Fact]
        public void EncodeMultiLineCaption_LongText_SplitsIntoMultipleFrames()
        {
            // Arrange
            var text = "This is a very long caption that needs to be wrapped across multiple lines for proper display on screen";
            
            // Act
            var result = _encoder.EncodeMultiLineCaption(text);
            
            // Assert
            Assert.NotNull(result);
            Assert.True(result.Count > 1); // Should split into multiple frames
            Assert.True(result.Count <= 4); // Max 4 lines
            
            // Each frame should be valid CEA-608 data
            foreach (var frame in result)
            {
                Assert.NotNull(frame);
                Assert.True(frame.Length >= 12); // At least control codes
            }
        }

        [Fact]
        public void EncodeMultiLineCaption_ShortText_ReturnsSingleFrame()
        {
            // Arrange
            var text = "Short text";
            
            // Act
            var result = _encoder.EncodeMultiLineCaption(text);
            
            // Assert
            Assert.NotNull(result);
            Assert.Single(result); // Only one frame needed
        }

        [Fact]
        public void GetEncodedSize_ValidText_ReturnsCorrectSize()
        {
            // Arrange
            var text = "Hello World";
            
            // Act
            var estimatedSize = _encoder.GetEncodedSize(text);
            var actualBytes = _encoder.EncodeCaptionText(text);
            
            // Assert
            Assert.Equal(actualBytes.Length, estimatedSize);
        }

        [Fact]
        public void EncodeCaptionText_WithLineNumber_UsesCorrectPAC()
        {
            // Arrange
            var text = "Test";
            
            // Act
            var line1 = _encoder.EncodeCaptionText(text, lineNumber: 1);
            var line2 = _encoder.EncodeCaptionText(text, lineNumber: 2);
            var line3 = _encoder.EncodeCaptionText(text, lineNumber: 3);
            var line4 = _encoder.EncodeCaptionText(text, lineNumber: 4);
            
            // Assert
            // PAC codes should be different for different lines
            // PAC is the second control code (bytes 3-5)
            // Byte 3 is 0xFC (packet marker), byte 4 is PAC code1, byte 5 is PAC code2
            
            // Verify structure: lines 1-2 use 0x11 base, lines 3-4 use 0x12 base
            Assert.Equal((byte)0x11, line1[4]);
            Assert.Equal((byte)0x11, line2[4]);
            Assert.Equal((byte)0x12, line3[4]);
            Assert.Equal((byte)0x12, line4[4]);
            
            // Verify PAC parameters differ between lines with same base code
            Assert.NotEqual(line1[5], line2[5]); // Line 1: 0x40, Line 2: 0x60
            Assert.NotEqual(line3[5], line4[5]); // Line 3: 0x40, Line 4: 0x60
            
            // Verify specific PAC codes
            Assert.Equal((byte)0x40, line1[5]); // Row 11
            Assert.Equal((byte)0x60, line2[5]); // Row 10
            Assert.Equal((byte)0x40, line3[5]); // Row 9
            Assert.Equal((byte)0x60, line4[5]); // Row 8
        }

        [Fact]
        public void EncodeCaptionText_InvalidLineNumber_DefaultsToLine1()
        {
            // Arrange
            var text = "Test";
            
            // Act
            var defaultLine = _encoder.EncodeCaptionText(text, lineNumber: 1);
            var invalidLine = _encoder.EncodeCaptionText(text, lineNumber: 99);
            
            // Assert
            // Should use same PAC code as line 1
            Assert.Equal(defaultLine[3], invalidLine[3]);
            Assert.Equal(defaultLine[4], invalidLine[4]);
        }

        [Fact]
        public void EncodeCaptionText_OddNumberOfCharacters_PadsCorrectly()
        {
            // Arrange
            var text = "Odd"; // 3 characters
            
            // Act
            var result = _encoder.EncodeCaptionText(text);
            
            // Assert
            Assert.NotNull(result);
            // Should handle odd number of characters without errors
            // "Odd" should be encoded as "Od" + "d\0" (padded)
            Assert.True(result.Length >= 18); // Control codes + 2 text packets
        }

        [Theory]
        [InlineData("Test 123", true)]
        [InlineData("Hello, World!", true)]
        [InlineData("ABC XYZ 789", true)]
        [InlineData("", true)]
        public void EncodeCaptionText_VariousInputs_AlwaysReturnsValidPackets(string text, bool shouldSucceed)
        {
            // Act
            var result = _encoder.EncodeCaptionText(text);
            
            // Assert
            if (shouldSucceed)
            {
                Assert.NotNull(result);
                Assert.True(result.Length % 3 == 0); // Always multiple of 3 (cc_data packets)
                
                // Verify packet structure
                for (int i = 0; i < result.Length; i += 3)
                {
                    Assert.Equal(0xFC, result[i]); // Valid cc_valid flag
                }
            }
        }

        [Fact]
        public void EncodeCaptionText_ASCIIBoundaryCharacters_EncodesCorrectly()
        {
            // Arrange
            var text = " !\"#$%&'()*+,-./"; // ASCII 0x20-0x2F
            
            // Act
            var result = _encoder.EncodeCaptionText(text);
            
            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 12); // Has text data
            
            // Should encode all visible ASCII characters
            // Each pair of chars = 1 packet (3 bytes)
            var expectedTextPackets = (text.Length + 1) / 2;
            var expectedTextBytes = expectedTextPackets * 3;
            Assert.Equal(12 + expectedTextBytes, result.Length);
        }
    }
}
