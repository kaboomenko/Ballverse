using System;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Xunit;
using static Ballverse.Core.Tests.RenderGeometry.GeometryTestUtil;

namespace Ballverse.Core.Tests.RenderGeometry
{
    /// <summary>Tessellation of every shape kind (spec §7.7): tolerance, scale rules, gaps, caps, joins, determinism, allocations.</summary>
    public class ShapeTessellatorTests
    {
        static TessellationSettings Settings(float ppm = 120f, float tol = 0.25f) => TessellationSettings.Create(ppm, tol);

        static ushort L(float metres) => Q.QuantizeScale(metres);

        // ------------------------------------------------------------------ arc math / tolerance

        [Theory]
        [InlineData(0.1f, 120f, 0.25f)]
        [InlineData(0.35f, 120f, 0.25f)]
        [InlineData(4f, 120f, 0.25f)]
        [InlineData(12f, 120f, 0.1f)]
        [InlineData(2f, 600f, 0.5f)]
        public void CircleSegmentsKeepTheSagittaWithinTolerance(float radius, float ppm, float tol)
        {
            var s = Settings(ppm, tol);
            int n = ArcMath.CircleSegments(radius, s);
            double sagittaPx = ArcMath.Sagitta(radius * ppm, 2 * Math.PI / n);
            Assert.True(sagittaPx <= tol + 1e-9, "sagitta " + sagittaPx + " px with " + n + " segments");
            // Not wastefully fine: one segment fewer would break the tolerance (unless clamped to the minimum).
            if (n > s.MinCircleSegments) Assert.True(ArcMath.Sagitta(radius * ppm, 2 * Math.PI / (n - 1)) > tol);
        }

        [Fact]
        public void CircleSegmentsAreClamped()
        {
            var s = Settings(1f, 0.25f);
            Assert.Equal(s.MinCircleSegments, ArcMath.CircleSegments(0.01f, s));
            var big = Settings(10000f, 0.01f);
            Assert.Equal(big.MaxCircleSegments, ArcMath.CircleSegments(100f, big));
        }

        [Fact]
        public void DiscVerticesLieOnTheCircleAndChordsRespectTolerance()
        {
            var g = new LocalGeometry();
            var s = Settings(200f, 0.25f);
            Assert.Equal(TessellationStatus.Ok, ShapeTessellator.Tessellate(ShapeDefinition.Circle, Q, 1.5f, s, g));
            ReadOnlySpan<Float2> p = g.Positions;
            for (int i = 1; i < p.Length; i++) Assert.Equal(1.5, p[i].Length, 4);
            // Chord midpoints are within tolerance (in pixels) of the true circle.
            for (int i = 1; i < p.Length; i++)
            {
                Float2 a = p[i], b = p[i == p.Length - 1 ? 1 : i + 1];
                double mid = ((a + b) * 0.5f).Length;
                Assert.True((1.5 - mid) * 200 <= 0.25 + 1e-3, "chord error " + (1.5 - mid) * 200 + " px");
            }
            // The inscribed polygon loses at most sagitta × perimeter of area.
            double deficit = Math.PI * 1.5 * 1.5 - g.TotalTriangleArea();
            Assert.InRange(deficit, 0.0, 0.25 / 200 * 2 * Math.PI * 1.5);
        }

        // ------------------------------------------------------------------ rings

        [Fact]
        public void FullRingCoversTheAnnulusExactlyOnce()
        {
            var ring = ShapeDefinition.Ring(L(2f), L(0.4f), ReadOnlySpan<RingGap>.Empty);
            var g = new LocalGeometry();
            Assert.Equal(TessellationStatus.Ok, ShapeTessellator.Tessellate(ring, Q, 1f, Settings(), g));
            double expected = Math.PI * (2.2 * 2.2 - 1.8 * 1.8);
            Assert.Equal(expected, g.TotalTriangleArea(), 1);          // no overlap
            foreach (Float2 p in g.Positions)
            {
                double r = p.Length;
                Assert.True(Math.Abs(r - 1.8) < 1e-3 || Math.Abs(r - 2.2) < 1e-3, "vertex off the ring edges: r = " + r);
            }
            Assert.True(Covers(g, 2.0, 0.0));
            Assert.False(Covers(g, 0.0, 0.0));
        }

