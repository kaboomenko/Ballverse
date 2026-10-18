using System;
using System.Linq;
using Ballverse.Core.Integration;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Studio
{
    public class ExportJobTests
    {
        static ProjectDocument Doc(bool withVideo)
        {
            ProjectDocument d = ProjectDefaults.NewProject("p1", "My Reel", "2026-10-18T12:00:00Z", "ring_escape", 600);
            if (withVideo) d.UserVideo = UserVideoDefaults.Create("media/user.mp4", 8000000);
            return d;
        }

        static ExportJobRequest Request(bool withVideo = true, int fps = 60)
        {
            return new ExportJobRequest
            {
                JobId = "job1",
                Project = Doc(withVideo),
                Settings = new ExportSettings(ExportResolution.P1080, fps),
                Watermark = true,
                TextBounds = new[] { new FloatRect(0.05f, 0.04f, 0.3f, 0.05f) },
                VideoSize = new IntVec2(1080, 608),
                VideoBox = new IntVec2(1080, 608),
                StreamTicks = 600,
                NowIso = "2026-10-18T12:06:00Z",
            };
        }

        [Fact]
        public void StateSnapshotsTheProjectAndPlan()
        {
            ExportJobRequest r = Request();
            ExportResumeState s = ExportJob.CreateState(r);
            r.Project.Name = "changed later";
            Assert.Equal("My Reel", s.Project.Name);
            Assert.Equal(600, s.FrameCount);
            Assert.Equal(120, s.SegmentFrames);
            Assert.Equal(1080, s.VideoWidth);
            Assert.Equal("Ballverse_My_Reel_20261018_120600.mp4", s.OutputName);
            Assert.Equal(PlatformUiMasks.Default.Union().Count, s.MaskRects.Count);
            ExportJob job = ExportJob.FromState(s);
            Assert.Equal(600, job.FrameCount);
            Assert.Equal(5, job.SegmentCount);
            Assert.Equal(new ExportSegment(4, 480, 120), job.GetSegment(4));
            Assert.True(job.VideoSlot.HasValue);
            ExportFrame f = job.Planner.GetFrame(0);
            Assert.True(f.NeedsVideoFrame);
            Assert.True(f.Plan.HasWatermark);
            Assert.Equal(1080, job.Request.CanvasWidth);
        }

        [Fact]
        public void ResumedJobRebuildsTheSamePlan()
        {
            ExportResumeState s = ExportJob.CreateState(Request());
            var store = new InMemoryFileStore();
            ExportResumeStore.Save(store, s);
            ExportJob a = ExportJob.FromState(s);
            ExportJob b = ExportJob.FromState(ExportResumeStore.Load(store, "job1", out _));
            for (int i = 0; i < a.FrameCount; i += 37)
            {
                ExportFrame fa = a.Planner.GetFrame(i), fb = b.Planner.GetFrame(i);
                Assert.Equal(fa.PtsUs, fb.PtsUs);
                Assert.Equal(fa.Sim, fb.Sim);
                Assert.Equal(fa.Video, fb.Video);
                Assert.Equal(fa.Plan.Watermark.Rect, fb.Plan.Watermark.Rect);
                Assert.Equal(fa.Plan.VideoDestination, fb.Plan.VideoDestination);
            }
        }

        [Fact]
        public void ProjectWithoutVideoDropsTheVideoSize()
        {
            ExportResumeState s = ExportJob.CreateState(Request(withVideo: false, fps: 30));
            Assert.Equal(0, s.VideoWidth);
            Assert.Equal(300, s.FrameCount);
            ExportJob job = ExportJob.FromState(s);
            Assert.False(job.VideoSlot.HasValue);
            Assert.False(job.Planner.GetFrame(0).DrawsVideoSlot);
        }

        [Fact]
        public void InconsistentOrIncompleteInputsThrow()
        {
            ExportJobRequest r = Request();
            r.Settings = null;
            Assert.Throws<ArgumentException>(() => ExportJob.CreateState(r));
            r = Request();
            r.JobId = "a/b";
            Assert.Throws<ArgumentException>(() => ExportJob.CreateState(r));
            ExportResumeState s = ExportJob.CreateState(Request());
            s.FrameCount = 599;
            Assert.Throws<ArgumentException>(() => ExportJob.FromState(s));
        }
    }

    public class ExportFrameFlowTests
    {
        [Fact]
        public void BackPressureLimitsFramesInFlight()
        {
            var flow = new ExportFrameFlow(10, 4);
            Assert.True(flow.AtSegmentStart);
            Assert.Equal(0, flow.OnRendered());
            flow.OnRendered();
            flow.OnRendered();
            Assert.False(flow.CanRender); // 3 in flight
            Assert.Equal(0, flow.OnAppended());
            Assert.True(flow.CanRender);
            flow.OnRendered();
            Assert.False(flow.CanRender); // segment end (4 frames)
            flow.OnAppended();
            flow.OnAppended();
            Assert.False(flow.SegmentComplete);
            flow.OnAppended();
            Assert.True(flow.SegmentComplete);
            Assert.Throws<InvalidOperationException>(() => flow.OnAppended());
            flow.OnSegmentFinished();
            Assert.Equal(1, flow.Segment);
            Assert.Equal(4, flow.NextRender);
            Assert.Equal(4, flow.FramesAppendedTotal);
        }

        [Fact]
        public void LastSegmentIsPartialAndDoneAfterIt()
        {
            var flow = new ExportFrameFlow(10, 4, completedSegments: 2);
            Assert.Equal(8, flow.SegmentStart);
            Assert.Equal(10, flow.SegmentEnd);
            flow.OnRendered();
            flow.OnRendered();
            Assert.False(flow.CanRender);
            flow.OnAppended();
            flow.OnAppended();
            flow.OnSegmentFinished();
            Assert.True(flow.IsDone);
            Assert.Equal(10, flow.FramesAppendedTotal);
            Assert.False(flow.CanRender);
            Assert.False(flow.SegmentComplete);
        }

        [Fact]
        public void RestartDiscardsTheCurrentSegment()
        {
            var flow = new ExportFrameFlow(10, 4, 1);
            flow.OnRendered();
            flow.OnRendered();
            flow.OnAppended();
            flow.RestartSegment();
            Assert.Equal(4, flow.NextRender);
            Assert.Equal(4, flow.NextAppend);
            Assert.Equal(0, flow.InFlight);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExportFrameFlow(10, 4, 4));
            Assert.True(new ExportFrameFlow(10, 4, 3).IsDone);
        }
    }

    public class ExportPolicyTests
    {
        [Fact]
        public void PreflightNeedsTwiceTheEstimatePlusReserve()
        {
            var t = new StudioTimeline(new ClipTiming(0, 1800, 1800, 100, 0, 50, SimAfterEnd.Freeze), null, DurationMode.Auto, 60);
            var plan = new ExportPlan(t, new ExportSettings(ExportResolution.P1080, 60));
            long required = plan.RequiredFreeSpaceBytes();
            Assert.True(ExportPolicy.Preflight(plan, required).Ok);
            ExportPreflight low = ExportPolicy.Preflight(plan, required - 1);
            Assert.Equal(ExportErrorCode.InsufficientSpace, low.Error);
            Assert.Equal(required, low.RequiredBytes);
            Assert.True(ExportPolicy.Preflight(plan, -1).Ok);
        }

        [Fact]
        public void ThermalRetryBudgetAndJoinOption()
        {
            Assert.Equal(0, ExportPolicy.PauseBetweenSegmentsMs(ExportThermalLevel.Fair));
            Assert.Equal(ExportPolicy.SeriousPauseMs, ExportPolicy.PauseBetweenSegmentsMs(ExportThermalLevel.Serious));
            Assert.True(ExportPolicy.IsThrottled(ExportThermalLevel.Critical));
            Assert.False(ExportPolicy.IsThrottled(ExportThermalLevel.Unknown));
            Assert.True(ExportPolicy.CanRetrySegment(1));
            Assert.False(ExportPolicy.CanRetrySegment(2));
            Assert.True(ExportPolicy.FitsInBudget(100, 50, 0));
            Assert.True(ExportPolicy.FitsInBudget(10, 10, 1));
            Assert.False(ExportPolicy.FitsInBudget(20, 10, 1));
            long e = 50L * 1024 * 1024;
            Assert.False(ExportPolicy.DeleteSegmentsBeforeFastStart(-1, e));
            Assert.True(ExportPolicy.DeleteSegmentsBeforeFastStart(2 * e + ExportSettings.FreeSpaceReserveBytes - 1, e));
            Assert.False(ExportPolicy.DeleteSegmentsBeforeFastStart(2 * e + ExportSettings.FreeSpaceReserveBytes, e));
        }

        [Fact]
        public void ResultHelpers()
        {
            StudioExportResult r = StudioExportResult.Failure(ExportErrorCode.EncoderFailed, "x", "p", "j");
            Assert.False(r.Succeeded);
            Assert.True(r.SuggestLowerSettings);
            Assert.True(new StudioExportResult().Succeeded);
        }
    }

    public class VideoDecodeSizingTests
    {
        [Fact]
        public void LandscapeIntoHalfSlotIsCappedBySource()
        {
            // 1920x1080 cover into 1080x960: scale = max(1080/1920, 960/1080) = 0.889 → 1708x960
            IntVec2 box = VideoDecodeSizing.Compute(1920, 1080, 1080, 960, SlotContent.Default);
            Assert.Equal(1708, box.X);
            Assert.Equal(960, box.Y);
        }

        [Fact]
        public void SmallSourceIsNeverUpscaledAnd4kIsReduced()
        {
            IntVec2 small = VideoDecodeSizing.Compute(640, 360, 1080, 1920, SlotContent.Default);
            Assert.Equal(new IntVec2(640, 360), small);
            IntVec2 big = VideoDecodeSizing.Compute(2160, 3840, 1080, 1920, SlotContent.Default);
            Assert.Equal(new IntVec2(1080, 1920), big);
            IntVec2 zoomed = VideoDecodeSizing.Compute(2160, 3840, 1080, 1920, new SlotContent(0, false, SlotFill.Cover, FloatVec2.Zero, 2f));
            Assert.Equal(new IntVec2(2160, 3840), zoomed);
            Assert.Throws<ArgumentOutOfRangeException>(() => VideoDecodeSizing.Compute(0, 1, 1, 1, SlotContent.Default));
        }
    }

    public class PreviewQualityGovernorTests
    {
        [Fact]
        public void StepsDownWhenSlowAndUpWhenFast()
        {
            var g = new PreviewQualityGovernor(60, false);
            Assert.Equal(1f, g.Scale);
            bool changed = false;
            for (int i = 0; i < 200 && !changed; i++) changed = g.OnFrame(33000);
            Assert.True(changed);
            Assert.Equal(0.85f, g.Scale);
            Assert.Equal(1, g.Version);
            changed = false;
            for (int i = 0; i < 2000 && !changed; i++) changed = g.OnFrame(8000);
            Assert.True(changed);
            Assert.Equal(1f, g.Scale);
            Assert.False(g.OnFrame(0));
        }

        [Fact]
        public void StartsReducedAndScalesCanvasEven()
        {
            var g = new PreviewQualityGovernor(30, true);
            Assert.Equal(0.85f, g.Scale);
            IntVec2 c = g.ScaleCanvas(new IntVec2(540, 960));
            Assert.Equal(0, c.X & 1);
            Assert.Equal(0, c.Y & 1);
            Assert.InRange(c.X, 450, 462);
            Assert.InRange(c.Y / (double)c.X, 1.7, 1.8);
        }
    }
}
