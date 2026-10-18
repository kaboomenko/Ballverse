using System;
using System.Collections.Generic;

namespace Ballverse.Core.Timeline
{
    /// <summary>
    /// Compiled time mapping of a <see cref="TimelineSpec"/> (spec §3.4, §3.7, §7.5): output frame count and, for every
    /// output frame or preview time, which simulation tick and which user-video pts to show. Immutable and thread-safe.
    /// <para>
    /// <b>Output layout.</b> The output is [cover lead-in][content]. The optional cover lead-in
    /// (<see cref="TimelineSpec.CoverTimeUs"/>, spec §3.4 "Поставить выбранный кадр первым на 0,1 с") is prepended:
    /// exactly 0.1 s = fps / 10 frames (3 at 30 fps, 6 at 60 fps) showing the chosen content frame as a still
    /// (decision: prepend rather than overlay, so no content frame is hidden). The content starts at the earlier of the
    /// clip start and the video start (decision): with video offset o (<see cref="UserVideoTiming.OffsetUs"/>) the clip
    /// starts at content time max(0, −o) and the video at max(0, o), so neither track's head is ever dropped.
    /// </para>
    /// <para>
    /// <b>Exact time.</b> Frame i shows output time t = i / fps exactly (spec §7.5); all arithmetic is integer. Clip
    /// time is counted in units of 1 / (60 · p · f) s, where p is the speed and f the slow-mo factor in hundredths, so
    /// every tick boundary is an integer number of units and nothing accumulates.
    /// </para>
    /// <para>
    /// <b>Clip output length.</b> Let L be the trimmed clip length in ticks, S = min(slow-mo ticks, L) and N = L − S.
    /// The first N clip ticks play at speed p/100 and the last S ticks at p/100 · f/100, so the clip lasts
    /// N / (60 · p/100) + S / (60 · p·f/10000) seconds on the output timeline. A sample at clip time c shows clip tick
    /// trimIn + ⌊c in ticks⌋ (floor: the tick whose interval contains t), so at 1.0x consecutive 60 fps frames show
    /// consecutive ticks and 30 fps frames show every 2nd tick.
    /// </para>
    /// <para>
    /// <b>Duration</b> (spec §3.4, §3.8), measured on the content: Auto = until the clip ends (equals the trimmed clip
    /// length when the clip starts first, o ≥ 0); MatchVideo ("= V") = until the trimmed video ends (equals the trimmed
    /// video length when the video starts first, o ≤ 0; falls back to Auto without a video); Manual = the given length.
    /// Content frames = ⌈duration · fps⌉; the cover lead-in is added and the total is clamped to [fps, 180 · fps], i.e.
    /// [1 s, 180 s], keeping the last partial frame.
    /// </para>
    /// </summary>
    public sealed class StudioTimeline
    {
        public const long MicrosPerSecond = 1000000L;
        public const int MinDurationSeconds = 1;
        /// <summary>Length of the cover lead-in, µs (spec §3.4: 0.1 s).</summary>
        public const long CoverLeadInUs = 100000L;
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
        readonly long _unitsPerNormalTick;  // 100 · f
        readonly long _unitsPerSlowTick;    // 10000
        readonly long _normalEndUnits;   // N · 100 · f
        readonly long _clipEndUnits;     // normal end + S · 10000
        readonly long _replayTicks;      // R = min(120, L)
        readonly long _replayPeriodUnits;   // R · units per replayed tick
        readonly long _unitsPerReplayTick;
        readonly int _coverFrames;
        readonly long _coverUs;
        readonly long _leadUs;           // clip start on the content timeline
        readonly long _videoStartUs;     // video start on the content timeline
        readonly long _clipStartFrame;   // output frames
        readonly long _clipEndFrame;
        readonly int _frameCount;
        readonly SimSample _coverSim;
        readonly VideoSample _coverVideo;

        public TimelineSpec Spec { get; }
        public int Fps => _fps;

        /// <summary>Total output frames (spec §3.8: 1..180 s), cover lead-in included. Frame i covers output time [i / fps, (i + 1) / fps).</summary>
        public int FrameCount => _frameCount;

        /// <summary>Output duration = FrameCount / fps, rounded to the nearest µs.</summary>
        public long DurationUs => (2L * _frameCount * MicrosPerSecond + _fps) / (2L * _fps);

        /// <summary>Frames of the cover lead-in at the start of the output: fps / 10 when the cover toggle is on, else 0.</summary>
        public int CoverFrameCount => _coverFrames;

        /// <summary>Output time where the content starts (after the cover lead-in), µs: 0 or 100 000.</summary>
        public long ContentStartUs => _coverUs;