        [Fact]
        public void RingGapsAreEmptyWithButtCaps()
        {
            // Gap from 80° to 100° (the top) and from 260° to 280° (the bottom).
            var gaps = new[] { RingGap.FromRadians(80f * MathF.PI / 180f, 100f * MathF.PI / 180f), RingGap.FromRadians(260f * MathF.PI / 180f, 280f * MathF.PI / 180f) };
            var ring = ShapeDefinition.Ring(L(2f), L(0.2f), gaps);
            var s = Settings();
            s.RingGapCap = LineCap.Butt;
            var g = new LocalGeometry();
            Assert.Equal(TessellationStatus.Ok, ShapeTessellator.Tessellate(ring, Q, 1f, s, g));
            Assert.False(Covers(g, 0, 2.0));     // 90°: in the gap
            Assert.False(Covers(g, 0, -2.0));    // 270°: in the gap
            Assert.True(Covers(g, 2.0, 0));
            Assert.True(Covers(g, -2.0, 0));
            double solid = 320.0 / 360.0;
            Assert.Equal(solid * Math.PI * (2.1 * 2.1 - 1.9 * 1.9), g.TotalTriangleArea(), 2);
        }

        [Fact]
        public void RoundGapCapsMatchTheColliderModel()
        {
            // M01: gap edges are semicircles of radius t/2 centred on the gap boundary at the centre line,
            // so the free width at the centre line is 2·r·sin(gap/2) − t.
            float r = 2f, t = 0.3f;
            float half = 15f * MathF.PI / 180f;
            var ring = ShapeDefinition.Ring(L(r), L(t), new[] { RingGap.FromRadians(MathF.PI / 2 - half, MathF.PI / 2 + half) });
            var g = new LocalGeometry();
            ShapeTessellator.Tessellate(ring, Q, 1f, Settings(400f), g);
            // A point at the gap edge angle but t/2·0.9 further into the gap (along the tangent) is covered by the cap...
            float edge = MathF.PI / 2 - half;
            float ex = MathF.Cos(edge) * r, ey = MathF.Sin(edge) * r;
            float tx = -MathF.Sin(edge), ty = MathF.Cos(edge);
            Assert.True(Covers(g, ex + tx * t * 0.45, ey + ty * t * 0.45));
            // ...and t/2·1.1 into the gap is not.
            Assert.False(Covers(g, ex + tx * t * 0.55, ey + ty * t * 0.55));
            Assert.False(Covers(g, 0, r));   // gap centre stays open
        }

        [Fact]
        public void FixedThicknessDoesNotScaleWhileNormalThicknessDoes()
        {
            var fixedRing = ShapeDefinition.Ring(L(2f), L(0.2f), ReadOnlySpan<RingGap>.Empty, fixedThickness: true);
            var scaledRing = ShapeDefinition.Ring(L(2f), L(0.2f), ReadOnlySpan<RingGap>.Empty);
            var g = new LocalGeometry();
            ShapeTessellator.Tessellate(fixedRing, Q, 0.5f, Settings(), g);
            MinMaxRadius(g, out double min, out double max);
            Assert.Equal(0.9, min, 3);
            Assert.Equal(1.1, max, 3);
            ShapeTessellator.Tessellate(scaledRing, Q, 0.5f, Settings(), g);
            MinMaxRadius(g, out min, out max);
            Assert.Equal(0.95, min, 3);
            Assert.Equal(1.05, max, 3);
        }

