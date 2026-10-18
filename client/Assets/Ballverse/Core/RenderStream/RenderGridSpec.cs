using System;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Declares the optional grid layer of a stream (spec §2.9 M08 Color War: "клетки — не физические тела, а
    /// текстура-маска"): dimensions, topology and the world rectangle it covers. Fixed for the whole stream and stored in
    /// the .bvs header; when present, every frame carries a full grid of palette indices of exactly these dimensions.
    /// <para>
    /// Cells are row-major: index = y · <see cref="Width"/> + x, with x = 0 the left column at <see cref="MinX"/> and
    /// y = 0 the bottom row at <see cref="MinY"/> (y grows upwards like arena coordinates, spec §2.1). Cell values are
    /// palette indices; their meaning (e.g. 0 = unpainted) is mode-defined.
    /// </para>
    /// <para>
    /// Bounds are positions quantized with <see cref="Quantization"/>, which must equal the stream header's quantization
    /// (<see cref="RenderStreamHeader"/> rejects a mismatch), so the renderer places the grid exactly where the mode put
    /// it. The cell geometry of each topology is defined in <see cref="GridTopology"/>; <see cref="CellCenter"/> and
    /// <see cref="TryGetCell"/> implement it once for the simulation (M08 "касание определяется по позиции шара в сетке")
    /// and the renderer. They use IEEE double arithmetic only (no transcendental functions), so results are deterministic.
    /// </para>
    /// </summary>
    public sealed class RenderGridSpec : IEquatable<RenderGridSpec>
    {
        readonly double _minX;
        readonly double _minY;
        readonly double _cellW;
        readonly double _cellH;

        /// <param name="quantization">The stream's quantization; the bounds are quantized positions in it.</param>
        public RenderGridSpec(RenderQuantization quantization, int width, int height, GridTopology topology, short minX, short minY, short maxX, short maxY)
        {
            if (quantization == null) throw new ArgumentNullException(nameof(quantization));
            if (width < 1 || width > RenderStreamLimits.MaxGridWidth) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1 || height > RenderStreamLimits.MaxGridHeight) throw new ArgumentOutOfRangeException(nameof(height));
            if ((byte)topology > (byte)GridTopology.HexOddRowsShifted) throw new ArgumentOutOfRangeException(nameof(topology));
            if (minX >= maxX) throw new ArgumentException("minX must be below maxX", nameof(maxX));
            if (minY >= maxY) throw new ArgumentException("minY must be below maxY", nameof(maxY));
            Quantization = quantization;
            Width = width;
            Height = height;
            Topology = topology;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;

            _minX = quantization.DequantizePosition(minX);
            _minY = quantization.DequantizePosition(minY);
            double spanX = quantization.DequantizePosition(maxX) - _minX;
            double spanY = quantization.DequantizePosition(maxY) - _minY;
            if (topology == GridTopology.Square)
            {
                _cellW = spanX / width;
                _cellH = spanY / height;
            }
            else
            {
                _cellW = spanX / (width + 0.5);
                _cellH = spanY / (0.75 * height + 0.25);   // hex height
            }
        }

        /// <summary>Builds a spec from world bounds in metres, quantized with <paramref name="quantization"/> (pass the header's).</summary>
        public static RenderGridSpec FromWorld(RenderQuantization quantization, int width, int height, GridTopology topology, float minX, float minY, float maxX, float maxY)
        {
            if (quantization == null) throw new ArgumentNullException(nameof(quantization));
            return new RenderGridSpec(quantization, width, height, topology,
                quantization.QuantizePosition(minX), quantization.QuantizePosition(minY),
                quantization.QuantizePosition(maxX), quantization.QuantizePosition(maxY));
        }

        /// <summary>The quantization of the bounds; equals the stream header's <see cref="RenderStreamHeader.Quantization"/>.</summary>
        public RenderQuantization Quantization { get; }
        /// <summary>Columns, 1..<see cref="RenderStreamLimits.MaxGridWidth"/>.</summary>
        public int Width { get; }
        /// <summary>Rows, 1..<see cref="RenderStreamLimits.MaxGridHeight"/>.</summary>
        public int Height { get; }
        public GridTopology Topology { get; }
        public short MinX { get; }
        public short MinY { get; }
        public short MaxX { get; }
        public short MaxY { get; }

        /// <summary>Width · Height.</summary>
        public int CellCount => Width * Height;

        /// <summary>World centre (metres) of cell (<paramref name="x"/>, <paramref name="y"/>), see <see cref="GridTopology"/>.</summary>
        public void CellCenter(int x, int y, out float worldX, out float worldY)
        {
            if ((uint)x >= (uint)Width) throw new ArgumentOutOfRangeException(nameof(x));
            if ((uint)y >= (uint)Height) throw new ArgumentOutOfRangeException(nameof(y));
            CenterUnits(x, y, out double u, out double v);
            worldX = (float)(_minX + u * _cellW);
            worldY = (float)(_minY + v * _cellH);
        }

        /// <summary>
        /// The cell containing world point (<paramref name="worldX"/>, <paramref name="worldY"/>) in metres, see
        /// <see cref="GridTopology"/>. Returns false (and −1, −1) for points outside the bounds rectangle (edges included
        /// in the rectangle) and for NaN. A point exactly on a border between cells goes to the cell with the lower row,
        /// then the lower column, for hexagons; for squares to the cell on the right / top (the last row / column keeps
        /// the max edge).
        /// </summary>
        public bool TryGetCell(float worldX, float worldY, out int x, out int y)
        {
            x = -1;
            y = -1;
            double u = (worldX - _minX) / _cellW;
            double v = (worldY - _minY) / _cellH;
            double maxU = Topology == GridTopology.Square ? Width : Width + 0.5;
            double maxV = Topology == GridTopology.Square ? Height : 0.75 * Height + 0.25;
            if (!(u >= 0 && u <= maxU && v >= 0 && v <= maxV)) return false;   // also rejects NaN

            if (Topology == GridTopology.Square)
            {
                x = Math.Min((int)Math.Floor(u), Width - 1);
                y = Math.Min((int)Math.Floor(v), Height - 1);
                return true;
            }

            // Nearest centre in the regular-hex metric: with cell width 1 a regular hex is 2/√3 high, so
            // distance² ∝ 3·du² + 4·dv² (dv in hex heights). The nearest centre is at most one circumradius away,
            // which bounds the candidates to rows r0-1..r0+2 and three columns per row.
            int r0 = (int)Math.Floor((v - 0.5) / 0.75);
            double best = double.MaxValue;
            for (int r = Math.Max(0, r0 - 1); r <= Math.Min(Height - 1, r0 + 2); r++)
            {
                double shift = (r & 1) != 0 ? 0.5 : 0.0;
                int c0 = (int)Math.Floor(u - shift);
                for (int c = Math.Max(0, c0 - 1); c <= Math.Min(Width - 1, c0 + 1); c++)
                {
                    CenterUnits(c, r, out double cu, out double cv);
                    double du = u - cu;
                    double dv = v - cv;
                    double d = 3.0 * du * du + 4.0 * dv * dv;
                    if (d < best)
                    {
                        best = d;
                        x = c;
                        y = r;
                    }
                }
            }
            return true;
        }

        /// <summary>Centre of a cell in grid units: cell widths from MinX, and cell (square) or hex heights from MinY.</summary>
        void CenterUnits(int x, int y, out double u, out double v)
        {
            if (Topology == GridTopology.Square)
            {
                u = x + 0.5;
                v = y + 0.5;
            }
            else
            {
                u = x + 0.5 + ((y & 1) != 0 ? 0.5 : 0.0);
                v = 0.5 + 0.75 * y;
            }
        }

        public bool Equals(RenderGridSpec other)
        {
            return other != null && Width == other.Width && Height == other.Height && Topology == other.Topology
                && MinX == other.MinX && MinY == other.MinY && MaxX == other.MaxX && MaxY == other.MaxY
                && Quantization.Equals(other.Quantization);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as RenderGridSpec);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Width;
                h = h * 31 + Height;
                h = h * 31 + (int)Topology;
                h = h * 31 + MinX;
                h = h * 31 + MinY;
                h = h * 31 + MaxX;
                h = h * 31 + MaxY;
                h = h * 31 + Quantization.GetHashCode();
                return h;
            }
        }
    }
}
