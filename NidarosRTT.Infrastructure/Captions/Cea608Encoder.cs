using System;
using System.Collections.Generic;
using System.Linq;

namespace NidarosRTT.Infrastructure.Captions
{
    /// <summary>
    /// Encodes text captions into CEA-608 closed caption format
    /// Reference: EIA-608 standard for closed captioning
    /// </summary>
    public class Cea608Encoder
    {
        // CEA-608 Control Codes (Channel 1)
        private const byte CONTROL_CODE_BASE = 0x14;
        private const byte RESUME_CAPTION_LOADING = 0x20;  // RCL - Start caption
        private const byte END_OF_CAPTION = 0x2F;          // EOC - Display caption
        private const byte ERASE_DISPLAYED_MEMORY = 0x2C;  // EDM - Clear screen
        
        // Caption positioning constraints
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
            packets.AddRange(CreateControlCode(CONTROL_CODE_BASE, ERASE_DISPLAYED_MEMORY));
            
            // 2. Position cursor (PAC - Preamble Address Code)
            var pacCode = GetPacForLine(lineNumber);
            packets.AddRange(pacCode);
            
            // 3. Resume Caption Loading (RCL)
            packets.AddRange(CreateControlCode(CONTROL_CODE_BASE, RESUME_CAPTION_LOADING));
            
            // 4. Encode text characters (2 bytes per packet)
            var cleanText = SanitizeText(text);
            for (int i = 0; i < cleanText.Length; i += 2)
            {
                var byte1 = (byte)cleanText[i];
                var byte2 = (i + 1 < cleanText.Length) ? (byte)cleanText[i + 1] : (byte)0x80;
                packets.AddRange(CreateDataPacket(byte1, byte2));
            }
            
            // 5. End Of Caption (EOC) - Display caption
            packets.AddRange(CreateControlCode(CONTROL_CODE_BASE, END_OF_CAPTION));
            
            return packets.ToArray();
        }
        
        /// <summary>
        /// Create a 3-byte cc_data packet
        /// CEA-708 structure: [reserved(2) | cc_valid(1) | cc_type(2) | reserved(3)]
        /// cc_type: 00=CEA-608 Field1, 01=CEA-608 Field2, 10=DTVCC Data, 11=DTVCC Start
        /// </summary>
        private byte[] CreateDataPacket(byte data1, byte data2)
        {
            return new byte[]
            {
                0xF8,  // 0b11111000: reserved=11, cc_valid=1, cc_type=00 (CEA-608 Field 1)
                data1,
                data2
            };
        }
        
        /// <summary>
        /// Create control code packet (PAC, EDM, EOC, etc.)
        /// </summary>
        private byte[] CreateControlCode(byte code1, byte code2)
        {
            return new byte[] { 0xF8, code1, code2 };  // CEA-608 Field 1
        }
        
        /// <summary>
        /// Get Preamble Address Code for line positioning
        /// CEA-608 row numbering (rows 11-14 typically used for pop-up captions)
        /// </summary>
        private byte[] GetPacForLine(int lineNumber)
        {
            // PAC codes for rows 1-4 (displayed bottom to top)
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
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;
            
            // Remove non-ASCII characters (CEA-608 basic set is ASCII 0x20-0x7F)
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
        /// Breaks on word boundaries when possible
        /// </summary>
        private List<string> WrapText(string text, int maxLength)
        {
            var lines = new List<string>();
            
            if (string.IsNullOrWhiteSpace(text))
                return lines;
            
            var words = text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var currentLine = "";
            
            foreach (var word in words)
            {
                // Check if adding this word would exceed max length
                var testLine = string.IsNullOrEmpty(currentLine) ? word : currentLine + " " + word;
                
                if (testLine.Length > maxLength)
                {
                    // Line is full, save it and start new line
                    if (!string.IsNullOrEmpty(currentLine))
                    {
                        lines.Add(currentLine.Trim());
                        currentLine = word;
                    }
                    else
                    {
                        // Single word exceeds max length, truncate it
                        lines.Add(word.Substring(0, Math.Min(word.Length, maxLength)));
                        currentLine = "";
                    }
                }
                else
                {
                    currentLine = testLine;
                }
            }
            
            // Add remaining text
            if (!string.IsNullOrEmpty(currentLine))
                lines.Add(currentLine.Trim());
            
            return lines;
        }
        
        /// <summary>
        /// Get the total number of bytes for encoded caption
        /// Useful for size estimation
        /// </summary>
        public int GetEncodedSize(string text)
        {
            var cleanText = SanitizeText(text);
            
            // Control codes: EDM (3) + PAC (3) + RCL (3) + EOC (3) = 12 bytes
            // Text data: 3 bytes per 2 characters
            var textBytes = (cleanText.Length + 1) / 2 * 3;
            
            return 12 + textBytes;
        }
    }
}