        /// <summary>Output time where the trimmed clip starts, µs (cover lead-in + max(0, −offset)).</summary>
        public long ClipStartUs => _coverUs + _leadUs;

        /// <summary>Output time where the trimmed user video starts, µs (cover lead-in + max(0, offset)); <see cref="ContentStartUs"/> without a video.</summary>
        public long VideoStartUs => _coverUs + _videoStartUs;

        /// <summary>First output frame of the clip's first pass (frames before it show the clip's first tick frozen). Not clamped to <see cref="FrameCount"/>.</summary>
        public int ClipStartFrame => (int)_clipStartFrame;

        /// <summary>First output frame after the clip's first pass, where the <see cref="SimAfterEnd"/> behaviour starts. Not clamped to <see cref="FrameCount"/>.</summary>
        public int ClipEndFrame => (int)_clipEndFrame;

        /// <summary>Exact clip output length (its first pass) rounded up to whole µs.</summary>
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
            _unitsPerNormalTick = 100L * _factor;
            _unitsPerSlowTick = 10000L;
            _normalEndUnits = _normalTicks * _unitsPerNormalTick;
            _clipEndUnits = _normalEndUnits + _slowTicks * _unitsPerSlowTick;

            _replayTicks = Math.Min(ClipTiming.SlowMoReplayTicks, _clip.LengthTicks);
            // Units per replayed tick at absolute 0.5x: 100 · p · f / 50 = 2pf. The replayed ticks always include the last
            // clip tick, which was first shown at p·f (slow-mo on) or p (off); the replay never runs faster than that.
            _unitsPerReplayTick = Math.Max(
                100L * _speed * _factor / ClipTiming.SlowMoReplaySpeedPercent,
                _slowTicks > 0 ? _unitsPerSlowTick : _unitsPerNormalTick);
            _replayPeriodUnits = _replayTicks * _unitsPerReplayTick;

            _coverFrames = spec.CoverTimeUs.HasValue ? _fps / 10 : 0;
            _coverUs = spec.CoverTimeUs.HasValue ? CoverLeadInUs : 0;
            long offset = _video != null ? _video.OffsetUs : 0;
            _leadUs = Math.Max(0, -offset);
            _videoStartUs = Math.Max(0, offset);

            // Content frame j shows clip time (j / fps − lead) s = (j·10⁶ − lead·fps) · U / (10⁶·fps) units.
            _clipStartFrame = _coverFrames + CeilDiv(_leadUs * _fps, MicrosPerSecond);
            _clipEndFrame = _coverFrames + CeilDiv(
                _clipEndUnits * MicrosPerSecond * _fps + _leadUs * _fps * _unitsPerSecond,
                _unitsPerSecond * MicrosPerSecond);
            _frameCount = ComputeFrameCount();

            if (_coverFrames > 0)
            {
                long coverFrame = Math.Min(spec.CoverTimeUs.Value * _fps / MicrosPerSecond, _frameCount - _coverFrames - 1);
                _coverSim = Still(SampleSimAtX(coverFrame * MicrosPerSecond));
                _coverVideo = Still(SampleVideoAtContentUs(FrameStartUs(coverFrame)));
            }
        }

        public StudioTimeline(ClipTiming clip, UserVideoTiming video, DurationMode durationMode, int fps, long manualDurationUs = 0, long? coverTimeUs = null)
            : this(new TimelineSpec(clip, video, durationMode, fps, manualDurationUs, coverTimeUs))
        {
        }

