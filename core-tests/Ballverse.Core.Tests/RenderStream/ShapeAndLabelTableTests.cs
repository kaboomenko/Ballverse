using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.RenderStream;
using Ballverse.Core.RenderStream.Internal;
using Xunit;

namespace Ballverse.Core.Tests.RenderStream
{
    /// <summary>Shape table and label table: registration, round trip, limits and corrupt-table handling (spec §7.4, §7.7, §2.9).</summary>
    public class ShapeAndLabelTableTests
    {
        static RenderStreamWriter NewWriter(MemoryStream ms)
        {
            return new RenderStreamWriter(ms, new RenderStreamHeader("m"), leaveOpen: true);
        }

        static RenderFrame Frame(params BodyState[] bodies)
        {
            var f = new RenderFrame();
            foreach (var b in bodies) f.AddBody(b);
            return f;
        }

        static BodyState Body(int id, int shape, int label = BodyState.NoLabel, short x = 0)
        {
            return new BodyState(id, shape, x, 0, 0, 8192, 1, BodyFlags.Visible, label);
        }

        // ------------------------------------------------------------------ registration

        [Fact]
        public void DefineShapeAssignsSequentialIdsAndDeduplicates()
        {
            using (var w = NewWriter(new MemoryStream()))
            {
                Assert.Equal(0, w.DefineShape(ShapeDefinition.Circle));
                Assert.Equal(1, w.DefineShape(ShapeDefinition.Capsule(10, 2)));
                Assert.Equal(0, w.DefineShape(ShapeDefinition.Circle));
                Assert.Equal(1, w.DefineShape(ShapeDefinition.Capsule(10, 2)));  // equal, distinct instance
                Assert.Equal(2, w.DefineShape(ShapeDefinition.Capsule(10, 3)));
                Assert.Equal(3, w.ShapeCount);
                Assert.Throws<ArgumentNullException>(() => w.DefineShape(null));
            }
        }

        [Fact]
        public void DefineLabelAssignsIdsFromOneAndDeduplicates()
        {
            using (var w = NewWriter(new MemoryStream()))
            {
                Assert.Equal(1, w.DefineLabel("Анна"));
                Assert.Equal(2, w.DefineLabel("анна"));   // ordinal, case-sensitive
                Assert.Equal(1, w.DefineLabel("Анна"));
                Assert.Equal(3, w.DefineLabel(new string('x', RenderStreamLimits.MaxLabelLength)));
                Assert.Equal(4, w.DefineLabel("🙂🙂"));     // surrogate pairs count as 2 units each
                Assert.Equal(4, w.LabelCount);

                Assert.Throws<ArgumentNullException>(() => w.DefineLabel(null));
                Assert.Throws<ArgumentException>(() => w.DefineLabel(""));
                Assert.Throws<ArgumentException>(() => w.DefineLabel(new string('x', RenderStreamLimits.MaxLabelLength + 1)));
                Assert.Throws<ArgumentException>(() => w.DefineLabel("\uD83D"));     // lone high surrogate
                Assert.Throws<ArgumentException>(() => w.DefineLabel("a\uDC00b"));                              // lone low surrogate
                Assert.Throws<ArgumentException>(() => w.DefineLabel("ab\uD800"));                              // high surrogate at the end
                Assert.False(w.IsFaulted);
                Assert.Equal(4, w.LabelCount);
            }
        }

        [Fact]
        public void LabelTableLimit()
        {
            var ms = new MemoryStream();
            using (var w = NewWriter(ms))
            {
                for (int i = 1; i <= RenderStreamLimits.MaxLabels; i++) Assert.Equal(i, w.DefineLabel("№" + i));
                Assert.Throws<InvalidOperationException>(() => w.DefineLabel("one more"));
                Assert.Equal(RenderStreamLimits.MaxLabels, w.DefineLabel("№" + RenderStreamLimits.MaxLabels)); // existing still resolves
                Assert.False(w.IsFaulted);
                w.DefineShape(ShapeDefinition.Circle);
                w.AppendFrame(0, Frame(Body(1, 0, RenderStreamLimits.MaxLabels)));
                w.Finish();
            }
            ms.Position = 0;
            using (var r = new RenderStreamReader(ms))
            {
                Assert.Equal(RenderStreamLimits.MaxLabels, r.LabelCount);
                Assert.Equal("№1024", r.GetLabel(r.ReadFrame(0).GetBody(0).LabelId));
            }
        }

