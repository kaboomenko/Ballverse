using System;
using Ballverse.Core;
using Ballverse.Core.RenderStream;
using Xunit;

namespace Ballverse.Core.Tests.RenderStream
{
    /// <summary>
    /// Grid layer geometry shared by the M08 simulation and the renderer (spec §2.9 M08 "касание определяется по позиции
    /// шара в сетке", "сетка 30×53 по умолчанию или гексы"): quantization consistency with the header, cell centres and
    /// point-to-cell mapping for square and hex topologies.
    /// </summary>
    public class GridGeometryTests
    {
        // ------------------------------------------------------------------ quantization consistency

        [Fact]
        public void HeaderRejectsGridQuantizedWithAnotherQuantization()
        {
            var custom = new RenderQuantization(12f, 4f);
            var foreign = RenderGridSpec.FromWorld(RenderQuantization.Default, 30, 53, GridTopology.Square, -4.5f, -8f, 4.5f, 8f);
            Assert.Throws<ArgumentException>(() => new RenderStreamHeader("color_war", 60, 60, custom, foreign));

            var own = RenderGridSpec.FromWorld(custom, 30, 53, GridTopology.Square, -4.5f, -8f, 4.5f, 8f);
            var header = new RenderStreamHeader("color_war", 60, 60, custom, own);
            Assert.Same(own, header.Grid);
            Assert.Same(custom, own.Quantization);

            // An equal (not identical) quantization is accepted.
            var equal = RenderGridSpec.FromWorld(new RenderQuantization(12f, 4f), 30, 53, GridTopology.Square, -4.5f, -8f, 4.5f, 8f);
            Assert.Same(equal, new RenderStreamHeader("color_war", 60, 60, custom, equal).Grid);
            Assert.Throws<ArgumentNullException>(() => new RenderGridSpec(null, 5, 5, GridTopology.Square, 0, 0, 1, 1));
        }

        [Fact]
        public void ReadBackGridCoversTheIntendedWorldRectangle()
        {
            var custom = new RenderQuantization(12f, 4f);
            var header = new RenderStreamHeader("color_war", 60, 60, custom,
                RenderGridSpec.FromWorld(custom, 30, 53, GridTopology.Square, -4.5f, -8f, 4.5f, 8f));
            var frame = new RenderFrame();
            frame.SetGridSize(30, 53);
            using (var r = StreamScenes.Open(StreamScenes.Write(new System.Collections.Generic.List<RenderFrame> { frame }, header)))
            {
                RenderGridSpec g = r.Header.Grid;
                Assert.Equal(header.Grid, g);
                Assert.Equal(custom, g.Quantization);
                double step = custom.PositionStep;
                Assert.InRange(g.Quantization.DequantizePosition(g.MinX), -4.5 - step, -4.5 + step);
                Assert.InRange(g.Quantization.DequantizePosition(g.MaxY), 8 - step, 8 + step);
            }
        }

        [Fact]
        public void GridSpecEqualityIncludesQuantization()
        {
            var a = new RenderGridSpec(RenderQuantization.Default, 4, 4, GridTopology.Square, -100, -100, 100, 100);
            var b = new RenderGridSpec(new RenderQuantization(12f, 4f), 4, 4, GridTopology.Square, -100, -100, 100, 100);
            Assert.NotEqual(a, b);
            Assert.Equal(a, new RenderGridSpec(new RenderQuantization(16f, 8f), 4, 4, GridTopology.Square, -100, -100, 100, 100));
        }

        // ------------------------------------------------------------------ square

        static RenderGridSpec Square()
        {
            // 9 x 16 m arena, 30 x 53 cells (M08 default).
            return RenderGridSpec.FromWorld(RenderQuantization.Default, 30, 53, GridTopology.Square, -4.5f, -8f, 4.5f, 8f);
        }

