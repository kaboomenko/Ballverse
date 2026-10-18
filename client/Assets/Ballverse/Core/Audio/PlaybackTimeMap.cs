using System;
using System.Collections.Generic;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Simulation tick → output frame under a piecewise-constant playback speed: realtime timeScale 0.25–3× changed while
    /// running (spec §2.2), studio speed S 0.5–2× and the finale slow-mo (spec §3.4). Within a segment that starts at tick
    /// t₀ / frame f₀ with speed s: frame(t) = f₀ + round((t − t₀) · 800 / s), halves rounded up; a new segment starts at
    /// the frame the previous one gives for its first tick, so the map is continuous and non-decreasing. Ticks before
    /// the origin extrapolate the first segment. Pure IEEE double arithmetic: deterministic on every platform.
    /// <para>Realtime use: the simulation-side adapter keeps one map (origin = the mixer <see cref="EventMixer.Position"/>
    /// when the run started plus a fixed scheduling latency), calls <see cref="SetSpeed"/> when timeScale changes and
    /// enqueues each event with <c>TryEnqueue(e, map.FrameOfTick(e.Tick))</c>, so sound stays in step with the picture
    /// at any speed. Offline use: build the same map for the clip and render <see cref="ScheduledSoundEvent"/>s.</para>
    /// Not thread-safe (one producer).
    /// </summary>
    public sealed class PlaybackTimeMap
    {
        /// <summary>Slowest supported speed (covers S 0.5× × slow-mo 0.25×).</summary>
        public const double MinSpeed = 0.0625;

        /// <summary>Fastest supported speed (covers timeScale 3× × S 2×).</summary>
        public const double MaxSpeed = 16.0;

        readonly List<int> _ticks = new List<int>();
        readonly List<long> _frames = new List<long>();
        readonly List<double> _speeds = new List<double>();

        /// <param name="originTick">Tick placed at <paramref name="originFrame"/>.</param>
        /// <param name="originFrame">Output frame of <paramref name="originTick"/>.</param>
        /// <param name="speed">Initial playback speed (1 = real time).</param>
        public PlaybackTimeMap(int originTick = 0, long originFrame = 0, double speed = 1.0)
        {
            CheckSpeed(speed);
            _ticks.Add(originTick);
            _frames.Add(originFrame);
            _speeds.Add(speed);
        }

        public int SegmentCount => _ticks.Count;

        /// <summary>Tick where the last segment starts.</summary>
        public int LastSegmentTick => _ticks[_ticks.Count - 1];

        /// <summary>
        /// Plays ticks from <paramref name="fromTick"/> on at <paramref name="speed"/>. <paramref name="fromTick"/> must not be
        /// before the last segment's start; the same tick replaces that segment's speed.
        /// </summary>
        public void SetSpeed(int fromTick, double speed)
        {
            CheckSpeed(speed);
            int last = _ticks.Count - 1;
            if (fromTick < _ticks[last]) throw new ArgumentOutOfRangeException(nameof(fromTick), "segments must be added in tick order");
            if (fromTick == _ticks[last])
            {
                _speeds[last] = speed;
                return;
            }
            long frame = FrameIn(last, fromTick);
            _ticks.Add(fromTick);
            _frames.Add(frame);
            _speeds.Add(speed);
        }

        /// <summary>Output frame where <paramref name="tick"/> starts.</summary>
        public long FrameOfTick(int tick)
        {
            int lo = 0, hi = _ticks.Count - 1;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) >> 1;
                if (_ticks[mid] <= tick) lo = mid;
                else hi = mid - 1;
            }
            return FrameIn(lo, tick);
        }

        long FrameIn(int segment, int tick)
        {
            double offset = ((double)tick - _ticks[segment]) * AudioConstants.FramesPerTick / _speeds[segment];
            return _frames[segment] + (long)Math.Floor(offset + 0.5);
        }

        static void CheckSpeed(double speed)
        {
            if (double.IsNaN(speed) || speed < MinSpeed || speed > MaxSpeed)
                throw new ArgumentOutOfRangeException(nameof(speed), "speed must be within 0.0625…16");
        }
    }
}
