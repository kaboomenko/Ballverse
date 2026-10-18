using System;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Visual state of one body at one tick, in the <b>quantized</b> domain used by the .bvs render stream (spec §7.4).
    /// The stream stores exactly these values, so decoding is bit-exact. Convert to/from world units with
    /// <see cref="RenderQuantization"/>.
    /// </summary>
    public struct BodyState : IEquatable<BodyState>
    {
        /// <summary>Stable body id, unique within a frame. Ids may be reused after a body despawns.</summary>
        public int Id;
        public BodyKind Kind;
        /// <summary>Quantized X (see <see cref="RenderQuantization.QuantizePosition"/>).</summary>
        public short X;
        /// <summary>Quantized Y (see <see cref="RenderQuantization.QuantizePosition"/>).</summary>
        public short Y;
        /// <summary>Quantized angle, full turn = 65536 (see <see cref="RenderQuantization.QuantizeAngle"/>).</summary>
        public ushort Angle;
        /// <summary>Quantized radius (circles) or scale (other kinds) (see <see cref="RenderQuantization.QuantizeScale"/>).</summary>
        public ushort Scale;
        /// <summary>Palette color index (spec §7.4 "цвета — индексы палитры").</summary>
        public byte Color;
        public BodyFlags Flags;

        public BodyState(int id, BodyKind kind, short x, short y, ushort angle, ushort scale, byte color, BodyFlags flags)
        {
            Id = id;
            Kind = kind;
            X = x;
            Y = y;
            Angle = angle;
            Scale = scale;
            Color = color;
            Flags = flags;
        }

        public bool Equals(BodyState other)
        {
            return Id == other.Id && Kind == other.Kind && X == other.X && Y == other.Y && Angle == other.Angle
                && Scale == other.Scale && Color == other.Color && Flags == other.Flags;
        }

        public override bool Equals(object obj)
        {
            return obj is BodyState other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Id;
                h = h * 31 + (int)Kind;
                h = h * 31 + X;
                h = h * 31 + Y;
                h = h * 31 + Angle;
                h = h * 31 + Scale;
                h = h * 31 + Color;
                h = h * 31 + (int)Flags;
                return h;
            }
        }

        public static bool operator ==(BodyState a, BodyState b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(BodyState a, BodyState b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return "#" + Id + " " + Kind + " (" + X + "," + Y + ") a=" + Angle + " s=" + Scale + " c=" + Color + " f=" + (byte)Flags;
        }
    }
}
