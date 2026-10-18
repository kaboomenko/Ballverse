using System;
using System.Collections.Generic;
using Ballverse.Core.Audio;
using Ballverse.Core.Integration;
using Ballverse.Core.Layout;
using Ballverse.Core.Tests.Audio;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Integration
{
    /// <summary>Timeline + Layout + export settings → per-frame export plan, and the matching sound schedule.</summary>
    public class ExportFramePlannerTests
    {
        static readonly LayoutSpec PipTopLeft = LayoutSpec.Default.WithType(LayoutType.L5)
            .WithPip(new PipSpec(SlotShape.RoundedRect, 0.35f, new FloatVec2(0.2f, 0.25f)));

        static FrameRequest Request(ExportSettings s, LayoutSpec layout)
        {
            return new FrameRequest(s.Width, s.Height, layout, new IntVec2(1920, 1080));
        }

        [Fact]
        public void ConstructionChecksFpsAndCanvas()
        {
            var settings = new ExportSettings(ExportResolution.P1080, 60);
            var timeline60 = new StudioTimeline(new TimelineSpec(new ClipTiming(0, 600, 600), null, DurationMode.Auto, 60));
            var timeline30 = new StudioTimeline(new TimelineSpec(new ClipTiming(0, 600, 600), null, DurationMode.Auto, 30));
            Assert.Throws<ArgumentException>(() => new ExportFramePlanner(timeline30, settings, Request(settings, LayoutSpec.Default)));
            Assert.Throws<ArgumentException>(() => new ExportFramePlanner(timeline60, settings, new FrameRequest(720, 1280, LayoutSpec.Default)));
            Assert.Throws<ArgumentNullException>(() => new ExportFramePlanner(timeline60, settings, null));
            Assert.Throws<ArgumentNullException>(() => new ExportFramePlanner(null, settings, Request(settings, LayoutSpec.Default)));
            var planner = new ExportFramePlanner(timeline60, settings, Request(settings, LayoutSpec.Default));
            Assert.Throws<ArgumentOutOfRangeException>(() => planner.GetFrame(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => planner.GetFrame(planner.FrameCount));
            Assert.Same(settings, planner.Settings);
            Assert.Equal(600, planner.Plan.FrameCount);
        }

        [Fact]
        public void ExpandSimSwitchesToL4AndKeepsTheWatermark()
        {
            var settings = new ExportSettings(ExportResolution.P720, 30);
            var spec = new TimelineSpec(new ClipTiming(0, 600, 600), new UserVideoTiming(0, 4000000, 0, VideoAfterEnd.ExpandSim), DurationMode.Auto, 30);
            var planner = new ExportFramePlanner(new StudioTimeline(spec), settings, Request(settings, PipTopLeft));
            Assert.True(planner.LayoutPlan.Layout.HasVideo);
            Assert.Equal(LayoutType.L4, planner.ExpandSimPlan.Layout.EffectiveType);
            Assert.False(planner.ExpandSimPlan.Layout.HasVideo);
            Assert.Equal(WatermarkCorner.TopRight, planner.LayoutPlan.Watermark.Corner); // the PiP window blocks the top-left
            // Composed alone, L4 would put the watermark top-left; the plan keeps the project's placement (no mid-video jump).
            Assert.Equal(WatermarkCorner.TopLeft, FrameComposer.Compose(new FrameRequest(720, 1280, PipTopLeft.WithType(LayoutType.L4))).Watermark.Corner);
            Assert.Equal(planner.LayoutPlan.Watermark.Rect, planner.ExpandSimPlan.Watermark.Rect);

            ExportFrame before = planner.GetFrame(119); // 3.966 s: the 4 s video still plays
            Assert.Equal(VideoSampleKind.Frame, before.Video.Kind);
            Assert.Same(planner.LayoutPlan, before.Plan);
            Assert.True(before.NeedsVideoFrame);
            ExportFrame after = planner.GetFrame(120);
            Assert.Equal(VideoSampleKind.ExpandSim, after.Video.Kind);
            Assert.Same(planner.ExpandSimPlan, after.Plan);
            Assert.False(after.DrawsVideoSlot);
            Assert.False(after.NeedsVideoFrame);
        }

        [Fact]
        public void LayoutWithoutVideoSlotUsesOnePlan()
        {
            var settings = new ExportSettings(ExportResolution.P720, 30);
            var spec = new TimelineSpec(new ClipTiming(0, 120, 120), null, DurationMode.Auto, 30);
            var planner = new ExportFramePlanner(new StudioTimeline(spec), settings,
                new FrameRequest(720, 1280, LayoutSpec.Default.WithType(LayoutType.L4)));
            Assert.Same(planner.LayoutPlan, planner.ExpandSimPlan);
            Assert.Equal(VideoSampleKind.NoVideo, planner.GetFrame(0).Video.Kind);
            Assert.False(planner.GetFrame(0).DrawsVideoSlot);
        }

        [Fact]
        public void CoverLeadInAndSlowMoAreVisibleInThePlan()
        {
            // 2× clip with a 1 s finale slow-mo, a 0.1 s cover, MatchVideo with a looping video.
            var settings = new ExportSettings(ExportResolution.P1080, 60);
            var clip = new ClipTiming(60, 660, 900, speedPercent: 200, slowMoTicks: 60, slowMoFactorPercent: 50, afterEnd: SimAfterEnd.Loop);
            var video = new UserVideoTiming(1000000, 9000000, 500000, VideoAfterEnd.Loop);
            var timeline = new StudioTimeline(new TimelineSpec(clip, video, DurationMode.MatchVideo, 60, coverTimeUs: 2000000));
            var planner = new ExportFramePlanner(timeline, settings, Request(settings, LayoutSpec.Default));
            Assert.Equal(6, timeline.CoverFrameCount);
            for (int i = 0; i < 6; i++) Assert.True(planner.GetFrame(i).IsCover);
            Assert.False(planner.GetFrame(6).IsCover);
            Assert.Equal(SimSampleKind.Frozen, planner.GetFrame(0).Sim.Kind);
            long last = -1;
            for (int i = 0; i < planner.FrameCount; i++)
            {
                ExportFrame f = planner.GetFrame(i);
                Assert.True(f.PtsUs > last);
                last = f.PtsUs;
                Assert.Equal(timeline.SampleSim(i), f.Sim);
                Assert.Equal(timeline.SampleVideo(i), f.Video);
            }
        }

        [Fact]
        public void FullLengthExportPlanRunsAt60FpsWithoutAllocating()
        {
            // 180 s at 60 fps (the export maximum): 10 800 frames; video ends at 120 s and the simulation expands.
            var settings = new ExportSettings(ExportResolution.P1080, 60);
            var clip = new ClipTiming(0, SimConstants.MaxDurationTicks, SimConstants.MaxDurationTicks);
            var video = new UserVideoTiming(0, 120000000, 0, VideoAfterEnd.ExpandSim);
            var timeline = new StudioTimeline(new TimelineSpec(clip, video, DurationMode.Auto, 60));
            var planner = new ExportFramePlanner(timeline, settings, Request(settings, PipTopLeft));
            Assert.Equal(10800, planner.FrameCount);
            Assert.Equal(90, planner.Plan.SegmentCount);

            planner.GetFrame(0); // warm-up (JIT)
            long checksum = 0;
            int keyframes = 0, segments = 0, expanded = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < planner.FrameCount; i++)
            {
                ExportFrame f = planner.GetFrame(i);
                checksum += f.Sim.Tick + f.Video.SourcePtsUs + f.PtsUs;
                if (f.IsKeyframe) keyframes++;
                if (f.StartsSegment) segments++;
                if (f.Video.Kind == VideoSampleKind.ExpandSim) expanded++;
            }
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.Equal(0, allocated);
            Assert.Equal(90, keyframes);
            Assert.Equal(90, segments);
            Assert.Equal(3600, expanded);
            Assert.True(checksum > 0);

            // Spot checks: 1× at 60 fps shows tick i on frame i; the video plays its own pts until 120 s.
            for (int i = 0; i < planner.FrameCount; i += 997)
            {
                ExportFrame f = planner.GetFrame(i);
                Assert.Equal(i, f.Index);
                Assert.Equal(SimSample.Playing(i), f.Sim);
                Assert.Equal(timeline.FrameTimeUs(i), f.PtsUs);
                Assert.Equal(i % 120 == 0, f.IsKeyframe);
                Assert.Equal(i / 120, f.Segment);
                if (i < 7200)
                {
                    Assert.Equal(VideoSampleKind.Frame, f.Video.Kind);
                    Assert.Equal(timeline.FrameTimeUs(i), f.Video.SourcePtsUs);
                    Assert.Same(planner.LayoutPlan, f.Plan);
                }
                else
                {
                    Assert.Same(planner.ExpandSimPlan, f.Plan);
                }
            }
            Assert.True(planner.Plan.IsFrameCountExact(10800));
            Assert.True(planner.Plan.IsDurationAcceptable(180000000));
        }

        [Fact]
        public void FullLengthSoundScheduleAndMixMatchTheFrames()
        {
            // The same 180 s export: one hit every 6 ticks lands exactly on tick × 800 frames and the mix covers the video.
            const int ticks = SimConstants.MaxDurationTicks;
            var timeline = new StudioTimeline(new TimelineSpec(new ClipTiming(0, ticks, ticks), null, DurationMode.Auto, 60));
            var events = new List<SoundEvent>();
            for (int t = 0; t < ticks; t += 6) events.Add(SoundEvent.Hit(t, t % 5, 4f + (t % 7)));
            ScheduledSoundEvent[] scheduled = ExportSoundSchedule.Build(timeline, events);
            Assert.Equal(events.Count, scheduled.Length);
            for (int i = 0; i < scheduled.Length; i++) Assert.Equal((long)events[i].Tick * AudioConstants.FramesPerTick, scheduled[i].Frame);
            long audioFrames = ExportSoundSchedule.AudioFrameCount(timeline);
            Assert.Equal(180L * AudioConstants.SampleRate, audioFrames);

            var mixer = new EventMixer(SoundPackCatalog.Get(SoundPackId.Pop), AudioTestUtil.FullBank());
            var output = new float[audioFrames * AudioConstants.Channels];
            mixer.RenderOffline(scheduled, output);
            Assert.Equal(0, mixer.MissingSamples);
            Assert.True(mixer.VoicesStarted >= events.Count * 9 / 10);
            float lastQuarterPeak = 0f;
            for (int i = output.Length * 3 / 4; i < output.Length; i++) lastQuarterPeak = Math.Max(lastQuarterPeak, Math.Abs(output[i]));
            Assert.True(lastQuarterPeak > 0.01f, "sound reaches the end of the export");
        }

        [Fact]
        public void LoopRepeatsScheduleTheEventAgainAndTrimmedTicksAreSilent()
        {
            // 2 s clip [60, 180) looping under a 5 s video (MatchVideo): every visible repetition of a tick sounds again.
            var clip = new ClipTiming(60, 180, 600, afterEnd: SimAfterEnd.Loop);
            var video = new UserVideoTiming(0, 5000000);
            var timeline = new StudioTimeline(new TimelineSpec(clip, video, DurationMode.MatchVideo, 30));
            var events = new List<SoundEvent> { SoundEvent.Hit(10, 0, 5f), SoundEvent.Hit(60, 1, 5f), SoundEvent.Hit(120, 2, 5f), SoundEvent.Hit(400, 3, 5f) };
            ScheduledSoundEvent[] s = ExportSoundSchedule.Build(timeline, events);
            var frames60 = new List<long>();
            var frames120 = new List<long>();
            foreach (var e in s)
            {
                Assert.NotEqual(10, e.Event.Tick);  // before trimIn: not in the clip
                Assert.NotEqual(400, e.Event.Tick); // after trimOut (Loop, not ContinueSim)
                if (e.Event.Tick == 60) frames60.Add(e.Frame);
                if (e.Event.Tick == 120) frames120.Add(e.Frame);
            }
            Assert.Equal(new long[] { 0, 96000, 192000 }, frames60);
            Assert.Equal(new long[] { 48000, 144000 }, frames120); // the third repeat would start at 5 s = the end of the output
            for (int i = 1; i < s.Length; i++) Assert.True(s[i - 1].Frame <= s[i].Frame);
        }

        [Fact]
        public void EqualFramesKeepEventOrder()
        {
            // QA mutant I7: dropping the index tie-breaker (an unstable Array.Sort) survived. Many hits share a tick (and the
            // loop repeats of those ticks), so the order of equal frames feeds the mixer's rate limits and voice stealing; it must
            // be the event order on every runtime (CoreCLR here, Mono / IL2CPP class libraries on devices).
            var clip = new ClipTiming(60, 180, 600, afterEnd: SimAfterEnd.Loop);
            var video = new UserVideoTiming(0, 5000000);
            var timeline = new StudioTimeline(new TimelineSpec(clip, video, DurationMode.MatchVideo, 30));
            var events = new List<SoundEvent>();
            int body = 0;
            for (int tick = 60; tick < 180; tick += 6)
            {
                for (int j = 0; j < 20; j++) events.Add(SoundEvent.Hit(tick, body++, 1f + j));
            }
            ScheduledSoundEvent[] s = ExportSoundSchedule.Build(timeline, events);
            Assert.True(s.Length > 2 * events.Count);
            int ties = 0;
            for (int i = 1; i < s.Length; i++)
            {
                Assert.True(s[i - 1].Frame <= s[i].Frame);
                if (s[i - 1].Frame != s[i].Frame) continue;
                ties++;
                Assert.True(s[i - 1].Event.BodyId < s[i].Event.BodyId,
                    "frame " + s[i].Frame + ": body " + s[i - 1].Event.BodyId + " before " + s[i].Event.BodyId);
            }
            Assert.True(ties > 500, "ties " + ties);
        }
    }
}
