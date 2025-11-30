# CEA-608 Embedded Captions Implementation TODO

## Executive Summary

**Goal**: Embed live subtitles as CEA-608 closed captions directly into the H.264 video bitstream for the delayed stream. This is the only viable solution for HLS live streaming with late-joiner support.

**Why CEA-608**: 
- ✅ Industry standard (broadcast TV, YouTube, Twitch)
- ✅ Embedded in video = survives transcoding/HLS segmentation
- ✅ Works for all players (late joiners see captions)
- ✅ No external VTT file synchronization issues
- ✅ Wowza natively passes through CEA-608 data

**Reference**: Wowza's official implementation at https://www.wowza.com/developer-ai-subtitles

---

## Phase 1: Research & Architecture (2-3 days)

### Task 1.1: Study CEA-608 Standard
**Priority**: 🔴 Critical

**Description**: Understand CEA-608 encoding format and requirements.

**Key Concepts to Research**:
1. **CEA-608 Data Structure**:
   ```
   Byte 1: 0x80 | cc_valid (1 bit) | cc_type (2 bits) | reserved (5 bits)
   Byte 2: First character or control code
   Byte 3: Second character or control code
   ```

2. **Control Codes**:
   - `0x1420`: Resume Caption Loading (RCL) - Start new caption
   - `0x142F`: End Of Caption (EOC) - Display caption
   - `0x142C`: Erase Displayed Memory (EDM) - Clear screen
   - `0x1421-0x1423`: Cursor positioning (Row 1-3)

3. **Character Encoding**:
   - ASCII characters use standard codes (0x20-0x7F)
   - Special characters use extended codes (0x00-0x1F)
   - Non-ASCII characters (Spanish accents, etc.) use special sequences

