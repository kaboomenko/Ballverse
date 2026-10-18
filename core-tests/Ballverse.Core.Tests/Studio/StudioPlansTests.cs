using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Studio
{
    public class StudioFramePlansTests
    {
        static ProjectDocument WithVideo(LayoutType type)
        {
            ProjectDocument d = ProjectDefaults.NewProject("p1", "P", "2026-10-18T12:00:00Z", "ring_escape", 600);
            d.UserVideo = UserVideoDefaults.Create("media/user.mp4", 5000000);
            d.Layout.Type = type;
            d.Layout.Pip = new PipSettings();
            return d;
        }

        [Fact]
        public void ExpandSimUsesL4AndKeepsTheWatermark()
        {
            FrameRequest r = ProjectComposition.BuildFrameRequest(WithVideo(LayoutType.L3), 540, 960, new IntVec2(1080, 1920), true, null, null);
            StudioFramePlans plans = StudioFramePlans.Compose(r);
            Assert.True(plans.LayoutPlan.Layout.HasVideo);
            Assert.False(plans.ExpandSimPlan.Layout.HasVideo);
            Assert.Equal(plans.LayoutPlan.Watermark.Rect, plans.ExpandSimPlan.Watermark.Rect);
            Assert.Same(plans.ExpandSimPlan, plans.For(VideoSample.ExpandSim));
            Assert.Same(plans.LayoutPlan, plans.For(VideoSample.Frame(0)));
            Assert.True(StudioFramePlans.NeedsVideoFrame(plans.LayoutPlan, VideoSample.Frozen(5)));
            Assert.False(StudioFramePlans.NeedsVideoFrame(plans.LayoutPlan, VideoSample.Black));
            Assert.True(StudioFramePlans.DrawsVideoSlot(plans.LayoutPlan, VideoSample.Black));
            Assert.False(StudioFramePlans.DrawsVideoSlot(plans.ExpandSimPlan, VideoSample.ExpandSim));
        }

        [Fact]
        public void PreviewAndExportPlansAgreeOnTheWatermarkCorner()
        {
            ProjectDocument d = WithVideo(LayoutType.L3);
            d.Layout.Pip.X = 0.2;
            d.Layout.Pip.Y = 0.15; // window in the top-left corner → watermark moves right
            var preview = StudioFramePlans.Compose(ProjectComposition.BuildFrameRequest(d, 360, 640, new IntVec2(1080, 1920), true, null, null));
            var export = StudioFramePlans.Compose(ProjectComposition.BuildFrameRequest(d, 1080, 1920, new IntVec2(1080, 1920), true, null, null));
            Assert.Equal(export.LayoutPlan.Watermark.Corner, preview.LayoutPlan.Watermark.Corner);
            Assert.Equal(WatermarkCorner.TopRight, export.LayoutPlan.Watermark.Corner);
        }

        [Fact]
        public void WithoutVideoBothPlansAreTheSame()
        {
            ProjectDocument d = ProjectDefaults.NewProject("p1", "P", "2026-10-18T12:00:00Z", "ring_escape", 600);
            var plans = StudioFramePlans.Compose(ProjectComposition.BuildFrameRequest(d, 540, 960, default(IntVec2), false, null, null));
            Assert.Same(plans.LayoutPlan, plans.ExpandSimPlan);
            Assert.False(plans.LayoutPlan.HasWatermark);
        }
    }

    public class HudCounterTests
    {
        [Fact]
        public void RemainingUsesTotalMinusProgressOrProgress()
        {
            Assert.Null(HudCounter.Remaining(null));
            var f = new RenderFrame();
            Assert.Null(HudCounter.Remaining(f));
            f.AddHud(HudCounter.ProgressKey, 4);
            Assert.Equal(4, HudCounter.Remaining(f));
            f.AddHud(HudCounter.ProgressTotalKey, 10);
            Assert.Equal(6, HudCounter.Remaining(f));
            var over = new RenderFrame();
            over.AddHud(HudCounter.ProgressKey, 12);
            over.AddHud(HudCounter.ProgressTotalKey, 10);
            Assert.Equal(0, HudCounter.Remaining(over));
            var openEnded = new RenderFrame();
            openEnded.AddHud(HudCounter.ProgressKey, 3);
            openEnded.AddHud(HudCounter.ProgressTotalKey, 0);
            Assert.Equal(3, HudCounter.Remaining(openEnded));
        }
    }
}
