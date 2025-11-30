using System;
using System.Collections.Generic;
using System.Linq;

namespace NidarosRTT.Infrastructure.Captions
{
    /// <summary>
    /// Modifies FLV video tags to inject CEA-608 captions as H.264 SEI NAL units
    /// </summary>
    public class FlvVideoTagModifier
    {
        private readonly H264SeiBuilder _seiBuilder;
        
        // FLV video tag frame types
        private const byte FRAME_TYPE_KEYFRAME = 0x10;
        private const byte FRAME_TYPE_INTER = 0x20;
        
        // FLV video tag codec IDs
        private const byte CODEC_ID_AVC = 0x07;
        
        // AVC packet types
        private const byte AVC_PACKET_TYPE_SEQUENCE_HEADER = 0x00;
        private const byte AVC_PACKET_TYPE_NALU = 0x01;
        private const byte AVC_PACKET_TYPE_END_OF_SEQUENCE = 0x02;
        
        public FlvVideoTagModifier()
        {
            _seiBuilder = new H264SeiBuilder();
        }
        
        /// <summary>
        /// Inject CEA-608 caption data into an FLV video tag
        /// </summary>
        /// <param name="videoTagData">Original FLV video tag data</param>
        /// <param name="cea608Data">CEA-608 cc_data packets to inject</param>
        /// <param name="injectOnKeyframesOnly">Only inject on keyframes (recommended)</param>
        /// <returns>Modified video tag data with injected SEI, or original if injection not applicable</returns>
        public byte[]? InjectCaption(byte[]? videoTagData, byte[]? cea608Data, bool injectOnKeyframesOnly = true)
        {
            if (videoTagData == null || videoTagData.Length < 5)
                return videoTagData;
            
            if (cea608Data == null || cea608Data.Length == 0)
                return videoTagData;
            
            // Parse FLV video tag header (first 5 bytes)
            // Byte 0: [FrameType (4 bits)][CodecID (4 bits)]
            // Byte 1: AVCPacketType
            // Bytes 2-4: CompositionTime (3 bytes, big-endian, signed)
            
            byte frameTypeAndCodec = videoTagData[0];
            byte frameType = (byte)(frameTypeAndCodec & 0xF0);
            byte codecId = (byte)(frameTypeAndCodec & 0x0F);
            byte avcPacketType = videoTagData[1];
            
            // Only process AVC (H.264) video with NALU packets
            if (codecId != CODEC_ID_AVC || avcPacketType != AVC_PACKET_TYPE_NALU)
                return videoTagData;
            
            // If keyframes only, check frame type
            if (injectOnKeyframesOnly && frameType != FRAME_TYPE_KEYFRAME)
                return videoTagData;
            
            // Build SEI NAL unit with CEA-608 data
            var seiNalUnit = _seiBuilder.BuildSeiNalUnit(cea608Data, useAvccFormat: true);
            
            // Inject SEI before existing NALUs
            return InjectSeiIntoAvccData(videoTagData, seiNalUnit);
        }
        
        /// <summary>
        /// Inject SEI NAL unit into AVCC-formatted video data
        /// </summary>
        private byte[] InjectSeiIntoAvccData(byte[] videoTagData, byte[] seiNalUnit)
        {
            // FLV video tag structure:
            // [0]: FrameType+CodecID
            // [1]: AVCPacketType
            // [2-4]: CompositionTime (3 bytes)
            // [5+]: AVCC NAL units (length-prefixed)
            
            var modifiedTag = new List<byte>();
            
            // Copy header (5 bytes)
            modifiedTag.AddRange(videoTagData.Take(5));
            
            // Insert SEI NAL unit first
            modifiedTag.AddRange(seiNalUnit);
            
            // Copy remaining AVCC NAL units
            modifiedTag.AddRange(videoTagData.Skip(5));
            
            return modifiedTag.ToArray();
        }
        
        /// <summary>
        /// Check if a video tag is a keyframe
        /// </summary>
        public bool IsKeyframe(byte[] videoTagData)
        {
            if (videoTagData == null || videoTagData.Length < 1)
                return false;
            
            byte frameType = (byte)(videoTagData[0] & 0xF0);
            return frameType == FRAME_TYPE_KEYFRAME;
        }
        