        [Fact]
        public void SquareCellCentresAndMapping()
        {
            var g = Square();
            g.CellCenter(0, 0, out float x, out float y);
            Assert.Equal(-4.5 + 0.15, x, 5);
            Assert.Equal(-8 + 8.0 / 53, y, 5);
            g.CellCenter(29, 52, out x, out y);
            Assert.Equal(4.5 - 0.15, x, 5);
            Assert.Equal(8 - 8.0 / 53, y, 5);

            for (int cy = 0; cy < g.Height; cy++)
            {
                for (int cx = 0; cx < g.Width; cx++)
                {
                    g.CellCenter(cx, cy, out x, out y);
                    Assert.True(g.TryGetCell(x, y, out int mx, out int my));
                    Assert.Equal((cx, cy), (mx, my));
                }
            }

            // Corners and edges of the bounds belong to the edge cells; the max edges stay in the last column / row.
            Assert.True(g.TryGetCell(-4.5f, -8f, out int ex, out int ey));
            Assert.Equal((0, 0), (ex, ey));
            Assert.True(g.TryGetCell(4.5f, 8f, out ex, out ey));
            Assert.Equal((29, 52), (ex, ey));
            // A border between columns 0 and 1 (x = -4.2) goes to the right-hand cell.
            Assert.True(g.TryGetCell(-4.2f, 0f, out ex, out _));
            Assert.Equal(1, ex);
        }

        [Fact]
        public void PointsOutsideTheBoundsHaveNoCell()
        {
            foreach (var g in new[] { Square(), Hex() })
            {
                Assert.False(g.TryGetCell(-4.51f, 0f, out int x, out int y));
                Assert.Equal((-1, -1), (x, y));
                Assert.False(g.TryGetCell(4.51f, 0f, out _, out _));
                Assert.False(g.TryGetCell(0f, -8.01f, out _, out _));
                Assert.False(g.TryGetCell(0f, 8.01f, out _, out _));
                Assert.False(g.TryGetCell(float.NaN, 0f, out _, out _));
                Assert.False(g.TryGetCell(0f, float.PositiveInfinity, out _, out _));
                Assert.Throws<ArgumentOutOfRangeException>(() => g.CellCenter(-1, 0, out _, out _));
                Assert.Throws<ArgumentOutOfRangeException>(() => g.CellCenter(g.Width, 0, out _, out _));
                Assert.Throws<ArgumentOutOfRangeException>(() => g.CellCenter(0, g.Height, out _, out _));
            }
        }

        // ------------------------------------------------------------------ hex

        static RenderGridSpec Hex()
        {
            return RenderGridSpec.FromWorld(RenderQuantization.Default, 20, 30, GridTopology.HexOddRowsShifted, -4.5f, -8f, 4.5f, 8f);
        }

        [Fact]
        public void HexGeometryMatchesTheDocumentedLayout()
        {
            var g = Hex();
            double w = 9.0 / 20.5;                 // Width + ½ cell widths across
            double h = 16.0 / (0.75 * 30 + 0.25);  // 0.75·Height + 0.25 hex heights up
            g.CellCenter(0, 0, out float x, out float y);
            Assert.Equal(-4.5 + 0.5 * w, x, 5);
            Assert.Equal(-8 + 0.5 * h, y, 5);
            g.CellCenter(0, 1, out x, out y);      // odd row: shifted right by half a cell
            Assert.Equal(-4.5 + 1.0 * w, x, 5);
            Assert.Equal(-8 + 1.25 * h, y, 5);     // row pitch ¾ hex height
            g.CellCenter(19, 29, out x, out y);    // last cell of the last (odd) row touches the right and top bounds
            Assert.Equal(4.5 - 0.5 * w, x, 5);
            Assert.Equal(8 - 0.5 * h, y, 5);
            g.CellCenter(19, 28, out x, out _);    // last cell of an even row leaves half a cell free on the right
            Assert.Equal(4.5 - 1.0 * w, x, 5);

            // Just above the bottom vertex of (5, 0) is that cell; beyond the upper slanted edges of a row-0 hex the
            // shifted row-1 neighbours take over (right edge -> (5, 1), left edge -> (4, 1)).
            g.CellCenter(5, 0, out x, out y);
            Assert.True(g.TryGetCell(x, (float)(-8 + 0.01 * h), out int cx, out int cy));
            Assert.Equal((5, 0), (cx, cy));
            Assert.True(g.TryGetCell((float)(x + 0.3 * w), (float)(y + 0.45 * h), out cx, out cy));
            Assert.Equal((5, 1), (cx, cy));
            Assert.True(g.TryGetCell((float)(x - 0.3 * w), (float)(y + 0.45 * h), out cx, out cy));
            Assert.Equal((4, 1), (cx, cy));
        }

