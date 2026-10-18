namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Geometry kind of a <see cref="ShapeDefinition"/> in the .bvs shape table (spec §7.7: SDF circles, batched polygon
    /// meshes, chains and rings; §2.9 mode cards). Stored as one byte; values are persisted and must stay stable.
    /// </summary>
    public enum ShapeKind : byte
    {
        /// <summary>Unit circle; the body's scale is its radius in metres (balls, pegs, particles).</summary>
        Circle = 0,
        /// <summary>Filled simple polygon, convex or concave (arenas M02/M07, blocks and Voronoi fragments M11, dominoes M12, gates M09, weapons M06).</summary>
        Polygon = 1,
        /// <summary>Open or closed chain of points stroked with a thickness (walls M10, track pieces M03, strings M13).</summary>
        Polyline = 2,
        /// <summary>Circular ring of a radius and thickness with zero or more angular gaps (rings M01, spiral platforms M05, wheel M07).</summary>
        Ring = 3,
        /// <summary>Segment with round caps: a capsule of a length and thickness centred at the origin along local +X (levers, weapons, strings).</summary>
        Capsule = 4,
    }
}
