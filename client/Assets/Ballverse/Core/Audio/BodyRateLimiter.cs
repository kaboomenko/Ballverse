using System;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// "At most one sound per body per 50 ms" (spec §2.7). A sound at time t is refused when the same body had an accepted
    /// sound at t' with t − t' &lt; 2400 frames; exactly 50 ms later is allowed. Negative body ids are exempt.
    /// Keeps only the last <c>capacity</c> accepted sounds: because accepted sounds are themselves limited (40/s overall +
    /// 20 melody notes/s), 60 entries always cover the last 50 ms, so no per-body dictionary is needed and nothing
    /// allocates on the audio thread.
    /// </summary>
    public sealed class BodyRateLimiter
    {
        readonly long[] _frames;
        readonly int[] _bodies;
        readonly long _minInterval;
        int _next;
        int _count;

        public BodyRateLimiter(int capacity = AudioConstants.MaxSoundsPerWindow + AudioConstants.MaxMelodyNotesPerWindow,
            long minIntervalFrames = AudioConstants.PerBodyMinIntervalFrames)
        {
            if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
            if (minIntervalFrames <= 0) throw new ArgumentOutOfRangeException(nameof(minIntervalFrames));
            _frames = new long[capacity];
            _bodies = new int[capacity];
            _minInterval = minIntervalFrames;
        }

        /// <summary>True when <paramref name="bodyId"/> sounded less than 50 ms before <paramref name="frame"/>.</summary>
        public bool IsLimited(int bodyId, long frame)
        {
            if (bodyId < 0) return false;
            for (int i = 0; i < _count; i++)
            {
                if (_bodies[i] == bodyId && frame - _frames[i] < _minInterval) return true;
            }
            return false;
        }

        /// <summary>Remembers an accepted sound.</summary>
        public void Record(int bodyId, long frame)
        {
            if (bodyId < 0) return;
            _frames[_next] = frame;
            _bodies[_next] = bodyId;
            _next = (_next + 1) % _frames.Length;
            if (_count < _frames.Length) _count++;
        }

        public void Reset()
        {
            _next = 0;
            _count = 0;
        }
    }
}
