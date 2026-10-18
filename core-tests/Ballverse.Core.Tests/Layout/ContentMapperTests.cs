using System;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Xunit;

namespace Ballverse.Core.Tests.Layout
{
    public class ContentMapperTests
    {
        const float Eps = 1e-3f;

        static SlotContent C(int rotation = 0, bool mirror = false, SlotFill fill = SlotFill.Cover, float panX = 0f, float panY = 0f, float zoom = 1f)
        {
            return new SlotContent(rotation, mirror, fill, new FloatVec2(panX, panY), zoom);
        }

        static void AssertRect(FloatRect expected, FloatRect actual)
        {
            Assert.True(expected.ApproximatelyEquals(actual, 0.01f), "expected " + expected + " actual " + actual);
        }

        // Landscape 1920x1080 phone video into the L1 bottom slot 1080x960.

        [Fact]
        public void CoverCropsSidesOfLandscapeVideo()
        {
            ContentMapping m = ContentMapper.Map(1920, 1080, 1080, 960, SlotContent.Default);
            Assert.Equal(1920, m.OrientedWidth);
            Assert.Equal(1080, m.OrientedHeight);
            Assert.Equal(960f / 1080f, m.Scale, 5);
            // visible width = 1080 / (8/9) = 1215, centered
            AssertRect(FloatRect.FromEdges(352.5f, 0f, 1567.5f, 1080f), m.SourceCrop);
            AssertRect(m.SourceCrop, m.DisplayCrop);
            Assert.Equal(new IntRect(0, 0, 1080, 960), m.Destination);
            Assert.False(m.HasLetterbox);
            Assert.True(m.Uv.ApproximatelyEquals(new UvTransform(1215f / 1920f, 0f, 0f, 1f, 352.5f / 1920f, 0f), Eps), m.Uv.ToString());
        }

        [Fact]
        public void FitLetterboxesLandscapeVideo()
        {
            ContentMapping m = ContentMapper.Map(1920, 1080, 1080, 960, C(fill: SlotFill.Fit));
            Assert.Equal(0.5625f, m.Scale, 5);
            AssertRect(new FloatRect(0f, 0f, 1920f, 1080f), m.SourceCrop);
            // displayed height 607.5 -> margin round(176.25) = 176, symmetric
            Assert.Equal(new IntRect(0, 176, 1080, 608), m.Destination);
            Assert.True(m.HasLetterbox);
            Assert.True(m.Uv.ApproximatelyEquals(UvTransform.Identity, Eps));
        }

        [Fact]
        public void PanMovesContentAndIsClamped()
        {
            // pan.x = +1: content moved fully right -> left edge of the video visible.
            ContentMapping right = ContentMapper.Map(1920, 1080, 1080, 960, C(panX: 1f));
            AssertRect(FloatRect.FromEdges(0f, 0f, 1215f, 1080f), right.SourceCrop);
            ContentMapping left = ContentMapper.Map(1920, 1080, 1080, 960, C(panX: -1f));
            AssertRect(FloatRect.FromEdges(705f, 0f, 1920f, 1080f), left.SourceCrop);
            ContentMapping half = ContentMapper.Map(1920, 1080, 1080, 960, C(panX: 0.5f));
            AssertRect(FloatRect.FromEdges(176.25f, 0f, 1391.25f, 1080f), half.SourceCrop);
            // Beyond the range: clamped, never an empty edge.
            ContentMapping far = ContentMapper.Map(1920, 1080, 1080, 960, C(panX: 7f, panY: -3f));
            AssertRect(right.SourceCrop, far.SourceCrop);
            Assert.Equal(new FloatVec2(1f, -1f), C(panX: 7f, panY: -3f).Pan);
        }

        [Fact]
        public void MirrorFlipsHorizontallyInDisplaySpace()
        {
            ContentMapping m = ContentMapper.Map(1920, 1080, 1080, 960, C(mirror: true, panX: 1f));
            // Display crop is the left part of the mirrored picture = the right part of the source.
            AssertRect(FloatRect.FromEdges(0f, 0f, 1215f, 1080f), m.DisplayCrop);
            AssertRect(FloatRect.FromEdges(705f, 0f, 1920f, 1080f), m.SourceCrop);
            Assert.True(m.Uv.ApproximatelyEquals(new UvTransform(-1215f / 1920f, 0f, 0f, 1f, 1f, 0f), Eps), m.Uv.ToString());
        }