        [Fact]
        public void FixedThicknessRingBelowHalfThicknessBecomesADisc()
        {
            var ring = ShapeDefinition.Ring(L(1f), L(0.4f), new[] { RingGap.FromRadians(0f, 0.5f) }, fixedThickness: true);
            var g = new LocalGeometry();
            Assert.Equal(TessellationStatus.Ok, ShapeTessellator.Tessellate(ring, Q, 0.1f, Settings(), g));   // radius 0.1 < 0.2
            MinMaxRadius(g, out double min, out double max);
            Assert.Equal(0.0, min, 6);
            Assert.Equal(0.3, max, 3);
            Assert.True(Covers(g, 0.0, 0.0));
        }

        static void MinMaxRadius(LocalGeometry g, out double min, out double max)
        {
            min = double.MaxValue;
            max = 0;
            foreach (Float2 p in g.Positions)
            {
                min = Math.Min(min, p.Length);
                max = Math.Max(max, p.Length);
            }
        }

        [Fact]
        public void LargeRingKeepsPixelToleranceOnTheOuterEdge()
        {
            var ring = ShapeDefinition.Ring(L(7.5f), L(0.12f), ReadOnlySpan<RingGap>.Empty);
            var g = new LocalGeometry();
            float ppm = 1920f / 16f;
            ShapeTessellator.Tessellate(ring, Q, 1f, Settings(ppm, 0.25f), g);
            ReadOnlySpan<Float2> p = g.Positions;
            double outer = 7.56;
            double worst = 0;
            for (int i = 1; i + 2 < p.Length; i += 2)
            {
                Float2 a = p[i], b = p[i + 2];   // consecutive outer vertices
                double mid = ((a + b) * 0.5f).Length;
                worst = Math.Max(worst, (outer - mid) * ppm);
            }
            Assert.True(worst <= 0.25 + 1e-3, "outer chord error " + worst + " px");
        }

        // ------------------------------------------------------------------ capsule

        [Fact]
        public void CapsuleAreaAndZeroLengthDot()
        {
            var g = new LocalGeometry();
            var cap = ShapeDefinition.Capsule(L(2f), L(0.5f));
            ShapeTessellator.Tessellate(cap, Q, 1f, Settings(400f), g);
            Assert.Equal(2.0 * 0.5 + Math.PI * 0.25 * 0.25, g.TotalTriangleArea(), 2);
            Assert.True(Covers(g, 1.2, 0));
            Assert.False(Covers(g, 1.3, 0));

            ShapeTessellator.Tessellate(ShapeDefinition.Capsule(0, L(0.5f)), Q, 1f, Settings(400f), g);
            Assert.Equal(Math.PI * 0.25 * 0.25, g.TotalTriangleArea(), 2);

            // Fixed thickness: scaled length, constant diameter.
            ShapeTessellator.Tessellate(ShapeDefinition.Capsule(L(2f), L(0.5f), fixedThickness: true), Q, 2f, Settings(400f), g);
            Assert.Equal(4.0 * 0.5 + Math.PI * 0.25 * 0.25, g.TotalTriangleArea(), 2);
        }

        // ------------------------------------------------------------------ polyline

        [Fact]
        public void StraightPolylineWithButtCapsIsARectangle()
        {
            var s = Settings();
            s.Cap = LineCap.Butt;
            var g = new LocalGeometry();
            var line = ShapeDefinition.Polyline(Pts(-1f, 0f, 1f, 0f), false, L(0.2f));
            ShapeTessellator.Tessellate(line, Q, 1f, s, g);
            Assert.Equal(2.0 * 0.2, g.TotalTriangleArea(), 3);
            s.Cap = LineCap.Round;
            ShapeTessellator.Tessellate(line, Q, 1f, s, g);
            Assert.Equal(2.0 * 0.2 + Math.PI * 0.01, g.TotalTriangleArea(), 2);
            s.Cap = LineCap.Square;
            ShapeTessellator.Tessellate(line, Q, 1f, s, g);
            Assert.Equal(2.2 * 0.2, g.TotalTriangleArea(), 3);
        }

