using System;

namespace Ballverse.Core.Timeline
{
    /// <summary>
    /// Compiled time mapping of a <see cref="TimelineSpec"/> (spec §3.4, §3.7, §7.5): output frame count and, for every
    /// output frame or preview time, which simulation tick and which user-video pts to show. Immutable and thread-safe.
    /// <para>
    /// <b>Output time.</b> Output time 0 is the clip start. Frame i shows output time t = i / fps exactly (spec §7.5);
    /// all arithmetic is integer. Internally output time is counted in units of 1 / (60 · p · f) s, where p is the speed
    /// and f the slow-mo factor in hundredths, so every boundary below is an integer and nothing accumulates.
    /// </para>
    /// <para>
    /// <b>Clip output length.</b> Let L be the trimmed clip length in ticks, S = min(slow-mo ticks, L) and N = L − S.
    /// The first N clip ticks play at speed p/100 and the last S ticks at p/100 · f/100, so the clip lasts
    /// N / (60 · p/100) + S / (60 · p·f/10000) seconds on the output timeline. A sample at output time t shows clip tick
    /// trimIn + ⌊clip time⌋ (floor: the tick whose interval contains t), so at 1.0x consecutive 60 fps frames show
    /// consecutive ticks and 30 fps frames show every 2nd tick.
    /// </para>
    /// <para>
    /// <b>Duration</b> (spec §3.4, §3.8): Auto = clip output length; MatchVideo = output time where the trimmed video
    /// ends (<see cref="UserVideoTiming.EndOnOutputUs"/>, so the whole visible video plays; falls back to Auto without a
    /// video); Manual = the given length. The frame count is ⌈duration · fps⌉ clamped to [fps, 180 · fps], i.e. the
    /// duration is clamped to [1 s, 180 s] and the last partial frame is kept.
    /// </para>
    /// </summary>
    public sealed class StudioTimeline
    {
        public const long MicrosPerSecond = 1000000L;
        public const int MinDurationSeconds = 1;
        /// <summary>Longest accepted preview time for the µs-based sampling methods.</summary>
        public const long MaxSampleTimeUs = SimConstants.MaxDurationSeconds * MicrosPerSecond;

        readonly ClipTiming _clip;
        readonly UserVideoTiming _video;
        readonly int _fps;
        readonly long _speed;            // p, hundredths
        readonly long _factor;           // f, hundredths
        readonly long _normalTicks;      // N
        readonly long _slowTicks;        // S
        readonly long _unitsPerSecond;   // 60 · p · f
        readonly long _unitsPerFrame;    // 60 · p · f / fps
        readonly long _unitsPerNormalTick;  // 100 · f
        readonly long _unitsPerSlowTick;    // 10000
        readonly long _normalEndUnits;   // N · 100 · f
        readonly long _clipEndUnits;     // normal end + S · 10000
        readonly long _replayTicks;      // R = min(120, L)
        readonly long _replayPeriodUnits;   // 2R ticks of output = 2R · p · f units
        readonly long _unitsPerReplayTick;  // 2 · p · f
        readonly int _frameCount;
        readonly int _clipFrameCount;

        public TimelineSpec Spec { get; }
        public int Fps => _fps;

        /// <summary>Total output frames (spec §3.8: 1..180 s). Frame i covers output time [i / fps, (i + 1) / fps).</summary>
        public int FrameCount => _frameCount;

        /// <summary>Output duration = FrameCount / fps, rounded to the nearest µs.</summary>
        public long DurationUs => (2L * _frameCount * MicrosPerSecond + _fps) / (2L * _fps);

        /// <summary>Output frames during which the first pass of the clip plays (⌈clip output length · fps⌉, not clamped).</summary>
        public int ClipFrameCount => _clipFrameCount;

        /// <summary>Exact clip output length rounded up to whole µs.</summary>
        public long ClipOutputDurationUs => CeilDiv(_clipEndUnits * MicrosPerSecond, _unitsPerSecond);

        public StudioTimeline(TimelineSpec spec)
        {
            Spec = spec ?? throw new ArgumentNullException(nameof(spec));
            _clip = spec.Clip;
            _video = spec.Video;
            _fps = spec.Fps;

            _speed = _clip.SpeedPercent;
            _factor = _clip.SlowMoFactorPercent;
            _slowTicks = _clip.EffectiveSlowMoTicks;
            _normalTicks = _clip.LengthTicks - _slowTicks;

            _unitsPerSecond = SimConstants.TickRate * _speed * _factor;
            _unitsPerFrame = _unitsPerSecond / _fps; // exact: 60 is divisible by 30 and 60
            _unitsPerNormalTick = 100L * _factor;
            _unitsPerSlowTick = 10000L;
            _normalEndUnits = _normalTicks * _unitsPerNormalTick;
            _clipEndUnits = _normalEndUnits + _slowTicks * _unitsPerSlowTick;

            _replayTicks = Math.Min(ClipTiming.SlowMoReplayTicks, _clip.LengthTicks);
            // Output units per replayed tick at absolute speed r%: 100 · p · f / r.
            _unitsPerReplayTick = 100L * _speed * _factor / ClipTiming.SlowMoReplaySpeedPercent;
            _replayPeriodUnits = _replayTicks * _unitsPerReplayTick;

            _clipFrameCount = (int)CeilDiv(_clipEndUnits, _unitsPerFrame);
            _frameCount = ComputeFrameCount();
        }