        [Fact]
        public void ShapeCountLimit()
        {
            var ms = new MemoryStream();
            using (var w = NewWriter(ms))
            {
                for (int i = 0; i < RenderStreamLimits.MaxShapes; i++) w.DefineShape(ShapeDefinition.Capsule((ushort)i, 1));
                Assert.Equal(RenderStreamLimits.MaxShapes, w.ShapeCount);
                Assert.Throws<InvalidOperationException>(() => w.DefineShape(ShapeDefinition.Capsule(0, 2)));
                Assert.Equal(17, w.DefineShape(ShapeDefinition.Capsule(17, 1)));
                Assert.False(w.IsFaulted);
                w.AppendFrame(0, Frame(Body(1, RenderStreamLimits.MaxShapes - 1)));
                w.Finish();
            }
            ms.Position = 0;
            using (var r = new RenderStreamReader(ms))
            {
                Assert.Equal(RenderStreamLimits.MaxShapes, r.Shapes.Count);
                Assert.Equal(ShapeDefinition.Capsule(65535, 1), r.Shapes[r.ReadFrame(0).GetBody(0).ShapeId]);
            }
        }

        [Fact]
        public void ShapeTableByteLimit()
        {
            // Random full-range polylines cost ~6 bytes per point; ~680 of them exceed 16 MiB.
            var rng = Rng.FromSeed(0x5A);
            var points = new ShapePoint[RenderStreamLimits.MaxPolylinePoints];
            for (int i = 0; i < points.Length; i++) points[i] = new ShapePoint((short)rng.NextUInt(), (short)rng.NextUInt());
            using (var w = NewWriter(new MemoryStream()))
            {
                bool threw = false;
                for (int s = 0; s < 1000 && !threw; s++)
                {
                    points[0] = new ShapePoint((short)s, 0); // distinct shapes defeat deduplication
                    try
                    {
                        w.DefineShape(ShapeDefinition.Polyline(points, false, 1));
                    }
                    catch (InvalidOperationException)
                    {
                        threw = true;
                    }
                }
                Assert.True(threw, "shape table byte limit not enforced");
                Assert.False(w.IsFaulted);
                Assert.Equal(w.ShapeCount, w.DefineShape(ShapeDefinition.Circle)); // small shapes still fit
                w.Finish();
            }
        }

        [Fact]
        public void DefinitionsAfterFinishOrDisposeAreRejected()
        {
            var w = NewWriter(new MemoryStream());
            w.Finish();
            Assert.Throws<InvalidOperationException>(() => w.DefineShape(ShapeDefinition.Circle));
            Assert.Throws<InvalidOperationException>(() => w.DefineLabel("a"));
            w.Dispose();
            var w2 = NewWriter(new MemoryStream());
            w2.Dispose();
            Assert.Throws<ObjectDisposedException>(() => w2.DefineShape(ShapeDefinition.Circle));
            Assert.Throws<ObjectDisposedException>(() => w2.DefineLabel("a"));
        }

        // ------------------------------------------------------------------ round trip

        [Fact]
        public void TablesRoundTripAndEmptyTablesAreValid()
        {
            byte[] data = StreamScenes.Write(new List<RenderFrame>(), null, w => { });
            using (var r = StreamScenes.Open(data))
            {
                Assert.Empty(r.Shapes);
                Assert.Equal(0, r.LabelCount);
                Assert.Null(r.GetLabel(BodyState.NoLabel));
                Assert.Throws<ArgumentOutOfRangeException>(() => r.GetLabel(1));
            }

            data = StreamScenes.Write(new List<RenderFrame>());
            using (var r = StreamScenes.Open(data))
            {
                Assert.Equal(StreamScenes.StandardShapes, r.Shapes);
                Assert.Equal(StreamScenes.StandardLabels, StreamScenes.Labels(r));
                Assert.Same(ShapeDefinition.Circle, r.Shapes[0]);
                Assert.Equal("Анна", r.GetLabel(1));
                Assert.Equal("Zoë 🙂", r.GetLabel(6));
                Assert.Throws<ArgumentOutOfRangeException>(() => r.GetLabel(-1));
                Assert.Throws<ArgumentOutOfRangeException>(() => r.GetLabel(StreamScenes.StandardLabels.Length + 1));
                Assert.Throws<NotSupportedException>(() => ((IList<ShapeDefinition>)r.Shapes)[0] = null);
            }
        }

