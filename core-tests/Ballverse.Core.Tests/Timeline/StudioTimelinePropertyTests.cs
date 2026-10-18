using System;
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
            int slowMo = rng.NextBool() ? 0 : rng.NextInt(1, ClipTiming.MaxSlowMoTicks + 1);
            int factor = rng.NextInt(ClipTiming.MinSlowMoFactorPercent, ClipTiming.MaxSlowMoFactorPercent + 1);
            var simAfterEnd = (SimAfterEnd)rng.NextInt(0, 4);
            var clip = new ClipTiming(trimIn, trimOut, available, speed, slowMo, factor, simAfterEnd);

            UserVideoTiming video = null;
            if (rng.NextInt(0, 4) != 0)
            {
                long vIn = rng.NextInt(0, 60000000);
                long vOut = vIn + rng.NextInt(1, 200000000);
                long offset = rng.NextInt(-30000000, 30000001);
                video = new UserVideoTiming(vIn, vOut, offset, (VideoAfterEnd)rng.NextInt(0, 4));
            }

            var mode = (DurationMode)rng.NextInt(0, 3);
            long manual = rng.NextInt(1, 200000000);
            int fps = rng.NextBool() ? 60 : 30;
            return new TimelineSpec(clip, video, mode, fps, manual);
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

                int lastTick = -1;
                int lastSimIteration = 0;
                long lastPts = long.MinValue;
                int lastVideoIteration = 0;
                bool simEnded = false;
                for (int i = 0; i < t.FrameCount; i++)
                {
                    var sim = t.SampleSim(i);
                    Assert.NotEqual(SimSampleKind.Hidden, sim.Kind);
                    Assert.InRange(sim.Tick, 0, clip.AvailableTicks - 1);
                    Assert.True(sim.Iteration >= lastSimIteration, "sim iteration decreased");

                    if (i < t.ClipFrameCount)
                    {
                        Assert.Equal(SimSampleKind.Playing, sim.Kind);
                        Assert.Equal(0, sim.Iteration);
                        Assert.InRange(sim.Tick, clip.TrimInTick, clip.TrimOutTick - 1);
                    }
                    else if (clip.AfterEnd != SimAfterEnd.ContinueSim)
                    {
                        Assert.InRange(sim.Tick, clip.TrimInTick, clip.TrimOutTick - 1);
                    }
                    if (i == 0) Assert.Equal(clip.TrimInTick, sim.Tick);

                    // Once frozen, frozen for good.
                    if (simEnded) Assert.Equal(SimSampleKind.Frozen, sim.Kind);
                    if (sim.Kind == SimSampleKind.Frozen) simEnded = true;

                    if (sim.Iteration == lastSimIteration) Assert.True(sim.Tick >= lastTick, "tick decreased within an iteration");
                    lastTick = sim.Tick;
                    lastSimIteration = sim.Iteration;

                    var video = t.SampleVideo(i);
                    if (spec.Video == null)
                    {
                        Assert.Equal(VideoSampleKind.NoVideo, video.Kind);
                    }
                    else if (video.Kind == VideoSampleKind.Frame || video.Kind == VideoSampleKind.Frozen)
                    {
                        Assert.InRange(video.SourcePtsUs, spec.Video.TrimInUs, spec.Video.TrimOutUs - 1);
                        Assert.True(video.Iteration >= lastVideoIteration, "video iteration decreased");
                        if (video.Iteration == lastVideoIteration) Assert.True(video.SourcePtsUs >= lastPts, "pts decreased within an iteration");
                        lastPts = video.SourcePtsUs;
                        lastVideoIteration = video.Iteration;
                    }
                    else
                    {
                        Assert.NotEqual(VideoSampleKind.NoVideo, video.Kind);
                    }

                    // Preview sampling at the frame time agrees with export sampling.
                    if ((i & 7) == 0)
                    {
                        long us = t.FrameTimeUs(i);
                        Assert.Equal(sim, t.SampleSimAtTimeUs(us));
                        Assert.Equal(video, t.SampleVideoAtTimeUs(us));
                        Assert.Equal(i, t.FrameAtTimeUs(us));
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
                int end = Math.Min(t.ClipFrameCount, t.FrameCount);
                for (int i = 1; i < end; i++)
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
        public void RandomSpecs_ClipFrameCountMatchesExactRational()
        {
            var rng = Rng.FromSeed(0xB411E7, 3);
            for (int s = 0; s < 2000; s++)
            {
                var spec = RandomSpec(ref rng);
                var t = new StudioTimeline(spec);
                var c = spec.Clip;
                long p = c.SpeedPercent, f = c.SlowMoFactorPercent;
                long slow = c.EffectiveSlowMoTicks, normal = c.LengthTicks - slow;
                // Clip output seconds = (100·N·f + 10000·S) / (60·p·f); frames = ⌈seconds · fps⌉.
                long num = (100 * normal * f + 10000 * slow) * spec.Fps;
                long den = 60 * p * f;
                long frames = t.ClipFrameCount;
                Assert.True(frames * den >= num);
                Assert.True((frames - 1) * den < num);

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
                long previous = -1;
                for (int tick = c.TrimInTick; tick < c.TrimOutTick; tick++)
                {
                    long us = t.ClipTickToOutputUs(tick);
                    Assert.True(us > previous);
                    previous = us;
                    if (us > StudioTimeline.MaxSampleTimeUs) break;
                    Assert.Equal(tick, t.SampleSimAtTimeUs(us).Tick);
                    if (us > 0) Assert.Equal(tick - 1, t.SampleSimAtTimeUs(us - 1).Tick);
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
                var expand = new StudioTimeline(new TimelineSpec(spec.Clip, new UserVideoTiming(v.TrimInUs, v.TrimOutUs, v.OffsetUs, VideoAfterEnd.ExpandSim), spec.DurationMode, spec.Fps, spec.ManualDurationUs));
                var none = new StudioTimeline(new TimelineSpec(spec.Clip, null, DurationMode.Manual, spec.Fps, (long)expand.FrameCount * 1000000 / spec.Fps));
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
