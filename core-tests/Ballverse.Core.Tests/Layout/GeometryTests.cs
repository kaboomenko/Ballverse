using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Xunit;

namespace Ballverse.Core.Tests.Layout
{
    public class GeometryTests
    {
        [Fact]
        public void IntRectEdgesAndArea()
        {
            var r = new IntRect(10, 20, 30, 40);
            Assert.Equal(40, r.Right);
            Assert.Equal(60, r.Bottom);
            Assert.Equal(1200L, r.Area);
            Assert.False(r.IsEmpty);
            Assert.Equal(new FloatVec2(25f, 40f), r.Center);
            Assert.Equal(r, IntRect.FromEdges(10, 20, 40, 60));
            Assert.True(IntRect.FromEdges(5, 5, 4, 9).IsEmpty);
        }

        [Fact]
        public void IntRectRejectsNegativeSize()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new IntRect(0, 0, -1, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new IntRect(0, 0, 1, -1));
        }

        [Fact]
        public void IntRectIsHalfOpen()
        {
            var a = new IntRect(0, 0, 10, 10);
            var touching = new IntRect(10, 0, 10, 10);
            Assert.False(a.Intersects(touching));
            Assert.Equal(0L, a.IntersectionArea(touching));
            Assert.True(a.Contains(9, 9));
            Assert.False(a.Contains(10, 9));
            var overlap = new IntRect(9, 9, 10, 10);
            Assert.True(a.Intersects(overlap));
            Assert.Equal(new IntRect(9, 9, 1, 1), a.Intersection(overlap));
            Assert.False(a.Intersects(new IntRect(5, 5, 0, 0)));
        }

        [Fact]
        public void IntRectContainsRect()
        {
            var a = new IntRect(0, 0, 10, 10);
            Assert.True(a.Contains(new IntRect(0, 0, 10, 10)));
            Assert.True(a.Contains(new IntRect(2, 2, 3, 3)));
            Assert.False(a.Contains(new IntRect(2, 2, 9, 3)));
        }

        [Fact]
        public void IntRectClampInsideAndMirror()
        {
            Assert.Equal(new IntRect(80, 0, 20, 10), new IntRect(95, -5, 20, 10).ClampInside(100, 50));
            Assert.Equal(new IntRect(0, 0, 200, 10), new IntRect(30, 3, 200, 10).ClampInside(100, 50).WithPosition(0, 0));
            Assert.Equal(new IntRect(0, 0, 200, 10), new IntRect(30, -3, 200, 10).ClampInside(100, 50));
            Assert.Equal(new IntRect(70, 5, 20, 10), new IntRect(10, 5, 20, 10).MirrorX(100));
            Assert.Equal(new IntRect(10, 35, 20, 10), new IntRect(10, 5, 20, 10).MirrorY(50));
        }

        [Fact]
        public void EqualityAndHashing()
        {
            Assert.True(new IntRect(1, 2, 3, 4) == new IntRect(1, 2, 3, 4));
            Assert.True(new IntRect(1, 2, 3, 4) != new IntRect(1, 2, 3, 5));
            Assert.Equal(new IntRect(1, 2, 3, 4).GetHashCode(), new IntRect(1, 2, 3, 4).GetHashCode());
            Assert.True(new IntVec2(1, 2) == new IntVec2(1, 2));
            Assert.False(new IntVec2(1, 2).Equals((object)new FloatVec2(1, 2)));
            Assert.True(new FloatVec2(0.5f, 1f) == new FloatVec2(0.5f, 1f));
            Assert.True(new FloatRect(0.1f, 0.2f, 0.3f, 0.4f) == new FloatRect(0.1f, 0.2f, 0.3f, 0.4f));
            Assert.Equal("[1,2 3x4]", new IntRect(1, 2, 3, 4).ToString());
            Assert.Equal("(0.5,1)", new FloatVec2(0.5f, 1f).ToString());
        }

