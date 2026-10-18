namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Visual primitive of a body in the render stream (spec §7.7: SDF circles, batched polygon meshes, chains/segments).
    /// Stored as one byte; values are persisted and must stay stable.
    /// </summary>
    public enum BodyKind : byte
    {
        Circle = 0,
        Polygon = 1,
        Segment = 2,
        /// <summary>Mode-specific visual (particles, metaballs, decorations…).</summary>
        Other = 3,
    }
}