        [Fact]
        public void HexCentresMapToTheirOwnCell()
        {
            var g = Hex();
            for (int cy = 0; cy < g.Height; cy++)
            {
                for (int cx = 0; cx < g.Width; cx++)
                {
                    g.CellCenter(cx, cy, out float x, out float y);
                    Assert.True(g.TryGetCell(x, y, out int mx, out int my));
                    Assert.Equal((cx, cy), (mx, my));
                }
            }
        }

        [Theory]
        [InlineData(GridTopology.HexOddRowsShifted, 20, 30)]
        [InlineData(GridTopology.HexOddRowsShifted, 1, 1)]
        [InlineData(GridTopology.HexOddRowsShifted, 3, 2)]
        [InlineData(GridTopology.HexOddRowsShifted, 128, 256)]
        [InlineData(GridTopology.Square, 7, 3)]
        public void MappingEqualsBruteForceNearestCentre(GridTopology topology, int width, int height)
        {
            var g = RenderGridSpec.FromWorld(RenderQuantization.Default, width, height, topology, -4.5f, -8f, 4.5f, 8f);
            var rng = Rng.FromSeed((ulong)(width * 1000 + height), 50);
            float minX = g.Quantization.DequantizePosition(g.MinX), maxX = g.Quantization.DequantizePosition(g.MaxX);
            float minY = g.Quantization.DequantizePosition(g.MinY), maxY = g.Quantization.DequantizePosition(g.MaxY);
            // Metric of the unstretched grid: for hexes, one cell width across and 2/√3 cell widths per hex height.
            double unitX = topology == GridTopology.Square ? (maxX - minX) / width : (maxX - minX) / (width + 0.5);
            double unitY = topology == GridTopology.Square ? (maxY - minY) / height : (maxY - minY) / (0.75 * height + 0.25);
            double yScale = topology == GridTopology.Square ? 1.0 : 2.0 / Math.Sqrt(3.0);
            for (int i = 0; i < 3000; i++)
            {
                float px = rng.NextFloat(minX, maxX);
                float py = rng.NextFloat(minY, maxY);
                Assert.True(g.TryGetCell(px, py, out int x, out int y));

                if (topology == GridTopology.Square)
                {
                    g.CellCenter(x, y, out float sx, out float sy);
                    Assert.True(Math.Abs(px - sx) <= unitX / 2 + 1e-4 && Math.Abs(py - sy) <= unitY / 2 + 1e-4);
                    continue;
                }

                double best = double.MaxValue;
                int bx = -1, by = -1;
                for (int cy = 0; cy < height; cy++)
                {
                    for (int cx = 0; cx < width; cx++)
                    {
                        g.CellCenter(cx, cy, out float sx, out float sy);
                        double du = (px - sx) / unitX;
                        double dv = (py - sy) / unitY * yScale;
                        double d = du * du + dv * dv;
                        if (d < best - 1e-9)
                        {
                            best = d;
                            bx = cx;
                            by = cy;
                        }
                    }
                }
                g.CellCenter(x, y, out float mx, out float my);
                double mu = (px - mx) / unitX, mv = (py - my) / unitY * yScale;
                Assert.True(Math.Abs(mu * mu + mv * mv - best) < 1e-6, "point (" + px + ", " + py + ") mapped to (" + x + ", " + y + "), nearest (" + bx + ", " + by + ")");
            }
        }

        [Fact]
        public void MappingIsDeterministic()
        {
            // Same inputs, same cells: pinned values guard against accidental geometry changes.
            var g = Hex();
            Assert.True(g.TryGetCell(0.1f, 0.2f, out int x, out int y));
            Assert.Equal((9, 15), (x, y));
            Assert.True(g.TryGetCell(-4.5f, -8f, out x, out y));
            Assert.Equal((0, 0), (x, y));
            Assert.True(g.TryGetCell(4.5f, 8f, out x, out y));
            Assert.Equal((19, 29), (x, y));
            Assert.True(g.TryGetCell(4.5f, -8f, out x, out y));
            Assert.Equal((19, 0), (x, y));
        }
    }
}
