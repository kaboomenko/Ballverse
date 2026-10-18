using System;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Xunit;
using static Ballverse.Core.Tests.RenderGeometry.GeometryTestUtil;

namespace Ballverse.Core.Tests.RenderGeometry
{
    public class HelixTests
    {
        static readonly float Tilt = 28f * MathF.PI / 180f;

        [Fact]
        public void FocusPointMapsToItselfAndFrontIsLowerThanBack()
        {
            var p = new HelixProjection(0f, -5f, Tilt, 14f, 1.5f);
            p.Project(0f, -5f, 0f, out float sx, out float sy, out float scale, out float depth);
            Assert.Equal(0f, sx, 5);
            Assert.Equal(-5f, sy, 5);
            Assert.Equal(1f, scale, 5);
            Assert.Equal(0f, depth, 5);
            p.ProjectCylindrical(2f, 0f, -5f, out _, out float frontY, out float frontScale, out float frontDepth);
            p.ProjectCylindrical(2f, MathF.PI, -5f, out _, out float backY, out float backScale, out float backDepth);
            Assert.True(frontY < backY);
            Assert.True(frontScale > backScale);
            Assert.True(frontDepth < backDepth);
            Assert.True(HelixProjection.IsFront(0.3f));
            Assert.False(HelixProjection.IsFront(MathF.PI - 0.3f));
        }

        [Fact]
        public void OrthographicLimitGivesTiltedEllipses()
        {
            var p = new HelixProjection(1f, 0f, Tilt, 0f, 1.5f);
            p.ProjectCylindrical(2f, 0f, 0f, out float fx, out float fy, out float s, out _);
            Assert.Equal(1f, s);
            Assert.Equal(1f, fx, 5);
            Assert.Equal(-2f * MathF.Sin(Tilt), fy, 5);   // ellipse half-height ρ·sin(tilt)
            p.ProjectCylindrical(2f, MathF.PI / 2, 0f, out float rx, out float ry, out _, out _);
            Assert.Equal(3f, rx, 5);                    // half-width ρ
            Assert.Equal(0f, ry, 5);
            p.Project(1f, 3f, 0f, out _, out float hy, out _, out _);
            Assert.Equal(3f * MathF.Cos(Tilt), hy, 5);  // heights compress by cos(tilt)
        }

        [Fact]
        public void BallProjectsOnTheFrontOfItsPathCircle()
        {
            var p = new HelixProjection(0f, 0f, Tilt, 0f, 1.5f);
            p.ProjectBody(0f, 0f, out float x, out float y, out _);
            Assert.Equal(0f, x, 5);
            Assert.Equal(-1.5f * MathF.Sin(Tilt), y, 5);
            p.ProjectBody(5f, 0f, out float cx, out float cy, out _);   // clamped to the side of the path
            Assert.Equal(1.5f, cx, 5);
            Assert.Equal(0f, cy, 4);
        }

        [Fact]
        public void PiecesSplitAtTheFrontBackBoundaryAndKeepGapEdges()
        {
            // One gap from 0 to 60° (local), platform not rotated: solid from 60° to 360°.
            var ring = ShapeDefinition.Ring(Q.QuantizeScale(1.5f), Q.QuantizeScale(1.8f), new[] { RingGap.FromRadians(0f, MathF.PI / 3) });
            Span<float> pieces = stackalloc float[64];
            int n = HelixTowerBuilder.SplitPieces(ring, 0f, pieces);
            // 60°..90° | 90°..270° | 270°..360°
            Assert.Equal(3, n);
            Assert.Equal(MathF.PI / 3, pieces[0], 3);
            Assert.Equal(MathF.PI / 2, pieces[1], 3);
            Assert.Equal(1f, pieces[2]);   // starts at a gap edge
            Assert.Equal(0f, pieces[3]);
            Assert.Equal(1.5f * MathF.PI, pieces[5], 3);
            Assert.Equal(2f * MathF.PI, pieces[9], 3);
            Assert.Equal(1f, pieces[11]);  // ends at a gap edge
            // Full ring: pieces cover 2π.
            var full = ShapeDefinition.Ring(Q.QuantizeScale(1.5f), Q.QuantizeScale(1.8f), ReadOnlySpan<RingGap>.Empty);
            n = HelixTowerBuilder.SplitPieces(full, 0.4f, pieces);
            float total = 0f;
            for (int i = 0; i < n; i++)
            {
                total += pieces[4 * i + 1] - pieces[4 * i];
                Assert.Equal(0f, pieces[4 * i + 2]);
                Assert.Equal(0f, pieces[4 * i + 3]);
            }
            Assert.Equal(2f * MathF.PI, total, 4);
        }

