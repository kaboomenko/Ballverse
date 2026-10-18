using System;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Per-body visual flags stored as one byte in the render stream (spec §7.4 "живые/мёртвые тела").
    /// Only <see cref="Visible"/> has a Core-wide meaning; the named bits are renderer conventions and the unnamed
    /// bits (5–7) are free for modes. All bits are carried through the stream bit-exactly.
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
        /// <summary>Fluid particle drawn into the metaball density buffer instead of as a shape (spec §7.7 "Метаболы (M15)").</summary>
        Metaball = 1 << 4,
    }
}
