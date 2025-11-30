#!/usr/bin/env fish

# CEA-608 Caption Extraction from HLS Stream
# This script extracts and displays embedded CEA-608 captions from HLS segments

echo "🎬 CEA-608 Caption Extractor"
echo "================================"

# Get latest HLS segments
echo "📡 Fetching latest HLS segments..."
set segments (curl -s "http://localhost:5032/hls/OBSstream_delayed/chunklist_w1253677526.m3u8" | grep "\.ts" | tail -5)

if test (count $segments) -eq 0
    echo "❌ No HLS segments found. Is the stream running?"
    exit 1
end

echo "✓ Found "(count $segments)" segments"
echo ""

# Function to extract CEA-608 text from hex dump
function extract_cea608_text
    set segment_url $argv[1]
    
    # Download segment and search for CEA-608 identifier
    set hex_data (curl -s "$segment_url" | hexdump -C | grep -A 10 "b5 00 31")
    
    if test -z "$hex_data"
        return 1
    end
    
    # Extract hex bytes containing caption text (after fc markers)
    # This is a simplified extraction - real parsing would decode the full structure
    echo "$hex_data" | string match -r '[0-9a-f]{2}\s+fc' | string replace -r '^.* ' '' | string trim
    return 0
end

# Check each segment
for segment in $segments
    echo "🔍 Checking: $segment"
    set url "http://localhost:5032$segment"
    
    # Look for ATSC identifier in hex dump
    set has_captions (curl -s "$url" | hexdump -C | grep -c "b5 00 31")
    
    if test $has_captions -gt 0
        echo "✅ CEA-608 captions found!"
        echo ""
        echo "   Hex dump extract:"
        curl -s "$url" | hexdump -C | grep -B 1 -A 5 "b5 00 31" | head -8
        echo ""
        
        # Try to decode visible ASCII text
        echo "   Decoded text preview:"
        set text_bytes (curl -s "$url" | hexdump -C | grep -A 4 "b5 00 31" | grep "fc" | string replace -ra '\|.*' '' | string replace -ra '[0-9a-f]{8}\s+' '' | string trim)
        
        # Extract printable ASCII (basic decoding)
        for line in $text_bytes
            set ascii_text (echo $line | string replace -ra 'fc [0-9a-f]{2}' '' | xxd -r -p 2>/dev/null | string collect)
            if test -n "$ascii_text"
                echo "   → $ascii_text"
            end
        end
        echo ""
    else
        echo "⚪ No captions in this segment"
    end
    echo "---"
end

echo ""
echo "✅ Caption extraction complete!"
echo ""
echo "📊 Summary:"
echo "- Captions are embedded as CEA-608 in H.264 SEI NAL units"
echo "- Format: ATSC A/53 Part 4 (identifier: 0xB5 0x00 0x31 'GA94')"
echo "- Each caption adds +79 bytes to video keyframes"
echo "- Text is encoded with 0xFC markers between character pairs"
echo ""
echo "💡 Note: Browser players don't support CEA-608 from HLS."
echo "   Use WebVTT (already working) for browser display."