        static (RenderFrame frame, System.Collections.Generic.List<ShapeDefinition> shapes) Tower(float ballY)
        {
            var shapes = Shapes(ShapeDefinition.Circle,
                ShapeDefinition.Ring(Q.QuantizeScale(1.5f), Q.QuantizeScale(1.8f), new[] { RingGap.FromRadians(-0.4f, 0.4f) }));
            var f = new RenderFrame();
            for (int k = 0; k < 6; k++) f.AddBody(Q.QuantizeBody(10 + k, 1, 0f, -k * 1.8f, k * 0.7f, 1f, PaletteSlots.Arena, BodyFlags.Visible));
            f.AddBody(Q.QuantizeBody(1, 0, 0f, ballY, 0f, 0.25f, 0, BodyFlags.Visible));
            f.Sort();
            return (f, shapes);
        }

        [Fact]
        public void PlatformsAboveTheBallGoToTheOverBatch()
        {
            var (frame, shapes) = Tower(-3.3f);   // above platform 2 (y = −3.6)
            var palette = new uint[256];
            new LookSettings().ResolvePalette(palette);
            var under = new GeometryBatch();
            var over = new GeometryBatch();
            var builder = new HelixTowerBuilder();
            var proj = new HelixProjection(0f, -3.3f, Tilt, 14f, 1.5f);
            builder.Build(frame, Q, shapes, palette, RenderThemeCatalog.Default, new HelixViewSettings(), proj, TessellationSettings.Create(120f),
                -3.3f + 0.25f, ReadOnlySpan<HelixSplat>.Empty, under, over);   // ball top
            Assert.Equal(6, builder.PlatformCount);
            Assert.Equal(2, builder.OverCount);   // platforms at 0 and −1.8
            Assert.True(under.IndexCount > 0);
            Assert.True(over.IndexCount > 0);
            // Over-batch geometry is the higher platforms: its lowest vertex is above the ball's platform top face.
            float minOver = float.MaxValue;
            foreach (BatchVertex v in over.Vertices) minOver = Math.Min(minOver, v.Y);
            proj.ProjectCylindrical(2.4f, 0f, -3.6f, out _, out float ballPlatformFront, out _, out _);
            Assert.True(minOver > ballPlatformFront);
        }

        [Fact]
        public void NoBallMeansEverythingUnderAndSplatsAreDrawn()
        {
            var (frame, shapes) = Tower(-3.3f);
            var palette = new uint[256];
            new LookSettings().ResolvePalette(palette);
            var under = new GeometryBatch();
            var over = new GeometryBatch();
            var builder = new HelixTowerBuilder();
            var proj = new HelixProjection(0f, 0f, Tilt, 0f, 1.5f);
            builder.Build(frame, Q, shapes, palette, RenderThemeCatalog.Default, new HelixViewSettings(), proj, TessellationSettings.Create(120f),
                float.NaN, ReadOnlySpan<HelixSplat>.Empty, under, over);
            Assert.Equal(0, over.VertexCount);
            int without = under.VertexCount;
            under.Clear();
            var splats = new[] { new HelixSplat { BodyId = 12, LocalAngle = 1f, Radius = 0.3f, Color = ColorMath.White } };
            builder.Build(frame, Q, shapes, palette, RenderThemeCatalog.Default, new HelixViewSettings(), proj, TessellationSettings.Create(120f),
                float.NaN, splats, under, over);
            Assert.True(under.VertexCount > without);
        }

        [Fact]
        public void BackOfPlatformIsDarkerThanFront()
        {
            Assert.Equal(1f, HelixProjection.Shade(0f, 0.4f), 5);
            Assert.Equal(0.4f, HelixProjection.Shade(MathF.PI, 0.4f), 5);
        }
    }
}
