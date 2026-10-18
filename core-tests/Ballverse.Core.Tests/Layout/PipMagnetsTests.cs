using System;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Xunit;

namespace Ballverse.Core.Tests.Layout
{
    public class PipMagnetsTests
    {
        static readonly PipSpec s_pip = new PipSpec(PipShape.RoundedRect, 0.35f, new FloatVec2(0.5f, 0.5f));

        [Fact]
        public void WithoutMasksSnapsToNearestSafeCorner()
        {
            PipSnapResult r = PipMagnets.Snap(1080, 1920, s_pip, new FloatVec2(0.9f, 0.9f), RectUnion.Empty);
            Assert.Equal(PipCorner.BottomRight, r.Corner);
            Assert.Equal(new IntRect(670, 1216, 378, 672), r.Rect);
            Assert.Equal(0L, r.MaskOverlap);

            Assert.Equal(PipCorner.TopLeft, PipMagnets.Snap(1080, 1920, s_pip, new FloatVec2(0.1f, 0.2f), RectUnion.Empty).Corner);
            Assert.Equal(PipCorner.TopRight, PipMagnets.Snap(1080, 1920, s_pip, new FloatVec2(0.8f, 0.1f), RectUnion.Empty).Corner);
            Assert.Equal(PipCorner.BottomLeft, PipMagnets.Snap(1080, 1920, s_pip, new FloatVec2(0.4f, 0.6f), RectUnion.Empty).Corner);
            Assert.Equal(new IntRect(32, 32, 378, 672), PipMagnets.Snap(1080, 1920, s_pip, FloatVec2.Zero, RectUnion.Empty).Rect);
        }

        [Fact]
        public void ExactTieGoesToTopLeft()
        {
            // The dragged window centered on the canvas is equidistant from all four anchors.
            PipSnapResult r = PipMagnets.Snap(1080, 1920, s_pip, new FloatVec2(0.5f, 0.5f), RectUnion.Empty);
            Assert.Equal(PipCorner.TopLeft, r.Corner);
        }

        [Fact]
        public void DefaultMasksPushAnchorsOutOfPlatformUi()
        {
            RectUnion masks = PlatformUiMasks.Default.ToPixels(1080, 1920);
            PipAnchor[] a = PipMagnets.ComputeAnchors(1080, 1920, s_pip, masks);
            Assert.Equal(PipCorner.TopLeft, a[0].Corner);
            // Top-left slides below the tallest top bar (125) + 32 px gap.
            Assert.Equal(new IntRect(32, 157, 378, 672), a[0].Rect);
            Assert.Equal(0L, a[0].MaskOverlap);
            // Bottom-left slides above the highest caption (1478) - 32 px gap.
            Assert.Equal(new IntRect(32, 774, 378, 672), a[2].Rect);
            Assert.Equal(0L, a[2].MaskOverlap);
            // Right corners cannot escape the right action rails: they stay in the corner and overlap.
            Assert.Equal(new IntRect(670, 32, 378, 672), a[1].Rect);
            Assert.True(a[1].MaskOverlap > 0);
            Assert.Equal(new IntRect(670, 1216, 378, 672), a[3].Rect);
            Assert.True(a[3].MaskOverlap > 0);
            Assert.Equal(masks.IntersectionArea(a[3].Rect), a[3].MaskOverlap);
        }

        [Fact]
        public void SnapPrefersMaskFreeAnchorsOverNearerOnes()
        {
            RectUnion masks = PlatformUiMasks.Default.ToPixels(1080, 1920);
            PipSnapResult nearBr = PipMagnets.Snap(1080, 1920, s_pip, new FloatVec2(0.95f, 0.95f), masks);
            Assert.Equal(PipCorner.BottomLeft, nearBr.Corner);
            Assert.Equal(0L, nearBr.MaskOverlap);
            PipSnapResult nearTr = PipMagnets.Snap(1080, 1920, s_pip, new FloatVec2(0.95f, 0.05f), masks);
            Assert.Equal(PipCorner.TopLeft, nearTr.Corner);
            Assert.Equal(4, nearTr.Anchors.Count);
        }

