using System;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// How a source (user video) is placed inside its slot (spec §3.3 "Регулировки внутри слота", project "userVideo"
    /// block of §7.4). Immutable; values are normalized on construction.
    /// <list type="bullet">
    /// <item><see cref="Rotation"/>: clockwise degrees applied to the decoded frame, any multiple of 90 (normalized to 0/90/180/270).</item>
    /// <item><see cref="Mirror"/>: horizontal flip of the displayed (already rotated) picture, for the front camera.</item>
    /// <item><see cref="Pan"/>: content offset per axis in [-1, 1] as a fraction of the available slack: +1 on X moves the
    /// content fully right (its left edge becomes visible), +1 on Y fully down (top edge visible). Clamping to [-1, 1]
    /// guarantees no empty edge in Cover mode.</item>
    /// <item><see cref="Zoom"/>: 1 (no zoom) to <see cref="MaxZoom"/>, multiplies the Cover/Fit scale.</item>
    /// </list>
    /// </summary>
    public sealed class SlotContent
    {
        public const float MinZoom = 1f;

        /// <summary>Upper zoom bound (not in the spec; chosen to keep at least 1/8 of the source visible).</summary>
        public const float MaxZoom = 8f;

        /// <summary>Default: no rotation, no mirror, Cover, centered, zoom 1.</summary>
        public static readonly SlotContent Default = new SlotContent(0, false, SlotFill.Cover, FloatVec2.Zero, 1f);

        public int Rotation { get; }
        public bool Mirror { get; }
        public SlotFill Fill { get; }
        public FloatVec2 Pan { get; }
        public float Zoom { get; }

        /// <summary>Throws for a rotation that is not a multiple of 90, an unknown fill or non-finite pan/zoom; clamps pan and zoom.</summary>
        public SlotContent(int rotation, bool mirror, SlotFill fill, FloatVec2 pan, float zoom)
        {
            if (rotation % 90 != 0) throw new ArgumentOutOfRangeException(nameof(rotation), rotation, "rotation must be a multiple of 90");
            if (fill != SlotFill.Cover && fill != SlotFill.Fit) throw new ArgumentOutOfRangeException(nameof(fill), fill, "unknown fill");
            if (!pan.IsFinite) throw new ArgumentException("pan must be finite", nameof(pan));
            PixelMath.RequireFinite(zoom, nameof(zoom));
            Rotation = ((rotation % 360) + 360) % 360;
            Mirror = mirror;
            Fill = fill;
            Pan = new FloatVec2(PixelMath.ClampF(pan.X, -1f, 1f), PixelMath.ClampF(pan.Y, -1f, 1f));
            Zoom = PixelMath.ClampF(zoom, MinZoom, MaxZoom);
        }

        /// <summary>Double-tap reset (spec §3.3, §4.6): pan 0, zoom 1; rotation, mirror and fill are kept.</summary>
        public SlotContent Reset()
        {
            return new SlotContent(Rotation, Mirror, Fill, FloatVec2.Zero, 1f);
        }

        public SlotContent WithPan(FloatVec2 pan) => new SlotContent(Rotation, Mirror, Fill, pan, Zoom);
        public SlotContent WithZoom(float zoom) => new SlotContent(Rotation, Mirror, Fill, Pan, zoom);
        public SlotContent WithFill(SlotFill fill) => new SlotContent(Rotation, Mirror, fill, Pan, Zoom);
        public SlotContent WithMirror(bool mirror) => new SlotContent(Rotation, mirror, Fill, Pan, Zoom);
        public SlotContent WithRotation(int rotation) => new SlotContent(rotation, Mirror, Fill, Pan, Zoom);

        /// <summary>Rotates by another 90° clockwise (the "rotate" button, spec §3.3).</summary>
        public SlotContent RotateClockwise() => WithRotation(Rotation + 90);
    }
}
