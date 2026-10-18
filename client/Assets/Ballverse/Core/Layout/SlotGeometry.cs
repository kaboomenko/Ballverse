using System;
using System.Globalization;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Placement of one content slot in the frame: pixel rect, clip shape, uniform corner radius and z-order
    /// (0 = drawn first / back). Produced by <see cref="LayoutCalculator.ComputeSlots"/> (spec §3.3).
    /// </summary>
    public readonly struct SlotGeometry : IEquatable<SlotGeometry>
    {
        public SlotKind Kind { get; }
        public IntRect Rect { get; }
        public SlotShape Shape { get; }

        /// <summary>Corner radius in canvas pixels (all four corners). For <see cref="SlotShape.Circle"/> it is half the side.</summary>
        public int CornerRadius { get; }

        public int ZIndex { get; }

        public SlotGeometry(SlotKind kind, IntRect rect, SlotShape shape, int cornerRadius, int zIndex)
        {
            Kind = kind;
            Rect = rect;
            Shape = shape;
            CornerRadius = cornerRadius;
            ZIndex = zIndex;
        }

        public bool Equals(SlotGeometry other)
        {
            return Kind == other.Kind && Rect == other.Rect && Shape == other.Shape
                && CornerRadius == other.CornerRadius && ZIndex == other.ZIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is SlotGeometry other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Kind;
                h = h * 397 ^ Rect.GetHashCode();
                h = h * 397 ^ (int)Shape;
                h = h * 397 ^ CornerRadius;
                h = h * 397 ^ ZIndex;
                return h;
            }
        }

        public static bool operator ==(SlotGeometry a, SlotGeometry b) => a.Equals(b);
        public static bool operator !=(SlotGeometry a, SlotGeometry b) => !a.Equals(b);

        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} r={3} z={4}", Kind, Rect, Shape, CornerRadius, ZIndex);
        }
    }
}