        [Fact]
        public void ShapesAndLabelsCanBeDefinedMidStream()
        {
            var ms = new MemoryStream();
            using (var w = NewWriter(ms))
            {
                int circle = w.DefineShape(ShapeDefinition.Circle);
                for (int t = 0; t < 70; t++)
                {
                    var f = Frame(Body(1, circle, BodyState.NoLabel, (short)t));
                    if (t == 65)
                    {
                        Assert.Throws<ArgumentException>(() => w.AppendFrame(t, Frame(Body(1, circle), Body(2, 1))));
                    }
                    if (t >= 65)
                    {
                        int ring = w.DefineShape(ShapeDefinition.Ring(1000, 50, new[] { new RingGap(0, 4000) }));
                        int label = w.DefineLabel("late");
                        f.AddBody(Body(2, ring, label));
                    }
                    w.AppendFrame(t, f);
                }
                w.Finish();
            }
            ms.Position = 0;
            using (var r = new RenderStreamReader(ms))
            {
                Assert.Equal(2, r.Shapes.Count);
                var f = r.ReadFrame(69);
                Assert.Equal(ShapeKind.Ring, r.Shapes[f.GetBody(1).ShapeId].Kind);
                Assert.Equal("late", r.GetLabel(f.GetBody(1).LabelId));
                Assert.Equal(1, r.ReadFrame(64).BodyCount);
            }
        }

        [Fact]
        public void ShapeOrLabelChangeOnSameIdIsADespawnAndSpawn()
        {
            var frames = new List<RenderFrame>();
            for (int t = 0; t < 12; t++)
            {
                int shape = (t / 3) % 2 == 0 ? 0 : 9;
                int label = t >= 6 ? 3 : BodyState.NoLabel;
                frames.Add(Frame(Body(5, shape, label, (short)(t * 10)), Body(6, 1, 1, (short)t)));
            }
            byte[] data = StreamScenes.Write(frames, new RenderStreamHeader("m", 60, 60, RenderQuantization.Default));
            using (var r = StreamScenes.Open(data))
            {
                for (int t = 0; t < frames.Count; t++) Assert.True(frames[t].ContentEquals(r.ReadFrame(t)), "tick " + t);
            }
        }

        /// <summary>Rng-driven property test: random valid shapes of every kind (including extremes) round-trip exactly.</summary>
        [Fact]
        public void RandomShapesRoundTrip()
        {
            var rng = Rng.FromSeed(0x7AB1E);
            var shapes = new List<ShapeDefinition>();
            for (int i = 0; i < 400; i++) shapes.Add(RandomShape(ref rng));
            var ids = new List<int>();
            var ms = new MemoryStream();
            using (var w = NewWriter(ms))
            {
                foreach (var s in shapes) ids.Add(w.DefineShape(s));
                var f = new RenderFrame();
                for (int i = 0; i < ids.Count; i++) f.AddBody(Body(i, ids[i]));
                w.AppendFrame(0, f);
                w.Finish();
            }
            ms.Position = 0;
            using (var r = new RenderStreamReader(ms))
            {
                for (int i = 0; i < shapes.Count; i++)
                {
                    Assert.Equal(shapes[i], r.Shapes[ids[i]]);
                    Assert.Equal(shapes[i], r.Shapes[r.ReadFrame(0).GetBody(i).ShapeId]);
                }
            }
        }