        [Fact]
        public void FloatRectToPixelsRoundsEdgesAndClips()
        {
            // 0.065 * 1920 = 124.8 -> 125
            Assert.Equal(new IntRect(0, 0, 1080, 125), new FloatRect(0f, 0f, 1f, 0.065f).ToPixels(1080, 1920));
            // 0.86 * 1080 = 928.8 -> 929; right edge clipped to the canvas.
            Assert.Equal(IntRect.FromEdges(929, 634, 1080, 1690), new FloatRect(0.86f, 0.33f, 0.2f, 0.55f).ToPixels(1080, 1920));
            Assert.True(new FloatRect(1.2f, 0f, 0.5f, 0.5f).ToPixels(100, 100).IsEmpty);
        }

        [Fact]
        public void PixelMathRounding()
        {
            Assert.Equal(1, PixelMath.RoundHalfUp(0.5));
            Assert.Equal(0, PixelMath.RoundHalfUp(0.49999));
            Assert.Equal(0, PixelMath.RoundHalfUp(-0.5));
            Assert.Equal(-1, PixelMath.RoundHalfUp(-0.51));
            Assert.Throws<ArgumentException>(() => PixelMath.RoundHalfUp(double.NaN));
            Assert.Equal(-2L, PixelMath.FloorDiv(-3, 2));
            Assert.Equal(1L, PixelMath.FloorDiv(3, 2));
            Assert.Equal(2, PixelMath.RoundDiv(3, 2));
            Assert.Equal(-1, PixelMath.RoundDiv(-3, 2));
            Assert.Equal(2, PixelMath.RoundDiv(7, 3));
            Assert.Equal(3, PixelMath.RoundDiv(5, 2));
        }

        [Theory]
        [InlineData(6, 720, 4)]
        [InlineData(48, 720, 32)]
        [InlineData(24, 720, 16)]
        [InlineData(16, 720, 11)] // 10.67
        [InlineData(5, 720, 3)]   // 3.33
        [InlineData(1, 540, 1)]   // 0.5 rounds up
        [InlineData(32, 1080, 32)]
        [InlineData(48, 9, 0)]
        public void ScaleRefRoundsHalfUp(int refPx, int width, int expected)
        {
            Assert.Equal(expected, PixelMath.ScaleRef(refPx, width));
        }

        [Fact]
        public void RectUnionCountsOverlapOnce()
        {
            var u = new RectUnion(new[] { new IntRect(0, 0, 10, 10), new IntRect(5, 5, 10, 10), IntRect.Empty });
            Assert.Equal(2, u.Count);
            Assert.Equal(175L, u.IntersectionArea(new IntRect(-100, -100, 1000, 1000)));
            Assert.Equal(25L, u.IntersectionArea(new IntRect(5, 5, 5, 5)));
            Assert.Equal(0L, u.IntersectionArea(new IntRect(10, 0, 5, 5)));
            Assert.True(u.Intersects(new IntRect(14, 14, 1, 1)));
            Assert.False(u.Intersects(new IntRect(15, 15, 1, 1)));
            Assert.Equal(0L, RectUnion.Empty.IntersectionArea(new IntRect(0, 0, 5, 5)));
            Assert.Equal(3, u.With(new[] { new IntRect(50, 50, 1, 1) }).Count);
        }

        [Fact]
        public void RectUnionAreaMatchesBruteForce()
        {
            var rng = Rng.FromSeed(0x5EED, 1);
            for (int iter = 0; iter < 200; iter++)
            {
                var rects = new List<IntRect>();
                int n = rng.NextInt(0, 7);
                for (int i = 0; i < n; i++) rects.Add(new IntRect(rng.NextInt(0, 40), rng.NextInt(0, 40), rng.NextInt(0, 20), rng.NextInt(0, 20)));
                var q = new IntRect(rng.NextInt(0, 30), rng.NextInt(0, 30), rng.NextInt(0, 30), rng.NextInt(0, 30));
                long brute = 0;
                for (int y = q.Y; y < q.Bottom; y++)
                {
                    for (int x = q.X; x < q.Right; x++)
                    {
                        foreach (IntRect r in rects)
                        {
                            if (r.Contains(x, y)) { brute++; break; }
                        }
                    }
                }
                Assert.Equal(brute, new RectUnion(rects).IntersectionArea(q));
            }
        }

