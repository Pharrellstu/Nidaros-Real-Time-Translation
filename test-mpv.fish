#!/usr/bin/env fish

# Test MPV with subtitles
# Make sure OBS is streaming to rtmp://localhost:1935/live with key "OBSstream"

echo "🎬 Testing MPV with Live Subtitles"
echo ""
echo "Stream URL: http://localhost:5032/hls/OBSstream/playlist.m3u8"
echo "Subtitles:  http://localhost:5032/vtt/OBSstream.vtt"
echo ""
echo "Controls:"
echo "  v - Toggle subtitle visibility"
echo "  z/x - Adjust subtitle delay (+/- 0.1s)"
echo "  q - Quit"
echo ""
echo "Starting MPV in 3 seconds..."
sleep 3

mpv http://localhost:5032/hls/OBSstream/playlist.m3u8 \
    --sub-file=http://localhost:5032/vtt/OBSstream.vtt \
    --sub-delay=3 \
    --cache=yes \
    --force-seekable=yes
