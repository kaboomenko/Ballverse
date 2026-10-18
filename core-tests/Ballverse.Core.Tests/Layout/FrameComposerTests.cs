using System;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Xunit;

namespace Ballverse.Core.Tests.Layout
{
    public class FrameComposerTests
    {
        [Fact]
        public void L1WithVideoMapsIntoBottomSlot()
        {
            var req = new FrameRequest(1080, 1920, LayoutSpec.Default, new IntVec2(1920, 1080),
                new SlotContent(0, false, SlotFill.Fit, FloatVec2.Zero, 1f));
            FramePlan plan = FrameComposer.Compose(req);
            Assert.True(plan.HasVideoMapping);
            Assert.Equal(new IntRect(0, 963, 1080, 957), plan.Layout.Video.Rect);
            // displayed height 607.5 -> margin round(174.75) = 175
            Assert.Equal(new IntRect(0, 175, 1080, 607), plan.VideoMapping.Destination);
            Assert.Equal(new IntRect(0, 963 + 175, 1080, 607), plan.VideoDestination);
            Assert.True(plan.HasWatermark);
            Assert.Equal(new IntRect(32, 157, 216, 48), plan.Watermark.Rect);
        }

        [Fact]
        public void NoVideoSourceMeansNoMapping()
        {
            FramePlan plan = FrameComposer.Compose(new FrameRequest(720, 1280, LayoutSpec.Default));
            Assert.False(plan.HasVideoMapping);
            Assert.Throws<InvalidOperationException>(() => plan.VideoMapping);
            Assert.Equal(IntRect.Empty, plan.VideoDestination);
        }

        [Fact]
        public void L4IgnoresVideo()
        {
            var req = new FrameRequest(1080, 1920, LayoutSpec.Default.WithType(LayoutType.L4), new IntVec2(1920, 1080));
            FramePlan plan = FrameComposer.Compose(req);
            Assert.False(plan.Layout.HasVideo);
            Assert.False(plan.HasVideoMapping);
        }

        [Fact]
        public void PipAtTopLeftPushesWatermarkRight()
        {
            RectUnion masks = PlatformUiMasks.Default.ToPixels(1080, 1920);
            var pip = new PipSpec(PipShape.RoundedRect, 0.35f, new FloatVec2(0.95f, 0.05f));
            PipSnapResult snapped = PipMagnets.Snap(1080, 1920, pip, pip.Center, masks);
            Assert.Equal(PipCorner.TopLeft, snapped.Corner);
            var spec = new LayoutSpec(LayoutType.L3, false, 0.5f, 0, 24, 0, snapped.Pip);
            FramePlan plan = FrameComposer.Compose(new FrameRequest(1080, 1920, spec, new IntVec2(1080, 1920), masks: masks));
            Assert.Equal(snapped.Rect, plan.Layout.Window.Rect);
            Assert.Equal(WatermarkCorner.TopRight, plan.Watermark.Corner);
            Assert.True(plan.Watermark.IsClear);
            // The video fills the window (Cover, same aspect).
            Assert.Equal(new IntRect(0, 0, 378, 672), plan.VideoMapping.Destination);
            Assert.Equal(snapped.Rect, plan.VideoDestination);
        }

        [Fact]
        public void TextBoundsAffectWatermark()
        {
            var req = new FrameRequest(1080, 1920, LayoutSpec.Default, textBounds: new[] { new IntRect(0, 140, 400, 120) });
            Assert.Equal(WatermarkCorner.TopRight, FrameComposer.Compose(req).Watermark.Corner);
        }

        [Fact]
        public void WatermarkCanBeOff()
        {
            FramePlan plan = FrameComposer.Compose(new FrameRequest(1080, 1920, LayoutSpec.Default, showWatermark: false));
            Assert.False(plan.HasWatermark);
        }

        [Fact]
        public void PreviewAndExportAgreeForAllLayoutsAndSwaps()
        {
            foreach (LayoutType type in new[] { LayoutType.L1, LayoutType.L2, LayoutType.L3, LayoutType.L4, LayoutType.L5 })
            {
                foreach (bool swap in new[] { false, true })
                {
                    var spec = new LayoutSpec(type, swap, 0.58f, 8, 30, 12);
                    FramePlan hi = FrameComposer.Compose(new FrameRequest(1080, 1920, spec, new IntVec2(1920, 1080)));
                    FramePlan lo = FrameComposer.Compose(new FrameRequest(720, 1280, spec, new IntVec2(1920, 1080)));
                    FramePlan again = FrameComposer.Compose(new FrameRequest(720, 1280, spec, new IntVec2(1920, 1080)));
                    Assert.Equal(hi.Layout.EffectiveType, lo.Layout.EffectiveType);
                    Assert.Equal(hi.Layout.SlotsBackToFront.Count, lo.Layout.SlotsBackToFront.Count);
                    for (int i = 0; i < hi.Layout.SlotsBackToFront.Count; i++)
                    {
                        SlotGeometry a = hi.Layout.SlotsBackToFront[i], b = lo.Layout.SlotsBackToFront[i];
                        Assert.Equal(a.Kind, b.Kind);
                        Assert.True(Math.Abs(a.Rect.X * 2.0 / 3.0 - b.Rect.X) <= 1.0);
                        Assert.True(Math.Abs(a.Rect.Bottom * 2.0 / 3.0 - b.Rect.Bottom) <= 1.0);
                    }
                    Assert.Equal(hi.Watermark.Corner, lo.Watermark.Corner);
                    Assert.Equal(hi.HasVideoMapping, lo.HasVideoMapping);
                    if (hi.HasVideoMapping)
                    {
                        Assert.True(hi.VideoMapping.Uv.ApproximatelyEquals(lo.VideoMapping.Uv, 2e-3f));
                    }
                    // Same request -> identical plan (determinism).
                    Assert.Equal(lo.Layout.Simulation, again.Layout.Simulation);
                    Assert.Equal(lo.Watermark.Rect, again.Watermark.Rect);
                }
            }
        }

        [Fact]
        public void RequestValidation()
        {
            Assert.Throws<ArgumentNullException>(() => FrameComposer.Compose(null));
            Assert.Throws<ArgumentNullException>(() => new FrameRequest(1080, 1920, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameRequest(1080, 1920, LayoutSpec.Default, new IntVec2(-1, 5)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new FrameRequest(1, 1920, LayoutSpec.Default));
            var r = new FrameRequest(1080, 1920, LayoutSpec.Default, new IntVec2(0, 1080));
            Assert.False(r.HasVideoSource);
            Assert.Same(SlotContent.Default, r.VideoContent);
        }
    }
}