        [Fact]
        public void RoundHalfUpSaturatesOutOfRangeValues()
        {
            Assert.Equal(int.MaxValue, PixelMath.RoundHalfUp(3e9));
            Assert.Equal(int.MinValue, PixelMath.RoundHalfUp(-3e9));
            Assert.Equal(int.MaxValue, PixelMath.RoundHalfUp(double.MaxValue));
            Assert.Equal(int.MinValue, PixelMath.RoundHalfUp(-double.MaxValue));
            Assert.Equal(3, PixelMath.RoundHalfUp(2.5));
            Assert.Equal(-2, PixelMath.RoundHalfUp(-2.5));
            Assert.Throws<ArgumentException>(() => PixelMath.RoundHalfUp(double.PositiveInfinity));
        }

        [Fact]
        public void ToPixelsClampsHugeNormalizedRects()
        {
            Assert.Equal(new IntRect(0, 0, 1080, 1920), new FloatRect(0f, 0f, 1e7f, 1f).ToPixels(1080, 1920));
            Assert.Equal(new IntRect(0, 0, 1080, 1920), new FloatRect(-1e30f, -1e30f, 3e30f, 3e30f).ToPixels(1080, 1920));
            Assert.True(new FloatRect(5f, 0f, 1f, 1f).ToPixels(1080, 1920).IsEmpty);
            Assert.True(new FloatRect(-3e9f, 0f, 1f, 1f).ToPixels(1080, 1920).IsEmpty);
            Assert.Throws<ArgumentException>(() => new FloatRect(float.NaN, 0f, 1f, 1f).ToPixels(1080, 1920));
        }

        [Fact]
        public void FromPixelsRoundTripsThroughToPixels()
        {
            var px = new IntRect(40, 204, 600, 90);
            FloatRect n = FloatRect.FromPixels(px, 1080, 1920);
            Assert.Equal(px, n.ToPixels(1080, 1920));
            Assert.Equal(new IntRect(27, 136, 400, 60), n.ToPixels(720, 1280));
            Assert.Throws<ArgumentOutOfRangeException>(() => FloatRect.FromPixels(px, 0, 1920));
        }

        [Fact]
        public void FromReferencePixelsNormalizesAgainst1080x1920()
        {
            FloatRect n = FloatRect.FromReferencePixels(new FloatRect(54f, 232.5f, 540f, 96f));
            Assert.True(n.ApproximatelyEquals(new FloatRect(0.05f, 232.5f / 1920f, 0.5f, 0.05f), 1e-7f));
            Assert.Equal(FloatRect.FromPixels(new IntRect(40, 204, 600, 90), 1080, 1920),
                FloatRect.FromReferencePixels(new IntRect(40, 204, 600, 90)));
            Assert.Equal(new IntRect(40, 204, 600, 90), FloatRect.FromReferencePixels(new IntRect(40, 204, 600, 90)).ToPixels(1080, 1920));
            Assert.Throws<ArgumentException>(() => FloatRect.FromReferencePixels(new FloatRect(0f, float.NaN, 1f, 1f)));
            Assert.Throws<ArgumentException>(() => FloatRect.FromReferencePixels(new FloatRect(0f, 0f, float.PositiveInfinity, 1f)));
        }

        [Fact]
        public void ReturnedCollectionsAreReadOnly()
        {
            var src = new List<IntRect> { new IntRect(0, 0, 5, 5) };
            var u = new RectUnion(src);
            src[0] = new IntRect(0, 0, 9, 9);
            Assert.Equal(new IntRect(0, 0, 5, 5), u[0]);
            Assert.False(u.Rects is IntRect[]);
            Assert.Throws<NotSupportedException>(() => ((IList<IntRect>)u.Rects)[0] = IntRect.Empty);

            FrameLayout f = LayoutCalculator.ComputeSlots(1080, 1920, LayoutSpec.Default);
            Assert.False(f.SlotsBackToFront is SlotGeometry[]);
            Assert.Throws<NotSupportedException>(() => ((IList<SlotGeometry>)f.SlotsBackToFront)[0] = default(SlotGeometry));

            var pip = new PipSpec(SlotShape.RoundedRect, 0.35f, new FloatVec2(0.5f, 0.5f));
            PipSnapResult r = PipMagnets.Snap(1080, 1920, pip, pip.Center, RectUnion.Empty);
            Assert.False(r.Anchors is PipAnchor[]);
            Assert.Throws<NotSupportedException>(() => ((IList<PipAnchor>)r.Anchors)[0] = default(PipAnchor));
        }
    }
}
