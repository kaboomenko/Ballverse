using System;
using System.IO;
using System.Text;

namespace Ballverse.Core.RenderStream.Internal
{
    /// <summary>
    /// Encodes / decodes the immutable shape table and label table sections of a .bvs file (spec §7.4, §7.7).
    /// <code>
    /// shapeTable: uvarint shapeCount · shape[shapeCount]                       (shape id = position in the table)
    ///   shape:    kind u8 · payload
    ///     Circle:   —
    ///     Polygon:  uvarint vertexCount · points
    ///     Polyline: flags u8 · thickness uvarint · uvarint pointCount · points
    ///     Ring:     flags u8 · radius uvarint · thickness uvarint · uvarint gapCount · (start uvarint · end uvarint)[gapCount]
    ///     Capsule:  flags u8 · length uvarint · thickness uvarint
    ///   flags:    bit0 closed (polyline only), bit1 fixed thickness; other bits zero
    ///   points:   first point x svarint · y svarint, then per point the int16-wrapped delta from the previous one (svarint x, y)
    /// labelTable: uvarint labelCount · (uvarint utf8Length · UTF-8 bytes)[labelCount]   (label id = position + 1)
    /// </code>
    /// The decoder validates everything the public factories validate and only throws <see cref="InvalidDataException"/>.
    /// </summary>
    internal static class TableCodec
    {
        const int FlagClosed = 1;
        const int FlagFixedThickness = 2;
        const int FlagInvalid = ~(FlagClosed | FlagFixedThickness) & 0xFF;

        /// <summary>Upper bound of an encoded label table section.</summary>
        public const int MaxLabelTableBytes = 3 + RenderStreamLimits.MaxLabels * (1 + RenderStreamLimits.MaxLabelUtf8Bytes);

        static readonly UTF8Encoding s_strictUtf8 = new UTF8Encoding(false, true);

        // ------------------------------------------------------------------ shapes

        public static void WriteShape(ByteWriter w, ShapeDefinition shape)
        {
            w.WriteByte((byte)shape.Kind);
            int flags = shape.FixedThickness ? FlagFixedThickness : 0;
            switch (shape.Kind)
            {
                case ShapeKind.Circle:
                    break;
                case ShapeKind.Polygon:
                    w.WriteUVarint((ulong)shape.PointCount);
                    WritePoints(w, shape.Points);
                    break;
                case ShapeKind.Polyline:
                    if (shape.Closed) flags |= FlagClosed;
                    w.WriteByte((byte)flags);
                    w.WriteUVarint(shape.Thickness);
                    w.WriteUVarint((ulong)shape.PointCount);
                    WritePoints(w, shape.Points);
                    break;
                case ShapeKind.Ring:
                    w.WriteByte((byte)flags);
                    w.WriteUVarint(shape.Radius);
                    w.WriteUVarint(shape.Thickness);
                    var gaps = shape.Gaps;
                    w.WriteUVarint((ulong)gaps.Length);
                    for (int i = 0; i < gaps.Length; i++)
                    {
                        w.WriteUVarint(gaps[i].Start);
                        w.WriteUVarint(gaps[i].End);
                    }
                    break;
                case ShapeKind.Capsule:
                    w.WriteByte((byte)flags);
                    w.WriteUVarint(shape.Length);
                    w.WriteUVarint(shape.Thickness);
                    break;
                default:
                    throw new ArgumentException("Unknown shape kind " + shape.Kind);
            }
        }

        static void WritePoints(ByteWriter w, ReadOnlySpan<ShapePoint> points)
        {
            short px = 0, py = 0;
            for (int i = 0; i < points.Length; i++)
            {
                w.WriteSVarint(unchecked((short)(points[i].X - px)));
                w.WriteSVarint(unchecked((short)(points[i].Y - py)));
                px = points[i].X;
                py = points[i].Y;
            }
        }

        /// <summary>Decodes a complete shape table section; trailing bytes are an error.</summary>
        public static ShapeDefinition[] ReadShapeTable(byte[] data, int offset, int length)
        {
            var r = new ByteReader(data, offset, length);
            int count = r.ReadCount(Math.Min(RenderStreamLimits.MaxShapes, r.Remaining), "shape count");
            var shapes = new ShapeDefinition[count];
            for (int i = 0; i < count; i++) shapes[i] = ReadShape(ref r);
            if (r.Remaining != 0) throw new InvalidDataException("Trailing bytes after the shape table");
            return shapes;
        }