        [Fact]
        public void WhenAllAnchorsOverlapLeastOverlapWins()
        {
            // Full-height columns cannot be escaped vertically. Left column overlaps the left anchors by 68 px,
            // right column the right anchors by 48 px.
            var masks = new RectUnion(new[] { new IntRect(0, 0, 100, 1920), new IntRect(1000, 0, 80, 1920) });
            PipAnchor[] a = PipMagnets.ComputeAnchors(1080, 1920, s_pip, masks);
            Assert.Equal(68L * 672, a[0].MaskOverlap);
            Assert.Equal(48L * 672, a[1].MaskOverlap);
            PipSnapResult r = PipMagnets.Snap(1080, 1920, s_pip, new FloatVec2(0.05f, 0.05f), masks);
            Assert.Equal(PipCorner.TopRight, r.Corner);
            Assert.Equal(48L * 672, r.MaskOverlap);
            PipSnapResult r2 = PipMagnets.Snap(1080, 1920, s_pip, new FloatVec2(0.05f, 0.95f), masks);
            Assert.Equal(PipCorner.BottomRight, r2.Corner);
        }

        [Fact]
        public void SnappedSpecReproducesRectThroughLayout()
        {
            RectUnion masks = PlatformUiMasks.Default.ToPixels(1080, 1920);
            var rng = Rng.FromSeed(5, 5);
            for (int i = 0; i < 300; i++)
            {
                int w = rng.NextInt(100, 2000), h = rng.NextInt(100, 3000);
                var pip = new PipSpec(rng.NextBool() ? PipShape.Circle : PipShape.RoundedRect, rng.NextFloat(0.2f, 0.6f), FloatVec2.Zero);
                RectUnion m = PlatformUiMasks.Default.ToPixels(w, h);
                PipSnapResult r = PipMagnets.Snap(w, h, pip, new FloatVec2(rng.NextFloat(-0.5f, 1.5f), rng.NextFloat(-0.5f, 1.5f)), m);
                var spec = new LayoutSpec(LayoutType.L3, false, 0.5f, 0, 24, 0, r.Pip);
                Assert.Equal(r.Rect, LayoutCalculator.ComputeSlots(w, h, spec).Window.Rect);
                Assert.True(new IntRect(0, 0, w, h).Contains(r.Rect));
                // The chosen anchor has the minimum overlap among all anchors.
                foreach (PipAnchor a in r.Anchors) Assert.True(r.MaskOverlap <= a.MaskOverlap);
            }
        }

        [Fact]
        public void CircleWindowAnchorsAreSquare()
        {
            var circle = new PipSpec(PipShape.Circle, 0.3f, FloatVec2.Zero);
            PipSnapResult r = PipMagnets.Snap(720, 1280, circle, new FloatVec2(1f, 1f), RectUnion.Empty);
            // 216 px circle, margin 21 px at 720 width.
            Assert.Equal(new IntRect(720 - 21 - 216, 1280 - 21 - 216, 216, 216), r.Rect);
            Assert.Equal(PipShape.Circle, r.Pip.Shape);
        }

        [Fact]
        public void HugeWindowOnSmallCanvasStillFits()
        {
            var big = new PipSpec(PipShape.RoundedRect, 0.6f, FloatVec2.Zero);
            PipSnapResult r = PipMagnets.Snap(40, 40, big, new FloatVec2(1f, 0f), RectUnion.Empty);
            Assert.True(new IntRect(0, 0, 40, 40).Contains(r.Rect));
        }

        [Fact]
        public void InvalidArgumentsThrow()
        {
            Assert.Throws<ArgumentNullException>(() => PipMagnets.Snap(1080, 1920, null, FloatVec2.Zero, RectUnion.Empty));
            Assert.Throws<ArgumentNullException>(() => PipMagnets.Snap(1080, 1920, s_pip, FloatVec2.Zero, null));
            Assert.Throws<ArgumentException>(() => PipMagnets.Snap(1080, 1920, s_pip, new FloatVec2(float.NaN, 0f), RectUnion.Empty));
            Assert.Throws<ArgumentOutOfRangeException>(() => PipMagnets.Snap(4, 1920, s_pip, FloatVec2.Zero, RectUnion.Empty));
        }
    }
}
