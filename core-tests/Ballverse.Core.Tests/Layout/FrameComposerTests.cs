using System;
using System.Collections.Generic;
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
            Assert.Equal(new IntRect(32, 186, 216, 48), plan.Watermark.Rect); // below the TikTok top bar (154) + 32
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
            var pip = new PipSpec(SlotShape.RoundedRect, 0.35f, new FloatVec2(0.95f, 0.05f));
            PipSnapResult snapped = PipMagnets.Snap(1080, 1920, pip, pip.Center, masks);
            Assert.Equal(PipCorner.TopLeft, snapped.Corner);
            var spec = new LayoutSpec(LayoutType.L3, false, 0.5f, 0, 24, 0, snapped.Pip);
            FramePlan plan = FrameComposer.Compose(new FrameRequest(1080, 1920, spec, new IntVec2(1080, 1920),
                masks: PlatformUiMasks.Default.Union()));
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
            var text = new[] { FloatRect.FromPixels(new IntRect(0, 140, 400, 120), 1080, 1920) };
            var req = new FrameRequest(1080, 1920, LayoutSpec.Default, textBounds: text);
            FramePlan plan = FrameComposer.Compose(req);
            Assert.Equal(WatermarkCorner.TopRight, plan.Watermark.Corner);
            Assert.True(plan.Watermark.IsClear);
        }

        [Fact]
        public void RequestCopiesListInputs()
        {
            var text = new List<FloatRect>();
            var masks = new List<FloatRect>();
            var req = new FrameRequest(1080, 1920, LayoutSpec.Default, masks: masks, textBounds: text);
            text.Add(new FloatRect(0f, 0f, 0.5f, 0.2f));
            masks.Add(new FloatRect(0f, 0f, 1f, 0.5f));
            Assert.Empty(req.TextBounds);
            Assert.Empty(req.MaskRects);
            Assert.Equal(0, req.Masks.Count);
            FramePlan plan = FrameComposer.Compose(req);
            Assert.Equal(WatermarkCorner.TopLeft, plan.Watermark.Corner);
            Assert.Equal(new IntRect(32, 32, 216, 48), plan.Watermark.Rect);
            Assert.False(req.TextBounds is FloatRect[]);
        }

        [Fact]
        public void DefaultMasksAreTheDefaultPlatformsAtEveryCanvas()
        {
            foreach (int[] c in new[] { new[] { 720, 1280 }, new[] { 1080, 1920 }, new[] { 405, 720 }, new[] { 720, 1280 } })
            {
                var req = new FrameRequest(c[0], c[1], LayoutSpec.Default);
                Assert.Equal(PlatformUiMasks.Default.ToPixels(c[0], c[1]).Rects, req.Masks.Rects);
                Assert.Equal(PlatformUiMasks.Default.ToPixels(1080, 1920).Rects, req.ReferenceMasks.Rects);
                Assert.Equal(PlatformUiMasks.Default.Union(), req.MaskRects);
            }
        }

        static readonly int[][] s_canvases =
        {
            new[] { 1080, 1920 }, new[] { 720, 1280 }, new[] { 540, 960 }, new[] { 405, 720 }, new[] { 360, 640 }, new[] { 393, 699 },
        };

        [Fact]
        public void WatermarkCornerAgreesAcrossCanvasesForPipNearItsEdge()
        {
            // Spec §3.3 acceptance / §3.10: preview (any size, incl. tier C lowered resolution) shows the export's corner.
            // Dense sweeps of PiP centres across the bands where the window edge meets the reference watermark
            // [32,186 216x48]: rect window top edge (cy ~ 0.297), its left edge (cx ~ 0.405), small circle top edge (cy ~ 0.178).
            var rect = new PipSpec(SlotShape.RoundedRect, 0.35f, FloatVec2.Zero);
            var circle = new PipSpec(SlotShape.Circle, 0.2f, FloatVec2.Zero);
            var cases = new List<PipSpec>();
            for (int i = 0; i <= 900; i++)
            {
                cases.Add(rect.WithCenter(new FloatVec2(0f, 0.25f + i * 0.0001f)));
                cases.Add(rect.WithCenter(new FloatVec2(0.25f, 0.25f + i * 0.0001f)));
            }
            for (int i = 0; i <= 300; i++) cases.Add(rect.WithCenter(new FloatVec2(0.39f + i * 0.0001f, 0.2f)));
            for (int i = 0; i <= 500; i++) cases.Add(circle.WithCenter(new FloatVec2(0.1f, 0.15f + i * 0.0001f)));

            int naiveMismatches = 0;
            foreach (PipSpec pip in cases)
            {
                var spec = LayoutSpec.Default.WithType(LayoutType.L3).WithPip(pip);
                WatermarkCorner? expected = null;
                foreach (int[] c in s_canvases)
                {
                    FramePlan plan = FrameComposer.Compose(new FrameRequest(c[0], c[1], spec));
                    if (expected == null) expected = plan.Watermark.Corner;
                    Assert.True(expected == plan.Watermark.Corner,
                        "center " + pip.Center + " canvas " + c[0] + "x" + c[1] + ": " + plan.Watermark.Corner + " vs " + expected);
                }
                // The old per-canvas decision (1080 vs 720), to prove the sweep reaches the 1 px boundary cases.
                WatermarkCorner hi = NaiveCorner(1080, 1920, LayoutCalculator.PipWindowRect(1080, 1920, pip));
                WatermarkCorner lo = NaiveCorner(720, 1280, LayoutCalculator.PipWindowRect(720, 1280, pip));
                if (hi != lo) naiveMismatches++;
            }
            Assert.True(naiveMismatches > 0, "sweep should contain cases a per-canvas decision gets wrong");
        }

        static WatermarkCorner NaiveCorner(int w, int h, IntRect occupied)
        {
            return WatermarkLayout.Place(w, h, WatermarkLayout.DefaultLogoAspect, PlatformUiMasks.Default.ToPixels(w, h), new[] { occupied }).Corner;
        }

        [Fact]
        public void WatermarkCornerAgreesAcrossCanvasesForTextNearItsEdge()
        {
            // Text box whose top edge slides across the watermark bottom (186 + 48 = 234 at the reference) and whose
            // right edge slides across its left edge (32).
            int naiveMismatches = 0;
            for (int i = 0; i <= 600; i++)
            {
                float top = (200f + i * 0.1f) / 1920f;
                float left = (i % 7) * 0.25f / 1080f;
                var boxes = new[]
                {
                    new FloatRect(0.037f, top, 0.555f, 0.047f),
                    new FloatRect(left - 0.5f, 0.09f, 0.5f + 32.5f / 1080f - (i % 3) * 0.6f / 1080f, 0.05f),
                };
                foreach (FloatRect box in boxes)
                {
                    WatermarkCorner? expected = null;
                    WatermarkCorner? naiveFirst = null;
                    foreach (int[] c in s_canvases)
                    {
                        FramePlan plan = FrameComposer.Compose(new FrameRequest(c[0], c[1], LayoutSpec.Default, textBounds: new[] { box }));
                        if (expected == null) expected = plan.Watermark.Corner;
                        Assert.True(expected == plan.Watermark.Corner, "box " + box + " canvas " + c[0] + "x" + c[1]);
                        WatermarkCorner naive = WatermarkLayout.Place(c[0], c[1], WatermarkLayout.DefaultLogoAspect,
                            PlatformUiMasks.Default.ToPixels(c[0], c[1]), new[] { box.ToPixels(c[0], c[1]) }).Corner;
                        if (naiveFirst == null) naiveFirst = naive;
                        else if (naive != naiveFirst) naiveMismatches++;
                    }
                }
            }
            Assert.True(naiveMismatches > 0, "sweep should contain cases a per-canvas decision gets wrong");
        }

        [Fact]
        public void QaCaseTextBoxScaledTo720KeepsTheCorner()
        {
            // 1080: [40,204 600x90]; the same box at 720 is [27,136 400x60].
            FloatRect box = FloatRect.FromPixels(new IntRect(40, 204, 600, 90), 1080, 1920);
            FramePlan hi = FrameComposer.Compose(new FrameRequest(1080, 1920, LayoutSpec.Default, textBounds: new[] { box }));
            FramePlan lo = FrameComposer.Compose(new FrameRequest(720, 1280, LayoutSpec.Default, textBounds: new[] { box }));
            Assert.Equal(hi.Watermark.Corner, lo.Watermark.Corner);
            Assert.Equal(WatermarkCorner.TopRight, hi.Watermark.Corner);
            Assert.Equal(new IntRect(832, 186, 216, 48), hi.Watermark.Rect);
            Assert.Equal(new IntRect(555, 123, 144, 32), lo.Watermark.Rect);
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
            // Exact 9:16 canvases scale uniformly, so every slot edge must agree with the 1080 plan within 1 px.
            var sizes = new[] { new[] { 720, 1280 }, new[] { 540, 960 }, new[] { 405, 720 } };
            foreach (LayoutType type in new[] { LayoutType.L1, LayoutType.L2, LayoutType.L3, LayoutType.L4, LayoutType.L5 })
            {
                foreach (bool swap in new[] { false, true })
                {
                    var spec = new LayoutSpec(type, swap, 0.58f, 8, 30, 12);
                    FramePlan hi = FrameComposer.Compose(new FrameRequest(1080, 1920, spec, new IntVec2(1920, 1080)));
                    foreach (int[] size in sizes)
                    {
                        string ctx = type + " swap=" + swap + " " + size[0] + "x" + size[1];
                        double k = size[0] / 1080.0;
                        FramePlan lo = FrameComposer.Compose(new FrameRequest(size[0], size[1], spec, new IntVec2(1920, 1080)));
                        FramePlan again = FrameComposer.Compose(new FrameRequest(size[0], size[1], spec, new IntVec2(1920, 1080)));
                        Assert.Equal(hi.Layout.EffectiveType, lo.Layout.EffectiveType);
                        Assert.Equal(hi.Layout.SlotsBackToFront.Count, lo.Layout.SlotsBackToFront.Count);
                        for (int i = 0; i < hi.Layout.SlotsBackToFront.Count; i++)
                        {
                            SlotGeometry a = hi.Layout.SlotsBackToFront[i], b = lo.Layout.SlotsBackToFront[i];
                            Assert.Equal(a.Kind, b.Kind);
                            Assert.Equal(a.Shape, b.Shape);
                            AssertScaled(a.Rect, b.Rect, k, ctx);
                        }
                        AssertScaled(hi.Layout.Divider, lo.Layout.Divider, k, ctx + " divider");
                        Assert.Equal(hi.Watermark.Corner, lo.Watermark.Corner);
                        AssertScaled(hi.Watermark.Rect, lo.Watermark.Rect, k, ctx + " watermark");
                        Assert.Equal(hi.HasVideoMapping, lo.HasVideoMapping);
                        if (hi.HasVideoMapping)
                        {
                            Assert.True(hi.VideoMapping.Uv.ApproximatelyEquals(lo.VideoMapping.Uv, 4e-3f), ctx);
                            AssertScaled(hi.VideoDestination, lo.VideoDestination, k, ctx + " video");
                        }
                        // Same request -> identical plan (determinism).
                        Assert.Equal(lo.Layout.Simulation, again.Layout.Simulation);
                        Assert.Equal(lo.Watermark.Rect, again.Watermark.Rect);
                    }
                }
            }
        }

        static void AssertScaled(IntRect reference, IntRect actual, double k, string ctx)
        {
            Assert.True(Math.Abs(reference.X * k - actual.X) <= 1.0, ctx + " left " + reference + " vs " + actual);
            Assert.True(Math.Abs(reference.Y * k - actual.Y) <= 1.0, ctx + " top " + reference + " vs " + actual);
            Assert.True(Math.Abs(reference.Right * k - actual.Right) <= 1.0, ctx + " right " + reference + " vs " + actual);
            Assert.True(Math.Abs(reference.Bottom * k - actual.Bottom) <= 1.0, ctx + " bottom " + reference + " vs " + actual);
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