        [Fact]
        public void Rotation90CoverConcreteNumbers()
        {
            ContentMapping m = ContentMapper.Map(1920, 1080, 1080, 960, C(rotation: 90));
            Assert.Equal(1080, m.OrientedWidth);
            Assert.Equal(1920, m.OrientedHeight);
            Assert.Equal(1f, m.Scale, 5);
            AssertRect(FloatRect.FromEdges(0f, 480f, 1080f, 1440f), m.DisplayCrop);
            AssertRect(FloatRect.FromEdges(480f, 0f, 1440f, 1080f), m.SourceCrop);
            // u = 0.25 + 0.5 y ; v = 1 - x
            Assert.True(m.Uv.ApproximatelyEquals(new UvTransform(0f, 0.5f, -1f, 0f, 0.25f, 1f), Eps), m.Uv.ToString());
        }

        [Fact]
        public void Rotation90And270DifferWhenPanned()
        {
            // pan.y = +1: content moved down -> top of the displayed picture visible: display y [0, 960].
            ContentMapping r90 = ContentMapper.Map(1920, 1080, 1080, 960, C(rotation: 90, panY: 1f));
            ContentMapping r270 = ContentMapper.Map(1920, 1080, 1080, 960, C(rotation: 270, panY: 1f));
            AssertRect(FloatRect.FromEdges(0f, 0f, 1080f, 960f), r90.DisplayCrop);
            AssertRect(FloatRect.FromEdges(0f, 0f, 960f, 1080f), r90.SourceCrop);
            AssertRect(FloatRect.FromEdges(960f, 0f, 1920f, 1080f), r270.SourceCrop);
        }

        [Fact]
        public void Rotation180FlipsBothAxes()
        {
            ContentMapping m = ContentMapper.Map(1920, 1080, 1080, 960, C(rotation: 180, panX: 1f));
            AssertRect(FloatRect.FromEdges(0f, 0f, 1215f, 1080f), m.DisplayCrop);
            AssertRect(FloatRect.FromEdges(705f, 0f, 1920f, 1080f), m.SourceCrop);
            Assert.True(m.Uv.ApproximatelyEquals(new UvTransform(-1215f / 1920f, 0f, 0f, -1f, 1f, 1f), Eps), m.Uv.ToString());
        }

        [Fact]
        public void Rotation90FitLetterboxesSides()
        {
            ContentMapping m = ContentMapper.Map(1920, 1080, 1080, 960, C(rotation: 90, fill: SlotFill.Fit));
            Assert.Equal(0.5f, m.Scale, 5);
            Assert.Equal(new IntRect(270, 0, 540, 960), m.Destination);
            AssertRect(new FloatRect(0f, 0f, 1920f, 1080f), m.SourceCrop);
        }

        [Fact]
        public void FitWithZoomCropsOnlyOverflowingAxis()
        {
            // Fit scale 0.5625, zoom 1.6 -> scale 0.9: displayed 1728 x 972 in a 1080 x 960 slot: crops both axes.
            ContentMapping both = ContentMapper.Map(1920, 1080, 1080, 960, C(fill: SlotFill.Fit, zoom: 1.6f));
            Assert.Equal(new IntRect(0, 0, 1080, 960), both.Destination);
            // zoom 1.25 -> scale 0.703125: displayed 1350 x 759.375: crops X, letterboxes Y (margin 100.3 -> 100), pan.y ignored.
            ContentMapping one = ContentMapper.Map(1920, 1080, 1080, 960, C(fill: SlotFill.Fit, zoom: 1.25f, panY: 1f));
            Assert.Equal(new IntRect(0, 100, 1080, 760), one.Destination);
            AssertRect(FloatRect.FromEdges(192f, 0f, 1728f, 1080f), one.SourceCrop);
        }

        [Fact]
        public void PortraitVideoCoverInPortraitCanvasIsIdentity()
        {
            ContentMapping m = ContentMapper.Map(1080, 1920, 1080, 1920, SlotContent.Default);
            AssertRect(new FloatRect(0f, 0f, 1080f, 1920f), m.SourceCrop);
            Assert.Equal(new IntRect(0, 0, 1080, 1920), m.Destination);
            Assert.True(m.Uv.ApproximatelyEquals(UvTransform.Identity, 1e-6f));
        }

        [Fact]
        public void ZoomShrinksVisibleRegion()
        {
            ContentMapping m = ContentMapper.Map(1080, 1920, 1080, 1920, C(zoom: 2f));
            AssertRect(FloatRect.FromEdges(270f, 480f, 810f, 1440f), m.SourceCrop);
            ContentMapping p = ContentMapper.Map(1080, 1920, 1080, 1920, C(zoom: 2f, panX: -1f, panY: -1f));
            AssertRect(FloatRect.FromEdges(540f, 960f, 1080f, 1920f), p.SourceCrop);
        }

