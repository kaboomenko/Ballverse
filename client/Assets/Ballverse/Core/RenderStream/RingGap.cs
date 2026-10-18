using System;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// One angular gap of a <see cref="ShapeKind.Ring"/> (spec §2.9 M01 "прорезь (или несколько)", M05 platform gaps,
    /// M07 wheel slot), in quantized angles (full turn = 65536, see <see cref="RenderQuantization.QuantizeAngle"/>).
    /// The gap runs counter-clockwise from <see cref="Start"/> to <see cref="End"/> in body-local space (0 = local +X)
    /// and may wrap through angle 0. <see cref="Start"/> == <see cref="End"/> is invalid (ambiguous between empty and full).
    /// </summary>
    public struct RingGap : IEquatable<RingGap>
    {
        public ushort Start;
        public ushort End;

        public RingGap(ushort start, ushort end)
        {
            Start = start;
            End = end;
        }

        /// <summary>Counter-clockwise extent of the gap in angle units, in [0, 65535] (0 only for an invalid gap).</summary>
        public int Span => (End - Start) & 0xFFFF;

        /// <summary>Builds a gap from world angles in radians (any finite values; they wrap).</summary>
        public static RingGap FromRadians(float start, float end)
        {
            return new RingGap(RenderQuantization.QuantizeAngle(start), RenderQuantization.QuantizeAngle(end));
        }

        public bool Equals(RingGap other)
        {
            return Start == other.Start && End == other.End;
        }

        public override bool Equals(object obj)
        {
            return obj is RingGap other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Start << 16) | End;
        }

        public static bool operator ==(RingGap a, RingGap b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(RingGap a, RingGap b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return "[" + Start + ".." + End + ")";
        }
    }
}