        internal static ShapeDefinition RandomShape(ref Rng rng)
        {
            bool extreme = rng.NextUInt(4) == 0;
            switch (rng.NextUInt(5))
            {
                case 0:
                    return ShapeDefinition.Circle;
                case 1:
                    return ShapeDefinition.Polygon(RandomPoints(ref rng, rng.NextInt(3, RenderStreamLimits.MaxPolygonVertices + 1), extreme));
                case 2:
                {
                    bool closed = rng.NextBool();
                    int n = rng.NextUInt(8) == 0 ? RenderStreamLimits.MaxPolylinePoints : rng.NextInt(closed ? 3 : 2, 200);
                    return ShapeDefinition.Polyline(RandomPoints(ref rng, n, extreme), closed, NonZero(U16(ref rng, extreme)), rng.NextBool());
                }
                case 3:
                {
                    int n = rng.NextInt(0, 6);
                    if (rng.NextUInt(10) == 0) n = RenderStreamLimits.MaxRingGaps;
                    var gaps = new RingGap[n];
                    int slot = 65536 / Math.Max(1, n);
                    int offset = rng.NextInt(0, 65536);
                    for (int i = 0; i < n; i++)
                    {
                        int start = i * slot + rng.NextInt(0, slot / 2);
                        int span = rng.NextInt(1, slot / 2);
                        gaps[i] = new RingGap((ushort)start, (ushort)((start + span) & 0xFFFF));
                    }
                    if (n == 1) gaps[0] = new RingGap((ushort)offset, (ushort)(offset + rng.NextInt(1, 65536)));
                    ushort radius = NonZero(U16(ref rng, extreme));
                    ushort thickness = (ushort)Math.Min(NonZero(U16(ref rng, extreme)), 2 * radius);
                    return ShapeDefinition.Ring(radius, thickness, gaps, rng.NextBool());
                }
                default:
                    return ShapeDefinition.Capsule(U16(ref rng, extreme), NonZero(U16(ref rng, extreme)), rng.NextBool());
            }
        }

        /// <summary>Thickness and ring radius must be at least 1.</summary>
        static ushort NonZero(ushort value)
        {
            return value == 0 ? (ushort)1 : value;
        }

        static ushort U16(ref Rng rng, bool extreme)
        {
            if (extreme) return rng.NextBool() ? ushort.MaxValue : (ushort)0;
            return (ushort)rng.NextUInt(65536);
        }

        static ShapePoint[] RandomPoints(ref Rng rng, int n, bool extreme)
        {
            var p = new ShapePoint[n];
            short x = (short)rng.NextUInt(), y = (short)rng.NextUInt();
            for (int i = 0; i < n; i++)
            {
                if (extreme)
                {
                    p[i] = new ShapePoint(rng.NextBool() ? short.MinValue : short.MaxValue, rng.NextBool() ? short.MinValue : short.MaxValue);
                }
                else
                {
                    x = unchecked((short)(x + rng.NextInt(-300, 301)));
                    y = unchecked((short)(y + rng.NextInt(-300, 301)));
                    p[i] = new ShapePoint(x, y);
                }
            }
            return p;
        }

        // ------------------------------------------------------------------ corrupt tables behind valid checksums

        static readonly byte[] s_file = StreamScenes.Write(StreamScenes.Churn(70, 4));

        static byte[] ShapeTable(params Action<ByteWriter>[] shapes)
        {
            var w = new ByteWriter(64);
            w.WriteUVarint((ulong)shapes.Length);
            foreach (var s in shapes) s(w);
            return w.Span.ToArray();
        }

        static byte[] LabelTable(params byte[][] labels)
        {
            var w = new ByteWriter(64);
            w.WriteUVarint((ulong)labels.Length);
            foreach (var l in labels)
            {
                w.WriteUVarint((ulong)l.Length);
                w.WriteBytes(l);
            }
            return w.Span.ToArray();
        }

        static void AssertRejected(byte[] shapeTable, byte[] labelTable, string what)
        {
            byte[] data = BvsSurgery.ResealTables(s_file, shapeTable, labelTable);
            Assert.True(Throws(data), what + " was accepted");
        }

