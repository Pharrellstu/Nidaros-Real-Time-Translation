using System;
using System.Collections.Generic;
using System.Linq;

namespace NidarosRTT.Infrastructure.Captions
{
    /// <summary>
    /// Builds H.264 SEI (Supplemental Enhancement Information) NAL units
    /// containing CEA-608 closed caption data according to ATSC A/53 Part 4
    /// </summary>
    public class H264SeiBuilder
    {
        // SEI payload type for user data registered by ITU-T Recommendation T.35
        private const byte SEI_TYPE_USER_DATA_REGISTERED = 0x04;
        
        // ITU-T T.35 Country Code for United States
        private const byte ITU_T_T35_COUNTRY_CODE_US = 0xB5;
        
        // ITU-T T.35 Provider Code for ATSC
        private const ushort ITU_T_T35_PROVIDER_CODE_ATSC = 0x0031;
        
        // ATSC user identifier (ASCII "GA94")
        private static readonly byte[] ATSC_USER_IDENTIFIER = { 0x47, 0x41, 0x39, 0x34 };
        
        // ATSC1_data() user_data_type_code for cc_data
        private const byte ATSC_USER_DATA_TYPE_CC_DATA = 0x03;
        
        // NAL unit type for SEI
        private const byte NAL_UNIT_TYPE_SEI = 0x06;
        
        // NAL unit reference IDC (0 for SEI)
        private const byte NAL_REF_IDC_ZERO = 0x00;
        
        /// <summary>
        /// Build an H.264 SEI NAL unit containing CEA-608 caption data
        /// </summary>
        /// <param name="cea608Data">CEA-608 cc_data packets (multiple of 3 bytes)</param>
        /// <param name="useAvccFormat">If true, prepend 4-byte length instead of start code</param>
        /// <returns>Complete NAL unit in AVCC format (length-prefixed) or Annex B format (start code)</returns>
        public byte[] BuildSeiNalUnit(byte[] cea608Data, bool useAvccFormat = true)
        {
            if (cea608Data == null || cea608Data.Length == 0)
                throw new ArgumentException("CEA-608 data cannot be null or empty", nameof(cea608Data));
            
            if (cea608Data.Length % 3 != 0)
                throw new ArgumentException("CEA-608 data must be a multiple of 3 bytes (cc_data packets)", nameof(cea608Data));
            
            var seiPayload = BuildAtscA53Payload(cea608Data);
            var nalUnit = BuildSeiMessage(seiPayload);
            
            if (useAvccFormat)
            {
                return ConvertToAvccFormat(nalUnit);
            }
            
            return nalUnit;
        }
        
        /// <summary>
        /// Build ATSC A/53 Part 4 caption data payload
        /// Structure: itu_t_t35_country_code | itu_t_t35_provider_code | ATSC_user_identifier | 
        ///            user_data_type_code | user_data_length | cc_data_packet
        /// </summary>
        private byte[] BuildAtscA53Payload(byte[] cea608Data)
        {
            var payload = new List<byte>();
            
            // ITU-T T.35 country code (1 byte) - United States
            payload.Add(ITU_T_T35_COUNTRY_CODE_US);
            
            // ITU-T T.35 provider code (2 bytes, big-endian) - ATSC
            payload.Add((byte)((ITU_T_T35_PROVIDER_CODE_ATSC >> 8) & 0xFF));
            payload.Add((byte)(ITU_T_T35_PROVIDER_CODE_ATSC & 0xFF));
            
            // ATSC user identifier (4 bytes) - "GA94"
            payload.AddRange(ATSC_USER_IDENTIFIER);
            
            // user_data_type_code (1 byte) - cc_data
            payload.Add(ATSC_USER_DATA_TYPE_CC_DATA);
            
            // Build cc_data() structure
            var ccData = BuildCcDataStructure(cea608Data);
            payload.AddRange(ccData);
            
            return payload.ToArray();
        }
        
