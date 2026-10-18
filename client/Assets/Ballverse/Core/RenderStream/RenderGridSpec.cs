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
    /// palette indices; their meaning (e.g. 0 = unpainted) is mode-defined. Bounds are quantized positions.
    /// </para>
    /// </summary>
    public sealed class RenderGridSpec : IEquatable<RenderGridSpec>
    {
        public RenderGridSpec(int width, int height, GridTopology topology, short minX, short minY, short maxX, short maxY)
        {
            if (width < 1 || width > RenderStreamLimits.MaxGridWidth) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1 || height > RenderStreamLimits.MaxGridHeight) throw new ArgumentOutOfRangeException(nameof(height));
            if ((byte)topology > (byte)GridTopology.HexOddRowsShifted) throw new ArgumentOutOfRangeException(nameof(topology));
            if (minX >= maxX) throw new ArgumentException("minX must be below maxX", nameof(maxX));
            if (minY >= maxY) throw new ArgumentException("minY must be below maxY", nameof(maxY));
            Width = width;
            Height = height;
            Topology = topology;
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        /// <summary>Builds a spec from world bounds in metres, quantized with <paramref name="quantization"/>.</summary>
        public static RenderGridSpec FromWorld(RenderQuantization quantization, int width, int height, GridTopology topology, float minX, float minY, float maxX, float maxY)
        {
            if (quantization == null) throw new ArgumentNullException(nameof(quantization));
            return new RenderGridSpec(width, height, topology,
                quantization.QuantizePosition(minX), quantization.QuantizePosition(minY),
                quantization.QuantizePosition(maxX), quantization.QuantizePosition(maxY));
        }

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

        public bool Equals(RenderGridSpec other)
        {
            return other != null && Width == other.Width && Height == other.Height && Topology == other.Topology
                && MinX == other.MinX && MinY == other.MinY && MaxX == other.MaxX && MaxY == other.MaxY;
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
                return h;
            }
        }
    }
}
