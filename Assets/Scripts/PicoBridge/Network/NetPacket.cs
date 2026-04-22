namespace PicoBridge.Network
{
    /// <summary>
    /// Parsed packet from the wire.
    /// </summary>
    public class NetPacket
    {
        public byte Head;
        public byte Cmd;
        public byte[] Data;
        public long Timestamp;
    }
}