        /// <summary>
        /// Build cc_data() structure according to ATSC A/53 Part 4 Section 6.2.3
        /// Structure: reserved(1) | process_cc_data_flag(1) | zero_bit(1) | cc_count(5) |
        ///            reserved(8) | cc_data_packets | marker_bits(8)
        /// </summary>
        private byte[] BuildCcDataStructure(byte[] cea608Data)
        {
            var ccData = new List<byte>();
            
            // Calculate cc_count (number of 3-byte cc_data packets)
            int ccCount = cea608Data.Length / 3;
            
            if (ccCount > 31)
                throw new ArgumentException("Too many cc_data packets (max 31)", nameof(cea608Data));
            
            // First byte: reserved(1) | process_cc_data_flag(1) | zero_bit(1) | cc_count(5)
            // process_cc_data_flag = 1 (process the cc_data)
            // reserved = 1, zero_bit = 0
            byte firstByte = (byte)(0xC0 | ccCount); // 0xC0 = 0b11000000
            ccData.Add(firstByte);
            
            // Second byte: reserved (0xFF)
            ccData.Add(0xFF);
            
            // Add all cc_data packets
            ccData.AddRange(cea608Data);
            
            // Marker bits (0xFF) - indicates end of cc_data
            ccData.Add(0xFF);
            
            return ccData.ToArray();
        }
        
        /// <summary>
        /// Build complete SEI message with NAL unit header
        /// Structure: NAL header (1 byte) | SEI payload type | SEI payload size | payload | rbsp_trailing_bits
        /// </summary>
        private byte[] BuildSeiMessage(byte[] payload)
        {
            var seiMessage = new List<byte>();
            
            // NAL unit header (1 byte): forbidden_zero_bit(1) | nal_ref_idc(2) | nal_unit_type(5)
            // For SEI: forbidden_zero_bit=0, nal_ref_idc=0, nal_unit_type=6
            byte nalHeader = (byte)((NAL_REF_IDC_ZERO << 5) | NAL_UNIT_TYPE_SEI);
            seiMessage.Add(nalHeader);
            
            // SEI payload type (0x04 for user_data_registered_itu_t_t35)
            seiMessage.Add(SEI_TYPE_USER_DATA_REGISTERED);
            
            // SEI payload size (variable length using ff_byte notation)
            seiMessage.AddRange(EncodePayloadSize(payload.Length));
            
            // SEI payload
            seiMessage.AddRange(payload);
            
            // RBSP trailing bits (0x80) - rbsp_stop_one_bit followed by zero bits
            seiMessage.Add(0x80);
            
            return seiMessage.ToArray();
        }
        
        /// <summary>
        /// Encode payload size using ff_byte notation
        /// If size >= 255, encode as multiple 0xFF bytes followed by the remainder
        /// Example: size=500 -> [0xFF, 0xFF, 0xF6] (255 + 255 + 246)
        /// </summary>
        private byte[] EncodePayloadSize(int size)
        {
            var encodedSize = new List<byte>();
            
            while (size >= 255)
            {
                encodedSize.Add(0xFF);
                size -= 255;
            }
            
            encodedSize.Add((byte)size);
            
            return encodedSize.ToArray();
        }
        
        /// <summary>
        /// Convert NAL unit from Annex B format (start codes) to AVCC format (length-prefixed)
        /// AVCC format: 4-byte length (big-endian) + NAL unit data
        /// </summary>
        private byte[] ConvertToAvccFormat(byte[] nalUnit)
        {
            var avccNal = new List<byte>();
            
            // 4-byte length prefix (big-endian)
            int length = nalUnit.Length;
            avccNal.Add((byte)((length >> 24) & 0xFF));
            avccNal.Add((byte)((length >> 16) & 0xFF));
            avccNal.Add((byte)((length >> 8) & 0xFF));
            avccNal.Add((byte)(length & 0xFF));
            
            // NAL unit data
            avccNal.AddRange(nalUnit);
            
            return avccNal.ToArray();
        }
        
        /// <summary>
        /// Calculate the total size of the SEI NAL unit for a given CEA-608 data
        /// Useful for buffer allocation
        /// </summary>
        public int GetSeiNalUnitSize(int cea608DataLength, bool useAvccFormat = true)
        {
            if (cea608DataLength % 3 != 0)
                throw new ArgumentException("CEA-608 data length must be a multiple of 3", nameof(cea608DataLength));
            
            // ATSC A/53 header: country(1) + provider(2) + user_id(4) + user_data_type(1) = 8 bytes
            // cc_data structure: first_byte(1) + reserved(1) + cc_data_packets + marker(1) = 3 + cea608DataLength
            int payloadSize = 8 + 3 + cea608DataLength;
            
            // SEI message: NAL_header(1) + payload_type(1) + payload_size(variable) + payload + rbsp_trailing(1)
            int payloadSizeBytes = (payloadSize >= 255) ? (payloadSize / 255) + 1 : 1;
            int seiMessageSize = 1 + 1 + payloadSizeBytes + payloadSize + 1;
            
            // AVCC format adds 4-byte length prefix
            if (useAvccFormat)
                seiMessageSize += 4;
            
            return seiMessageSize;
        }
    }
}
