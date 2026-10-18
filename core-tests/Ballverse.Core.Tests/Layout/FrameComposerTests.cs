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
            var text = new[] { FloatRect.FromReferencePixels(new IntRect(0, 140, 400, 120)) };
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
            FloatRect box = FloatRect.FromReferencePixels(new IntRect(40, 204, 600, 90));
            FramePlan hi = FrameComposer.Compose(new FrameRequest(1080, 1920, LayoutSpec.Default, textBounds: new[] { box }));
            FramePlan lo = FrameComposer.Compose(new FrameRequest(720, 1280, LayoutSpec.Default, textBounds: new[] { box }));
            Assert.Equal(hi.Watermark.Corner, lo.Watermark.Corner);
            Assert.Equal(WatermarkCorner.TopRight, hi.Watermark.Corner);
            Assert.Equal(new IntRect(832, 186, 216, 48), hi.Watermark.Rect);
            Assert.Equal(new IntRect(555, 124, 144, 32), lo.Watermark.Rect); // round(186 * 1280 / 1920)
        }

        static List<int[]> PreviewAndExportCanvases()
        {
            var list = new List<int[]>(s_canvases);
            for (int w = 300; w <= 1000; w++)
            {
                IntVec2 c = OutputCanvas.FitPreview(w, 4000);
                list.Add(new[] { c.X, c.Y });
            }
            return list;
        }

        /// <summary>
        /// Every watermark edge of <paramref name="actual"/> (canvas w x h) within tolerance of the reference rect scaled by
        /// w / 1080 horizontally and h / 1920 vertically. The anchored edges (top, and the corner's side) are held to 1 px;
        /// the far edges also carry the rounding of <see cref="WatermarkLayout.Size"/> (height 2.5% then width = height x
        /// aspect, i.e. up to 0.5 x 4.5 + 0.5 px plus the 0.5 px edge rounding) on non-exact 9:16 previews such as 326x580.
        /// </summary>
        static void AssertWatermarkScaled(WatermarkPlacement reference, WatermarkPlacement actual, int w, int h, string ctx)
        {
            double kx = w / 1080.0, ky = h / 1920.0;
            IntRect a = reference.Rect, b = actual.Rect;
            string msg = ctx + " " + w + "x" + h + ": " + a + " vs " + b;
            Assert.True(reference.Corner == actual.Corner, msg);
            Assert.True(Math.Abs(a.Y * ky - b.Y) <= 1.0, msg + " top");
            Assert.True(Math.Abs(a.Bottom * ky - b.Bottom) <= 1.5, msg + " bottom");
            bool left = reference.Corner == WatermarkCorner.TopLeft;
            Assert.True(Math.Abs(a.X * kx - b.X) <= (left ? 1.0 : 3.5), msg + " left");
            Assert.True(Math.Abs(a.Right * kx - b.Right) <= (left ? 3.5 : 1.0), msg + " right");
        }

        [Fact]
        public void WatermarkPositionAgreesAcrossCanvasesWithRemoteConfigMasks()
        {
            // QA round 2: masks are Remote Config data (spec §3.3). A top bar plus a second band swept across the
            // reference watermark bottom (234 px = 0.121875) must give the same corner and position on export and on every
            // preview size; re-running the mask push per canvas moved it by hundreds of px.
            List<int[]> canvases = PreviewAndExportCanvases();
            int perCanvasJumps = 0;
            var bands = new List<FloatRect[]>();
            for (int i = 0; i <= 120; i++)
            {
                float top2 = 0.115f + i * 0.0001f;
                bands.Add(new[] { new FloatRect(0f, 0f, 1f, 0.08f), new FloatRect(0f, top2, 0.3f, 0.1f) });
            }
            foreach (float top2 in new[] { 0.115f, 0.12f, 0.1225f, 0.125f, 0.13f })
                bands.Add(new[] { new FloatRect(0f, 0f, 1f, 0.08f), new FloatRect(0f, top2, 1f, 0.05f) });
            // Right-hand band too, so the top-right corner is also exercised (PiP blocks the top-left).
            var pipSpec = LayoutSpec.Default.WithType(LayoutType.L3).WithPip(new PipSpec(SlotShape.RoundedRect, 0.35f, new FloatVec2(0.2f, 0.25f)));
            foreach (FloatRect[] masks in bands)
            {
                foreach (LayoutSpec spec in new[] { LayoutSpec.Default, pipSpec })
                {
                    FramePlan hi = FrameComposer.Compose(new FrameRequest(1080, 1920, spec, masks: masks));
                    string ctx = "band " + masks[1] + " " + spec.Type;
                    for (int c = 1; c < canvases.Count; c++)
                    {
                        int w = canvases[c][0], h = canvases[c][1];
                        FramePlan lo = FrameComposer.Compose(new FrameRequest(w, h, spec, masks: masks));
                        AssertWatermarkScaled(hi.Watermark, lo.Watermark, w, h, ctx);
                        // Scaling may let the watermark touch a mask edge by at most one pixel row on this canvas.
                        if (hi.Watermark.MaskOverlap == 0) Assert.True(lo.Watermark.MaskOverlap <= lo.Watermark.Rect.Width, ctx + " mask overlap");
                        // The old per-canvas push, to prove the sweep reaches the cases it got wrong.
                        RectUnion canvasMasks = RectUnion.FromNormalized(masks, w, h);
                        IntRect naive = WatermarkLayout.CornerRect(w, h, WatermarkLayout.DefaultLogoAspect, hi.Watermark.Corner, canvasMasks);
                        if (Math.Abs(hi.Watermark.Rect.Y * (h / 1920.0) - naive.Y) > 50) perCanvasJumps++;
                    }
                }
            }
            Assert.True(perCanvasJumps > 0, "sweep should contain cases a per-canvas mask push gets wrong");
        }

        [Fact]
        public void TextMeasuredOnceAtTheReferenceKeepsCornerAndPositionOnEveryCanvas()
        {
            // QA round 2: the supported path for text bounds is one measurement in the 1080x1920 reference layout
            // (FloatRect.FromReferencePixels), the same list for every request. The text top slides across the reference
            // watermark bottom (186 + 48 = 234) in 0.01 px steps.
            int perCanvasMismatches = 0;
            for (int i = 0; i <= 6000; i++)
            {
                float topRef = 200f + i * 0.01f;
                var bounds = new[] { FloatRect.FromReferencePixels(new FloatRect(0f, topRef, 600f, 90f)) };
                FramePlan hi = FrameComposer.Compose(new FrameRequest(1080, 1920, LayoutSpec.Default, textBounds: bounds));
                foreach (int[] c in s_canvases)
                {
                    FramePlan lo = FrameComposer.Compose(new FrameRequest(c[0], c[1], LayoutSpec.Default, textBounds: bounds));
                    Assert.True(hi.Watermark.Corner == lo.Watermark.Corner, "topRef " + topRef + " canvas " + c[0] + "x" + c[1]);
                    AssertWatermarkScaled(hi.Watermark, lo.Watermark, c[0], c[1], "topRef " + topRef);

                    // Unsupported path (documented on FrameRequest.TextBounds): measuring the box per canvas in integer
                    // pixels quantizes it differently on each canvas and can flip the corner.
                    double k = c[0] / 1080.0;
                    var measured = new IntRect(0, PixelMath.RoundHalfUp(topRef * k), PixelMath.RoundHalfUp(600 * k), PixelMath.RoundHalfUp(90 * k));
                    FramePlan perCanvas = FrameComposer.Compose(new FrameRequest(c[0], c[1], LayoutSpec.Default,
                        textBounds: new[] { FloatRect.FromPixels(measured, c[0], c[1]) }));
                    if (perCanvas.Watermark.Corner != hi.Watermark.Corner) perCanvasMismatches++;
                }
            }
            Assert.True(perCanvasMismatches > 0, "per-canvas measurement should be shown to disagree");
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