        static bool Throws(byte[] data)
        {
            try
            {
                using (var r = StreamScenes.Open(data))
                {
                    for (int t = 0; t < r.TickCount; t++) r.ReadFrame(t);
                }
                return false;
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }

        static Action<ByteWriter> Polygon(int n)
        {
            return w =>
            {
                w.WriteByte((byte)ShapeKind.Polygon);
                w.WriteUVarint((ulong)n);
                for (int i = 0; i < n; i++)
                {
                    w.WriteSVarint(i);
                    w.WriteSVarint(-i);
                }
            };
        }

        [Fact]
        public void ResealedTablesWithoutChangesAreIdentical()
        {
            var parts = BvsSurgery.Split(s_file);
            Assert.Equal(s_file, BvsSurgery.ResealTables(s_file, parts.ShapeTable, parts.LabelTable));
            Assert.False(Throws(s_file));
        }

        [Fact]
        public void InvalidShapeTablesAreRejected()
        {
            var full = BvsSurgery.Split(s_file).ShapeTable;
            AssertRejected(new byte[0], null, "empty shape table");
            AssertRejected(ShapeTable(w => w.WriteByte(9)), null, "unknown shape kind");
            AssertRejected(ShapeTable(Polygon(2)), null, "2-vertex polygon");
            AssertRejected(ShapeTable(Polygon(65)), null, "65-vertex polygon");
            AssertRejected(ShapeTable(w =>
            {
                w.WriteByte((byte)ShapeKind.Polyline);
                w.WriteByte(1); // closed
                w.WriteUVarint(10);
                w.WriteUVarint(2);
                w.WriteSVarint(0); w.WriteSVarint(0); w.WriteSVarint(1); w.WriteSVarint(1);
            }), null, "closed 2-point polyline");
            AssertRejected(ShapeTable(w =>
            {
                w.WriteByte((byte)ShapeKind.Polyline);
                w.WriteByte(0);
                w.WriteUVarint(10);
                w.WriteUVarint(RenderStreamLimits.MaxPolylinePoints + 1);
            }), null, "4097-point polyline");
            AssertRejected(ShapeTable(w =>
            {
                w.WriteByte((byte)ShapeKind.Polyline);
                w.WriteByte(0);
                w.WriteUVarint(10);
                w.WriteUVarint(4000); // declares far more points than bytes follow
                w.WriteSVarint(0);
            }), null, "polyline points beyond the table");
            AssertRejected(ShapeTable(w =>
            {
                w.WriteByte((byte)ShapeKind.Polyline);
                w.WriteByte(4); // undefined flag bit
                w.WriteUVarint(10);
                w.WriteUVarint(2);
                w.WriteSVarint(0); w.WriteSVarint(0); w.WriteSVarint(1); w.WriteSVarint(1);
            }), null, "undefined polyline flag");
            AssertRejected(ShapeTable(w =>
            {
                w.WriteByte((byte)ShapeKind.Polygon);
                w.WriteUVarint(3);
                w.WriteSVarint(70000); w.WriteSVarint(0); w.WriteSVarint(0); w.WriteSVarint(0); w.WriteSVarint(0); w.WriteSVarint(0);
            }), null, "point delta beyond int16");
            AssertRejected(ShapeTable(w =>
            {
                w.WriteByte((byte)ShapeKind.Ring);
                w.WriteByte(1); // closed flag is polyline-only
                w.WriteUVarint(10);
                w.WriteUVarint(10);
                w.WriteUVarint(0);
            }), null, "closed ring");
            AssertRejected(ShapeTable(w =>
            {
                w.WriteByte((byte)ShapeKind.Ring);
                w.WriteByte(0);
                w.WriteUVarint(10);
                w.WriteUVarint(10);
                w.WriteUVarint(2);
                w.WriteUVarint(500); w.WriteUVarint(600);
                w.WriteUVarint(100); w.WriteUVarint(200);
            }), null, "unordered ring gaps");
            AssertRejected(ShapeTable(w =>
            {
                w.WriteByte((byte)ShapeKind.Ring);
                w.WriteByte(0);
                w.WriteUVarint(10);
                w.WriteUVarint(10);
                w.WriteUVarint(1);
                w.WriteUVarint(7); w.WriteUVarint(7);
            }), null, "zero-span ring gap");
            AssertRejected(ShapeTable(w =>
            {
                w.WriteByte((byte)ShapeKind.Ring);
                w.WriteByte(0);
                w.WriteUVarint(10);
                w.WriteUVarint(10);
                w.WriteUVarint(RenderStreamLimits.MaxRingGaps + 1);
            }), null, "too many ring gaps");
            AssertRejected(ShapeTable(w =>
            {
                w.WriteByte((byte)ShapeKind.Capsule);
                w.WriteByte(0);
                w.WriteUVarint(65536);
                w.WriteUVarint(1);
            }), null, "capsule length beyond uint16");
            var trailing = new byte[full.Length + 1];
            Array.Copy(full, trailing, full.Length);
            AssertRejected(trailing, null, "trailing shape table byte");
            AssertRejected(ShapeTable(w => w.WriteByte(0)), null, "bodies referencing shapes beyond a 1-shape table");
        }

        static Action<ByteWriter> RawShape(ShapeKind kind, int flags, params ulong[] values)
        {
            return w =>
            {
                w.WriteByte((byte)kind);
                w.WriteByte((byte)flags);
                foreach (ulong v in values) w.WriteUVarint(v);
            };
        }

        static Action<ByteWriter> RawPolyline(ulong thickness)
        {
            return w =>
            {
                w.WriteByte((byte)ShapeKind.Polyline);
                w.WriteByte(0);
                w.WriteUVarint(thickness);
                w.WriteUVarint(2);
                w.WriteSVarint(0); w.WriteSVarint(0); w.WriteSVarint(1); w.WriteSVarint(1);
            };
        }

        static void AssertTableDecode(bool valid, byte[] table, string what)
        {
            if (valid)
            {
                Assert.Single(TableCodec.ReadShapeTable(table, 0, table.Length));
            }
            else
            {
                var e = Assert.Throws<InvalidDataException>(() => TableCodec.ReadShapeTable(table, 0, table.Length));
                Assert.False(string.IsNullOrEmpty(e.Message), what);
            }
        }

        [Fact]
        public void DegenerateShapeDimensionsInTableAreInvalidData()
        {
            // Rejected: zero thickness, zero ring radius, ring thicker than its diameter. Each next to a legal control.
            AssertTableDecode(false, ShapeTable(RawPolyline(0)), "polyline thickness 0");
            AssertTableDecode(true, ShapeTable(RawPolyline(1)), "polyline thickness 1");
            AssertTableDecode(false, ShapeTable(RawShape(ShapeKind.Capsule, 0, 100, 0)), "capsule thickness 0");
            AssertTableDecode(true, ShapeTable(RawShape(ShapeKind.Capsule, 0, 0, 1)), "capsule length 0, thickness 1");
            AssertTableDecode(false, ShapeTable(RawShape(ShapeKind.Ring, 0, 0, 0, 0)), "ring r=0 t=0");
            AssertTableDecode(false, ShapeTable(RawShape(ShapeKind.Ring, 0, 0, 1, 0)), "ring r=0 t=1");
            AssertTableDecode(false, ShapeTable(RawShape(ShapeKind.Ring, 0, 100, 0, 0)), "ring t=0");
            AssertTableDecode(false, ShapeTable(RawShape(ShapeKind.Ring, 0, 100, 5000, 0)), "ring t > 2r");
            AssertTableDecode(false, ShapeTable(RawShape(ShapeKind.Ring, 0, 100, 201, 0)), "ring t = 2r + 1");
            AssertTableDecode(true, ShapeTable(RawShape(ShapeKind.Ring, 0, 100, 200, 0)), "ring t = 2r");

            // Through the whole file as well (behind a valid checksum): the standard table with its capsule made degenerate.
            byte[] table = BvsSurgery.Split(s_file).ShapeTable;
            int at = table.Length - 14;   // Capsule(6000, 400), followed by the 8-byte Capsule(65535, 65535, fixed)
            Assert.Equal(new byte[] { (byte)ShapeKind.Capsule, 0, 0xF0, 0x2E, 0x90, 0x03 }, table.AsSpan(at, 6).ToArray());
            var degenerate = new byte[table.Length - 1];
            Array.Copy(table, degenerate, at + 4);
            degenerate[at + 4] = 0;       // thickness 400 (2-byte uvarint) -> 0
            Array.Copy(table, at + 6, degenerate, at + 5, table.Length - at - 6);
            Assert.True(Throws(BvsSurgery.ResealTables(s_file, degenerate, null)), "degenerate capsule accepted");
            degenerate[at + 4] = 1;       // control: thickness 1 is legal
            Assert.False(Throws(BvsSurgery.ResealTables(s_file, degenerate, null)));
        }

        [Fact]
        public void NonCircleShapesNeedScaleMaxOfAtLeastOne()
        {
            var fine = new RenderQuantization(16f, 0.5f);
            var ms = new MemoryStream();
            using (var w = new RenderStreamWriter(ms, new RenderStreamHeader("liquid_sand", 60, 60, fine), leaveOpen: true))
            {
                Assert.Equal(0, w.DefineShape(ShapeDefinition.Circle));      // circles: scale is a radius, fine resolution is legal
                Assert.Throws<ArgumentException>(() => w.DefineShape(ShapeDefinition.Capsule(100, 10)));
                Assert.Throws<ArgumentException>(() => w.DefineShape(StreamScenes.StandardShapes[1]));
                Assert.False(w.IsFaulted);
                Assert.Equal(1, w.ShapeCount);
                w.Finish();
            }
            ms.Position = 0;
            using (var r = new RenderStreamReader(ms)) Assert.Single(r.Shapes);

            ms = new MemoryStream();
            using (var w = new RenderStreamWriter(ms, new RenderStreamHeader("m", 60, 60, new RenderQuantization(16f, 1f)), leaveOpen: true))
            {
                Assert.Equal(0, w.DefineShape(ShapeDefinition.Capsule(100, 10)));  // ScaleMax 1 is enough
            }

            // A file whose header claims ScaleMax < 1 while holding non-circle shapes is rejected by the reader.
            var parts = BvsSurgery.Split(s_file);
            BitConverter.GetBytes(0.5f).CopyTo(parts.Header, 14);
            Assert.Throws<InvalidDataException>(() => StreamScenes.Open(BvsSurgery.Join(parts)));
            BitConverter.GetBytes(1f).CopyTo(parts.Header, 14);
            using (var r = StreamScenes.Open(BvsSurgery.Join(parts))) Assert.Equal(1f, r.Header.Quantization.ScaleMax);
        }

        [Fact]
        public void SkinsRoundTripIndependentlyOfLabelsAndAreStatic()
        {
            // M03 participant: name label + flag skin at the same time (spec §2.9 M03 "имена и флаги участников").
            var ms = new MemoryStream();
            using (var w = NewWriter(ms))
            {
                int circle = w.DefineShape(ShapeDefinition.Circle);
                int anna = w.DefineLabel("Анна");
                int flagUa = w.DefineLabel("flag/ua");
                int flagBr = w.DefineLabel("flag/br");
                Assert.Equal(4, w.DefineLabel("Bob"));
                var undefined = Frame(new BodyState(1, circle, 0, 0, 0, 8192, 1, BodyFlags.Visible, anna, 5));
                Assert.Throws<ArgumentException>(() => w.AppendFrame(0, undefined));
                undefined = Frame(new BodyState(1, circle, 0, 0, 0, 8192, 1, BodyFlags.Visible, anna, -1));
                Assert.Throws<ArgumentException>(() => w.AppendFrame(0, undefined));
                Assert.False(w.IsFaulted);
                for (int t = 0; t < 10; t++)
                {
                    int skin = t < 5 ? flagUa : flagBr;   // skin change on the same id: despawn + spawn
                    w.AppendFrame(t, Frame(
                        new BodyState(1, circle, (short)t, 0, 0, 8192, 1, BodyFlags.Visible, anna, skin),
                        new BodyState(2, circle, 0, (short)t, 0, 8192, 2, BodyFlags.Visible, 4, BodyState.NoSkin),
                        new BodyState(3, circle, 0, 0, 0, 8192, 3, BodyFlags.Visible, BodyState.NoLabel, flagUa)));
                }
                w.Finish();
            }
            ms.Position = 0;
            using (var r = new RenderStreamReader(ms))
            {
                for (int t = 0; t < 10; t++)
                {
                    var f = r.ReadFrame(t);
                    Assert.Equal("Анна", r.GetLabel(f.GetBody(0).LabelId));
                    Assert.Equal(t < 5 ? "flag/ua" : "flag/br", r.GetLabel(f.GetBody(0).SkinId));
                    Assert.Equal((short)t, f.GetBody(0).X);
                    Assert.Equal("Bob", r.GetLabel(f.GetBody(1).LabelId));
                    Assert.Null(r.GetLabel(f.GetBody(1).SkinId));
                    Assert.Null(r.GetLabel(f.GetBody(2).LabelId));
                    Assert.Equal("flag/ua", r.GetLabel(f.GetBody(2).SkinId));
                }
            }

            // In the raw block, frame 5 codes body 1 as a despawn + spawn.
            var parts = BvsSurgery.Split(ms.ToArray());
            var codec = new FrameCodec();
            var br = new ByteReader(parts.RawBlocks[0], 0, parts.RawBlocks[0].Length);
            var dest = new RenderFrame();
            for (int t = 0; t < 5; t++) codec.Decode(ref br, dest);
            Assert.Equal(1UL, br.ReadUVarint()); // despawn count of frame 5
        }

        [Fact]
        public void InvalidLabelTablesAreRejected()
        {
            var full = BvsSurgery.Split(s_file).LabelTable;
            AssertRejected(null, new byte[0], "empty label table");
            AssertRejected(null, LabelTable(new byte[] { 0xC3 }), "truncated UTF-8");
            AssertRejected(null, LabelTable(new byte[] { 0xC0, 0x80 }), "overlong UTF-8");
            AssertRejected(null, LabelTable(new byte[] { 0xED, 0xA0, 0x80 }), "UTF-8 encoded surrogate");
            AssertRejected(null, LabelTable(new byte[0]), "empty label");
            AssertRejected(null, LabelTable(System.Text.Encoding.UTF8.GetBytes(new string('a', 33))), "33-char label");
            var w = new ByteWriter(16);
            w.WriteUVarint(RenderStreamLimits.MaxLabels + 1);
            AssertRejected(null, w.Span.ToArray(), "label count beyond the limit");
            var trailing = new byte[full.Length + 1];
            Array.Copy(full, trailing, full.Length);
            AssertRejected(null, trailing, "trailing label table byte");
            AssertRejected(null, LabelTable(new byte[] { (byte)'a' }), "bodies referencing labels beyond a 1-label table");
        }

        [Fact]
        public void RandomTableCorruptionOnlyThrowsInvalidData()
        {
            var parts = BvsSurgery.Split(s_file);
            var rng = Rng.FromSeed(0x7AB);
            for (int iter = 0; iter < 300; iter++)
            {
                byte[] shapes = (byte[])parts.ShapeTable.Clone();
                byte[] labels = (byte[])parts.LabelTable.Clone();
                byte[] target = iter % 2 == 0 ? shapes : labels;
                int edits = rng.NextInt(1, 4);
                for (int e = 0; e < edits; e++) target[rng.NextInt(0, target.Length)] = (byte)rng.NextUInt();
                if (iter % 5 == 0) Array.Resize(ref target, rng.NextInt(0, target.Length));
                byte[] data = iter % 2 == 0 ? BvsSurgery.ResealTables(s_file, target, null) : BvsSurgery.ResealTables(s_file, null, target);
                Throws(data); // accepted (still valid) or InvalidDataException; anything else fails the test
            }
        }

        [Fact]
        public void DecoderRejectsUndefinedShapeAndLabelIds()
        {
            var w = new ByteWriter(64);
            w.WriteUVarint(1);  // one body
            w.WriteSVarint(7);  // id
            w.WriteUVarint(3);  // shape id
            w.WriteUVarint(2);  // label id
            w.WriteUVarint(1);  // skin id
            w.WriteSVarint(0); w.WriteSVarint(0); w.WriteUVarint(0); w.WriteUVarint(0); w.WriteByte(0); w.WriteByte(0);
            w.WriteUVarint(0);  // HUD
            byte[] data = w.Span.ToArray();

            var dest = new RenderFrame();
            var codec = new FrameCodec();
            codec.SetDecodeLimits(4, 2);
            var r = new ByteReader(data, 0, data.Length);
            codec.Decode(ref r, dest);
            Assert.Equal(new BodyState(7, 3, 0, 0, 0, 0, 0, BodyFlags.None, 2, 1), dest.GetBody(0));

            codec = new FrameCodec();
            codec.SetDecodeLimits(3, 2);
            r = new ByteReader(data, 0, data.Length);
            Assert.Throws<InvalidDataException>(() => codec.Decode(ref r, dest));

            codec = new FrameCodec();
            codec.SetDecodeLimits(4, 1);
            r = new ByteReader(data, 0, data.Length);
            Assert.Throws<InvalidDataException>(() => codec.Decode(ref r, dest));

            // Skin id above the string table count (label id 1 valid, skin id 2 not).
            byte[] skin = (byte[])data.Clone();
            skin[3] = 1;  // label id
            skin[4] = 2;  // skin id
            codec = new FrameCodec();
            codec.SetDecodeLimits(4, 1);
            r = new ByteReader(skin, 0, skin.Length);
            Assert.Throws<InvalidDataException>(() => codec.Decode(ref r, dest));
            codec = new FrameCodec();
            codec.SetDecodeLimits(4, 2);
            r = new ByteReader(skin, 0, skin.Length);
            codec.Decode(ref r, dest);
            Assert.Equal(2, dest.GetBody(0).SkinId);
        }
    }
}
