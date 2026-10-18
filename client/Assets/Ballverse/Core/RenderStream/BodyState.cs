using System;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Visual state of one body at one tick, in the <b>quantized</b> domain used by the .bvs render stream (spec §7.4).
    /// The stream stores exactly these values, so decoding is bit-exact. Convert to/from world units with
    /// <see cref="RenderQuantization"/>.
    /// <para>
    /// Geometry is static and lives in the stream's shape table (spec §7.7): <see cref="ShapeId"/> references a
    /// <see cref="ShapeDefinition"/> registered with <see cref="RenderStreamWriter.DefineShape"/>; only the transform
    /// (<see cref="X"/>, <see cref="Y"/>, <see cref="Angle"/>, uniform <see cref="Scale"/>), colour and flags change per frame.
    /// <see cref="ShapeId"/> and <see cref="LabelId"/> are fixed for a body's lifetime (spawn to despawn); if a frame
    /// changes either for an existing id, the stream codes it as a despawn of the old body and a spawn of a new one.
    /// </para>
    /// </summary>
    public struct BodyState : IEquatable<BodyState>
    {
        /// <summary><see cref="LabelId"/> value for a body without a label.</summary>
        public const int NoLabel = 0;

        /// <summary>Stable body id, unique within a frame. Ids may be reused after a body despawns.</summary>
        public int Id;
        /// <summary>Shape table id (see <see cref="RenderStreamWriter.DefineShape"/>); default 0 = the first shape defined.</summary>
        public int ShapeId;
        /// <summary>Quantized X (see <see cref="RenderQuantization.QuantizePosition"/>).</summary>
        public short X;
        /// <summary>Quantized Y (see <see cref="RenderQuantization.QuantizePosition"/>).</summary>
        public short Y;
        /// <summary>Quantized angle, full turn = 65536 (see <see cref="RenderQuantization.QuantizeAngle"/>).</summary>
        public ushort Angle;
        /// <summary>
        /// Quantized uniform scale (see <see cref="RenderQuantization.QuantizeScale"/>): the radius in metres for a
        /// <see cref="ShapeKind.Circle"/>, a dimensionless factor for other shapes (see <see cref="ShapeDefinition"/>).
        /// </summary>
        public ushort Scale;
        /// <summary>Palette color index (spec §7.4 "цвета — индексы палитры").</summary>
        public byte Color;
        public BodyFlags Flags;
        /// <summary>Label string id (see <see cref="RenderStreamWriter.DefineLabel"/>), or <see cref="NoLabel"/>.</summary>
        public int LabelId;

        public BodyState(int id, int shapeId, short x, short y, ushort angle, ushort scale, byte color, BodyFlags flags, int labelId = NoLabel)
        {
            Id = id;
            ShapeId = shapeId;
            X = x;
            Y = y;
            Angle = angle;
            Scale = scale;
            Color = color;
            Flags = flags;
            LabelId = labelId;
        }

        public bool Equals(BodyState other)
        {
            return Id == other.Id && ShapeId == other.ShapeId && X == other.X && Y == other.Y && Angle == other.Angle
                && Scale == other.Scale && Color == other.Color && Flags == other.Flags && LabelId == other.LabelId;
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
                h = h * 31 + ShapeId;
                h = h * 31 + X;
                h = h * 31 + Y;
                h = h * 31 + Angle;
                h = h * 31 + Scale;
                h = h * 31 + Color;
                h = h * 31 + (int)Flags;
                h = h * 31 + LabelId;
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
            return "#" + Id + " shape " + ShapeId + " (" + X + "," + Y + ") a=" + Angle + " s=" + Scale + " c=" + Color + " f=" + (byte)Flags
                + (LabelId != NoLabel ? " label " + LabelId : "");
        }
    }
}