        public StudioTimeline(ClipTiming clip, UserVideoTiming video, DurationMode durationMode, int fps, long manualDurationUs = 0)
            : this(new TimelineSpec(clip, video, durationMode, fps, manualDurationUs))
        {
        }

        int ComputeFrameCount()
        {
            long frames;
            switch (Spec.DurationMode)
            {
                case DurationMode.MatchVideo:
                    frames = _video != null ? CeilDivNonNegative(_video.EndOnOutputUs * _fps, MicrosPerSecond) : _clipFrameCount;
                    break;
                case DurationMode.Manual:
                    frames = CeilDivNonNegative(Spec.ManualDurationUs * _fps, MicrosPerSecond);
                    break;
                default:
                    frames = _clipFrameCount;
                    break;
            }
            long min = (long)MinDurationSeconds * _fps;
            long max = (long)SimConstants.MaxDurationSeconds * _fps;
            if (frames < min) frames = min;
            if (frames > max) frames = max;
            return (int)frames;
        }

        /// <summary>
        /// Output start time of frame i, i·10⁶/fps rounded up to whole µs (presentation time for the encoder, spec §7.5
        /// step e). Rounding up keeps <c>SampleSimAtTimeUs(FrameTimeUs(i)) == SampleSim(i)</c> and
        /// <c>FrameAtTimeUs(FrameTimeUs(i)) == i</c>.
        /// </summary>
        public long FrameTimeUs(int frameIndex)
        {
            CheckFrame(frameIndex);
            return FrameStartUs(frameIndex);
        }

        /// <summary>Frame showing output time <paramref name="outputUs"/> (preview scrubbing, spec §3.7), clamped to [0, FrameCount − 1].</summary>
        public int FrameAtTimeUs(long outputUs)
        {
            if (outputUs <= 0) return 0;
            if (outputUs > MaxSampleTimeUs) return _frameCount - 1;
            long f = outputUs * _fps / MicrosPerSecond;
            return f >= _frameCount ? _frameCount - 1 : (int)f;
        }

        /// <summary>True while frame i lies in the first pass of the clip (before the <see cref="SimAfterEnd"/> behaviour starts).</summary>
        public bool IsClipPlaying(int frameIndex)
        {
            CheckFrame(frameIndex);
            return frameIndex < _clipFrameCount;
        }

        /// <summary>
        /// Simulation sample for output frame i (spec §3.4, §7.5 step a). During the clip: Playing(tick) with speed and
        /// finale slow-mo applied. After the clip, per <see cref="ClipTiming.AfterEnd"/>:
        /// <list type="bullet">
        /// <item><b>Freeze</b> — Frozen(last clip tick).</item>
        /// <item><b>Loop</b> — the clip repeats exactly as first played, finale slow-mo included (decision: a seamless
        /// repeat of what the viewer saw); the period is the clip output length; Iteration counts repeats.</item>
        /// <item><b>SlowMoReplay</b> — the last 2 s of the clip (whole clip if shorter) replay repeatedly at an absolute
        /// 0.5x of real time, independent of clip speed and slow-mo factor (decision); Iteration = replay number (1, 2, …).</item>
        /// <item><b>ContinueSim</b> — ticks past trimOut play on at the base clip speed (finale slow-mo ends with the clip,
        /// decision) up to <see cref="ClipTiming.AvailableTicks"/> − 1, then Frozen at that tick.</item>
        /// </list>
        /// <see cref="VideoAfterEnd.ExpandSim"/> changes only the layout; it never affects this result.
        /// </summary>
        public SimSample SampleSim(int frameIndex)
        {
            CheckFrame(frameIndex);
            return SampleSimUnits(frameIndex * _unitsPerFrame);
        }

        /// <summary>Simulation sample at an arbitrary output time (preview, spec §3.7). Same rules as <see cref="SampleSim"/>; frame i corresponds to <see cref="FrameTimeUs"/>.</summary>
        public SimSample SampleSimAtTimeUs(long outputUs)
        {
            CheckTimeUs(outputUs);
            // Flooring to whole units is exact: every boundary below is an integer number of units.
            return SampleSimUnits(outputUs * _unitsPerSecond / MicrosPerSecond);
        }