        /// <summary>
        /// Check if a video tag contains AVC/H.264 NALU data
        /// </summary>
        public bool IsAvcNaluPacket(byte[] videoTagData)
        {
            if (videoTagData == null || videoTagData.Length < 2)
                return false;
            
            byte codecId = (byte)(videoTagData[0] & 0x0F);
            byte avcPacketType = videoTagData[1];
            
            return codecId == CODEC_ID_AVC && avcPacketType == AVC_PACKET_TYPE_NALU;
        }
        
        /// <summary>
        /// Get the frame type of a video tag
        /// </summary>
        public VideoFrameType GetFrameType(byte[] videoTagData)
        {
            if (videoTagData == null || videoTagData.Length < 1)
                return VideoFrameType.Unknown;
            
            byte frameType = (byte)(videoTagData[0] & 0xF0);
            
            return frameType switch
            {
                FRAME_TYPE_KEYFRAME => VideoFrameType.Keyframe,
                FRAME_TYPE_INTER => VideoFrameType.InterFrame,
                _ => VideoFrameType.Unknown
            };
        }
        
        /// <summary>
        /// Extract composition time from video tag (DTS-PTS offset)
        /// </summary>
        public int GetCompositionTime(byte[] videoTagData)
        {
            if (videoTagData == null || videoTagData.Length < 5)
                return 0;
            
            // Composition time is a 24-bit signed integer (big-endian) at bytes 2-4
            int compositionTime = (videoTagData[2] << 16) | (videoTagData[3] << 8) | videoTagData[4];
            
            // Handle sign extension for negative values
            if ((compositionTime & 0x800000) != 0)
            {
                compositionTime |= unchecked((int)0xFF000000);
            }
            
            return compositionTime;
        }
        
        /// <summary>
        /// Parse all AVCC NAL units from video tag data
        /// </summary>
        public List<AvccNalUnit> ParseNalUnits(byte[] videoTagData)
        {
            var nalUnits = new List<AvccNalUnit>();
            
            if (videoTagData == null || videoTagData.Length < 6)
                return nalUnits;
            
            // Skip FLV header (5 bytes), start parsing NAL units
            int offset = 5;
            
            while (offset + 4 < videoTagData.Length)
            {
                // Read 4-byte length (big-endian)
                int length = (videoTagData[offset] << 24) | 
                            (videoTagData[offset + 1] << 16) | 
                            (videoTagData[offset + 2] << 8) | 
                            videoTagData[offset + 3];
                
                offset += 4;
                
                // Validate length
                if (length <= 0 || offset + length > videoTagData.Length)
                    break;
                
                // Extract NAL unit data
                var nalData = new byte[length];
                Array.Copy(videoTagData, offset, nalData, 0, length);
                
                // Parse NAL unit header
                byte nalHeader = nalData[0];
                byte nalUnitType = (byte)(nalHeader & 0x1F);
                byte nalRefIdc = (byte)((nalHeader >> 5) & 0x03);
                
                nalUnits.Add(new AvccNalUnit
                {
                    Type = (NalUnitType)nalUnitType,
                    RefIdc = nalRefIdc,
                    Data = nalData,
                    Length = length
                });
                
                offset += length;
            }
            
            return nalUnits;
        }
    }
    
    /// <summary>
    /// Video frame types
    /// </summary>
    public enum VideoFrameType
    {
        Unknown = 0,
        Keyframe = 1,
        InterFrame = 2,
        DisposableInterFrame = 3,
        GeneratedKeyframe = 4,
        VideoInfoFrame = 5
    }
    
    /// <summary>
    /// H.264 NAL unit types
    /// </summary>
    public enum NalUnitType
    {
        Unspecified = 0,
        CodedSliceNonIdr = 1,
        CodedSliceDataPartitionA = 2,
        CodedSliceDataPartitionB = 3,
        CodedSliceDataPartitionC = 4,
        CodedSliceIdr = 5,
        SEI = 6,
        SPS = 7,
        PPS = 8,
        AccessUnitDelimiter = 9,
        EndOfSequence = 10,
        EndOfStream = 11,
        FillerData = 12
    }
    
    /// <summary>
    /// Represents a parsed AVCC NAL unit
    /// </summary>
    public class AvccNalUnit
    {
        public NalUnitType Type { get; set; }
        public byte RefIdc { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public int Length { get; set; }
    }
}
