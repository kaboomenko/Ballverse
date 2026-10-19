using System;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>
    /// A reusable triangle list in body-local metres: the output of <see cref="ShapeTessellator"/> and the cached geometry
    /// of <see cref="ShapeGeometryCache"/> (spec §7.7 "пакетные динамические меши"). <see cref="Clear"/> keeps the backing
    /// arrays, so refilling allocates nothing once the peak size was reached.
    /// </summary>
    public sealed class LocalGeometry
    {
        Float2[] _positions;
        int[] _indices;
        int _vertexCount;
        int _indexCount;

        public LocalGeometry()
            : this(64, 192)
        {
        }

        public LocalGeometry(int vertexCapacity, int indexCapacity)
        {
            _positions = new Float2[Math.Max(4, vertexCapacity)];
            _indices = new int[Math.Max(6, indexCapacity)];
        }

        public int VertexCount => _vertexCount;
        public int IndexCount => _indexCount;
        public int TriangleCount => _indexCount / 3;

        public ReadOnlySpan<Float2> Positions => new ReadOnlySpan<Float2>(_positions, 0, _vertexCount);
        public ReadOnlySpan<int> Indices => new ReadOnlySpan<int>(_indices, 0, _indexCount);

        public void Clear()
        {
            _vertexCount = 0;
            _indexCount = 0;
        }

        /// <summary>Grows the backing arrays so that <paramref name="extraVertices"/> / <paramref name="extraIndices"/> more fit.</summary>
        public void Reserve(int extraVertices, int extraIndices)
        {
            int v = _vertexCount + extraVertices;
            if (v > _positions.Length) Array.Resize(ref _positions, Math.Max(v, _positions.Length * 2));
            int i = _indexCount + extraIndices;
            if (i > _indices.Length) Array.Resize(ref _indices, Math.Max(i, _indices.Length * 2));
        }

        public int AddVertex(float x, float y)
        {
            if (_vertexCount == _positions.Length) Array.Resize(ref _positions, _positions.Length * 2);
            _positions[_vertexCount] = new Float2(x, y);
            return _vertexCount++;
        }

        public void AddTriangle(int a, int b, int c)
        {
            if (_indexCount + 3 > _indices.Length) Array.Resize(ref _indices, _indices.Length * 2);
            _indices[_indexCount++] = a;
            _indices[_indexCount++] = b;
            _indices[_indexCount++] = c;
        }

        /// <summary>Replaces the content with a copy of <paramref name="other"/>.</summary>
        public void CopyFrom(LocalGeometry other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            Clear();
            Reserve(other._vertexCount, other._indexCount);
            Array.Copy(other._positions, _positions, other._vertexCount);
            Array.Copy(other._indices, _indices, other._indexCount);
            _vertexCount = other._vertexCount;
            _indexCount = other._indexCount;
        }

        /// <summary>Sum of the absolute triangle areas, m² (overlapping parts count twice).</summary>
        public double TotalTriangleArea()
        {
            double sum = 0;
            for (int i = 0; i + 2 < _indexCount; i += 3)
            {
                Float2 a = _positions[_indices[i]];
                Float2 b = _positions[_indices[i + 1]];
                Float2 c = _positions[_indices[i + 2]];
                sum += Math.Abs(((double)b.X - a.X) * ((double)c.Y - a.Y) - ((double)b.Y - a.Y) * ((double)c.X - a.X)) * 0.5;
            }
            return sum;
        }

        /// <summary>Axis-aligned bounds of the vertices; false when empty.</summary>
        public bool TryGetBounds(out float minX, out float minY, out float maxX, out float maxY)
        {
            minX = minY = float.MaxValue;
            maxX = maxY = float.MinValue;
            for (int i = 0; i < _vertexCount; i++)
            {
                Float2 p = _positions[i];
                if (p.X < minX) minX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.X > maxX) maxX = p.X;
                if (p.Y > maxY) maxY = p.Y;
            }
            return _vertexCount > 0;
        }
    }
}
