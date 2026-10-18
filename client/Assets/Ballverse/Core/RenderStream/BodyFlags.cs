using System;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Per-body visual flags stored as one byte in the render stream (spec §7.4 "живые/мёртвые тела").
    /// Only <see cref="Visible"/> has a Core-wide meaning; the other bits are reserved for modes and the renderer
    /// and are carried through the stream bit-exactly.
    /// </summary>
    [Flags]
    public enum BodyFlags : byte
    {
        None = 0,
        Visible = 1 << 0,
        /// <summary>Body is alive in the simulation but drawn as eliminated / dead (dimmed).</summary>
        Dead = 1 << 1,
        /// <summary>Draw a trail behind the body (spec §7.7, trails).</summary>
        Trail = 1 << 2,
        /// <summary>Highlight (e.g. the leader or the forced winner).</summary>
        Highlight = 1 << 3,
    }
}
