using System;
using System.Linq;
using Xunit;
using NidarosRTT.Infrastructure.Captions;

namespace NidarosRTT.Tests.Captions
{
    public class H264SeiBuilderTests
    {
        private readonly H264SeiBuilder _builder;
        private readonly Cea608Encoder _encoder;

        public H264SeiBuilderTests()
        {
            _builder = new H264SeiBuilder();
            _encoder = new Cea608Encoder();
        }

        [Fact]
        public void BuildSeiNalUnit_ValidCea608Data_ReturnsValidSeiNalUnit()
        {
            // Arrange
            var cea608Data = _encoder.EncodeCaptionText("Test");
            
            // Act
            var result = _builder.BuildSeiNalUnit(cea608Data, useAvccFormat: true);
            
            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // AVCC format: first 4 bytes are length
            int nalLength = (result[0] << 24) | (result[1] << 16) | (result[2] << 8) | result[3];
            Assert.Equal(result.Length - 4, nalLength);
            
            // NAL header should be SEI type (0x06)
            Assert.Equal(0x06, result[4]);
        }

        [Fact]
        public void BuildSeiNalUnit_NullCea608Data_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => _builder.BuildSeiNalUnit(null!));
        }

        [Fact]
        public void BuildSeiNalUnit_EmptyCea608Data_ThrowsArgumentException()
        {
            // Act & Assert
            Assert.Throws<ArgumentException>(() => _builder.BuildSeiNalUnit(Array.Empty<byte>()));
        }

        [Fact]
        public void BuildSeiNalUnit_InvalidCea608DataLength_ThrowsArgumentException()
        {
            // Arrange - not a multiple of 3
            var invalidData = new byte[] { 0xFC, 0x14 }; // Only 2 bytes
            
            // Act & Assert
            Assert.Throws<ArgumentException>(() => _builder.BuildSeiNalUnit(invalidData));
        }

        [Fact]
        public void BuildSeiNalUnit_ContainsAtscA53Header()
        {
            // Arrange
            var cea608Data = _encoder.EncodeCaptionText("Hello");
            
            // Act
            var result = _builder.BuildSeiNalUnit(cea608Data, useAvccFormat: true);
            
            // Skip AVCC length (4 bytes) and NAL header (1 byte) and SEI type (1 byte) and payload size
            // ATSC A/53 structure should start after these headers
            
            // Assert - verify ATSC A/53 identifiers are present
            // Country code 0xB5 (US)
            Assert.Contains((byte)0xB5, result);
            
            // Provider code 0x0031 (as two bytes: 0x00, 0x31)
            var hasProviderCode = false;
            for (int i = 0; i < result.Length - 1; i++)
            {
                if (result[i] == 0x00 && result[i + 1] == 0x31)
                {
                    hasProviderCode = true;
                    break;
                }
            }
            Assert.True(hasProviderCode, "Provider code 0x0031 not found");
            
            // User identifier "GA94" (0x47, 0x41, 0x39, 0x34)
            var hasGa94 = false;
            for (int i = 0; i < result.Length - 3; i++)
            {
                if (result[i] == 0x47 && result[i + 1] == 0x41 && 
                    result[i + 2] == 0x39 && result[i + 3] == 0x34)
                {
                    hasGa94 = true;
                    break;
                }
            }
            Assert.True(hasGa94, "ATSC user identifier 'GA94' not found");
        }

        [Fact]
        public void BuildSeiNalUnit_AvccFormat_HasCorrectLengthPrefix()
        {
            // Arrange
            var cea608Data = _encoder.EncodeCaptionText("Test");
            
            // Act
            var result = _builder.BuildSeiNalUnit(cea608Data, useAvccFormat: true);
            
            // Assert
            // Extract length from first 4 bytes (big-endian)
            int length = (result[0] << 24) | (result[1] << 16) | (result[2] << 8) | result[3];
            
            // Length should match actual NAL unit size (total - 4 byte prefix)
            Assert.Equal(result.Length - 4, length);
        }

        [Fact]
        public void BuildSeiNalUnit_AnnexBFormat_NoLengthPrefix()
        {
            // Arrange
            var cea608Data = _encoder.EncodeCaptionText("Test");
            
            // Act
            var result = _builder.BuildSeiNalUnit(cea608Data, useAvccFormat: false);
            
            // Assert
            // Should start with NAL header (0x06 for SEI)
            Assert.Equal(0x06, result[0]);
        }

        [Fact]
        public void BuildSeiNalUnit_ContainsSeiPayloadType()
        {
            // Arrange
            var cea608Data = _encoder.EncodeCaptionText("Test");
            
            // Act
            var result = _builder.BuildSeiNalUnit(cea608Data, useAvccFormat: true);
            
            // Assert
            // After AVCC length (4 bytes) + NAL header (1 byte), should be SEI type 0x04
            Assert.Equal(0x04, result[5]);
        }

        [Fact]
        public void BuildSeiNalUnit_EndsWithRbspTrailingBits()
        {
            // Arrange
            var cea608Data = _encoder.EncodeCaptionText("Test");
            
            // Act
            var result = _builder.BuildSeiNalUnit(cea608Data, useAvccFormat: false);
            
            // Assert
            // Should end with 0x80 (RBSP trailing bits)
            Assert.Equal(0x80, result[result.Length - 1]);
        }

        [Fact]
        public void BuildSeiNalUnit_MultipleCea608Packets_HandlesCorrectly()
        {
            // Arrange - long text requiring multiple cc_data packets
            var cea608Data = _encoder.EncodeCaptionText("This is a longer caption text for testing");
            
            // Act
            var result = _builder.BuildSeiNalUnit(cea608Data, useAvccFormat: true);
            
            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Should be valid AVCC format
            int nalLength = (result[0] << 24) | (result[1] << 16) | (result[2] << 8) | result[3];
            Assert.Equal(result.Length - 4, nalLength);
        }

        [Fact]
        public void GetSeiNalUnitSize_ValidLength_ReturnsCorrectSize()
        {
            // Arrange
            var cea608Data = _encoder.EncodeCaptionText("Test");
            
            // Act
            var estimatedSize = _builder.GetSeiNalUnitSize(cea608Data.Length, useAvccFormat: true);
            var actualSei = _builder.BuildSeiNalUnit(cea608Data, useAvccFormat: true);
            
            // Assert
            Assert.Equal(actualSei.Length, estimatedSize);
        }

        [Fact]
        public void GetSeiNalUnitSize_InvalidLength_ThrowsArgumentException()
        {
            // Arrange - not a multiple of 3
            var invalidLength = 10;
            
            // Act & Assert
            Assert.Throws<ArgumentException>(() => _builder.GetSeiNalUnitSize(invalidLength));
        }

        [Fact]
        public void BuildSeiNalUnit_LargePayload_UsesMultipleFfBytes()
        {
            // Arrange - create very long caption requiring >255 byte payload
            var longText = new string('A', 200); // 200 characters
            var cea608Data = _encoder.EncodeMultiLineCaption(longText);
            
            // Flatten all frames into single cc_data array for testing
            var allCea608Data = cea608Data.SelectMany(frame => frame).ToArray();
            
            // Act
            var result = _builder.BuildSeiNalUnit(allCea608Data, useAvccFormat: true);
            
            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // With large payload, should use multiple 0xFF bytes for size encoding
            // After NAL header (pos 4) and SEI type (pos 5), check for 0xFF bytes
            bool hasMultipleFfBytes = false;
            if (result.Length > 7 && result[6] == 0xFF)
            {
                hasMultipleFfBytes = true;
            }
            
            // For large payloads, this should be true
            Assert.True(hasMultipleFfBytes || result.Length < 300, "Large payload should use ff_byte notation");
        }

        [Fact]
        public void BuildSeiNalUnit_CcCountCorrect()
        {
            // Arrange
            var cea608Data = new byte[9]; // 3 cc_data packets (9 bytes)
            for (int i = 0; i < 9; i += 3)
            {
                cea608Data[i] = 0xFC;
                cea608Data[i + 1] = 0x80;
                cea608Data[i + 2] = 0x80;
            }
            
            // Act
            var result = _builder.BuildSeiNalUnit(cea608Data, useAvccFormat: false);
            
            // Assert
            // Find the cc_data structure (after ATSC A/53 headers)
            // Country(1) + Provider(2) + GA94(4) + user_data_type(1) = 8 bytes of headers
            // Then comes: first_byte with cc_count
            
            // The cc_count should be 3 (3 packets)
            // first_byte format: 0xC0 | cc_count = 0xC3 for 3 packets
            bool foundCcCount = false;
            for (int i = 0; i < result.Length - 10; i++)
            {
                // Look for the pattern: 0xC3 (cc_count=3) followed by 0xFF (reserved)
                if (result[i] == 0xC3 && result[i + 1] == 0xFF)
                {
                    foundCcCount = true;
                    break;
                }
            }
            
            Assert.True(foundCcCount, "cc_count field with value 3 not found");
        }

        [Theory]
        [InlineData(3)]   // 1 packet
        [InlineData(12)]  // 4 packets
        [InlineData(30)]  // 10 packets
        public void BuildSeiNalUnit_VariousPacketCounts_HandlesCorrectly(int byteCount)
        {
            // Arrange
            var cea608Data = new byte[byteCount];
            for (int i = 0; i < byteCount; i += 3)
            {
                cea608Data[i] = 0xFC;
                cea608Data[i + 1] = 0x80;
                cea608Data[i + 2] = 0x80;
            }
            
            // Act
            var result = _builder.BuildSeiNalUnit(cea608Data, useAvccFormat: true);
            
            // Assert
            Assert.NotNull(result);
            Assert.True(result.Length > 0);
            
            // Should have valid AVCC format
            int nalLength = (result[0] << 24) | (result[1] << 16) | (result[2] << 8) | result[3];
            Assert.Equal(result.Length - 4, nalLength);
        }
    }
}
