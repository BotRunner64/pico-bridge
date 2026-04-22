using System;
using System.Text;

namespace PicoBridge.Network
{
    /// <summary>
    /// Pack/Unpack for the binary protocol.
    /// </summary>
    public static class PackageHandle
    {
        public static byte[] Pack(byte cmd, byte[] data = null)
        {
            data ??= Array.Empty<byte>();
            long ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            int total = NetCMD.DEFAULT_PACKAGE_SIZE + data.Length;
            var buf = new byte[total];
            int pos = 0;

            buf[pos++] = NetCMD.HEAD_VR_TO_PC;
            buf[pos++] = cmd;

            // LEN (4 bytes LE)
            int len = data.Length;
            buf[pos++] = (byte)(len & 0xFF);
            buf[pos++] = (byte)((len >> 8) & 0xFF);
            buf[pos++] = (byte)((len >> 16) & 0xFF);
            buf[pos++] = (byte)((len >> 24) & 0xFF);

            // DATA
            if (data.Length > 0)
                Array.Copy(data, 0, buf, pos, data.Length);
            pos += data.Length;

            // TIMESTAMP (8 bytes LE)
            buf[pos++] = (byte)(ts & 0xFF);
            buf[pos++] = (byte)((ts >> 8) & 0xFF);
            buf[pos++] = (byte)((ts >> 16) & 0xFF);
            buf[pos++] = (byte)((ts >> 24) & 0xFF);
            buf[pos++] = (byte)((ts >> 32) & 0xFF);
            buf[pos++] = (byte)((ts >> 40) & 0xFF);
            buf[pos++] = (byte)((ts >> 48) & 0xFF);
            buf[pos++] = (byte)((ts >> 56) & 0xFF);

            buf[pos] = NetCMD.END_BYTE;
            return buf;
        }

        /// <summary>
        /// Try to unpack one packet from the ByteBuffer.
        /// Returns null if not enough data.
        /// </summary>
        public static NetPacket Unpack(ByteBuffer buffer)
        {
            // Scan for valid HEAD
            while (buffer.ReadableBytes > 0)
            {
                byte head = buffer.PeekByte();
                if (head == NetCMD.HEAD_VR_TO_PC || head == NetCMD.HEAD_PC_TO_VR)
                    break;
                buffer.ReadByte(); // discard garbage
            }

            if (buffer.ReadableBytes < 6) // HEAD + CMD + LEN
                return null;

            int dataLen = buffer.PeekInt(2);
            if (dataLen < 0 || dataLen > NetCMD.MAX_PAYLOAD_SIZE)
            {
                buffer.ReadByte();
                buffer.DiscardReadBytes();
                return Unpack(buffer);
            }

            int totalLen = NetCMD.DEFAULT_PACKAGE_SIZE + dataLen;
            if (totalLen < NetCMD.DEFAULT_PACKAGE_SIZE)
            {
                buffer.ReadByte();
                buffer.DiscardReadBytes();
                return Unpack(buffer);
            }

            if (buffer.ReadableBytes < totalLen)
                return null;

            // Validate END byte
            if (buffer.PeekByte(totalLen - 1) != NetCMD.END_BYTE)
            {
                buffer.ReadByte(); // discard bad HEAD, rescan
                buffer.DiscardReadBytes();
                return Unpack(buffer);
            }

            var pkt = new NetPacket();
            pkt.Head = buffer.ReadByte();
            pkt.Cmd = buffer.ReadByte();
            buffer.ReadInt(); // skip LEN (already read)

            pkt.Data = new byte[dataLen];
            if (dataLen > 0)
                buffer.ReadBytes(pkt.Data, 0, dataLen);

            pkt.Timestamp = buffer.ReadLong();
            buffer.ReadByte(); // END

            buffer.DiscardReadBytes();
            return pkt;
        }
    }
}
