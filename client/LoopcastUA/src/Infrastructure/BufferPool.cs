using System.Collections.Concurrent;

namespace LoopcastUA.Infrastructure
{
    internal sealed class BufferPool
    {
        private readonly ConcurrentStack<byte[]> _pool = new ConcurrentStack<byte[]>();
        private readonly int _bufferSize;

        public BufferPool(int bufferSize)
        {
            _bufferSize = bufferSize;
        }

        public byte[] Rent()
        {
            if (_pool.TryPop(out var buf))
                return buf;
            return new byte[_bufferSize];
        }

        public void Return(byte[] buffer)
        {
            if (buffer != null && buffer.Length == _bufferSize)
                _pool.Push(buffer);
        }

        public int BufferSize => _bufferSize;
    }
}
