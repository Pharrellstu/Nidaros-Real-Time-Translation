namespace NidarosRTT.Infrastructure
{
    /// <summary>
    /// Represents a media packet from the stream with timing information.
    /// Used for 30-second buffering and delayed re-ingestion.
    /// </summary>
    public class MediaPacket
    {
        /// <summary>
        /// Packet type: video, audio, or data
        /// </summary>
        public MediaPacketType Type { get; set; }

        /// <summary>
        /// Absolute timecode in milliseconds (from stream)
        /// </summary>
        public long Timecode { get; set; }

        /// <summary>
        /// Raw packet data (FLV or other format)
        /// </summary>
        public byte[] Data { get; set; }

        /// <summary>
        /// Wall-clock time when packet was received (for debugging)
        /// </summary>
        public DateTime ReceivedAt { get; set; }

        public MediaPacket(MediaPacketType type, long timecode, byte[] data)
        {
            Type = type;
            Timecode = timecode;
            Data = data;
            ReceivedAt = DateTime.UtcNow;
        }
    }

    public enum MediaPacketType
    {
        Video,
        Audio,
        Data
    }
}
