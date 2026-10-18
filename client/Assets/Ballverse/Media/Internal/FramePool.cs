using System;
using System.Collections.Generic;
using System.Threading;

namespace Ballverse.Media.Internal
{
    /// <summary>
    /// Fixed-capacity pool of frame buffers (capacity 3 per reader: at most 3 frames in flight). Buffers are created
    /// lazily by the reader's worker thread (so Android can allocate Java buffers on an attached thread) and returned
    /// from any thread when a <see cref="VideoFrame"/> is disposed or finalized. After <see cref="Dispose"/>, returned
    /// buffers are released instead of pooled, so a frame held past its reader's lifetime stays valid until disposed.
    /// </summary>
    internal sealed class FramePool
    {
        readonly object _lock = new object();
        readonly Stack<IFrameMemory> _free = new Stack<IFrameMemory>();
        readonly int _capacity;
        int _created;
        bool _disposed;

        public FramePool(int capacity)
        {
            _capacity = capacity;
        }

        public int Capacity => _capacity;

        /// <summary>
        /// Takes a free buffer, creating one with <paramref name="factory"/> while under capacity; otherwise waits until a
        /// buffer is returned or <paramref name="shouldStop"/> becomes true (re-checked every 50 ms). Returns null if stopped.
        /// </summary>
        public IFrameMemory Acquire(Func<IFrameMemory> factory, Func<bool> shouldStop)
        {
            bool create = false;
            lock (_lock)
            {
                while (true)
                {
                    if (_disposed || shouldStop()) return null;
                    if (_free.Count > 0) return _free.Pop();
                    if (_created < _capacity)
                    {
                        _created++;
                        create = true;
                        break;
                    }
                    Monitor.Wait(_lock, 50);
                }
            }
            if (create)
            {
                try
                {
                    return factory();
                }
                catch
                {
                    lock (_lock) _created--;
                    throw;
                }
            }
            return null;
        }

        /// <summary>Returns a buffer; any thread.</summary>
        public void Return(IFrameMemory memory)
        {
            if (memory == null) return;
            bool release;
            lock (_lock)
            {
                release = _disposed;
                if (!release)
                {
                    _free.Push(memory);
                    Monitor.PulseAll(_lock);
                }
                else
                {
                    _created--;
                }
            }
            if (release) memory.Release();
        }

        /// <summary>Releases pooled buffers; outstanding ones are released when returned.</summary>
        public void Dispose()
        {
            IFrameMemory[] free;
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
                free = _free.ToArray();
                _created -= free.Length;
                _free.Clear();
                Monitor.PulseAll(_lock);
            }
            foreach (IFrameMemory m in free) m.Release();
        }
    }
}