        static ShapeDefinition ReadShape(ref ByteReader r)
        {
            byte kind = r.ReadByte();
            switch ((ShapeKind)kind)
            {
                case ShapeKind.Circle:
                    return ShapeDefinition.Circle;
                case ShapeKind.Polygon:
                {
                    int n = r.ReadCount(RenderStreamLimits.MaxPolygonVertices, "polygon vertex count");
                    Check(ShapeDefinition.ValidatePointCount(ShapeKind.Polygon, false, n));
                    var points = ReadPoints(ref r, n);
                    return ShapeDefinition.CreateValidated(ShapeKind.Polygon, points, Array.Empty<RingGap>(), false, false, 0, 0, 0);
                }
                case ShapeKind.Polyline:
                {
                    int flags = ReadFlags(ref r, true);
                    ushort thickness = ReadUInt16Varint(ref r);
                    int n = r.ReadCount(RenderStreamLimits.MaxPolylinePoints, "polyline point count");
                    bool closed = (flags & FlagClosed) != 0;
                    Check(ShapeDefinition.ValidatePointCount(ShapeKind.Polyline, closed, n));
                    if (n * 2 > r.Remaining) throw new InvalidDataException("Polyline points exceed the table");
                    var points = ReadPoints(ref r, n);
                    return ShapeDefinition.CreateValidated(ShapeKind.Polyline, points, Array.Empty<RingGap>(), closed, (flags & FlagFixedThickness) != 0, thickness, 0, 0);
                }
                case ShapeKind.Ring:
                {
                    int flags = ReadFlags(ref r, false);
                    ushort radius = ReadUInt16Varint(ref r);
                    ushort thickness = ReadUInt16Varint(ref r);
                    int n = r.ReadCount(RenderStreamLimits.MaxRingGaps, "ring gap count");
                    var gaps = n == 0 ? Array.Empty<RingGap>() : new RingGap[n];
                    for (int i = 0; i < n; i++) gaps[i] = new RingGap(ReadUInt16Varint(ref r), ReadUInt16Varint(ref r));
                    Check(ShapeDefinition.ValidateGaps(gaps));
                    return ShapeDefinition.CreateValidated(ShapeKind.Ring, Array.Empty<ShapePoint>(), gaps, false, (flags & FlagFixedThickness) != 0, thickness, radius, 0);
                }
                case ShapeKind.Capsule:
                {
                    int flags = ReadFlags(ref r, false);
                    ushort length = ReadUInt16Varint(ref r);
                    ushort thickness = ReadUInt16Varint(ref r);
                    return ShapeDefinition.CreateValidated(ShapeKind.Capsule, Array.Empty<ShapePoint>(), Array.Empty<RingGap>(), false, (flags & FlagFixedThickness) != 0, thickness, 0, length);
                }
                default:
                    throw new InvalidDataException("Unknown shape kind " + kind);
            }
        }

        static int ReadFlags(ref ByteReader r, bool allowClosed)
        {
            int flags = r.ReadByte();
            if ((flags & FlagInvalid) != 0 || (!allowClosed && (flags & FlagClosed) != 0)) throw new InvalidDataException("Invalid shape flags " + flags);
            return flags;
        }

        static ShapePoint[] ReadPoints(ref ByteReader r, int count)
        {
            var points = new ShapePoint[count];
            short x = 0, y = 0;
            for (int i = 0; i < count; i++)
            {
                x = unchecked((short)(x + ReadInt16Delta(ref r)));
                y = unchecked((short)(y + ReadInt16Delta(ref r)));
                points[i] = new ShapePoint(x, y);
            }
            return points;
        }

        static int ReadInt16Delta(ref ByteReader r)
        {
            long v = r.ReadSVarint();
            if (v < short.MinValue || v > short.MaxValue) throw new InvalidDataException("Point delta out of int16 range");
            return (int)v;
        }

        static ushort ReadUInt16Varint(ref ByteReader r)
        {
            ulong v = r.ReadUVarint();
            if (v > ushort.MaxValue) throw new InvalidDataException("Value out of uint16 range");
            return (ushort)v;
        }

        static void Check(string error)
        {
            if (error != null) throw new InvalidDataException(error);
        }

        // ------------------------------------------------------------------ labels

        /// <summary>Returns null if <paramref name="label"/> is a valid label, otherwise the reason.</summary>
        public static string ValidateLabel(string label)
        {
            if (label == null) return "Label is null";
            if (label.Length == 0) return "Label is empty (use BodyState.NoLabel instead)";
            if (label.Length > RenderStreamLimits.MaxLabelLength) return "Label longer than " + RenderStreamLimits.MaxLabelLength + " characters";
            for (int i = 0; i < label.Length; i++)
            {
                char c = label[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= label.Length || !char.IsLowSurrogate(label[i + 1])) return "Label contains an unpaired surrogate";
                    i++;
                }
                else if (char.IsLowSurrogate(c))
                {
                    return "Label contains an unpaired surrogate";
                }
            }
            return null;
        }

        /// <summary>Appends one validated label (UTF-8 with a length prefix).</summary>
        public static void WriteLabel(ByteWriter w, string label)
        {
            byte[] bytes = s_strictUtf8.GetBytes(label);
            w.WriteUVarint((ulong)bytes.Length);
            w.WriteBytes(bytes);
        }

        /// <summary>Decodes a complete label table section; trailing bytes are an error.</summary>
        public static string[] ReadLabelTable(byte[] data, int offset, int length)
        {
            var r = new ByteReader(data, offset, length);
            int count = r.ReadCount(Math.Min(RenderStreamLimits.MaxLabels, r.Remaining), "label count");
            var labels = new string[count];
            for (int i = 0; i < count; i++)
            {
                int n = r.ReadCount(RenderStreamLimits.MaxLabelUtf8Bytes, "label length");
                ReadOnlySpan<byte> bytes = r.ReadBytes(n);
                string label;
                try
                {
                    label = s_strictUtf8.GetString(bytes.ToArray());
                }
                catch (ArgumentException e)
                {
                    throw new InvalidDataException("Label " + (i + 1) + " is not valid UTF-8", e);
                }
                Check(ValidateLabel(label));
                labels[i] = label;
            }
            if (r.Remaining != 0) throw new InvalidDataException("Trailing bytes after the label table");
            return labels;
        }
    }
}
