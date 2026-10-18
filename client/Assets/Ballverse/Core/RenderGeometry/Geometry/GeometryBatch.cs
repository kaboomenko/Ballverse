using System;
using System.Runtime.InteropServices;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>
    /// One vertex of a world-space render batch (24 bytes, sequential layout). The Unity renderer uploads arrays of this
    /// struct directly as a vertex buffer: position float3, colour UNorm8×4 (sRGB bytes R, G, B, A in memory order),
    /// texcoord0 float2 (U = emission multiplier for bloom, 1 = plain colour; V = reserved, 0).
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct BatchVertex
    {
        public float X;
        public float Y;
        public float Z;
        public byte R;
        public byte G;
        public byte B;
        public byte A;
        public float U;
        public float V;

        public BatchVertex(float x, float y, uint rgba, float emission)
        {
            X = x;
            Y = y;
            Z = 0f;
            R = (byte)(rgba >> 24);
            G = (byte)(rgba >> 16);
            B = (byte)(rgba >> 8);
            A = (byte)rgba;
            U = emission;
            V = 0f;
        }

        /// <summary>The colour packed as 0xRRGGBBAA.</summary>
        public uint Rgba => ((uint)R << 24) | ((uint)G << 16) | ((uint)B << 8) | A;
    }

    /// <summary>
    /// A reusable world-space triangle batch with per-vertex colour and emission: all non-circle shapes, trails or
    /// overlays of one frame drawn with one draw call (spec §7.7 "пакетные динамические меши, перестраиваемые раз в кадр").
    /// Triangles are drawn in insertion order (painter's order). <see cref="Clear"/> keeps capacity: no allocation per
    /// frame once the peak size was reached.
    /// </summary>
    public sealed class GeometryBatch
    {
        BatchVertex[] _vertices;
        int[] _indices;
        int _vertexCount;
        int _indexCount;

        public GeometryBatch()
            : this(256, 768)
        {
        }

        public GeometryBatch(int vertexCapacity, int indexCapacity)
        {
            _vertices = new BatchVertex[Math.Max(4, vertexCapacity)];
            _indices = new int[Math.Max(6, indexCapacity)];
        }

        public int VertexCount => _vertexCount;
        public int IndexCount => _indexCount;

        public ReadOnlySpan<BatchVertex> Vertices => new ReadOnlySpan<BatchVertex>(_vertices, 0, _vertexCount);
        public ReadOnlySpan<int> Indices => new ReadOnlySpan<int>(_indices, 0, _indexCount);

        /// <summary>Backing vertex array (valid entries: [0, <see cref="VertexCount"/>)), for zero-copy upload.</summary>
        public BatchVertex[] VertexArray => _vertices;

        /// <summary>Backing index array (valid entries: [0, <see cref="IndexCount"/>)), for zero-copy upload.</summary>
        public int[] IndexArray => _indices;

        public void Clear()
        {
            _vertexCount = 0;
            _indexCount = 0;
        }

        /// <summary>Drops vertices from <paramref name="vertexCount"/> on (no index may reference them).</summary>
        public void TruncateVertices(int vertexCount)
        {
            if ((uint)vertexCount > (uint)_vertexCount) throw new ArgumentOutOfRangeException(nameof(vertexCount));
            _vertexCount = vertexCount;
        }

        public void Reserve(int extraVertices, int extraIndices)
        {
            int v = _vertexCount + extraVertices;
            if (v > _vertices.Length) Array.Resize(ref _vertices, Math.Max(v, _vertices.Length * 2));
            int i = _indexCount + extraIndices;
            if (i > _indices.Length) Array.Resize(ref _indices, Math.Max(i, _indices.Length * 2));
        }

        public int AddVertex(float x, float y, uint rgba, float emission)
        {
            if (_vertexCount == _vertices.Length) Array.Resize(ref _vertices, _vertices.Length * 2);
            _vertices[_vertexCount] = new BatchVertex(x, y, rgba, emission);
            return _vertexCount++;
        }

        public void AddTriangle(int a, int b, int c)
        {
            if (_indexCount + 3 > _indices.Length) Array.Resize(ref _indices, _indices.Length * 2);
            _indices[_indexCount++] = a;
            _indices[_indexCount++] = b;
            _indices[_indexCount++] = c;
        }

        /// <summary>Adds the quad a-b-c-d (two triangles a-b-c, a-c-d).</summary>
        public void AddQuad(int a, int b, int c, int d)
        {
            AddTriangle(a, b, c);
            AddTriangle(a, c, d);
        }

        /// <summary>
        /// Appends <paramref name="geometry"/> transformed by p' = R(angle)·(scale·p) + (tx, ty), where
        /// <paramref name="cos"/>/<paramref name="sin"/> are those of the angle, with one colour and emission.
        /// </summary>
        public void AppendTransformed(LocalGeometry geometry, float tx, float ty, float cos, float sin, float scale, uint rgba, float emission)
        {
            if (geometry == null) throw new ArgumentNullException(nameof(geometry));
            int baseVertex = _vertexCount;
            Reserve(geometry.VertexCount, geometry.IndexCount);
            ReadOnlySpan<Float2> src = geometry.Positions;
            float c = cos * scale;
            float s = sin * scale;
            for (int i = 0; i < src.Length; i++)
            {
                float x = src[i].X;
                float y = src[i].Y;
                _vertices[_vertexCount++] = new BatchVertex(tx + c * x - s * y, ty + s * x + c * y, rgba, emission);
            }
            ReadOnlySpan<int> idx = geometry.Indices;
            for (int i = 0; i < idx.Length; i++) _indices[_indexCount++] = baseVertex + idx[i];
        }
    }
}
