using System;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// "At most N per sliding window" counter (spec §2.7: 40 sounds/s overall, 20 melody notes/s).
    /// An acquisition at time t succeeds when fewer than N acquisitions lie in the half-open window (t − W, t]; so a new
    /// one exactly W frames after the oldest of the last N succeeds. Times must not decrease; an earlier time is treated
    /// as the latest time seen. Allocation-free after construction.
    /// </summary>
    public sealed class SlidingRateWindow
    {
        readonly long[] _times;
        readonly long _windowFrames;
        int _head;
        int _count;
        long _latest = long.MinValue;

        public SlidingRateWindow(int maxPerWindow, long windowFrames)
        {
            if (maxPerWindow <= 0) throw new ArgumentOutOfRangeException(nameof(maxPerWindow));
            if (windowFrames <= 0) throw new ArgumentOutOfRangeException(nameof(windowFrames));
            _times = new long[maxPerWindow];
            _windowFrames = windowFrames;
        }

        public int MaxPerWindow => _times.Length;
        public long WindowFrames => _windowFrames;

        /// <summary>Would an acquisition at <paramref name="frame"/> succeed? Does not change state.</summary>
        public bool CanAcquire(long frame)
        {
            if (frame < _latest) frame = _latest;
            if (_count < _times.Length) return true;
            return frame - _times[_head] >= _windowFrames;
        }

        /// <summary>Records an acquisition at <paramref name="frame"/> if the limit allows it.</summary>
        public bool TryAcquire(long frame)
        {
            if (frame < _latest) frame = _latest;
            if (_count < _times.Length)
            {
                _times[(_head + _count) % _times.Length] = frame;
                _count++;
                _latest = frame;
                return true;
            }
            if (frame - _times[_head] < _windowFrames) return false;
            _times[_head] = frame;
            _head = (_head + 1) % _times.Length;
            _latest = frame;
            return true;
        }

        public void Reset()
        {
            _head = 0;
            _count = 0;
            _latest = long.MinValue;
        }
    }
}
