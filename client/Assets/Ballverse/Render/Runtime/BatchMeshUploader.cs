using System;
using Ballverse.Core.RenderGeometry;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ballverse.Render
{
    /// <summary>
    /// Uploads a <see cref="GeometryBatch"/> into a dynamic <see cref="Mesh"/> with the advanced mesh API: the Core vertex
    /// array is copied as-is (position float3, colour UNorm8×4, uv float2 = 24 bytes, see <see cref="BatchVertex"/>).
    /// Buffers grow to the next power of two and are never shrunk, so steady-state uploads allocate nothing.
    /// </summary>
    sealed class BatchMeshUploader : IDisposable
    {
        const MeshUpdateFlags Flags = MeshUpdateFlags.DontValidateIndices | MeshUpdateFlags.DontRecalculateBounds
            | MeshUpdateFlags.DontNotifyMeshUsers | MeshUpdateFlags.DontResetBoneBounds;

        static readonly VertexAttributeDescriptor[] s_layout =
        {
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
        };

        readonly Mesh _mesh;
        int _vertexCapacity;
        int _indexCapacity;

        public BatchMeshUploader(string name)
        {
            _mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave };
            _mesh.MarkDynamic();
            _mesh.subMeshCount = 1;
            // Never culled (command-buffer draws are not culled anyway); keeps bounds valid for tools.
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(100000f, 100000f, 100f));
        }

        public Mesh Mesh => _mesh;

        /// <summary>Indices uploaded by the last <see cref="Upload"/> (0 = nothing to draw).</summary>
        public int IndexCount { get; private set; }

        public void Upload(GeometryBatch batch)
        {
            int vc = batch.VertexCount;
            int ic = batch.IndexCount;
            IndexCount = vc > 0 ? ic : 0;
            if (IndexCount == 0) return;
            if (vc > _vertexCapacity)
            {
                _vertexCapacity = Mathf.NextPowerOfTwo(Math.Max(vc, 256));
                _mesh.SetVertexBufferParams(_vertexCapacity, s_layout);
            }
            if (ic > _indexCapacity)
            {
                _indexCapacity = Mathf.NextPowerOfTwo(Math.Max(ic, 768));
                _mesh.SetIndexBufferParams(_indexCapacity, IndexFormat.UInt32);
            }
            _mesh.SetVertexBufferData(batch.VertexArray, 0, 0, vc, 0, Flags);
            _mesh.SetIndexBufferData(batch.IndexArray, 0, 0, ic, Flags);
            var sub = new SubMeshDescriptor(0, ic, MeshTopology.Triangles)
            {
                firstVertex = 0,
                vertexCount = vc,
                bounds = _mesh.bounds,
            };
            _mesh.SetSubMesh(0, sub, Flags);
        }

        public void Dispose()
        {
            RenderResources.Destroy(_mesh);
        }
    }
}