        [Theory]
        [InlineData(LineJoin.Miter)]
        [InlineData(LineJoin.Bevel)]
        [InlineData(LineJoin.Round)]
        public void ClosedSquareOutlineCoversTheFrame(LineJoin join)
        {
            var s = Settings(200f);
            s.Join = join;
            var g = new LocalGeometry();
            var square = ShapeDefinition.Polyline(Pts(-1f, -1f, 1f, -1f, 1f, 1f, -1f, 1f), true, L(0.2f));
            ShapeTessellator.Tessellate(square, Q, 1f, s, g);
            double frame = 2.2 * 2.2 - 1.8 * 1.8;
            double corners = join == LineJoin.Miter ? 0 : join == LineJoin.Bevel ? 4 * 0.5 * 0.01 : 4 * (0.01 - Math.PI * 0.01 / 4);
            Assert.Equal(frame - corners, CoverageArea(g, 500), 1);
            Assert.True(Covers(g, 0, 1.0));
            Assert.False(Covers(g, 0, 0));
            if (join == LineJoin.Miter) Assert.True(Covers(g, 1.09, 1.09));   // sharp outer corner
        }

        [Fact]
        public void SharpMiterBeyondLimitBecomesBevel()
        {
            var s = Settings(200f);
            s.Join = LineJoin.Miter;
            s.MiterLimit = 2f;
            var g = new LocalGeometry();
            // Very sharp spike: the miter would be ~23 half-widths long.
            var spike = ShapeDefinition.Polyline(Pts(-2f, 0f, 0f, 0f, -2f, 0.17f), false, L(0.1f));
            ShapeTessellator.Tessellate(spike, Q, 1f, s, g);
            g.TryGetBounds(out _, out _, out float maxX, out _);
            Assert.True(maxX < 0.2f, "miter not limited, extends to x = " + maxX);
        }

        [Fact]
        public void DuplicatePolylinePointsAreSkipped()
        {
            var a = new LocalGeometry();
            var b = new LocalGeometry();
            ShapeTessellator.Tessellate(ShapeDefinition.Polyline(Pts(0f, 0f, 1f, 0f, 1f, 1f), false, L(0.1f)), Q, 1f, Settings(), a);
            ShapeTessellator.Tessellate(ShapeDefinition.Polyline(Pts(0f, 0f, 0f, 0f, 1f, 0f, 1f, 0f, 1f, 1f, 1f, 1f), false, L(0.1f)), Q, 1f, Settings(), b);
            Assert.Equal(a.Positions.ToArray(), b.Positions.ToArray());
            Assert.Equal(a.Indices.ToArray(), b.Indices.ToArray());

            // All points equal: a dot with round caps.
            ShapeTessellator.Tessellate(ShapeDefinition.Polyline(Pts(1f, 1f, 1f, 1f), false, L(0.2f)), Q, 1f, Settings(), a);
            Assert.Equal(Math.PI * 0.01, a.TotalTriangleArea(), 2);
        }

        // ------------------------------------------------------------------ polygon

        [Fact]
        public void ConvexAndConcavePolygonsTriangulateExactly()
        {
            var g = new LocalGeometry();
            var square = ShapeDefinition.Polygon(Pts(0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f));
            Assert.Equal(TessellationStatus.Ok, ShapeTessellator.Tessellate(square, Q, 1f, Settings(), g));
            Assert.Equal(2, g.TriangleCount);
            Assert.Equal(1.0, g.TotalTriangleArea(), 5);

            ShapePoint[] lShape = Pts(0f, 0f, 2f, 0f, 2f, 1f, 1f, 1f, 1f, 2f, 0f, 2f);
            Assert.Equal(TessellationStatus.Ok, ShapeTessellator.Tessellate(ShapeDefinition.Polygon(lShape), Q, 1f, Settings(), g));
            Assert.Equal(4, g.TriangleCount);
            Assert.Equal(3.0, g.TotalTriangleArea(), 5);
            Assert.False(Covers(g, 1.5, 1.5));   // the notch

            // Clockwise input gives the same area; scale multiplies points.
            Array.Reverse(lShape);
            ShapeTessellator.Tessellate(ShapeDefinition.Polygon(lShape), Q, 2f, Settings(), g);
            Assert.Equal(12.0, g.TotalTriangleArea(), 4);
        }

