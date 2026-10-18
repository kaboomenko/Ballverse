using System;
using System.Threading;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Bounded single-producer / single-consumer lock-free FIFO of <see cref="SoundEvent"/> (spec §7.8: "events from the
    /// simulation → lock-free queue → mixer in OnAudioFilterRead"). One thread enqueues (simulation), one thread peeks and
    /// dequeues (audio). Allocation-free after construction. A full queue refuses the event (counted in <see cref="Dropped"/>).
    /// </summary>
    public sealed class SoundEventQueue
    {
        readonly SoundEvent[] _items;
        readonly int _mask;
        int _head; // next slot to read; written only by the consumer
        int _tail; // next slot to write; written only by the producer
        int _dropped;

        /// <summary>Capacity is rounded up to a power of two (minimum 2).</summary>
        public SoundEventQueue(int capacity = 4096)
        {
            if (capacity <= 0 || capacity > (1 << 24)) throw new ArgumentOutOfRangeException(nameof(capacity));
            int size = 2;
            while (size < capacity) size <<= 1;
            _items = new SoundEvent[size];
            _mask = size - 1;
        }

        public int Capacity => _items.Length;

        /// <summary>Approximate number of queued events (exact when called from either side while the other is idle).</summary>
        public int Count => Volatile.Read(ref _tail) - Volatile.Read(ref _head);

        /// <summary>Events refused because the queue was full.</summary>
        public int Dropped => Volatile.Read(ref _dropped);

        /// <summary>Producer side. Returns false (and counts a drop) when full.</summary>
        public bool TryEnqueue(in SoundEvent e)
        {
            int tail = _tail;
            if (tail - Volatile.Read(ref _head) >= _items.Length)
            {
                Interlocked.Increment(ref _dropped);
                return false;
            }
            _items[tail & _mask] = e;
            Volatile.Write(ref _tail, tail + 1);
            return true;
        }

        /// <summary>Consumer side: the oldest event without removing it.</summary>
        public bool TryPeek(out SoundEvent e)
        {
            int head = _head;
            if (head == Volatile.Read(ref _tail))
            {
                e = default;
                return false;
            }
            e = _items[head & _mask];
            return true;
        }

        /// <summary>Consumer side: removes and returns the oldest event.</summary>
        public bool TryDequeue(out SoundEvent e)
        {
            if (!TryPeek(out e)) return false;
            Volatile.Write(ref _head, _head + 1);
            return true;
        }

        /// <summary>Consumer side: discards everything queued so far.</summary>
        public void Clear()
        {
            Volatile.Write(ref _head, Volatile.Read(ref _tail));
        }
    }
}
