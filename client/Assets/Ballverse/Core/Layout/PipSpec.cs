using System;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Floating window of L3/L5 (spec §3.3): shape, size 20–60% of the canvas width and center position normalized to
    /// the canvas (0..1, top-left origin). Immutable; out-of-range values are clamped on construction, NaN/infinity throw.
    /// Border and shadow are styling and live with the renderer, not here.
    /// </summary>
    public sealed class PipSpec
    {
        public const float MinSizeFraction = 0.20f;
        public const float MaxSizeFraction = 0.60f;
        public const float DefaultSizeFraction = 0.35f;

        /// <summary>Default: rounded rectangle, 35% width, near the top-right corner (magnets snap it on first drag).</summary>
        public static readonly PipSpec Default = new PipSpec(SlotShape.RoundedRect, DefaultSizeFraction, new FloatVec2(0.75f, 0.25f));

        /// <summary>Window shape: <see cref="SlotShape.RoundedRect"/> or <see cref="SlotShape.Circle"/> (spec §3.3).</summary>
        public SlotShape Shape { get; }

        /// <summary>Window width as a fraction of the canvas width (RoundedRect) or circle diameter (Circle), 0.20–0.60.</summary>
        public float SizeFraction { get; }

        /// <summary>Window center normalized to the canvas; clamped to [0,1]. The window itself is kept inside the canvas.</summary>
        public FloatVec2 Center { get; }

        /// <summary>Throws for a shape other than RoundedRect/Circle or non-finite values; clamps size and center.</summary>
        public PipSpec(SlotShape shape, float sizeFraction, FloatVec2 center)
        {
            if (shape != SlotShape.RoundedRect && shape != SlotShape.Circle)
                throw new ArgumentOutOfRangeException(nameof(shape), shape, "unknown PiP shape");
            PixelMath.RequireFinite(sizeFraction, nameof(sizeFraction));
            if (!center.IsFinite) throw new ArgumentException("center must be finite", nameof(center));
            Shape = shape;
            SizeFraction = PixelMath.ClampF(sizeFraction, MinSizeFraction, MaxSizeFraction);
            Center = new FloatVec2(PixelMath.ClampF(center.X, 0f, 1f), PixelMath.ClampF(center.Y, 0f, 1f));
        }

        public PipSpec WithCenter(FloatVec2 center)
        {
            return new PipSpec(Shape, SizeFraction, center);
        }

        public PipSpec WithSizeFraction(float sizeFraction)
        {
            return new PipSpec(Shape, sizeFraction, Center);
        }

        public PipSpec WithShape(SlotShape shape)
        {
            return new PipSpec(shape, SizeFraction, Center);
        }
    }
}