        [Fact]
        public void PolygonTrianglesAreCounterClockwise()
        {
            var g = new LocalGeometry();
            ShapePoint[] star = Star(5, 2f, 0.8f, 0f);
            Array.Reverse(star);
            ShapeTessellator.Tessellate(ShapeDefinition.Polygon(star), Q, 1f, Settings(), g);
            ReadOnlySpan<Float2> p = g.Positions;
            ReadOnlySpan<int> idx = g.Indices;
            for (int t = 0; t < idx.Length; t += 3) Assert.True(Float2.Cross(p[idx[t + 1]] - p[idx[t]], p[idx[t + 2]] - p[idx[t]]) > 0f);
            Assert.Equal(ShoelaceArea(star), g.TotalTriangleArea(), 4);
        }

        static ShapePoint[] Star(int points, float outer, float inner, float phase)
        {
            var pts = new ShapePoint[points * 2];
            for (int i = 0; i < pts.Length; i++)
            {
                float a = phase + MathF.PI * i / points;
                float r = (i & 1) == 0 ? outer : inner;
                pts[i] = P(MathF.Cos(a) * r, MathF.Sin(a) * r);
            }
            return pts;
        }

        [Fact]
        public void RandomSimplePolygonsMatchTheShoelaceArea()
        {
            var rng = Rng.FromSeed(1234);
            var g = new LocalGeometry();
            for (int iter = 0; iter < 300; iter++)
            {
                int n = rng.NextInt(3, 65);
                // Star-shaped polygon: sorted distinct angles, random radii → simple.
                var angles = new float[n];
                for (int i = 0; i < n; i++) angles[i] = (i + 0.1f + 0.8f * rng.NextFloat()) / n * 2f * MathF.PI;
                var pts = new ShapePoint[n];
                for (int i = 0; i < n; i++)
                {
                    float r = 0.3f + 3f * rng.NextFloat();
                    pts[i] = P(MathF.Cos(angles[i]) * r, MathF.Sin(angles[i]) * r);
                }
                TessellationStatus status = ShapeTessellator.Tessellate(ShapeDefinition.Polygon(pts), Q, 1f, Settings(), g);
                double area = ShoelaceArea(pts);
                if (status == TessellationStatus.Empty)
                {
                    Assert.True(area < 1e-6);
                    continue;
                }
                Assert.Equal(TessellationStatus.Ok, status);
                Assert.True(Math.Abs(area - g.TotalTriangleArea()) < 1e-3 * Math.Max(1, area), "iteration " + iter + ": area " + area + " vs " + g.TotalTriangleArea());
            }
        }

        [Fact]
        public void SelfIntersectingPolygonIsReportedButStillDrawn()
        {
            var g = new LocalGeometry();
            var bowTie = ShapeDefinition.Polygon(Pts(0f, 0f, 2f, 2f, 2f, 0f, 0f, 2f));
            TessellationStatus status = ShapeTessellator.Tessellate(bowTie, Q, 1f, Settings(), g);
            Assert.Equal(TessellationStatus.SelfIntersecting, status);
            Assert.True(g.TriangleCount >= 1);

            // A pentagram drawn as one self-crossing loop.
            var pts = new ShapePoint[5];
            for (int i = 0; i < 5; i++)
            {
                float a = MathF.PI / 2 + i * 4f * MathF.PI / 5;
                pts[i] = P(MathF.Cos(a) * 2, MathF.Sin(a) * 2);
            }
            status = ShapeTessellator.Tessellate(ShapeDefinition.Polygon(pts), Q, 1f, Settings(), g);
            Assert.Equal(TessellationStatus.SelfIntersecting, status);
            Assert.True(g.TriangleCount >= 1);
        }