        [Fact]
        public void UvMapsDestinationCornersToSourceCropCorners()
        {
            var rng = Rng.FromSeed(4242, 9);
            for (int i = 0; i < 1000; i++)
            {
                int sw = rng.NextInt(16, 4000), sh = rng.NextInt(16, 4000);
                int dw = rng.NextInt(8, 2000), dh = rng.NextInt(8, 2000);
                var c = new SlotContent(rng.NextInt(0, 4) * 90, rng.NextBool(), rng.NextBool() ? SlotFill.Cover : SlotFill.Fit,
                    new FloatVec2(rng.NextFloat(-1f, 1f), rng.NextFloat(-1f, 1f)), rng.NextFloat(1f, 8f));
                ContentMapping m = ContentMapper.Map(sw, sh, dw, dh, c);
                float minU = float.MaxValue, maxU = float.MinValue, minV = float.MaxValue, maxV = float.MinValue;
                foreach (var p in new[] { new FloatVec2(0, 0), new FloatVec2(1, 0), new FloatVec2(0, 1), new FloatVec2(1, 1) })
                {
                    FloatVec2 uv = m.Uv.Apply(p);
                    minU = Math.Min(minU, uv.X); maxU = Math.Max(maxU, uv.X);
                    minV = Math.Min(minV, uv.Y); maxV = Math.Max(maxV, uv.Y);
                }
                var uvRect = FloatRect.FromEdges(minU * sw, minV * sh, maxU * sw, maxV * sh);
                Assert.True(uvRect.ApproximatelyEquals(m.SourceCrop, Math.Max(sw, sh) * 1e-4f), uvRect + " vs " + m.SourceCrop);
            }
        }

        [Fact]
        public void CoverNeverShowsEmptyEdgesProperty()
        {
            var rng = Rng.FromSeed(31337, 4);
            for (int i = 0; i < 5000; i++)
            {
                int sw = rng.NextInt(1, 5000), sh = rng.NextInt(1, 5000);
                int dw = rng.NextInt(1, 3000), dh = rng.NextInt(1, 3000);
                var c = new SlotContent(rng.NextInt(-4, 8) * 90, rng.NextBool(), SlotFill.Cover,
                    new FloatVec2(rng.NextFloat(-5f, 5f), rng.NextFloat(-5f, 5f)), rng.NextFloat(0.2f, 12f));
                ContentMapping m = ContentMapper.Map(sw, sh, dw, dh, c);
                string ctx = sw + "x" + sh + " -> " + dw + "x" + dh + " rot=" + c.Rotation + " zoom=" + c.Zoom + " pan=" + c.Pan;

                Assert.Equal(new IntRect(0, 0, dw, dh), m.Destination);
                Assert.False(m.HasLetterbox);
                float tol = Math.Max(sw, sh) * 1e-5f;
                FloatRect crop = m.SourceCrop;
                Assert.True(crop.X >= -tol && crop.Y >= -tol && crop.Right <= sw + tol && crop.Bottom <= sh + tol, ctx + " crop " + crop);
                Assert.True(crop.Width > 0f && crop.Height > 0f, ctx);
                // The visible region has the slot's aspect (no stretching) in display space.
                double cropAspect = m.DisplayCrop.Width / (double)m.DisplayCrop.Height;
                double slotAspect = dw / (double)dh;
                Assert.True(Math.Abs(cropAspect / slotAspect - 1.0) < 1e-3, ctx);
                // Uv stays inside the texture.
                foreach (var p in new[] { new FloatVec2(0, 0), new FloatVec2(1, 1) })
                {
                    FloatVec2 uv = m.Uv.Apply(p);
                    Assert.InRange(uv.X, -1e-4f, 1f + 1e-4f);
                    Assert.InRange(uv.Y, -1e-4f, 1f + 1e-4f);
                }
            }
        }

        [Fact]
        public void FitAtZoomOneShowsWholeSourceProperty()
        {
            var rng = Rng.FromSeed(8, 8);
            for (int i = 0; i < 3000; i++)
            {
                int sw = rng.NextInt(1, 5000), sh = rng.NextInt(1, 5000);
                int dw = rng.NextInt(1, 3000), dh = rng.NextInt(1, 3000);
                var c = new SlotContent(rng.NextInt(0, 4) * 90, rng.NextBool(), SlotFill.Fit,
                    new FloatVec2(rng.NextFloat(-1f, 1f), rng.NextFloat(-1f, 1f)), 1f);
                ContentMapping m = ContentMapper.Map(sw, sh, dw, dh, c);
                string ctx = sw + "x" + sh + " -> " + dw + "x" + dh + " rot=" + c.Rotation + " dest=" + m.Destination + " crop=" + m.SourceCrop + " s=" + m.Scale;
                Assert.True(m.SourceCrop.ApproximatelyEquals(new FloatRect(0, 0, sw, sh), Math.Max(sw, sh) * 1e-5f), ctx);
                Assert.True(new IntRect(0, 0, dw, dh).Contains(m.Destination), ctx);
                Assert.True(m.Destination.Width >= 1 && m.Destination.Height >= 1, ctx);
                // Symmetric margins.
                Assert.Equal(m.Destination.X, dw - m.Destination.Right);
                Assert.Equal(m.Destination.Y, dh - m.Destination.Bottom);
                // Destination size equals the scaled source within one pixel (a sub-pixel picture still gets 1-2 px).
                double s = m.Scale;
                Assert.True(Math.Abs(m.Destination.Width - m.OrientedWidth * s) <= 1.0 + 1e-3 || m.Destination.Width <= 2, ctx);
                Assert.True(Math.Abs(m.Destination.Height - m.OrientedHeight * s) <= 1.0 + 1e-3 || m.Destination.Height <= 2, ctx);
            }
        }