**Resources**:
- [CEA-608 Standard (EIA-608)](https://en.wikipedia.org/wiki/EIA-608)
- [SMPTE 334M](https://ieeexplore.ieee.org/document/7291706) - CEA-608 in MPEG-2
- [A/53 ATSC Standard](https://www.atsc.org/atsc-documents/a53-atsc-digital-television-standard/) - Part 4 covers closed captioning

**Deliverable**: 
- Document: `docs/CEA608_Format_Specification.md`
- Sample caption byte sequences for testing

---

### Task 1.2: Study H.264 SEI Message Structure
**Priority**: 🔴 Critical

**Description**: Understand how to inject CEA-608 data into H.264 bitstream via SEI (Supplemental Enhancement Information) messages.

**Key Concepts**:
1. **H.264 NAL Unit Structure**:
   ```
   NAL Unit = [Start Code (3-4 bytes)] [NAL Header (1 byte)] [Payload] [Trailing bits]
   ```

2. **SEI Message Types**:
   - Type 4: `user_data_registered_itu_t_t35` ← CEA-608 goes here
   - Format: `[Country Code (1 byte)][Provider Code (2 bytes)][User Identifier (4 bytes)][Payload]`

3. **CEA-608 in SEI**:
   ```
   Country Code: 0xB5 (United States)
   Provider Code: 0x0031 (ATSC)
   User Identifier: "GA94"
   cc_data_pkt structure:
     - reserved (1 bit)
     - process_cc_data_flag (1 bit)
     - zero_bit (1 bit)
     - cc_count (5 bits) - Number of 3-byte caption data packets
     - reserved (8 bits)
     - [cc_data triplets...]
     - marker_bits (8 bits) = 0xFF
   ```

4. **Injection Points**:
   - **Best**: Before each IDR (Instant Decoder Refresh) frame
   - **Alternative**: Every N frames (30 frames = 1 second @ 30fps)
   - **Timing**: Match caption timecode with video PTS (Presentation Timestamp)

**Resources**:
- [ITU-T H.264 Specification](https://www.itu.int/rec/T-REC-H.264) - Annex D (SEI messages)
- [FFmpeg H.264 Parser](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/h264_sei.c)
- [ATSC A/72 Part 1](https://www.atsc.org/atsc-documents/a72-part-1-video-index/) - CEA-708 in H.264

**Deliverable**:
- Document: `docs/H264_SEI_Injection_Guide.md`
- Diagram: NAL unit structure with SEI placement

---

### Task 1.3: Analyze Wowza Java Reference Implementation
**Priority**: 🟡 High

**Description**: Reverse-engineer the working Java implementation to understand the exact flow.

**Files to Analyze** (from `wse-plugin-caption-handlers`):
1. **`src/main/java/com/wowza/wms/plugin/captions/caption/CaptionHandler.java`**
   - How captions are queued and timed
   - Caption text formatting rules (max length, line breaks)

2. **`src/main/java/com/wowza/wms/plugin/captions/caption/DelayedStreamCaptionHandler.java`**
   - How captions are synchronized with delayed stream timecode
   - Integration with stream buffer

3. **`src/main/java/com/wowza/wms/plugin/captions/stream/DelayedStream.java`**
   - Packet buffering mechanism (similar to our `StreamBuffer.cs`)
   - Timecode reset logic

4. **`lib/wse-plugin-caption-handlers-1.1.0.jar`**
   - Decompile JAR to view actual CEA-608 encoding logic
   - Look for `Cea608Encoder` or similar class

**Key Questions to Answer**:
- How does Java code inject CEA-608 into FLV `onTextData` packets?
- Does it modify video frames or use separate data stream?
- How are caption timecodes synchronized with video PTS?
- What's the maximum caption text length per frame?

**Tools**:
```bash
# Decompile JAR file
cd wse-plugin-caption-handlers/lib
jar xf wse-plugin-caption-handlers-1.1.0.jar
# Use JD-GUI or IntelliJ to view decompiled sources
```

**Deliverable**:
- Document: `docs/Wowza_Java_Implementation_Analysis.md`
- Flow diagram: Caption injection pipeline
- Code snippets: Key Java methods to replicate in C#

---

## Phase 2: Core Components (5-7 days)

### Task 2.1: Implement CEA-608 Encoder
**Priority**: 🔴 Critical  
**Estimated Time**: 2 days

**Description**: Create C# class to encode text captions into CEA-608 byte format.

**Implementation**:
```csharp
namespace NidarosRTT.Infrastructure.Captions
{
    /// <summary>
    /// Encodes text captions into CEA-608 closed caption format
    /// </summary>
    public class Cea608Encoder
    {
        // CEA-608 Control Codes
        private const byte RESUME_CAPTION_LOADING = 0x14;
        private const byte END_OF_CAPTION = 0x2F;
        private const byte ERASE_DISPLAYED_MEMORY = 0x2C;
        
        // Caption positioning
        private const int MAX_CHARS_PER_LINE = 32;
        private const int MAX_LINES = 4;
        
        /// <summary>
        /// Encode text into CEA-608 cc_data triplets
        /// </summary>
        /// <param name="text">Caption text (max 128 characters)</param>
        /// <param name="lineNumber">Line number (1-4, bottom to top)</param>
        /// <returns>Array of 3-byte cc_data packets</returns>
        public byte[] EncodeCaptionText(string text, int lineNumber = 1)
        {
            var packets = new List<byte>();
            
            // 1. Clear previous caption (EDM command)
            packets.AddRange(CreateControlCode(0x14, 0x2C));
            
            // 2. Position cursor (PAC - Preamble Address Code)
            var pacCode = GetPacForLine(lineNumber);
            packets.AddRange(pacCode);
            
            // 3. Resume Caption Loading (RCL)
            packets.AddRange(CreateControlCode(0x14, 0x20));
            
            // 4. Encode text characters (2 bytes per packet)
            var cleanText = SanitizeText(text);
            for (int i = 0; i < cleanText.Length; i += 2)
            {
                var byte1 = (byte)cleanText[i];
                var byte2 = (i + 1 < cleanText.Length) ? (byte)cleanText[i + 1] : (byte)0x80;
                packets.AddRange(CreateDataPacket(byte1, byte2));
            }
            
            // 5. End Of Caption (EOC) - Display caption
            packets.AddRange(CreateControlCode(0x14, 0x2F));
            
            return packets.ToArray();
        }
        
        /// <summary>
        /// Create a 3-byte cc_data packet
        /// </summary>
        private byte[] CreateDataPacket(byte data1, byte data2)
        {
            return new byte[]
            {
                0xFC,  // cc_valid=1, cc_type=0 (NTSC line 21 field 1)
                data1,
                data2
            };
        }
        
        /// <summary>
        /// Create control code packet (PAC, EDM, EOC, etc.)
        /// </summary>
        private byte[] CreateControlCode(byte code1, byte code2)
        {
            return new byte[] { 0xFC, code1, code2 };
        }
        
        /// <summary>
        /// Get Preamble Address Code for line positioning
        /// </summary>
        private byte[] GetPacForLine(int lineNumber)
        {
            // PAC codes for rows 1-4 (bottom to top)
            var pacCodes = new Dictionary<int, (byte, byte)>
            {
                { 1, (0x11, 0x40) }, // Row 11 (bottom)
                { 2, (0x11, 0x60) }, // Row 10
                { 3, (0x12, 0x40) }, // Row 9
                { 4, (0x12, 0x60) }  // Row 8
            };
            
            if (!pacCodes.ContainsKey(lineNumber))
                lineNumber = 1;
            
            var (code1, code2) = pacCodes[lineNumber];
            return CreateControlCode(code1, code2);
        }
        
        /// <summary>
        /// Sanitize text for CEA-608 (ASCII only, max length)
        /// </summary>
        private string SanitizeText(string text)
        {
            // Remove non-ASCII characters
            var cleaned = new string(text.Where(c => c >= 0x20 && c <= 0x7F).ToArray());
            
            // Truncate to max length
            if (cleaned.Length > MAX_CHARS_PER_LINE)
                cleaned = cleaned.Substring(0, MAX_CHARS_PER_LINE);
            
            return cleaned;
        }
        
        /// <summary>
        /// Split long text into multiple caption frames
        /// </summary>
        public List<byte[]> EncodeMultiLineCaption(string text)
        {
            var lines = WrapText(text, MAX_CHARS_PER_LINE);
            var frames = new List<byte[]>();
            
            for (int i = 0; i < lines.Count && i < MAX_LINES; i++)
            {
                frames.Add(EncodeCaptionText(lines[i], i + 1));
            }
            
            return frames;
        }
        
        /// <summary>
        /// Word-wrap text to fit CEA-608 line length
        /// </summary>
        private List<string> WrapText(string text, int maxLength)
        {
            var lines = new List<string>();
            var words = text.Split(' ');
            var currentLine = "";
            
            foreach (var word in words)
            {
                if ((currentLine + word).Length > maxLength)
                {
                    if (!string.IsNullOrEmpty(currentLine))
                    {
                        lines.Add(currentLine.Trim());
                        currentLine = "";
                    }
                }
                currentLine += word + " ";
            }
            
            if (!string.IsNullOrEmpty(currentLine))
                lines.Add(currentLine.Trim());
            
            return lines;
        }
    }
}
```

**Test Cases**:
```csharp
// Test 1: Simple short caption
var encoder = new Cea608Encoder();
var bytes = encoder.EncodeCaptionText("Hello World");
// Expected: [EDM][PAC][RCL][H][e][l][l][o][ ][W][o][r][l][d][EOC]

// Test 2: Multi-line caption
var frames = encoder.EncodeMultiLineCaption("This is a very long caption that needs to be wrapped across multiple lines for proper display");
// Expected: 2-3 frames with proper line breaks

// Test 3: Special characters
var bytes = encoder.EncodeCaptionText("Test: 123 & symbols!");
// Expected: Sanitized ASCII output
```

**Deliverable**:
- File: `NidarosRTT.Infrastructure/Captions/Cea608Encoder.cs`
- Unit tests: `NidarosRTT.Tests/Captions/Cea608EncoderTests.cs`

---

### Task 2.2: Implement H.264 SEI Message Builder
**Priority**: 🔴 Critical  
**Estimated Time**: 3 days

**Description**: Create class to construct H.264 SEI (Supplemental Enhancement Information) messages containing CEA-608 data.

**Implementation**:
```csharp
namespace NidarosRTT.Infrastructure.Captions
{
    /// <summary>
    /// Builds H.264 SEI messages with embedded CEA-608 captions
    /// </summary>
    public class H264SeiBuilder
    {
        // ATSC A/53 identifiers
        private const byte ITU_T_T35_COUNTRY_CODE_US = 0xB5;
        private const ushort ITU_T_T35_PROVIDER_CODE_ATSC = 0x0031;
        private const uint USER_IDENTIFIER_GA94 = 0x47413934; // "GA94"
        
        /// <summary>
        /// Create SEI NAL unit with CEA-608 caption data
        /// </summary>
        /// <param name="captionData">CEA-608 cc_data packets (3 bytes each)</param>
        /// <returns>Complete SEI NAL unit bytes</returns>
        public byte[] BuildCea608SeiNalUnit(byte[] captionData)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // 1. NAL Unit Header
            //    forbidden_zero_bit(1) + nal_ref_idc(2) + nal_unit_type(5)
            //    Type 6 = SEI message
            byte nalHeader = 0x06; // 00000110 = SEI
            writer.Write(nalHeader);
            
            // 2. SEI Payload Type (user_data_registered_itu_t_t35)
            byte payloadType = 0x04;
            writer.Write(payloadType);
            
            // 3. SEI Payload Size (will calculate later)
            //    Use ff_byte (0xFF) for sizes >= 255
            var payloadData = BuildCea608Payload(captionData);
            WriteSeiPayloadSize(writer, payloadData.Length);
            
            // 4. SEI Payload
            writer.Write(payloadData);
            
            // 5. RBSP Trailing Bits (byte alignment)
            writer.Write((byte)0x80); // rbsp_stop_one_bit + trailing zeros
            
            var seiBytes = ms.ToArray();
            
            // 6. Prepend start code (0x00 0x00 0x00 0x01)
            var nalUnit = new byte[4 + seiBytes.Length];
            nalUnit[0] = 0x00;
            nalUnit[1] = 0x00;
            nalUnit[2] = 0x00;
            nalUnit[3] = 0x01;
            Array.Copy(seiBytes, 0, nalUnit, 4, seiBytes.Length);
            
            return nalUnit;
        }
        
        /// <summary>
        /// Build CEA-608 payload according to ATSC A/53 Part 4
        /// </summary>
        private byte[] BuildCea608Payload(byte[] captionData)
        {
            using var ms = new MemoryStream();
            using var writer = new BinaryWriter(ms);
            
            // ITU-T T.35 header
            writer.Write(ITU_T_T35_COUNTRY_CODE_US);           // country_code (1 byte)
            writer.Write((byte)(ITU_T_T35_PROVIDER_CODE_ATSC >> 8));   // provider_code high byte
            writer.Write((byte)(ITU_T_T35_PROVIDER_CODE_ATSC & 0xFF)); // provider_code low byte
            
            // User identifier "GA94"
            writer.Write((byte)'G');
            writer.Write((byte)'A');
            writer.Write((byte)'9');
            writer.Write((byte)'4');
            
            // User data type code (0x03 = cc_data)
            writer.Write((byte)0x03);
            
            // cc_data() structure
            var ccCount = captionData.Length / 3;
            
            // First byte: reserved(1) + process_cc_data_flag(1) + zero_bit(1) + cc_count(5)
            byte ccDataByte1 = (byte)(0xC0 | (ccCount & 0x1F)); // 11000000 | cc_count
            writer.Write(ccDataByte1);
            
            // Reserved byte (8 bits of 1s)
            writer.Write((byte)0xFF);
            
            // Caption data packets
            writer.Write(captionData);
            
            // Marker bits (8 bits of 1s)
            writer.Write((byte)0xFF);
            
            return ms.ToArray();
        }
        
        /// <summary>
        /// Write SEI payload size using ff_byte notation
        /// </summary>
        private void WriteSeiPayloadSize(BinaryWriter writer, int size)
        {
            // Sizes 0-254: single byte
            // Sizes >= 255: multiple 0xFF bytes + final byte
            while (size >= 255)
            {
                writer.Write((byte)0xFF);
                size -= 255;
            }
            writer.Write((byte)size);
        }
    }
}
```

**Test Cases**:
```csharp
// Test 1: Build SEI with single caption
var seiBuilder = new H264SeiBuilder();
var cea608Encoder = new Cea608Encoder();
var captionBytes = cea608Encoder.EncodeCaptionText("Test");
var seiNalUnit = seiBuilder.BuildCea608SeiNalUnit(captionBytes);

// Verify structure:
// [00 00 00 01][06][04][payload_size][B5 00 31 GA94 03 ...][80]

// Test 2: Verify NAL unit starts with correct start code
Assert.Equal(0x00, seiNalUnit[0]);
Assert.Equal(0x00, seiNalUnit[1]);
Assert.Equal(0x00, seiNalUnit[2]);
Assert.Equal(0x01, seiNalUnit[3]);
Assert.Equal(0x06, seiNalUnit[4]); // SEI NAL type

// Test 3: Large payload (> 255 bytes)
var largeCaptionBytes = new byte[300]; // Multiple ff_bytes in size field
var largeSeiNalUnit = seiBuilder.BuildCea608SeiNalUnit(largeCaptionBytes);
```

**Deliverable**:
- File: `NidarosRTT.Infrastructure/Captions/H264SeiBuilder.cs`
- Unit tests: `NidarosRTT.Tests/Captions/H264SeiBuilderTests.cs`
- Documentation: SEI structure diagram

---

### Task 2.3: Implement FLV Video Tag Modifier
**Priority**: 🔴 Critical  
**Estimated Time**: 2 days

**Description**: Modify FLV video tags to insert SEI NAL units before existing video data.

**Background**:
- FLV video tags contain H.264 NALU (Network Abstraction Layer Units)
- CEA-608 SEI must be inserted as a separate NALU before the video frame
- Must maintain AVCC format (length-prefixed NALUs, not Annex B start codes)

**Implementation**:
```csharp
namespace NidarosRTT.Infrastructure.Captions
{
    /// <summary>
    /// Modifies FLV video packets to inject SEI NAL units with captions
    /// </summary>
    public class FlvVideoTagModifier
    {
        private readonly H264SeiBuilder _seiBuilder;
        
        public FlvVideoTagModifier()
        {
            _seiBuilder = new H264SeiBuilder();
        }
        
        /// <summary>
        /// Inject CEA-608 caption into FLV video tag
        /// </summary>
        /// <param name="originalVideoData">Original FLV video tag data</param>
        /// <param name="captionBytes">CEA-608 encoded caption data</param>
        /// <returns>Modified FLV video tag data with embedded caption</returns>
        public byte[] InjectCaption(byte[] originalVideoData, byte[] captionBytes)
        {
            // FLV Video Tag Structure:
            // [FrameType+CodecID (1 byte)][AVCPacketType (1 byte)][CompositionTime (3 bytes)][NALU Data]
            
            if (originalVideoData.Length < 5)
                return originalVideoData; // Invalid tag, return as-is
            
            var frameTypeCodec = originalVideoData[0];
            var avcPacketType = originalVideoData[1];
            
            // Only inject into AVC NALU packets (avcPacketType == 1)
            // avcPacketType 0 = AVCDecoderConfigurationRecord (don't modify)
            // avcPacketType 2 = End of sequence (don't modify)
            if (avcPacketType != 0x01)
                return originalVideoData;
            
            // Extract composition time (3 bytes, big-endian, signed)
            var compositionTime = new byte[3];
            Array.Copy(originalVideoData, 2, compositionTime, 0, 3);
            
            // Build SEI NAL unit
            var seiNalUnit = _seiBuilder.BuildCea608SeiNalUnit(captionBytes);
            
            // Convert Annex B format (start codes) to AVCC format (length-prefixed)
            var seiNalUnitAvcc = ConvertAnnexBToAvcc(seiNalUnit);
            
            // Extract existing NALUs
            var existingNalus = ExtractNalusFromFlvTag(originalVideoData, 5);
            
            // Reconstruct FLV video tag with SEI inserted before other NALUs
            using var ms = new MemoryStream();
            ms.WriteByte(frameTypeCodec);
            ms.WriteByte(avcPacketType);
            ms.Write(compositionTime, 0, 3);
            
            // Write SEI NALU first (length-prefixed)
            ms.Write(seiNalUnitAvcc, 0, seiNalUnitAvcc.Length);
            
            // Write existing NALUs
            ms.Write(existingNalus, 0, existingNalus.Length);
            
            return ms.ToArray();
        }
        
        /// <summary>
        /// Convert Annex B format NAL (start codes) to AVCC format (length prefix)
        /// </summary>
        private byte[] ConvertAnnexBToAvcc(byte[] nalUnitAnnexB)
        {
            // Remove start code (0x00 0x00 0x00 0x01 or 0x00 0x00 0x01)
            int startCodeSize = 0;
            if (nalUnitAnnexB.Length >= 4 &&
                nalUnitAnnexB[0] == 0x00 && nalUnitAnnexB[1] == 0x00 &&
                nalUnitAnnexB[2] == 0x00 && nalUnitAnnexB[3] == 0x01)
            {
                startCodeSize = 4;
            }
            else if (nalUnitAnnexB.Length >= 3 &&
                     nalUnitAnnexB[0] == 0x00 && nalUnitAnnexB[1] == 0x00 &&
                     nalUnitAnnexB[2] == 0x01)
            {
                startCodeSize = 3;
            }
            
            var nalData = new byte[nalUnitAnnexB.Length - startCodeSize];
            Array.Copy(nalUnitAnnexB, startCodeSize, nalData, 0, nalData.Length);
            
            // AVCC format: [4-byte length][NAL data]
            var avccNalu = new byte[4 + nalData.Length];
            
            // Write length (big-endian)
            avccNalu[0] = (byte)((nalData.Length >> 24) & 0xFF);
            avccNalu[1] = (byte)((nalData.Length >> 16) & 0xFF);
            avccNalu[2] = (byte)((nalData.Length >> 8) & 0xFF);
            avccNalu[3] = (byte)(nalData.Length & 0xFF);
            
            // Write NAL data
            Array.Copy(nalData, 0, avccNalu, 4, nalData.Length);
            
            return avccNalu;
        }
        
        /// <summary>
        /// Extract existing NALUs from FLV video tag (already in AVCC format)
        /// </summary>
        private byte[] ExtractNalusFromFlvTag(byte[] flvVideoData, int offset)
        {
            var nalusLength = flvVideoData.Length - offset;
            var nalus = new byte[nalusLength];
            Array.Copy(flvVideoData, offset, nalus, 0, nalusLength);
            return nalus;
        }
        
        /// <summary>
        /// Check if video tag is a keyframe (IDR frame)
        /// SEI messages should ideally be inserted before keyframes
        /// </summary>
        public bool IsKeyFrame(byte[] flvVideoData)
        {
            if (flvVideoData.Length < 1)
                return false;
            
            var frameTypeCodec = flvVideoData[0];
            var frameType = (frameTypeCodec >> 4) & 0x0F;
            
            // Frame type 1 = keyframe (IDR)
            return frameType == 1;
        }
    }
}
```

**Test Cases**:
```csharp
// Test 1: Inject caption into keyframe
var modifier = new FlvVideoTagModifier();
var originalVideoTag = GetSampleFlvVideoTag(); // Mock data
var captionBytes = new Cea608Encoder().EncodeCaptionText("Test");
var modifiedTag = modifier.InjectCaption(originalVideoTag, captionBytes);

// Verify: modified tag is larger (has SEI)
Assert.True(modifiedTag.Length > originalVideoTag.Length);

// Verify: AVCC format (first 4 bytes after header = length)
var lengthPrefix = BitConverter.ToUInt32(modifiedTag, 5);
Assert.True(lengthPrefix > 0);

// Test 2: Don't modify AVCDecoderConfigurationRecord (avcPacketType == 0)
var configRecord = new byte[] { 0x17, 0x00, 0x00, 0x00, 0x00, /* ... */ };
var unmodified = modifier.InjectCaption(configRecord, captionBytes);
Assert.Equal(configRecord, unmodified);

// Test 3: Detect keyframes
var keyFrameTag = new byte[] { 0x17, /* ... */ }; // Frame type 1 (keyframe)
Assert.True(modifier.IsKeyFrame(keyFrameTag));

var interFrameTag = new byte[] { 0x27, /* ... */ }; // Frame type 2 (inter frame)
Assert.False(modifier.IsKeyFrame(interFrameTag));
```

**Deliverable**:
- File: `NidarosRTT.Infrastructure/Captions/FlvVideoTagModifier.cs`
- Unit tests: `NidarosRTT.Tests/Captions/FlvVideoTagModifierTests.cs`

---

## Phase 3: Integration (3-4 days)

### Task 3.1: Create Caption Queue System
**Priority**: 🟡 High  
**Estimated Time**: 1 day

**Description**: Queue system to match captions with video frames by timecode.

**Implementation**:
```csharp
namespace NidarosRTT.Infrastructure.Captions
{
    /// <summary>
    /// Queues captions for injection at specific stream timecodes
    /// </summary>
    public class CaptionQueue
    {
        private readonly SortedDictionary<long, Queue<string>> _captionsByTimecode;
        private readonly object _lock = new object();
        private const long TIMECODE_TOLERANCE_MS = 500; // Match captions within 500ms
        
        public CaptionQueue()
        {
            _captionsByTimecode = new SortedDictionary<long, Queue<string>>();
        }
        
        /// <summary>
        /// Add caption to queue for specific timecode
        /// </summary>
        public void EnqueueCaption(string text, long timecodeMs)
        {
            lock (_lock)
            {
                if (!_captionsByTimecode.ContainsKey(timecodeMs))
                {
                    _captionsByTimecode[timecodeMs] = new Queue<string>();
                }
                
                _captionsByTimecode[timecodeMs].Enqueue(text);
                
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"[CAPTION QUEUE] Queued caption at {timecodeMs}ms: \"{text}\"");
                Console.ResetColor();
            }
        }
        
        /// <summary>
        /// Get captions that should be displayed at this timecode
        /// </summary>
        public List<string> GetCaptionsForTimecode(long timecodeMs)
        {
            lock (_lock)
            {
                var captions = new List<string>();
                
                // Find captions within tolerance range
                var matchingTimecodes = _captionsByTimecode.Keys
                    .Where(tc => Math.Abs(tc - timecodeMs) <= TIMECODE_TOLERANCE_MS)
                    .ToList();
                
                foreach (var tc in matchingTimecodes)
                {
                    while (_captionsByTimecode[tc].Count > 0)
                    {
                        captions.Add(_captionsByTimecode[tc].Dequeue());
                    }
                    
                    // Remove empty queue
                    _captionsByTimecode.Remove(tc);
                }
                
                return captions;
            }
        }
        
        /// <summary>
        /// Clear old captions (older than retention period)
        /// </summary>
        public void ClearOldCaptions(long currentTimecodeMs, long retentionMs = 60000)
        {
            lock (_lock)
            {
                var expiredTimecodes = _captionsByTimecode.Keys
                    .Where(tc => tc < currentTimecodeMs - retentionMs)
                    .ToList();
                
                foreach (var tc in expiredTimecodes)
                {
                    _captionsByTimecode.Remove(tc);
                }
            }
        }
    }
}
```

**Deliverable**:
- File: `NidarosRTT.Infrastructure/Captions/CaptionQueue.cs`
- Unit tests: Test timecode matching and queuing

---

### Task 3.2: Modify StreamPublisher to Inject Captions
**Priority**: 🔴 Critical  
**Estimated Time**: 2 days

**Description**: Update `StreamPublisher.cs` to inject captions into video packets.

**Changes to `StreamPublisher.cs`**:
```csharp
public class StreamPublisher : IStreamPublisher
{
    private readonly FlvVideoTagModifier _videoModifier;
    private readonly CaptionQueue _captionQueue;
    private readonly Cea608Encoder _cea608Encoder;
    
    public StreamPublisher(string outputUrl, string ffmpegPath, IStreamBuffer streamBuffer)
    {
        // ... existing code ...
        _videoModifier = new FlvVideoTagModifier();
        _captionQueue = new CaptionQueue();
        _cea608Encoder = new Cea608Encoder();
    }
    
    /// <summary>
    /// Enqueue caption for injection at specific timecode
    /// </summary>
    public void QueueCaption(string text, long timecodeMs)
    {
        _captionQueue.EnqueueCaption(text, timecodeMs);
    }
    
    private async Task WritePacketAsync(MediaPacket packet, CancellationToken cancellationToken)
    {
        if (_ffmpegStdin == null || !_headerWritten)
            throw new InvalidOperationException("FFmpeg stdin not ready");

        byte[] dataToWrite = packet.Data;
        
        // For video packets, check if we need to inject captions
        if (packet.Type == MediaPacketType.Video)
        {
            var captions = _captionQueue.GetCaptionsForTimecode(packet.Timecode);
            
            if (captions.Count > 0)
            {
                // Use first caption (TODO: handle multiple captions)
                var captionText = captions[0];
                var cea608Bytes = _cea608Encoder.EncodeCaptionText(captionText);
                
                dataToWrite = _videoModifier.InjectCaption(packet.Data, cea608Bytes);
                
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"[CAPTION INJECT] Injected caption at {packet.Timecode}ms: \"{captionText}\"");
                Console.ResetColor();
            }
        }
        
        // Construct FLV tag with (potentially modified) data
        var tagType = packet.Type switch
        {
            MediaPacketType.Audio => (byte)0x08,
            MediaPacketType.Video => (byte)0x09,
            MediaPacketType.Data => (byte)0x12,
            _ => (byte)0x12
        };

        var dataSize = dataToWrite.Length;
        var timestamp = packet.Timecode;

        // Build tag header (11 bytes)
        var tagHeader = new byte[11];
        tagHeader[0] = tagType;
        
        // Data size (3 bytes, big-endian)
        tagHeader[1] = (byte)((dataSize >> 16) & 0xFF);
        tagHeader[2] = (byte)((dataSize >> 8) & 0xFF);
        tagHeader[3] = (byte)(dataSize & 0xFF);
        
        // Timestamp (4 bytes: 3 bytes timestamp + 1 byte extended)
        tagHeader[4] = (byte)((timestamp >> 16) & 0xFF);
        tagHeader[5] = (byte)((timestamp >> 8) & 0xFF);
        tagHeader[6] = (byte)(timestamp & 0xFF);
        tagHeader[7] = (byte)((timestamp >> 24) & 0xFF); // Extended timestamp
        
        // Stream ID (3 bytes, always 0)
        tagHeader[8] = 0x00;
        tagHeader[9] = 0x00;
        tagHeader[10] = 0x00;
        
        // Write tag header
        await _ffmpegStdin.WriteAsync(tagHeader, 0, 11, cancellationToken);
        
        // Write tag data
        await _ffmpegStdin.WriteAsync(dataToWrite, 0, dataToWrite.Length, cancellationToken);
        
        // Write previous tag size (4 bytes)
        var prevTagSize = 11 + dataToWrite.Length;
        var prevTagSizeBytes = new byte[4];
        prevTagSizeBytes[0] = (byte)((prevTagSize >> 24) & 0xFF);
        prevTagSizeBytes[1] = (byte)((prevTagSize >> 16) & 0xFF);
        prevTagSizeBytes[2] = (byte)((prevTagSize >> 8) & 0xFF);
        prevTagSizeBytes[3] = (byte)(prevTagSize & 0xFF);
        await _ffmpegStdin.WriteAsync(prevTagSizeBytes, 0, 4, cancellationToken);
        
        // Update stats
        lock (_statsLock)
        {
            _stats.TotalPacketsPublished++;
            _stats.TotalBytesPublished += dataToWrite.Length;
            _stats.LastPacketTime = DateTime.UtcNow;
            
            switch (packet.Type)
            {
                case MediaPacketType.Video:
                    _stats.VideoPacketsPublished++;
                    break;
                case MediaPacketType.Audio:
                    _stats.AudioPacketsPublished++;
                    break;
                case MediaPacketType.Data:
                    _stats.DataPacketsPublished++;
                    break;
            }
        }
        
        // Cleanup old captions periodically
        if (packet.Timecode % 10000 == 0) // Every 10 seconds
        {
            _captionQueue.ClearOldCaptions(packet.Timecode);
        }
    }
}
```

**Deliverable**:
- Modified: `NidarosRTT.Infrastructure/StreamBuffering/StreamPublisher.cs`
- Update `IStreamPublisher` interface to include `QueueCaption()` method

---

### Task 3.3: Connect Caption Generation to Queue
**Priority**: 🟡 High  
**Estimated Time**: 1 day

**Description**: Connect the transcription pipeline to the caption queue in `Program.cs`.

**Changes to `Program.cs`**:
```csharp
// In the transcription worker loop (around line 300-400)
var transcriptionResult = await whisperService.TranscribeAsync(cleanedFile);

if (transcriptionResult != null && !string.IsNullOrWhiteSpace(transcriptionResult.Text))
{
    var englishText = transcriptionResult.Text.Trim();
    
    // Calculate timecode for this caption
    // wallClockStartTS → stream timecode conversion
    var streamTimecode = /* TODO: Calculate based on stream start time */;
    
    // Queue caption for injection into delayed stream
    if (streamPublisherService != null)
    {
        streamPublisherService.QueueCaption(englishText, streamTimecode);
    }
    
    // Continue with existing VTT writing and SignalR broadcast...
}
```

**Key Challenge**: Converting wall-clock timestamps to stream timecodes. Need to:
1. Track stream start time (first packet timestamp)
2. Calculate: `streamTimecode = (wallClockStartTS - streamStartWallClock) + streamStartTimecode`
3. Pass timecode to `QueueCaption()`

**Deliverable**:
- Modified: `NidarosRTT.API/Program.cs`
- Add timecode conversion logic

---

## Phase 4: Testing & Validation (3-5 days)

### Task 4.1: Unit Testing
**Priority**: 🟡 High  
**Estimated Time**: 2 days

**Test Coverage**:
1. ✅ CEA-608 encoder produces valid byte sequences
2. ✅ SEI builder creates correct H.264 NAL units
3. ✅ FLV modifier correctly injects SEI without corrupting video
4. ✅ Caption queue handles timecode matching with tolerance
5. ✅ AVCC format conversion (Annex B → length-prefixed)

**Tools**:
- xUnit test framework
- Mock video packets for testing
- Hex dump comparison for byte-level verification

**Deliverable**:
- Test suite: `NidarosRTT.Tests/Captions/`
- Code coverage report (target: >80%)

---

### Task 4.2: Integration Testing
**Priority**: 🔴 Critical  
**Estimated Time**: 2 days

**Test Scenarios**:
1. **End-to-End Caption Flow**:
   - Start OBS stream
   - Verify captions queued at correct timecodes
   - Verify captions injected into video packets
   - Play delayed stream in mpv/VLC
   - Confirm CEA-608 captions display correctly

2. **Late Joiner Test**:
   - Stream for 5 minutes
   - Join as new viewer
   - Verify captions appear immediately (not delayed)

3. **Caption Timing Accuracy**:
   - Compare caption display time vs. audio speech time
   - Acceptable latency: <2 seconds

4. **Special Characters Test**:
   - Test non-ASCII characters (Spanish, Dutch accents)
   - Verify proper encoding/sanitization

5. **Long Caption Test**:
   - Send caption >32 characters
   - Verify word-wrap and multi-line display

**Tools**:
```bash
# Play with CEA-608 captions enabled
mpv rtmp://localhost:1935/live/OBSstream_delayed --sub-codepage=eia_608

# Dump CEA-608 data from stream
ffmpeg -i rtmp://localhost:1935/live/OBSstream_delayed -c copy -bsf:v trace_headers output.log
grep "user_data_registered_itu_t_t35" output.log

# Verify SEI messages
ffprobe -show_frames -select_streams v:0 -of json rtmp://localhost:1935/live/OBSstream_delayed | jq '.frames[] | select(.side_data_list != null)'
```

**Deliverable**:
- Test report: `docs/CEA608_Integration_Test_Results.md`
- Screen recordings showing captions in players

---

### Task 4.3: Performance Testing
**Priority**: 🟢 Medium  
**Estimated Time**: 1 day

**Metrics to Measure**:
1. **CPU Usage**: Caption injection overhead on StreamPublisher
2. **Memory Usage**: Caption queue growth over time
3. **Latency**: Time from transcription → caption display
4. **Throughput**: Packets/second with vs. without caption injection

**Tools**:
```bash
# Monitor CPU/memory
docker stats nidaros-real-time-translation-api-service-1

# Profile with dotnet-counters
dotnet-counters monitor --process-id <pid> System.Runtime
```

**Performance Targets**:
- CPU overhead: <10% increase
- Memory: <50MB for caption queue
- Latency: <2 seconds end-to-end
- No packet drops (0% loss)

**Deliverable**:
- Performance report: `docs/CEA608_Performance_Metrics.md`

---

## Phase 5: Documentation & Deployment (2 days)

### Task 5.1: Update Architecture Documentation
**Priority**: 🟢 Medium  
**Estimated Time**: 1 day

**Documents to Create/Update**:
1. **Architecture Overview**: Add CEA-608 pipeline diagram
2. **API Documentation**: Document `QueueCaption()` method
3. **Configuration Guide**: New environment variables (if any)
4. **Troubleshooting**: Common CEA-608 issues

**Deliverable**:
- Updated: `README.md`, `docs/ARCHITECTURE.md`
- New: `docs/CEA608_IMPLEMENTATION_GUIDE.md`

---

### Task 5.2: Deployment & Cleanup
**Priority**: 🟡 High  
**Estimated Time**: 1 day

**Tasks**:
1. Remove old dynamic VTT generation code (Solution 1 attempt)
2. Update docker-compose.yml if needed
3. Test fresh deployment on clean system
4. Create migration guide for existing installations

**Deliverable**:
- Clean main branch with CEA-608 implementation
- Deployment checklist

---

## Phase 6: Optional Enhancements (Future Work)

### Task 6.1: Advanced Caption Styling
- Support for CEA-608 styling codes (italic, underline)
- Color-coded captions for multiple speakers
- Position captions at top/bottom based on content

### Task 6.2: CEA-708 Support
- Upgrade to CEA-708 (newer standard)
- Support for multiple caption tracks (multi-language)
- Better Unicode support

### Task 6.3: Caption Analytics
- Track caption display accuracy
- Measure viewer engagement with captions
- A/B testing for caption positioning

---

## Risk Assessment

### Critical Risks 🔴

1. **H.264 Bitstream Corruption**
   - Risk: Incorrectly formatted SEI breaks video playback
   - Mitigation: Extensive unit testing, validate with FFprobe
   - Fallback: Detect corrupted frames, skip caption injection for that frame

2. **Timecode Synchronization**
   - Risk: Captions appear out of sync with audio
   - Mitigation: Accurate wall-clock → timecode conversion
   - Fallback: Add manual offset adjustment in config

3. **Wowza CEA-608 Passthrough**
   - Risk: Wowza may strip CEA-608 during HLS transcoding
   - Mitigation: Test Wowza configuration, enable caption passthrough
   - Fallback: Contact Wowza support, verify trial license includes captioning

### Medium Risks 🟡

4. **Player Compatibility**
   - Risk: Some players may not support CEA-608
   - Mitigation: Test with mpv, VLC, browser players
   - Fallback: Maintain dual system (CEA-608 + VTT)

5. **Performance Overhead**
   - Risk: Caption injection slows down stream publishing
   - Mitigation: Profile code, optimize critical paths
   - Fallback: Queue captions less frequently (every N frames)

### Low Risks 🟢

6. **Caption Queue Memory**
   - Risk: Queue grows unbounded if captions aren't consumed
   - Mitigation: Implement cleanup (remove captions >60s old)

---

## Success Criteria

✅ **Minimum Viable Implementation**:
1. CEA-608 captions embedded in delayed stream
2. Captions display in mpv/VLC when joining late
3. Timing accuracy within 2 seconds
4. No video corruption or packet drops
5. Basic ASCII text support

✅ **Full Implementation**:
1. All above +
2. Multi-line caption support
3. Special character handling
4. Performance overhead <10%
5. Comprehensive test coverage
6. Documentation complete

---

## Timeline Summary

| Phase | Duration | Completion Date |
|-------|----------|----------------|
| 1. Research & Architecture | 2-3 days | Day 3 |
| 2. Core Components | 5-7 days | Day 10 |
| 3. Integration | 3-4 days | Day 14 |
| 4. Testing & Validation | 3-5 days | Day 19 |
| 5. Documentation & Deployment | 2 days | Day 21 |
| **Total** | **15-21 days** | **~3 weeks** |

---

## Next Immediate Steps

**TODAY**:
1. ✅ Read this TODO document
2. ✅ Review CEA-608 standard basics (Wikipedia, sample code)
3. ✅ Start Task 1.1: Study CEA-608 format

**THIS WEEK**:
1. Complete Phase 1 (Research)
2. Begin Task 2.1: CEA-608 encoder implementation
3. Set up test environment with sample video packets

**THIS SPRINT**:
1. Complete Phases 1-2 (Research + Core Components)
2. Have working CEA-608 encoder and SEI builder
3. First integration test (inject caption into sample FLV packet)

---

## Questions for Discussion

1. **Wowza Configuration**: Do we have access to Wowza REST API to verify CEA-608 passthrough settings?
2. **Decompiling Java Code**: Permission to decompile `wse-plugin-caption-handlers-1.1.0.jar`?
3. **Test Resources**: Can we create small test FLV files with known video frames for testing?
4. **Performance Budget**: What's the acceptable CPU/latency overhead for captioning?
5. **Fallback Strategy**: If CEA-608 fails, should we maintain VTT system as backup?

---

## Resources & References

### Standards & Specifications
- [EIA-608 (CEA-608) Standard](https://en.wikipedia.org/wiki/EIA-608)
- [ATSC A/53: Digital Television Standard](https://www.atsc.org/atsc-documents/a53-atsc-digital-television-standard/)
- [ITU-T H.264: Advanced Video Coding](https://www.itu.int/rec/T-REC-H.264)
- [SMPTE 334M: Vertical Ancillary Data Mapping](https://ieeexplore.ieee.org/document/7291706)

### Example Implementations
- [Wowza Caption Handlers (Java)](https://www.wowza.com/developer-ai-subtitles)
- [FFmpeg CEA-608 Parser](https://github.com/FFmpeg/FFmpeg/blob/master/libavcodec/eia608.c)
- [libcaption (C library)](https://github.com/szatmary/libcaption)

### Tools
- **FFprobe**: Inspect SEI messages in video streams
- **Hex Workshop**: View binary FLV/H.264 data
- **mpv/VLC**: Test caption playback
- **Wireshark**: Debug RTMP streams

---

**Document Version**: 1.0  
**Last Updated**: November 30, 2025  
**Author**: GitHub Copilot (AI Assistant)  
**Status**: 📋 Ready for Implementation