        int ComputeFrameCount()
        {
            long frames;
            switch (Spec.DurationMode)
            {
                case DurationMode.MatchVideo:
                    frames = _video != null
                        ? CeilDiv((_videoStartUs + _video.LengthUs) * _fps, MicrosPerSecond)
                        : _clipEndFrame - _coverFrames;
                    break;
                case DurationMode.Manual:
                    frames = CeilDiv(Spec.ManualDurationUs * _fps, MicrosPerSecond);
                    break;
                default:
                    frames = _clipEndFrame - _coverFrames;
                    break;
            }
            frames += _coverFrames;
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

        /// <summary>True for the frames of the cover lead-in (spec §3.4).</summary>
        public bool IsCoverFrame(int frameIndex)
        {
            CheckFrame(frameIndex);
            return frameIndex < _coverFrames;
        }

        /// <summary>True while frame i lies in the first pass of the clip (after its start, before the <see cref="SimAfterEnd"/> behaviour starts).</summary>
        public bool IsClipPlaying(int frameIndex)
        {
            CheckFrame(frameIndex);
            return frameIndex >= _clipStartFrame && frameIndex < _clipEndFrame;
        }

        /// <summary>
        /// Simulation sample for output frame i (spec §3.4, §7.5 step a).
        /// <list type="bullet">
        /// <item><b>Cover lead-in</b> — the sample of the chosen cover frame, as a still (Frozen).</item>
        /// <item><b>Before the clip starts</b> (video offset &lt; 0) — Frozen(trimIn): the starting position is shown, so
        /// the slot is never empty (decision, symmetric with the video rule).</item>
        /// <item><b>During the clip</b> — Playing(tick) with speed and finale slow-mo applied.</item>
        /// </list>
        /// After the clip, per <see cref="ClipTiming.AfterEnd"/>:
        /// <list type="bullet">
        /// <item><b>Freeze</b> — Frozen(last clip tick).</item>
        /// <item><b>Loop</b> — the clip repeats exactly as first played, finale slow-mo included (decision: a seamless
        /// repeat of what the viewer saw); the period is the clip output length; Iteration counts repeats.</item>
        /// <item><b>SlowMoReplay</b> — the last 2 s of the clip (whole clip if shorter) replay repeatedly at
        /// min(0.5x of real time, the rate at which the clip's last tick was first shown): 0.5x normally, the finale
        /// slow-mo rate when that is slower, so the replay is never faster than the finale (decision);
        /// Iteration = replay number (1, 2, …).</item>
        /// <item><b>ContinueSim</b> — ticks past trimOut play on at the base clip speed (finale slow-mo ends with the clip,
        /// decision) up to <see cref="ClipTiming.AvailableTicks"/> − 1, then Frozen at that tick.</item>
        /// </list>
        /// <see cref="VideoAfterEnd.ExpandSim"/> changes only the layout; it never affects this result.
        /// <see cref="SimSampleKind.Hidden"/> is never returned.
        /// </summary>
        public SimSample SampleSim(int frameIndex)
        {
            CheckFrame(frameIndex);
            if (frameIndex < _coverFrames) return _coverSim;
            return SampleSimAtX((long)(frameIndex - _coverFrames) * MicrosPerSecond);
        }

        /// <summary>
        /// Simulation sample at an arbitrary output time (preview, spec §3.7). Same rules as <see cref="SampleSim"/>.
        /// A time equal to <see cref="FrameTimeUs"/>(i) returns exactly <c>SampleSim(i)</c>; other times sample the exact µs.
        /// </summary>
        public SimSample SampleSimAtTimeUs(long outputUs)
        {
            CheckTimeUs(outputUs);
            if (outputUs < _coverUs) return _coverSim;
            long contentUs = outputUs - _coverUs;
            long j = contentUs * _fps / MicrosPerSecond;
            // Frame start times are rounded up to whole µs; map them back to the exact frame time.
            long x = FrameStartUs(j) == contentUs ? j * MicrosPerSecond : contentUs * _fps;
            return SampleSimAtX(x);
        }

        /// <summary>
        /// Video sample for output frame i (spec §3.4, §7.5 step b). Cover lead-in frames show the chosen cover frame as a
        /// still. Otherwise, with video local time v = t − <see cref="VideoStartUs"/> (t = <see cref="FrameTimeUs"/>):
        /// v &lt; 0 (video not started yet) shows its first frame frozen (decision); 0 ≤ v &lt; length plays source pts
        /// trimIn + v; afterwards <see cref="UserVideoTiming.AfterEnd"/>: Freeze = Frozen(trimOut − 1), Loop = wraps to
        /// trimIn, Black, ExpandSim. NoVideo when the project has no video.
        /// </summary>
        public VideoSample SampleVideo(int frameIndex)
        {
            CheckFrame(frameIndex);
            if (frameIndex < _coverFrames) return _coverVideo;
            return SampleVideoAtContentUs(FrameStartUs(frameIndex - _coverFrames));
        }

        /// <summary>Video sample at an arbitrary output time (preview, spec §3.7). Same rules as <see cref="SampleVideo"/>.</summary>
        public VideoSample SampleVideoAtTimeUs(long outputUs)
        {
            CheckTimeUs(outputUs);
            if (outputUs < _coverUs) return _coverVideo;
            return SampleVideoAtContentUs(outputUs - _coverUs);
        }

        /// <summary>
        /// Output time (µs, rounded up) at which clip tick <paramref name="tick"/> first appears during the first pass of
        /// the clip (spec §3.6, §7.5 step 3). <c>tick == TrimOutTick</c> returns the clip end. For every appearance,
        /// repeats and ContinueSim included, use <see cref="GetTickOutputTimesUs"/>.
        /// </summary>
        public long ClipTickToOutputUs(int tick)
        {
            if (tick < _clip.TrimInTick || tick > _clip.TrimOutTick)
                throw new ArgumentOutOfRangeException(nameof(tick), tick, "Tick must be in [trimIn, trimOut].");
            return ClipStartUs + CeilDiv(ClipUnitsOfTick(tick - _clip.TrimInTick) * MicrosPerSecond, _unitsPerSecond);
        }

        /// <summary>
        /// Appends to <paramref name="results"/>, in ascending order, every output time (µs, rounded up from the exact
        /// rational time) at which render-stream tick <paramref name="tick"/> starts playing within [0, FrameCount / fps):
        /// the first pass, every <see cref="SimAfterEnd.Loop"/> and <see cref="SimAfterEnd.SlowMoReplay"/> repeat, and
        /// <see cref="SimAfterEnd.ContinueSim"/> ticks. Frozen frames (pre-roll, cover, Freeze, end of the stream) start
        /// no tick. The offline sound mixer places each simulation event at these times, so export audio and frames share
        /// one clock (spec §3.7 "в экспорте — 0", §7.8). Ticks that never play add nothing. Returns the number added.
        /// Times are independent of whether a frame happens to show the tick (at 2x and 30 fps most ticks fall between frames).
        /// </summary>
        public int GetTickOutputTimesUs(int tick, IList<long> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            int count = 0;
            if (tick >= _clip.TrimInTick && tick < _clip.TrimOutTick)
            {
                long u0 = ClipUnitsOfTick(tick - _clip.TrimInTick);
                if (_clip.AfterEnd == SimAfterEnd.Loop)
                {
                    for (long u = u0; TryAddTickTime(u, results); u += _clipEndUnits) count++;
                }
                else if (TryAddTickTime(u0, results))
                {
                    count++;
                }
            }

            if (_clip.AfterEnd == SimAfterEnd.SlowMoReplay)
            {
                long first = _clip.TrimOutTick - _replayTicks;
                if (tick >= first && tick < _clip.TrimOutTick)
                {
                    for (long u = _clipEndUnits + (tick - first) * _unitsPerReplayTick; TryAddTickTime(u, results); u += _replayPeriodUnits) count++;
                }
            }
            else if (_clip.AfterEnd == SimAfterEnd.ContinueSim && tick >= _clip.TrimOutTick && tick < _clip.AvailableTicks)
            {
                if (TryAddTickTime(_clipEndUnits + (long)(tick - _clip.TrimOutTick) * _unitsPerNormalTick, results)) count++;
            }
            return count;
        }

        bool TryAddTickTime(long clipUnits, IList<long> results)
        {
            // Exact output time: ClipStartUs + clipUnits / U s; keep it if < FrameCount / fps.
            long clipStartUs = ClipStartUs;
            if ((clipStartUs * _unitsPerSecond + clipUnits * MicrosPerSecond) * _fps >= _frameCount * MicrosPerSecond * _unitsPerSecond)
                return false;
            results.Add(clipStartUs + CeilDiv(clipUnits * MicrosPerSecond, _unitsPerSecond));
            return true;
        }

        long ClipUnitsOfTick(long c)
        {
            return c <= _normalTicks
                ? c * _unitsPerNormalTick
                : _normalEndUnits + (c - _normalTicks) * _unitsPerSlowTick;
        }

        /// <summary>Sim sample at content time x / (10⁶ · fps) s.</summary>
        SimSample SampleSimAtX(long x)
        {
            long num = x - _leadUs * _fps;
            if (num < 0) return SimSample.Frozen(_clip.TrimInTick);
            return SampleSimUnits(num * _unitsPerSecond / (MicrosPerSecond * _fps));
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

        VideoSample SampleVideoAtContentUs(long contentUs)
        {
            if (_video == null) return VideoSample.NoVideo;
            // v ≤ 180 s, so v / len fits in an int for any accepted length.
            long v = contentUs - _videoStartUs;
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

        static SimSample Still(SimSample s)
        {
            return s.Kind == SimSampleKind.Playing ? SimSample.Frozen(s.Tick) : s;
        }

        static VideoSample Still(VideoSample s)
        {
            return s.Kind == VideoSampleKind.Frame ? VideoSample.Frozen(s.SourcePtsUs) : s;
        }

        long FrameStartUs(long frameIndex)
        {
            return CeilDiv(frameIndex * MicrosPerSecond, _fps);
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
    }
}