        /// <summary>
        /// Video sample for output frame i (spec §3.4, §7.5 step b). With local time v = t − offset: v &lt; 0 (video not
        /// started yet) shows its first frame frozen (decision); 0 ≤ v &lt; length plays source pts trimIn + v (t = <see cref="FrameTimeUs"/>); afterwards
        /// <see cref="UserVideoTiming.AfterEnd"/>: Freeze = Frozen(trimOut − 1), Loop = wraps to trimIn, Black, ExpandSim.
        /// NoVideo when the project has no video.
        /// </summary>
        public VideoSample SampleVideo(int frameIndex)
        {
            CheckFrame(frameIndex);
            if (_video == null) return VideoSample.NoVideo;
            return SampleVideoLocal(FrameStartUs(frameIndex) - _video.OffsetUs);
        }

        /// <summary>Video sample at an arbitrary output time (preview, spec §3.7). Same rules as <see cref="SampleVideo"/>.</summary>
        public VideoSample SampleVideoAtTimeUs(long outputUs)
        {
            CheckTimeUs(outputUs);
            if (_video == null) return VideoSample.NoVideo;
            return SampleVideoLocal(outputUs - _video.OffsetUs);
        }

        /// <summary>
        /// Output time (µs, rounded up) at which clip tick <paramref name="tick"/> first appears during the first pass of the
        /// clip — for aligning simulation sound events with frames (spec §3.6, §7.5 step 3). <c>tick == TrimOutTick</c>
        /// returns the clip end. Repeats (Loop, SlowMoReplay) and ContinueSim are not covered.
        /// </summary>
        public long ClipTickToOutputUs(int tick)
        {
            if (tick < _clip.TrimInTick || tick > _clip.TrimOutTick)
                throw new ArgumentOutOfRangeException(nameof(tick), tick, "Tick must be in [trimIn, trimOut].");
            long c = tick - _clip.TrimInTick;
            long units = c <= _normalTicks
                ? c * _unitsPerNormalTick
                : _normalEndUnits + (c - _normalTicks) * _unitsPerSlowTick;
            return CeilDiv(units * MicrosPerSecond, _unitsPerSecond);
        }

        SimSample SampleSimUnits(long u)
        {
            if (u < _clipEndUnits) return SimSample.Playing(ClipTickAt(u), 0);

            long w = u - _clipEndUnits;
            switch (_clip.AfterEnd)
            {
                case SimAfterEnd.Loop:
                {
                    long iteration = u / _clipEndUnits;
                    return SimSample.Playing(ClipTickAt(u % _clipEndUnits), (int)iteration);
                }
                case SimAfterEnd.SlowMoReplay:
                {
                    long iteration = w / _replayPeriodUnits + 1;
                    long c = w % _replayPeriodUnits / _unitsPerReplayTick;
                    return SimSample.Playing((int)(_clip.TrimOutTick - _replayTicks + c), (int)iteration);
                }
                case SimAfterEnd.ContinueSim:
                {
                    long tick = _clip.TrimOutTick + w / _unitsPerNormalTick;
                    if (tick < _clip.AvailableTicks) return SimSample.Playing((int)tick, 0);
                    return SimSample.Frozen(_clip.AvailableTicks - 1);
                }
                default:
                    return SimSample.Frozen(_clip.LastTick);
            }
        }

        int ClipTickAt(long u)
        {
            long c = u < _normalEndUnits
                ? u / _unitsPerNormalTick
                : _normalTicks + (u - _normalEndUnits) / _unitsPerSlowTick;
            return (int)(_clip.TrimInTick + c);
        }

        VideoSample SampleVideoLocal(long v)
        {
            if (v < 0) return VideoSample.Frozen(_video.TrimInUs);
            long len = _video.LengthUs;
            if (v < len) return VideoSample.Frame(_video.TrimInUs + v, 0);
            switch (_video.AfterEnd)
            {
                case VideoAfterEnd.Loop:
                    return VideoSample.Frame(_video.TrimInUs + v % len, (int)(v / len));
                case VideoAfterEnd.Black:
                    return VideoSample.Black;
                case VideoAfterEnd.ExpandSim:
                    return VideoSample.ExpandSim;
                default:
                    return VideoSample.Frozen(_video.LastPtsUs);
            }
        }

        long FrameStartUs(int frameIndex)
        {
            return CeilDiv((long)frameIndex * MicrosPerSecond, _fps);
        }

        void CheckFrame(int frameIndex)
        {
            if (frameIndex < 0 || frameIndex >= _frameCount)
                throw new ArgumentOutOfRangeException(nameof(frameIndex), frameIndex, "Frame index must be in [0, FrameCount).");
        }

        static void CheckTimeUs(long outputUs)
        {
            if (outputUs < 0 || outputUs > MaxSampleTimeUs)
                throw new ArgumentOutOfRangeException(nameof(outputUs), outputUs, "Output time must be in [0, 180 s].");
        }

        internal static long CeilDiv(long a, long b)
        {
            return (a + b - 1) / b;
        }

        static long CeilDivNonNegative(long a, long b)
        {
            return a <= 0 ? 0 : CeilDiv(a, b);
        }
    }
}