        [Fact]
        public void DegeneratePolygonsAreEmpty()
        {
            var g = new LocalGeometry();
            Assert.Equal(TessellationStatus.Empty, ShapeTessellator.Tessellate(ShapeDefinition.Polygon(Pts(0f, 0f, 1f, 1f, 2f, 2f)), Q, 1f, Settings(), g));
            Assert.Equal(0, g.IndexCount);
            Assert.Equal(TessellationStatus.Empty, ShapeTessellator.Tessellate(ShapeDefinition.Polygon(Pts(1f, 1f, 1f, 1f, 1f, 1f)), Q, 1f, Settings(), g));
            // Collinear vertices are dropped without breaking the fill.
            Assert.Equal(TessellationStatus.Ok, ShapeTessellator.Tessellate(ShapeDefinition.Polygon(Pts(0f, 0f, 1f, 0f, 2f, 0f, 2f, 2f, 0f, 2f)), Q, 1f, Settings(), g));
            Assert.Equal(4.0, g.TotalTriangleArea(), 5);
        }

        [Fact]
        public void TriangulatorRejectsOversizedInputAndSmallOutput()
        {
            var pts = new ShapePoint[PolygonTriangulator.MaxVertices + 1];
            Assert.Throws<ArgumentException>(() => PolygonTriangulator.Triangulate(pts, new int[3 * pts.Length], out _));
            Assert.Throws<ArgumentException>(() => PolygonTriangulator.Triangulate(Pts(0, 0, 1, 0, 1, 1, 0, 1), new int[3], out _));
        }

        // ------------------------------------------------------------------ determinism & allocations

        [Fact]
        public void TessellationIsDeterministic()
        {
            ShapeDefinition[] shapes =
            {
                ShapeDefinition.Ring(L(3f), L(0.12f), new[] { RingGap.FromRadians(0.3f, 0.9f), RingGap.FromRadians(3f, 3.5f) }),
                ShapeDefinition.Polyline(Pts(-1f, 0f, 0f, 1f, 1f, 0f, 2f, 1.5f), false, L(0.15f)),
                ShapeDefinition.Polygon(Star(6, 2f, 1f, 0.2f)),
                ShapeDefinition.Capsule(L(1.2f), L(0.3f)),
            };
            foreach (ShapeDefinition shape in shapes)
            {
                var a = new LocalGeometry();
                var b = new LocalGeometry();
                ShapeTessellator.Tessellate(shape, Q, 1.3f, Settings(150f), a);
                ShapeTessellator.Tessellate(shape, Q, 1.3f, Settings(150f), b);
                Assert.Equal(a.Positions.ToArray(), b.Positions.ToArray());
                Assert.Equal(a.Indices.ToArray(), b.Indices.ToArray());
            }
        }

        [Fact]
        public void TessellatingIntoAWarmBufferDoesNotAllocate()
        {
            ShapeDefinition[] shapes =
            {
                ShapeDefinition.Ring(L(3f), L(0.12f), new[] { RingGap.FromRadians(0.3f, 0.9f) }),
                ShapeDefinition.Polyline(Pts(-1f, 0f, 0f, 1f, 1f, 0f, 2f, 1.5f), true, L(0.15f)),
                ShapeDefinition.Polygon(Star(8, 2f, 1f, 0.2f)),
                ShapeDefinition.Capsule(L(1.2f), L(0.3f)),
                ShapeDefinition.Circle,
            };
            var g = new LocalGeometry();
            var s = Settings();
            s.Join = LineJoin.Round;
            foreach (ShapeDefinition shape in shapes) ShapeTessellator.Tessellate(shape, Q, 1f, s, g);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 50; i++)
            {
                foreach (ShapeDefinition shape in shapes) ShapeTessellator.Tessellate(shape, Q, 1f, s, g);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Fact]
        public void InvalidSettingsThrow()
        {
            var s = Settings();
            s.TolerancePixels = 0f;
            Assert.Throws<ArgumentOutOfRangeException>(() => ShapeTessellator.Tessellate(ShapeDefinition.Circle, Q, 1f, s, new LocalGeometry()));
            Assert.Equal(TessellationStatus.Empty, ShapeTessellator.Tessellate(ShapeDefinition.Circle, Q, 0f, Settings(), new LocalGeometry()));
        }
    }
}
