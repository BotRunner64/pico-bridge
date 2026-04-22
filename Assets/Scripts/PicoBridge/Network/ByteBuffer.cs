namespace PicoBridge.Network
{
    /// <summary>
    /// Ring buffer for TCP stream accumulation.
    /// Simplified from XRoboToolkit ByteBuffer.
    /// </summary>
    public class ByteBuffer
    {
        private byte[] _buf;
        private int _readPos;
        private int _writePos;

        public ByteBuffer(int capacity = 65536)
        {
            _buf = new byte[capacity];
        }

        public int ReadableBytes => _writePos - _readPos;

        public void WriteBytes(byte[] data, int offset, int count)
        {
            EnsureCapacity(count);
            System.Array.Copy(data, offset, _buf, _writePos, count);
            _writePos += count;
        }

        public byte ReadByte()
        {
            return _buf[_readPos++];
        }

        public byte PeekByte(int offset = 0)
        {
            return _buf[_readPos + offset];
        }

        public int ReadInt()
        {
            int val = _buf[_readPos]
                    | (_buf[_readPos + 1] << 8)
                    | (_buf[_readPos + 2] << 16)
                    | (_buf[_readPos + 3] << 24);
            _readPos += 4;
            return val;
        }

        public int PeekInt(int offset = 0)
        {
            int pos = _readPos + offset;
            return _buf[pos]
                 | (_buf[pos + 1] << 8)
                 | (_buf[pos + 2] << 16)
                 | (_buf[pos + 3] << 24);
        }

        public long ReadLong()
        {
            long val = (long)_buf[_readPos]
                     | ((long)_buf[_readPos + 1] << 8)
                     | ((long)_buf[_readPos + 2] << 16)
                     | ((long)_buf[_readPos + 3] << 24)
                     | ((long)_buf[_readPos + 4] << 32)
                     | ((long)_buf[_readPos + 5] << 40)
                     | ((long)_buf[_readPos + 6] << 48)
                     | ((long)_buf[_readPos + 7] << 56);
            _readPos += 8;
            return val;
        }

        public void ReadBytes(byte[] dst, int offset, int count)
        {
            System.Array.Copy(_buf, _readPos, dst, offset, count);
            _readPos += count;
        }

        public void DiscardReadBytes()
        {
            if (_readPos == 0) return;
            int remaining = _writePos - _readPos;
            if (remaining > 0)
                System.Array.Copy(_buf, _readPos, _buf, 0, remaining);
            _writePos = remaining;
            _readPos = 0;
        }

        public void Clear()
        {
            _readPos = 0;
            _writePos = 0;
        }

        private void EnsureCapacity(int additional)
        {
            int needed = _writePos + additional;
            if (needed <= _buf.Length) return;
            int newSize = _buf.Length;
            while (newSize < needed) newSize *= 2;
            var newBuf = new byte[newSize];
            System.Array.Copy(_buf, 0, newBuf, 0, _writePos);
            _buf = newBuf;
        }
    }
}
