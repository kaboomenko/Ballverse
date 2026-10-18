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
    /// <see cref="ShapeId"/>, <see cref="LabelId"/> and <see cref="SkinId"/> are fixed for a body's lifetime (spawn to
    /// despawn); if a frame changes any of them for an existing id, the stream codes it as a despawn of the old body and a
    /// spawn of a new one.
    /// </para>
    /// <para>
    /// Participant identity (spec §2.9 M03 "имена и флаги участников", M08 "флаги или цвета команд"): a body can carry a
    /// text <see cref="LabelId"/> (drawn on it) and, independently, a <see cref="SkinId"/> naming the image drawn on it
    /// (a flag or avatar). Both reference the stream's string table (<see cref="RenderStreamWriter.DefineLabel"/>), so the
    /// stream stays the only source for preview and export (§7.4). A skin string is an asset key whose meaning belongs to
    /// the renderer's skin catalogue (e.g. "flag/ua"); the stream only carries it.
    /// </para>
    /// </summary>
    public struct BodyState : IEquatable<BodyState>
    {
        /// <summary><see cref="LabelId"/> value for a body without a label.</summary>
        public const int NoLabel = 0;
        /// <summary><see cref="SkinId"/> value for a body drawn with its palette colour only.</summary>
        public const int NoSkin = 0;

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
        /// <summary>
        /// Skin key string id (see <see cref="RenderStreamWriter.DefineLabel"/>; same string table as labels), or
        /// <see cref="NoSkin"/>: the flag / image drawn on the body (spec §2.9 M03, M08).
        /// </summary>
        public int SkinId;

        public BodyState(int id, int shapeId, short x, short y, ushort angle, ushort scale, byte color, BodyFlags flags, int labelId = NoLabel, int skinId = NoSkin)
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
            SkinId = skinId;
        }

        public bool Equals(BodyState other)
        {
            return Id == other.Id && ShapeId == other.ShapeId && X == other.X && Y == other.Y && Angle == other.Angle
                && Scale == other.Scale && Color == other.Color && Flags == other.Flags && LabelId == other.LabelId
                && SkinId == other.SkinId;
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
                h = h * 31 + SkinId;
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
                + (LabelId != NoLabel ? " label " + LabelId : "") + (SkinId != NoSkin ? " skin " + SkinId : "");
        }
    }
}
