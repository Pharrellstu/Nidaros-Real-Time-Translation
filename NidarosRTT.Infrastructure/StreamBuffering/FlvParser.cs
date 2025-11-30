using System;
using System.IO;
using System.Text;

namespace NidarosRTT.Infrastructure.StreamBuffering
{
    /// <summary>
    /// Parses FLV (Flash Video) format packets from a stream.
    /// FLV format specification: https://rtmp.veriskope.com/pdf/video_file_format_spec_v10.pdf
    /// 
    /// FLV Structure:
    /// - FLV Header (13 bytes)
    /// - FLV Tags (repeated):
    ///   - Previous Tag Size (4 bytes)
    ///   - Tag Header (11 bytes)
    ///   - Tag Data (variable)
    /// </summary>
    public class FlvParser
    {
        private const byte FLV_TAG_TYPE_AUDIO = 0x08;
        private const byte FLV_TAG_TYPE_VIDEO = 0x09;
        private const byte FLV_TAG_TYPE_SCRIPT = 0x12;

        /// <summary>
        /// Parse FLV header and validate format
        /// </summary>
        public static bool ParseHeader(Stream stream)
        {
            // FLV header is 9 bytes total
            var header = new byte[9];
            var bytesRead = stream.Read(header, 0, 9);
            
            if (bytesRead < 9)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FLV PARSER] Error: Incomplete FLV header");
                Console.ResetColor();
                return false;
            }

            // Check FLV signature "FLV"
            if (header[0] != 'F' || header[1] != 'L' || header[2] != 'V')
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[FLV PARSER] Error: Invalid FLV signature");
                Console.ResetColor();
                return false;
            }

            var version = header[3];
            var flags = header[4];
            // Data offset is in bytes 5-8 (big-endian)
            var dataOffset = (uint)((header[5] << 24) | (header[6] << 16) | (header[7] << 8) | header[8]);

            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"[FLV PARSER] Header parsed - Version: {version}, Flags: 0x{flags:X2}, Offset: {dataOffset}");
            Console.ResetColor();

            // Skip any extra header bytes (usually dataOffset is 9, so no skip needed)
            if (dataOffset > 9)
            {
                var skip = new byte[dataOffset - 9];
                var skipRead = stream.Read(skip, 0, (int)(dataOffset - 9));
                if (skipRead < dataOffset - 9)
                {
                    Console.WriteLine("[FLV PARSER] Warning: Could not skip to data offset");
                }
            }

            // Read first previous tag size (should be 0)
            var firstPrevTagSize = new byte[4];
            var prevRead = stream.Read(firstPrevTagSize, 0, 4);
            if (prevRead < 4)
            {
                Console.WriteLine("[FLV PARSER] Warning: Could not read previous tag size");
            }

            return true;
        }

        /// <summary>
        /// Parse a single FLV tag from the stream
        /// Returns null if end of stream or parse error
        /// </summary>
        public static FlvTag? ParseTag(Stream stream)
        {
            try
            {
                // Read tag header (11 bytes)
                var tagHeader = new byte[11];
                var bytesRead = stream.Read(tagHeader, 0, 11);

                if (bytesRead == 0)
                {
                    // End of stream
                    return null;
                }

                if (bytesRead < 11)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[FLV PARSER] Warning: Incomplete tag header ({bytesRead} bytes)");
                    Console.ResetColor();
                    return null;
                }

                // Parse tag header
                var tagType = tagHeader[0];
                
                // Data size (3 bytes, big-endian)
                var dataSize = (tagHeader[1] << 16) | (tagHeader[2] << 8) | tagHeader[3];
                
                // Timestamp (4 bytes, special format)
                // Bytes: [timestamp lower 3 bytes] [timestamp extended (upper byte)]
                var timestampLower = (tagHeader[4] << 16) | (tagHeader[5] << 8) | tagHeader[6];
                var timestampExtended = tagHeader[7];
                var timestamp = (timestampExtended << 24) | timestampLower;

                // Stream ID (3 bytes, always 0 for FLV files)
                var streamId = (tagHeader[8] << 16) | (tagHeader[9] << 8) | tagHeader[10];

                // Read tag data
                var data = new byte[dataSize];
                var dataRead = 0;
                while (dataRead < dataSize)
                {
                    var read = stream.Read(data, dataRead, dataSize - dataRead);
                    if (read == 0)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[FLV PARSER] Warning: Incomplete tag data ({dataRead}/{dataSize} bytes)");
                        Console.ResetColor();
                        return null;
                    }
                    dataRead += read;
                }

                // Read previous tag size (4 bytes) - this is the size of the tag we just read
                // Format: 11 (header) + dataSize (body) = total tag size
                var prevTagSize = new byte[4];
                var prevSizeRead = stream.Read(prevTagSize, 0, 4);
                if (prevSizeRead < 4)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"[FLV PARSER] Warning: Incomplete previous tag size ({prevSizeRead}/4 bytes)");
                    Console.ResetColor();
                    return null;
                }

                // Convert tag type to MediaPacketType
                MediaPacketType packetType;
                switch (tagType)
                {
                    case FLV_TAG_TYPE_AUDIO:
                        packetType = MediaPacketType.Audio;
                        break;
                    case FLV_TAG_TYPE_VIDEO:
                        packetType = MediaPacketType.Video;
                        break;
                    case FLV_TAG_TYPE_SCRIPT:
                        packetType = MediaPacketType.Data;
                        break;
                    default:
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine($"[FLV PARSER] Warning: Unknown tag type: 0x{tagType:X2}");
                        Console.ResetColor();
                        packetType = MediaPacketType.Data;
                        break;
                }

                return new FlvTag
                {
                    Type = packetType,
                    Timestamp = timestamp,
                    StreamId = streamId,
                    Data = data,
                    RawHeader = tagHeader
                };
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"[FLV PARSER] Error parsing tag: {ex.Message}");
                Console.ResetColor();
                return null;
            }
        }

        /// <summary>
        /// Reconstruct complete FLV tag for re-publishing (header + data + previous tag size)
        /// </summary>
        public static byte[] ReconstructTag(FlvTag tag)
        {
            var totalSize = 11 + tag.Data.Length + 4; // header + data + prevTagSize
            var buffer = new byte[totalSize];
            
            // Copy tag header
            Array.Copy(tag.RawHeader, 0, buffer, 0, 11);
            
            // Copy tag data
            Array.Copy(tag.Data, 0, buffer, 11, tag.Data.Length);
            
            // Write previous tag size (header + data)
            var prevTagSize = 11 + tag.Data.Length;
            buffer[totalSize - 4] = (byte)((prevTagSize >> 24) & 0xFF);
            buffer[totalSize - 3] = (byte)((prevTagSize >> 16) & 0xFF);
            buffer[totalSize - 2] = (byte)((prevTagSize >> 8) & 0xFF);
            buffer[totalSize - 1] = (byte)(prevTagSize & 0xFF);
            
            return buffer;
        }
    }

    /// <summary>
    /// Represents a parsed FLV tag
    /// </summary>
    public class FlvTag
    {
        public MediaPacketType Type { get; set; }
        public int Timestamp { get; set; }
        public int StreamId { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public byte[] RawHeader { get; set; } = Array.Empty<byte>();

        public override string ToString()
        {
            return $"[FLV Tag] Type: {Type}, Timestamp: {Timestamp}ms, Size: {Data.Length} bytes";
        }
    }
}