        [Fact]
        public void BottomLeftOriginConversionIsConsistent()
        {
            ContentMapping m = ContentMapper.Map(1920, 1080, 1080, 960, C(rotation: 90, mirror: true, panX: 0.3f, panY: -0.7f, zoom: 1.7f));
            UvTransform bl = m.Uv.ToBottomLeftOrigin();
            foreach (var p in new[] { new FloatVec2(0, 0), new FloatVec2(1, 0), new FloatVec2(0.3f, 0.8f) })
            {
                FloatVec2 tl = m.Uv.Apply(new FloatVec2(p.X, 1f - p.Y));
                FloatVec2 b = bl.Apply(p);
                Assert.Equal(tl.X, b.X, 4);
                Assert.Equal(1f - tl.Y, b.Y, 4);
            }
        }

        [Fact]
        public void DoubleTapResetKeepsRotationMirrorFill()
        {
            SlotContent c = C(rotation: 270, mirror: true, fill: SlotFill.Fit, panX: 0.4f, panY: -1f, zoom: 3f);
            SlotContent r = c.Reset();
            Assert.Equal(FloatVec2.Zero, r.Pan);
            Assert.Equal(1f, r.Zoom);
            Assert.Equal(270, r.Rotation);
            Assert.True(r.Mirror);
            Assert.Equal(SlotFill.Fit, r.Fill);
        }

        [Fact]
        public void SlotContentNormalizesAndValidates()
        {
            Assert.Equal(270, C(rotation: -90).Rotation);
            Assert.Equal(90, C(rotation: 450).Rotation);
            Assert.Equal(0, C(rotation: 270).RotateClockwise().Rotation);
            Assert.Equal(1f, C(zoom: 0.5f).Zoom);
            Assert.Equal(SlotContent.MaxZoom, C(zoom: 100f).Zoom);
            Assert.Throws<ArgumentOutOfRangeException>(() => C(rotation: 45));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SlotContent(0, false, (SlotFill)5, FloatVec2.Zero, 1f));
            Assert.Throws<ArgumentException>(() => C(zoom: float.NaN));
            Assert.Throws<ArgumentException>(() => new SlotContent(0, false, SlotFill.Cover, new FloatVec2(float.NaN, 0f), 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => ContentMapper.Map(0, 10, 10, 10, SlotContent.Default));
            Assert.Throws<ArgumentOutOfRangeException>(() => ContentMapper.Map(10, 10, 10, -1, SlotContent.Default));
            Assert.Throws<ArgumentNullException>(() => ContentMapper.Map(10, 10, 10, 10, null));
        }

        [Fact]
        public void PanByPixelsFollowsTheFinger()
        {
            // Displayed width 1920 * 8/9 = 1706.67, slack per side 313.33 px.
            SlotContent c = ContentMapper.PanByPixels(SlotContent.Default, 313.3333f, 50f, 1920, 1080, 1080, 960);
            Assert.Equal(1f, c.Pan.X, 3);
            Assert.Equal(0f, c.Pan.Y); // no vertical slack
            SlotContent half = ContentMapper.PanByPixels(SlotContent.Default, -156.6667f, 0f, 1920, 1080, 1080, 960);
            Assert.Equal(-0.5f, half.Pan.X, 3);
            SlotContent clamped = ContentMapper.PanByPixels(c, 5000f, 0f, 1920, 1080, 1080, 960);
            Assert.Equal(1f, clamped.Pan.X);
            Assert.Throws<ArgumentException>(() => ContentMapper.PanByPixels(c, float.NaN, 0f, 1920, 1080, 1080, 960));
        }

        [Fact]
        public void ZoomByClamps()
        {
            Assert.Equal(2f, ContentMapper.ZoomBy(SlotContent.Default, 2f).Zoom);
            Assert.Equal(1f, ContentMapper.ZoomBy(SlotContent.Default, 0.5f).Zoom);
            Assert.Equal(SlotContent.MaxZoom, ContentMapper.ZoomBy(C(zoom: 6f), 2f).Zoom);
            Assert.Throws<ArgumentOutOfRangeException>(() => ContentMapper.ZoomBy(SlotContent.Default, 0f));
        }
    }
}
