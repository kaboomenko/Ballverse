using System;
using Ballverse.Core.RenderStream;
using Xunit;

namespace Ballverse.Core.Tests.RenderStream
{
    /// <summary>Shape definitions: factories, limits, ring gap rules, immutability and equality (spec §7.7 shape table).</summary>
    public class ShapeDefinitionTests
    {
        static ShapePoint[] Points(int n)
        {
            var p = new ShapePoint[n];
            for (int i = 0; i < n; i++) p[i] = new ShapePoint((short)(i * 3), (short)(-i * 7));
            return p;
        }

        [Fact]
        public void CircleIsUnitAndHasNoGeometry()
        {
            var c = ShapeDefinition.Circle;
            Assert.Equal(ShapeKind.Circle, c.Kind);
            Assert.Equal(0, c.PointCount);
            Assert.True(c.Gaps.IsEmpty);
            Assert.Equal(0, c.Thickness);
            Assert.False(c.Closed);
        }

        [Fact]
        public void PolygonVertexLimits()
        {
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Polygon(Points(0)));
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Polygon(Points(2)));
            Assert.Equal(3, ShapeDefinition.Polygon(Points(3)).PointCount);
            Assert.Equal(64, ShapeDefinition.Polygon(Points(RenderStreamLimits.MaxPolygonVertices)).PointCount);
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Polygon(Points(RenderStreamLimits.MaxPolygonVertices + 1)));
        }

        [Fact]
        public void ConcavePolygonIsAccepted()
        {
            var star = ShapeDefinition.Polygon(StreamScenes.Star(5, 1000, 400));
            Assert.Equal(ShapeKind.Polygon, star.Kind);
            Assert.Equal(10, star.PointCount);
        }

        [Fact]
        public void PolylinePointLimits()
        {
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Polyline(Points(1), false, 10));
            Assert.Equal(2, ShapeDefinition.Polyline(Points(2), false, 10).PointCount);
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Polyline(Points(2), true, 10));
            Assert.True(ShapeDefinition.Polyline(Points(3), true, 10).Closed);
            Assert.Equal(4096, ShapeDefinition.Polyline(Points(RenderStreamLimits.MaxPolylinePoints), false, 10).PointCount);
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Polyline(Points(RenderStreamLimits.MaxPolylinePoints + 1), true, 10));
        }

        [Fact]
        public void RingGapRules()
        {
            Assert.Empty(ShapeDefinition.Ring(100, 10, new RingGap[0]).Gaps.ToArray());
            Assert.Single(ShapeDefinition.Ring(100, 10, new[] { new RingGap(65000, 1000) }).Gaps.ToArray()); // wraps through 0
            Assert.Single(ShapeDefinition.Ring(100, 10, new[] { new RingGap(1, 0) }).Gaps.ToArray());      // 65535-unit gap
            ShapeDefinition.Ring(100, 10, new[] { new RingGap(0, 100), new RingGap(101, 200), new RingGap(60000, 65535) });

            // Zero span.
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Ring(100, 10, new[] { new RingGap(5, 5) }));
            // Not ascending / duplicate start.
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Ring(100, 10, new[] { new RingGap(500, 600), new RingGap(100, 200) }));
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Ring(100, 10, new[] { new RingGap(100, 200), new RingGap(100, 300) }));
            // Overlap and touching.
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Ring(100, 10, new[] { new RingGap(100, 300), new RingGap(200, 400) }));
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Ring(100, 10, new[] { new RingGap(100, 200), new RingGap(200, 400) }));
            // Last gap wrapping into the first.
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Ring(100, 10, new[] { new RingGap(100, 200), new RingGap(60000, 150) }));
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Ring(100, 10, new[] { new RingGap(100, 200), new RingGap(60000, 100) }));
            ShapeDefinition.Ring(100, 10, new[] { new RingGap(100, 200), new RingGap(60000, 99) });
        }

        [Fact]
        public void RingGapCountLimit()
        {
            var gaps = new RingGap[RenderStreamLimits.MaxRingGaps + 1];
            for (int i = 0; i < gaps.Length; i++) gaps[i] = new RingGap((ushort)(i * 1000), (ushort)(i * 1000 + 500));
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Ring(100, 10, gaps));
            Assert.Equal(RenderStreamLimits.MaxRingGaps, ShapeDefinition.Ring(100, 10, gaps.AsSpan(0, RenderStreamLimits.MaxRingGaps)).Gaps.Length);
        }

        [Fact]
        public void RingGapSpanAndRadians()
        {
            Assert.Equal(100, new RingGap(10, 110).Span);
            Assert.Equal(1036, new RingGap(65000, 500).Span);
            Assert.Equal(0, new RingGap(7, 7).Span);
            var g = RingGap.FromRadians((float)(-Math.PI / 2), (float)(Math.PI / 2));
            Assert.Equal(49152, g.Start);
            Assert.Equal(16384, g.End);
            Assert.Equal(32768, g.Span);
        }

        [Fact]
        public void CapsuleAndFlags()
        {
            var c = ShapeDefinition.Capsule(3000, 200, fixedThickness: true);
            Assert.Equal(ShapeKind.Capsule, c.Kind);
            Assert.Equal(3000, c.Length);
            Assert.Equal(200, c.Thickness);
            Assert.True(c.FixedThickness);
            var r = ShapeDefinition.Ring(5000, 100, new[] { new RingGap(0, 1) });
            Assert.Equal(5000, r.Radius);
            Assert.False(r.FixedThickness);
        }

        [Fact]
        public void DefinitionsAreImmutable()
        {
            var points = Points(4);
            var poly = ShapeDefinition.Polygon(points);
            points[0] = new ShapePoint(999, 999);
            Assert.Equal(new ShapePoint(0, 0), poly.Points[0]);

            var gaps = new[] { new RingGap(0, 10) };
            var ring = ShapeDefinition.Ring(1, 1, gaps);
            gaps[0] = new RingGap(5, 6);
            Assert.Equal(new RingGap(0, 10), ring.Gaps[0]);
        }

        [Fact]
        public void EqualityCoversEveryField()
        {
            var a = ShapeDefinition.Polyline(Points(5), false, 10);
            Assert.Equal(a, ShapeDefinition.Polyline(Points(5), false, 10));
            Assert.Equal(a.GetHashCode(), ShapeDefinition.Polyline(Points(5), false, 10).GetHashCode());
            Assert.NotEqual(a, ShapeDefinition.Polyline(Points(5), true, 10));
            Assert.NotEqual(a, ShapeDefinition.Polyline(Points(5), false, 11));
            Assert.NotEqual(a, ShapeDefinition.Polyline(Points(5), false, 10, fixedThickness: true));
            Assert.NotEqual(a, ShapeDefinition.Polyline(Points(6), false, 10));
            var moved = Points(5);
            moved[4].Y++;
            Assert.NotEqual(a, ShapeDefinition.Polyline(moved, false, 10));
            Assert.NotEqual(a, ShapeDefinition.Polygon(Points(5)));
            Assert.NotEqual(ShapeDefinition.Capsule(1, 2), ShapeDefinition.Capsule(2, 1));
            Assert.NotEqual(ShapeDefinition.Ring(1, 2, new RingGap[0]), ShapeDefinition.Ring(2, 1, new RingGap[0]));
            Assert.NotEqual(ShapeDefinition.Ring(1, 2, new[] { new RingGap(1, 2) }), ShapeDefinition.Ring(1, 2, new[] { new RingGap(1, 3) }));
            Assert.False(a.Equals(null));
            Assert.False(a.Equals((object)"x"));
            Assert.True(a.Equals((object)a));
            Assert.Contains("Polyline", a.ToString());
        }

        [Fact]
        public void QuantizationHelpers()
        {
            var q = RenderQuantization.Default;
            Assert.Equal(new ShapePoint(2048, -1024), q.QuantizePoint(1f, -0.5f));
            Assert.Equal(8192, q.UnitScale);
            Assert.InRange(q.DequantizeScale(q.UnitScale), 0.9999f, 1.0001f);
            Assert.Equal(ushort.MaxValue, new RenderQuantization(16f, 0.5f).UnitScale);
            var b = q.QuantizeBody(3, 7, 0f, 0f, 0f, 1f, 2, BodyFlags.Visible, 5);
            Assert.Equal(7, b.ShapeId);
            Assert.Equal(5, b.LabelId);
            Assert.Equal(BodyState.NoLabel, q.QuantizeBody(3, 7, 0f, 0f, 0f, 1f, 2, BodyFlags.Visible).LabelId);
        }

        [Fact]
        public void DegenerateDimensionsAreRejected()
        {
            // Zero thickness: nothing to draw.
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Polyline(Points(3), false, 0));
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Polyline(Points(3), true, 0, fixedThickness: true));
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Capsule(100, 0));
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Capsule(0, 0));
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Ring(100, 0, new RingGap[0]));
            // Ring radius 0, or thicker than its diameter (negative inner radius).
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Ring(0, 0, new RingGap[0]));
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Ring(0, 1, new RingGap[0]));
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Ring(100, 201, new RingGap[0]));
            Assert.Throws<ArgumentException>(() => ShapeDefinition.Ring(ushort.MaxValue / 2, ushort.MaxValue, new RingGap[0]));

            // Boundaries that stay legal.
            Assert.Equal(1, ShapeDefinition.Polyline(Points(2), false, 1).Thickness);
            Assert.Equal(200, ShapeDefinition.Ring(100, 200, new RingGap[0]).Thickness);   // inner radius 0: a disc
            Assert.Equal(1, ShapeDefinition.Ring(1, 1, new RingGap[0]).Radius);
            Assert.Equal(ushort.MaxValue, ShapeDefinition.Ring(ushort.MaxValue, ushort.MaxValue, new RingGap[0]).Thickness);
            Assert.Equal(0, ShapeDefinition.Capsule(0, 1).Length);                          // a dot
            // Documented as legal (renderer draws nothing / skips): coincident polygon vertices and polyline points.
            var same = new[] { new ShapePoint(5, 5), new ShapePoint(5, 5), new ShapePoint(5, 5) };
            Assert.Equal(3, ShapeDefinition.Polygon(same).PointCount);
            Assert.Equal(3, ShapeDefinition.Polyline(same, false, 10).PointCount);
        }

        [Fact]
        public void UnitScaleIsOnlyApproximatelyOne()
        {
            // Renderers must compare the quantized scale with UnitScale, not the dequantized float with 1f.
            var q = RenderQuantization.Default;
            Assert.NotEqual(1f, q.DequantizeScale(q.UnitScale));
            Assert.Equal(q.UnitScale, q.QuantizeScale(q.DequantizeScale(q.UnitScale)));
            Assert.Equal(1f, new RenderQuantization(16f, 1f).DequantizeScale(new RenderQuantization(16f, 1f).UnitScale));
        }
    }
}
