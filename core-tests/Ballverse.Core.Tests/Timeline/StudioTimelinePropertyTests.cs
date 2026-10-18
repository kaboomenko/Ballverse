using System;
using System.Collections.Generic;
using System.Numerics;
using Ballverse.Core;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Timeline
{
    /// <summary>Rng-driven property tests over random timeline specs (deterministic seeds).</summary>
    public class StudioTimelinePropertyTests
    {
        const int SpecCount = 300;

        static TimelineSpec RandomSpec(ref Rng rng)
        {
            int available = rng.NextInt(1, SimConstants.MaxDurationTicks + 1);
            int trimIn = rng.NextInt(0, available);
            int trimOut = rng.NextInt(trimIn + 1, available + 1);
            int speed = rng.NextInt(ClipTiming.MinSpeedPercent, ClipTiming.MaxSpeedPercent + 1);
            int slowMo = rng.NextBool() ? 0 : rng.NextInt(ClipTiming.MinSlowMoTicks, ClipTiming.MaxSlowMoTicks + 1);
            int factor = rng.NextInt(ClipTiming.MinSlowMoFactorPercent, ClipTiming.MaxSlowMoFactorPercent + 1);
            var simAfterEnd = (SimAfterEnd)rng.NextInt(0, 4);
            var clip = new ClipTiming(trimIn, trimOut, available, speed, slowMo, factor, simAfterEnd);

            UserVideoTiming video = null;
            if (rng.NextInt(0, 4) != 0)
            {
                long vIn = rng.NextInt(0, 60000000);
                long vOut;
                long offset;
                switch (rng.NextInt(0, 4))
                {
                    case 0: // tiny lengths
                        vOut = vIn + rng.NextInt(1, 11);
                        offset = rng.NextInt(-30000000, 30000001);
                        break;
                    case 1: // extreme offsets
                        vOut = vIn + rng.NextInt(1, 200000000);
                        offset = rng.NextBool() ? UserVideoTiming.MaxAbsOffsetUs - rng.NextInt(0, 1000) : -UserVideoTiming.MaxAbsOffsetUs + rng.NextInt(0, 1000);
                        break;
                    default:
                        vOut = vIn + rng.NextInt(1, 200000000);
                        offset = rng.NextInt(-30000000, 30000001);
                        break;
                }
                video = new UserVideoTiming(vIn, vOut, offset, (VideoAfterEnd)rng.NextInt(0, 4));
            }

            var mode = (DurationMode)rng.NextInt(0, 3);
            long manual = rng.NextInt(1, 200000000);
            int fps = rng.NextBool() ? 60 : 30;
            long? cover = rng.NextInt(0, 4) == 0 ? rng.NextInt(0, 180000001) : (long?)null;
            return new TimelineSpec(clip, video, mode, fps, manual, cover);
        }

        [Fact]
        public void RandomSpecs_SamplingInvariantsHold()
        {
            var rng = Rng.FromSeed(0xB411E7, 1);
            for (int s = 0; s < SpecCount; s++)
            {
                var spec = RandomSpec(ref rng);
                var t = new StudioTimeline(spec);
                var clip = spec.Clip;

                Assert.InRange(t.FrameCount, spec.Fps, SimConstants.MaxDurationSeconds * spec.Fps);
                Assert.Equal(spec.CoverTimeUs.HasValue ? spec.Fps / 10 : 0, t.CoverFrameCount);
                Assert.True(t.ClipStartFrame >= t.CoverFrameCount);
                Assert.True(t.ClipEndFrame >= t.ClipStartFrame); // equal when a tiny clip falls between two frames
                int maxStep = (int)Math.Ceiling(clip.SpeedPercent / 100.0 * SimConstants.TickRate / spec.Fps);

                int lastTick = -1;
                int lastSimIteration = 0;
                long lastPts = long.MinValue;
                int lastVideoIteration = 0;
                bool simEnded = false;
                for (int i = 0; i < t.FrameCount; i++)
                {
                    var sim = t.SampleSim(i);
                    var video = t.SampleVideo(i);
                    Assert.NotEqual(SimSampleKind.Hidden, sim.Kind);
                    Assert.InRange(sim.Tick, 0, clip.AvailableTicks - 1);
                    Assert.True(sim.Iteration >= 0);
                    Assert.True(video.Iteration >= 0, "video iteration overflowed");

                    // Preview sampling at the frame time agrees with export sampling.
                    if ((i & 7) == 0 || i < 8)
                    {
                        long us = t.FrameTimeUs(i);
                        Assert.Equal(sim, t.SampleSimAtTimeUs(us));
                        Assert.Equal(video, t.SampleVideoAtTimeUs(us));
                        Assert.Equal(i, t.FrameAtTimeUs(us));
                    }

                    if (t.IsCoverFrame(i))
                    {
                        Assert.True(i < t.CoverFrameCount);
                        Assert.NotEqual(SimSampleKind.Playing, sim.Kind);
                        Assert.NotEqual(VideoSampleKind.Frame, video.Kind);
                        Assert.Equal(t.SampleSim(0), sim);
                        Assert.Equal(t.SampleVideo(0), video);
                        continue;
                    }

                    Assert.True(sim.Iteration >= lastSimIteration, "sim iteration decreased");
                    if (i < t.ClipStartFrame)
                    {
                        Assert.Equal(SimSample.Frozen(clip.TrimInTick), sim);
                        Assert.False(t.IsClipPlaying(i));
                    }
                    else if (i < t.ClipEndFrame)
                    {
                        Assert.True(t.IsClipPlaying(i));
                        Assert.Equal(SimSampleKind.Playing, sim.Kind);
                        Assert.Equal(0, sim.Iteration);
                        Assert.InRange(sim.Tick, clip.TrimInTick, clip.TrimOutTick - 1);
                        if (i == t.ClipStartFrame && t.ClipStartUs >= t.ContentStartUs) Assert.InRange(sim.Tick, clip.TrimInTick, clip.TrimInTick + maxStep - 1);
                    }
                    else
                    {
                        Assert.False(t.IsClipPlaying(i));
                        if (clip.AfterEnd != SimAfterEnd.ContinueSim) Assert.InRange(sim.Tick, clip.TrimInTick, clip.TrimOutTick - 1);
                        // Once frozen after the clip, frozen for good.
                        if (simEnded) Assert.Equal(SimSampleKind.Frozen, sim.Kind);
                        if (sim.Kind == SimSampleKind.Frozen) simEnded = true;
                    }
                    if (i == 0 && t.ClipStartUs >= t.ContentStartUs) Assert.Equal(clip.TrimInTick, sim.Tick);

                    if (sim.Iteration == lastSimIteration) Assert.True(sim.Tick >= lastTick, "tick decreased within an iteration");
                    lastTick = sim.Tick;
                    lastSimIteration = sim.Iteration;

                    if (spec.Video == null)
                    {
                        Assert.Equal(VideoSampleKind.NoVideo, video.Kind);
                    }
                    else if (video.Kind == VideoSampleKind.Frame || video.Kind == VideoSampleKind.Frozen)
                    {
                        Assert.InRange(video.SourcePtsUs, spec.Video.TrimInUs, spec.Video.TrimOutUs - 1);
                        Assert.True(video.Iteration >= lastVideoIteration, "video iteration decreased");
                        if (video.Iteration == lastVideoIteration) Assert.True(video.SourcePtsUs >= lastPts, "pts decreased within an iteration");
                        if (i == t.CoverFrameCount && t.VideoStartUs == t.ContentStartUs) Assert.Equal(VideoSample.Frame(spec.Video.TrimInUs), video);
                        lastPts = video.SourcePtsUs;
                        lastVideoIteration = video.Iteration;
                    }
                    else
                    {
                        Assert.NotEqual(VideoSampleKind.NoVideo, video.Kind);
                    }
                }
            }
        }

        [Fact]
        public void RandomSpecs_PlayingTicksAdvanceAtTheExpectedRate()
        {
            var rng = Rng.FromSeed(0xB411E7, 2);
            for (int s = 0; s < SpecCount; s++)
            {
                var spec = RandomSpec(ref rng);
                var t = new StudioTimeline(spec);
                var clip = spec.Clip;
                int normalTicks = clip.LengthTicks - clip.EffectiveSlowMoTicks;
                // Clip ticks per output frame during the normal part: speed · 60 / fps (≤ 4).
                int maxStep = (int)Math.Ceiling(clip.SpeedPercent / 100.0 * SimConstants.TickRate / spec.Fps);
                int end = Math.Min(t.ClipEndFrame, t.FrameCount);
                for (int i = t.ClipStartFrame + 1; i < end; i++)
                {
                    int step = t.SampleSim(i).Tick - t.SampleSim(i - 1).Tick;
                    Assert.InRange(step, 0, maxStep);
                    if (t.SampleSim(i - 1).Tick - clip.TrimInTick >= normalTicks)
                    {
                        // Slow-mo part: at most speed · factor · 60 / fps ticks per frame, rounded up (≤ 2).
                        Assert.InRange(step, 0, 2);
                    }
                }
            }
        }

        [Fact]
        public void RandomSpecs_ClipFramesMatchExactRational()
        {
            var rng = Rng.FromSeed(0xB411E7, 3);
            for (int s = 0; s < 2000; s++)
            {
                var spec = RandomSpec(ref rng);
                var t = new StudioTimeline(spec);
                var c = spec.Clip;
                long p = c.SpeedPercent, f = c.SlowMoFactorPercent;
                long slow = c.EffectiveSlowMoTicks, normal = c.LengthTicks - slow;
                bool videoOrigin = spec.Video != null && spec.DurationMode == DurationMode.MatchVideo;
                long coverUs = spec.CoverTimeUs.HasValue ? 100000 : 0;
                long leadUs = videoOrigin ? -spec.Video.OffsetUs : 0;
                Assert.Equal(coverUs + leadUs, t.ClipStartUs);
                Assert.Equal(coverUs + (spec.Video == null ? 0 : videoOrigin ? 0 : spec.Video.OffsetUs), t.VideoStartUs);
                // Exact lead (s · 10⁶ · fps): ±FrameTimeUs(k) means exactly ±k frames, anything else exact µs.
                long absLead = Math.Abs(leadUs);
                long k = (long)Math.Ceiling(absLead * (double)spec.Fps / 1000000);
                long leadX = absLead * spec.Fps;
                for (long kk = Math.Max(0, k - 1); kk <= k + 1; kk++)
                {
                    if (kk > 0 && (kk * 1000000 + spec.Fps - 1) / spec.Fps == absLead) leadX = kk * 1000000;
                }
                if (leadUs < 0) leadX = -leadX;
                long coverFrames = t.CoverFrameCount;
                // Clip end (content s) = leadX / (10⁶·fps) + (100·N·f + 10000·S) / (60·p·f); frames = max(0, ⌈end · fps⌉).
                var num = (BigInteger)leadX * 60 * p * f + (BigInteger)(100 * normal * f + 10000 * slow) * spec.Fps * 1000000;
                var den = (BigInteger)60 * p * f * 1000000;
                long frames = t.ClipEndFrame - coverFrames;
                Assert.True(frames >= 0);
                if (frames > 0) Assert.True((frames - 1) * den < num);
                Assert.True(frames * den >= num);
                long startFrames = t.ClipStartFrame - coverFrames;
                Assert.True(startFrames >= 0);
                Assert.True(startFrames * 1000000 >= leadX);
                if (startFrames > 0) Assert.True((startFrames - 1) * 1000000 < leadX);

                double seconds = normal / (0.6 * p) + slow / (0.006 * p * f);
                Assert.InRange(t.ClipOutputDurationUs - seconds * 1e6, -1e-3, 1.0 + 1e-3);
            }
        }

        [Fact]
        public void RandomSpecs_ClipTickToOutputUs_IsTheFirstTimeTheTickIsShown()
        {
            var rng = Rng.FromSeed(0xB411E7, 4);
            for (int s = 0; s < 100; s++)
            {
                var spec = RandomSpec(ref rng);
                var t = new StudioTimeline(spec);
                var c = spec.Clip;
                long previous = long.MinValue;
                for (int tick = c.TrimInTick; tick < c.TrimOutTick; tick++)
                {
                    long us = t.ClipTickToOutputUs(tick);
                    Assert.True(us > previous);
                    previous = us;
                    if (us + 1 > StudioTimeline.MaxSampleTimeUs) break;
                    if (us < t.ContentStartUs) continue; // clip head cut by MatchVideo
                    // us is the exact time rounded up: just after it the tick plays, just before it it does not yet.
                    Assert.Equal(SimSample.Playing(tick), t.SampleSimAtTimeUs(us + 1));
                    if (us - 1 < t.ContentStartUs) continue;
                    var before = t.SampleSimAtTimeUs(us - 1);
                    if (tick > c.TrimInTick) Assert.Equal(SimSample.Playing(tick - 1), before);
                    else Assert.NotEqual(SimSampleKind.Playing, before.Kind);
                }
            }
        }

        [Fact]
        public void RandomSpecs_TickOutputTimesMatchSampling()
        {
            var rng = Rng.FromSeed(0xB411E7, 7);
            var times = new List<long>();
            for (int s = 0; s < 80; s++)
            {
                var spec = RandomSpec(ref rng);
                var t = new StudioTimeline(spec);
                var c = spec.Clip;
                // Every appearance is a real tick start: just after it the tick plays, just before it something else.
                var appearances = new Dictionary<long, int>(); // output µs → tick
                for (int tick = 0; tick < c.AvailableTicks; tick++)
                {
                    times.Clear();
                    int n = t.GetTickOutputTimesUs(tick, times);
                    Assert.Equal(n, times.Count);
                    for (int k = 0; k < times.Count; k++)
                    {
                        long us = times[k];
                        if (k > 0) Assert.True(us > times[k - 1], "times not ascending");
                        Assert.True(us >= t.ClipStartUs);
                        Assert.True(us >= t.ContentStartUs);
                        Assert.True(us - 1 < t.FrameCount * 1000000L / spec.Fps + 1);
                        if (us + 1 <= StudioTimeline.MaxSampleTimeUs)
                        {
                            var after = t.SampleSimAtTimeUs(us + 1);
                            Assert.Equal(SimSampleKind.Playing, after.Kind);
                            Assert.Equal(tick, after.Tick);
                            var before = t.SampleSimAtTimeUs(Math.Max(0, us - 1));
                            Assert.False(us > 0 && before.Kind == SimSampleKind.Playing && before.Tick == tick && before.Iteration == after.Iteration,
                                "tick already playing before its start time");
                        }
                        Assert.False(appearances.ContainsKey(us), "two ticks start at the same µs");
                        appearances[us] = tick;
                    }
                }
                // Completeness: every playing (tick, iteration) seen on a frame has an appearance at or before that frame.
                SimSample previous = SimSample.Frozen(-1);
                for (int i = 0; i < t.FrameCount; i++)
                {
                    var sim = t.SampleSim(i);
                    if (sim.Kind == SimSampleKind.Playing && sim != previous)
                    {
                        times.Clear();
                        t.GetTickOutputTimesUs(sim.Tick, times);
                        long frameUs = t.FrameTimeUs(i);
                        bool found = false;
                        foreach (long us in times)
                        {
                            if (us <= frameUs + 1 && t.SampleSimAtTimeUs(Math.Min(us + 1, StudioTimeline.MaxSampleTimeUs)).Iteration == sim.Iteration) found = true;
                        }
                        // The tick in progress when MatchVideo cuts the clip's head started before the output.
                        bool cutHead = t.ClipStartUs < t.ContentStartUs && sim == t.SampleSimAtTimeUs(t.ContentStartUs);
                        Assert.True(found || cutHead, "frame " + i + " shows " + sim + " with no start time");
                    }
                    previous = sim;
                }
            }
        }

        [Fact]
        public void RandomSpecs_ExpandSimNeverChangesSimSampling()
        {
            var rng = Rng.FromSeed(0xB411E7, 5);
            for (int s = 0; s < 50; s++)
            {
                var spec = RandomSpec(ref rng);
                if (spec.Video == null) continue;
                var v = spec.Video;
                var expand = new StudioTimeline(new TimelineSpec(spec.Clip, new UserVideoTiming(v.TrimInUs, v.TrimOutUs, v.OffsetUs, VideoAfterEnd.ExpandSim), spec.DurationMode, spec.Fps, spec.ManualDurationUs, spec.CoverTimeUs));
                var none = new StudioTimeline(new TimelineSpec(spec.Clip, new UserVideoTiming(v.TrimInUs, v.TrimOutUs, v.OffsetUs, VideoAfterEnd.Freeze), spec.DurationMode, spec.Fps, spec.ManualDurationUs, spec.CoverTimeUs));
                Assert.Equal(expand.FrameCount, none.FrameCount);
                for (int i = 0; i < expand.FrameCount; i++) Assert.Equal(none.SampleSim(i), expand.SampleSim(i));
            }
        }

        [Fact]
        public void NormalSpeed_AllTrimsAndFps_FramesMapExactly()
        {
            var rng = Rng.FromSeed(77, 6);
            for (int s = 0; s < 200; s++)
            {
                int length = rng.NextInt(60, SimConstants.MaxDurationTicks + 1);
                int trimIn = rng.NextInt(0, SimConstants.MaxDurationTicks - length + 1);
                int fps = rng.NextBool() ? 60 : 30;
                var t = new StudioTimeline(new ClipTiming(trimIn, trimIn + length, SimConstants.MaxDurationTicks), null, DurationMode.Auto, fps);
                int step = 60 / fps;
                Assert.Equal((length + step - 1) / step, t.FrameCount);
                for (int i = 0; i < t.FrameCount; i += 13) Assert.Equal(SimSample.Playing(trimIn + step * i), t.SampleSim(i));
                Assert.Equal(SimSample.Playing(trimIn + step * (t.FrameCount - 1)), t.SampleSim(t.FrameCount - 1));
            }
        }
    }
}
