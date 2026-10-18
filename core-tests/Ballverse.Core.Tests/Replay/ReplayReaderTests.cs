using System;
using System.IO;
using System.Text;
using Ballverse.Core;
using Ballverse.Core.Replay;
using Xunit;

namespace Ballverse.Core.Tests.Replay
{
    public class ReplayReaderTests
    {
        /// <summary>Hand-built header so individual fields can be corrupted.</summary>
        static byte[] RawHeader(ushort version = 1, string modeId = "m", ushort tickRate = 60, string paramsJson = "{}",
            ulong? paramsHash = null, uint duration = 100, byte backend = 1, byte[] modeIdBytes = null, ulong? modeIdLength = null, string appVersion = "")
        {
            var ms = new MemoryStream();
            ms.Write(Encoding.ASCII.GetBytes("BVR1"), 0, 4);
            ms.Write(BitConverter.GetBytes(version), 0, 2);
            byte[] id = modeIdBytes ?? Encoding.UTF8.GetBytes(modeId);
            VarInt.WriteULong(ms, modeIdLength ?? (ulong)id.Length);
            ms.Write(id, 0, id.Length);
            ms.Write(BitConverter.GetBytes((ushort)7), 0, 2);
            byte[] app = Encoding.UTF8.GetBytes(appVersion);
            VarInt.WriteULong(ms, (ulong)app.Length);
            ms.Write(app, 0, app.Length);
            ms.WriteByte(backend);
            ms.Write(BitConverter.GetBytes(tickRate), 0, 2);
            ms.Write(BitConverter.GetBytes(42UL), 0, 8);
            byte[] p = Encoding.UTF8.GetBytes(paramsJson);
            VarInt.WriteULong(ms, (ulong)p.Length);
            ms.Write(p, 0, p.Length);
            ms.Write(BitConverter.GetBytes(paramsHash ?? XxHash64.Hash(p)), 0, 8);
            ms.Write(BitConverter.GetBytes(duration), 0, 4);
            return ms.ToArray();
        }

        static byte[] Concat(params byte[][] parts)
        {
            var ms = new MemoryStream();
            foreach (var p in parts) ms.Write(p, 0, p.Length);
            return ms.ToArray();
        }

        static void AssertInvalid(byte[] bytes)
        {
            Assert.Throws<InvalidDataException>(() => ReplayReader.Read(bytes));
            Assert.Throws<InvalidDataException>(() => ReplayReader.Read(new NonSeekableStream(new MemoryStream(bytes))));
        }

        [Fact]
        public void RawHeaderIsAccepted()
        {
            var r = ReplayReader.Read(RawHeader());
            Assert.Equal("m", r.Header.ModeId);
            Assert.Equal(7, r.Header.ModeSimVersion);
            Assert.Equal(42UL, r.Header.Seed);
            Assert.Equal(100u, r.Header.DurationTicks);
        }

        [Fact]
        public void BadMagicIsInvalidData()
        {
            byte[] b = RawHeader();
            b[3] = (byte)'2';
            AssertInvalid(b);
            AssertInvalid(Encoding.ASCII.GetBytes("BVS1xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(2)]
        [InlineData(65535)]
        public void UnsupportedVersionThrowsSpecificException(int version)
        {
            var e = Assert.Throws<UnsupportedReplayVersionException>(() => ReplayReader.Read(RawHeader((ushort)version)));
            Assert.Equal((ushort)version, e.Version);
            Assert.Throws<UnsupportedReplayVersionException>(() => ReplayReader.ReadHeader(new MemoryStream(RawHeader((ushort)version))));
        }

        [Fact]
        public void EmptyAndTinyInputsAreInvalidData()
        {
            AssertInvalid(new byte[0]);
            AssertInvalid(Encoding.ASCII.GetBytes("BV"));
            AssertInvalid(Encoding.ASCII.GetBytes("BVR1"));
            AssertInvalid(Concat(Encoding.ASCII.GetBytes("BVR1"), new byte[] { 1 }));
        }

        [Fact]
        public void EveryHeaderTruncationIsInvalidData()
        {
            byte[] full = RawHeader();
            for (int len = 0; len < full.Length; len++)
            {
                var cut = new byte[len];
                Array.Copy(full, cut, len);
                AssertInvalid(cut);
            }
        }

        [Fact]
        public void HeaderFieldValidation()
        {
            AssertInvalid(RawHeader(tickRate: 30));
            AssertInvalid(RawHeader(tickRate: 0));
            AssertInvalid(RawHeader(paramsHash: 1));
            AssertInvalid(RawHeader(modeId: ""));
            AssertInvalid(RawHeader(modeIdBytes: new byte[] { 0xC3, 0x28 }));          // invalid UTF-8
            AssertInvalid(RawHeader(modeIdBytes: new byte[] { 0xC0, 0x80 }));          // overlong encoding
            AssertInvalid(RawHeader(modeIdLength: (ulong)BvrFormat.MaxStringBytes + 1));
            AssertInvalid(RawHeader(modeIdLength: ulong.MaxValue));
            AssertInvalid(RawHeader(modeIdLength: 1000));                                // longer than the stream
            AssertInvalid(RawHeader(paramsJson: "\"" + new string('a', BvrFormat.MaxParamsJsonBytes) + "\""));
        }

        [Fact]
        public void UnknownPhysicsBackendIsPreserved()
        {
            Assert.Equal((PhysicsBackend)200, ReplayReader.Read(RawHeader(backend: 200)).Header.PhysicsBackend);
        }

        [Fact]
        public void UnknownChunksAreSkipped()
        {
            byte[] hash = ReplayTestUtil.Chunk("HASH", ReplayTestUtil.Bytes(60, 1, 2, 3, 4, 5, 6, 7, 8));
            byte[] unknown = ReplayTestUtil.Chunk("ZZZZ", new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF });
            byte[] emptyUnknown = ReplayTestUtil.Chunk("FUTR", new byte[0]);
            byte[] hash2 = ReplayTestUtil.Chunk("HASH", ReplayTestUtil.Bytes(60, 0, 0, 0, 0, 0, 0, 0, 0));
            byte[] bigUnknown = ReplayTestUtil.Chunk("BLOB", new byte[200 * 1024]);
            byte[] bytes = Concat(RawHeader(duration: 200), unknown, hash, emptyUnknown, bigUnknown, hash2);

            foreach (var stream in new Stream[] { new MemoryStream(bytes), new NonSeekableStream(new MemoryStream(bytes)) })
            {
                var r = ReplayReader.Read(stream);
                Assert.Equal(3, r.SkippedChunkCount);
                Assert.Equal(2, r.Hashes.Count);
                Assert.Equal(new ReplayStateHash(60, 0x0807060504030201UL), r.Hashes[0]);
                // Delta continues across chunks of the same tag.
                Assert.Equal(new ReplayStateHash(120, 0UL), r.Hashes[1]);
            }
        }

        [Fact]
        public void WriterOutputWithInsertedUnknownChunksStillReads()
        {
            var sample = ReplayTestUtil.Sample();
            byte[] original = ReplayWriter.WriteToArray(sample, 2);
            int headerLength = (int)HeaderLength(original);
            byte[] unknown = ReplayTestUtil.Chunk("X1ab", new byte[] { 1, 2, 3 });
            byte[] bytes = Concat(Slice(original, 0, headerLength), unknown, Slice(original, headerLength, original.Length - headerLength), unknown);
            var read = ReplayReader.Read(bytes);
            ReplayTestUtil.AssertEqual(sample, read);
            Assert.Equal(2, read.SkippedChunkCount);
        }

        [Fact]
        public void ChunkLengthBeyondStreamIsInvalidData()
        {
            var lying = Concat(RawHeader(), Encoding.ASCII.GetBytes("ZZZZ"), BitConverter.GetBytes(uint.MaxValue), new byte[] { 1, 2 });
            AssertInvalid(lying);
            var lyingKnown = Concat(RawHeader(), Encoding.ASCII.GetBytes("EVNT"), BitConverter.GetBytes(0x7FFFFFFFu), new byte[] { 1 });
            AssertInvalid(lyingKnown);
            var oversizeKnown = Concat(RawHeader(), Encoding.ASCII.GetBytes("HASH"), BitConverter.GetBytes((uint)BvrFormat.MaxChunkPayloadBytes + 1));
            AssertInvalid(oversizeKnown);
        }

        [Fact]
        public void TruncatedChunkTagOrLengthIsInvalidData()
        {
            AssertInvalid(Concat(RawHeader(), Encoding.ASCII.GetBytes("HA")));
            AssertInvalid(Concat(RawHeader(), Encoding.ASCII.GetBytes("HASH"), new byte[] { 1, 0 }));
        }

        [Fact]
        public void EmptyKnownChunkIsAllowed()
        {
            var r = ReplayReader.Read(Concat(RawHeader(), ReplayTestUtil.Chunk("EVNT", new byte[0])));
            Assert.Empty(r.Events);
        }

        [Fact]
        public void RecordCutInsideChunkIsInvalidData()
        {
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("HASH", ReplayTestUtil.Bytes(1, 2, 3))));
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("EVNT", ReplayTestUtil.Bytes(1, 0, 5))));
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("INPT", ReplayTestUtil.Bytes(1, 0, 3, 2))));
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("PARM", ReplayTestUtil.Bytes(1, 1, (byte)'a'))));
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("HASH", ReplayTestUtil.Bytes(0x80))));
        }

        [Fact]
        public void TickBeyondDurationIsInvalidData()
        {
            // duration 100: 60 + 41 = 101.
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("HASH", ReplayTestUtil.Bytes(60, 0, 0, 0, 0, 0, 0, 0, 0, 41, 0, 0, 0, 0, 0, 0, 0, 0))));
            // A huge delta must not overflow.
            var huge = new MemoryStream();
            VarInt.WriteULong(huge, ulong.MaxValue);
            huge.Write(new byte[8], 0, 8);
            AssertInvalid(Concat(RawHeader(duration: uint.MaxValue), ReplayTestUtil.Chunk("HASH", huge.ToArray())));
            // Exactly durationTicks is fine.
            var ok = ReplayReader.Read(Concat(RawHeader(), ReplayTestUtil.Chunk("HASH", ReplayTestUtil.Bytes(100, 0, 0, 0, 0, 0, 0, 0, 0))));
            Assert.Equal(100u, ok.Hashes[0].Tick);
        }

        [Fact]
        public void EventFieldValidation()
        {
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("EVNT", ReplayTestUtil.Bytes(0, 8, 0, 0, 0, 0))));      // type 8
            var bigBody = new MemoryStream();
            bigBody.WriteByte(0);
            bigBody.WriteByte(0);
            VarInt.WriteULong(bigBody, (ulong)int.MaxValue + 1);
            bigBody.Write(new byte[] { 0, 0, 0 }, 0, 3);
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("EVNT", bigBody.ToArray())));
        }

        [Fact]
        public void InputFieldValidation()
        {
            var tooMany = new MemoryStream();
            tooMany.WriteByte(0);
            tooMany.WriteByte(1);
            VarInt.WriteULong(tooMany, BvrFormat.MaxInputValues + 1);
            tooMany.Write(new byte[BvrFormat.MaxInputValues + 1], 0, BvrFormat.MaxInputValues + 1);
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("INPT", tooMany.ToArray())));

            var countBeyondPayload = ReplayTestUtil.Bytes(0, 1, 10, 0);
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("INPT", countBeyondPayload)));

            var outOfRange = new MemoryStream();
            outOfRange.WriteByte(0);
            outOfRange.WriteByte(1);
            outOfRange.WriteByte(1);
            VarInt.WriteLong(outOfRange, (long)int.MaxValue + 1);
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("INPT", outOfRange.ToArray())));
        }

        [Fact]
        public void ParamFieldValidation()
        {
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("PARM", ReplayTestUtil.Bytes(0, 0, 1, (byte)'1'))));    // empty id
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("PARM", ReplayTestUtil.Bytes(0, 1, (byte)'a', 0))));    // empty value
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("PARM", ReplayTestUtil.Bytes(0, 1, 0xFF, 1, (byte)'1'))));
            var r = ReplayReader.Read(Concat(RawHeader(), ReplayTestUtil.Chunk("PARM", ReplayTestUtil.Bytes(3, 1, (byte)'a', 1, (byte)'1'))));
            Assert.Equal(new ReplayParamChange(3, "a", "1"), r.ParamChanges[0]);
        }

        [Fact]
        public void ResultValidation()
        {
            byte[] ok = ReplayTestUtil.Bytes(0, 1, 0, 50);              // "", -1, no scores, finish 50
            Assert.Equal(new ReplayResult("", -1, null, 50), ReplayReader.Read(Concat(RawHeader(), ReplayTestUtil.Chunk("RSLT", ok))).Result);
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("RSLT", ok), ReplayTestUtil.Chunk("RSLT", ok)));   // duplicate
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("RSLT", ReplayTestUtil.Bytes(0, 1, 0, 50, 0))));   // trailing
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("RSLT", new byte[0])));                            // empty
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("RSLT", ReplayTestUtil.Bytes(0, 3, 0, 50))));      // index -2
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("RSLT", ReplayTestUtil.Bytes(0, 1, 0, 101))));     // beyond duration
            AssertInvalid(Concat(RawHeader(), ReplayTestUtil.Chunk("RSLT", ReplayTestUtil.Bytes(0, 1, 100, 0))));     // count > payload
        }

        [Fact]
        public void NullAndUnreadableStreamsThrowArgumentExceptions()
        {
            Assert.Throws<ArgumentNullException>(() => ReplayReader.Read((Stream)null));
            Assert.Throws<ArgumentNullException>(() => ReplayReader.Read((byte[])null));
            var closed = new MemoryStream();
            closed.Dispose();
            Assert.Throws<ArgumentException>(() => ReplayReader.Read(closed));
        }

        static byte[] Parm(string id, string value)
        {
            var ms = new MemoryStream();
            VarInt.WriteULong(ms, 1);
            byte[] a = Encoding.UTF8.GetBytes(id);
            VarInt.WriteULong(ms, (ulong)a.Length);
            ms.Write(a, 0, a.Length);
            byte[] b = Encoding.UTF8.GetBytes(value);
            VarInt.WriteULong(ms, (ulong)b.Length);
            ms.Write(b, 0, b.Length);
            return ReplayTestUtil.Chunk("PARM", ms.ToArray());
        }

        [Theory]
        [InlineData("2/0")]
        [InlineData("{a:1}")]
        [InlineData("'x'")]
        [InlineData("NaN")]
        [InlineData("[1,]")]
        [InlineData("[1, 2]")]
        [InlineData("not json")]
        public void Reader_RejectsNonJsonParmValue(string value)
        {
            AssertInvalid(Concat(RawHeader(), Parm("p", value)));
        }

        [Fact]
        public void Reader_AcceptsCompactJsonParmValues()
        {
            var r = ReplayReader.Read(Concat(RawHeader(), Parm("p", "{\"x\":[1,-2.5e3,true,null,\"a b\"]}")));
            Assert.Equal("{\"x\":[1,-2.5e3,true,null,\"a b\"]}", r.ParamChanges[0].ValueJson);
            ReplayWriter.WriteToArray(r);
        }

        [Fact]
        public void SingleByteFlipInParmValueIsDetected()
        {
            var w = new ReplayFile(ReplayTestUtil.Header(10), null, new[] { new ReplayParamChange(1, "speed", "2.0") }, null, null, null);
            byte[] bytes = ReplayWriter.WriteToArray(w);
            int dot = -1;
            for (int i = 1; i + 1 < bytes.Length; i++)
            {
                if (bytes[i - 1] == (byte)'2' && bytes[i] == (byte)'.' && bytes[i + 1] == (byte)'0') dot = i;
            }
            Assert.True(dot > 0);
            bytes[dot] = (byte)'/';
            AssertInvalid(bytes);
        }

        [Theory]
        [InlineData("not json at all")]
        [InlineData("")]
        [InlineData("{a:1}")]
        [InlineData("{\"a\":NaN}")]
        public void Reader_RejectsNonJsonParamsJson(string paramsJson)
        {
            AssertInvalid(RawHeader(paramsJson: paramsJson));
        }

        [Fact]
        public void Reader_AcceptsIndentedParamsJson()
        {
            var r = ReplayReader.Read(RawHeader(paramsJson: "{\n  \"a\": 1\n}"));
            Assert.Equal("{\n  \"a\": 1\n}", r.Header.ParamsJson);
            ReplayWriter.WriteToArray(r);
        }

        [Theory]
        [InlineData("../../projects/x")]
        [InlineData("Ring_Escape")]
        [InlineData("ring escape")]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        public void Reader_RejectsMalformedModeId(string modeId)
        {
            AssertInvalid(RawHeader(modeId: modeId));
        }

        [Fact]
        public void Reader_RejectsControlCharactersInHeaderStrings()
        {
            AssertInvalid(RawHeader(modeId: "ring_escape\0"));
            AssertInvalid(RawHeader(modeId: "ring\u202Eescape"));
            AssertInvalid(RawHeader(appVersion: "1.0\0"));
            AssertInvalid(RawHeader(appVersion: "1.0\u202E"));
        }

        [Fact]
        public void Reader_RejectsOverlongModeIdBeforeAllocating()
        {
            AssertInvalid(RawHeader(modeIdBytes: new byte[0], modeIdLength: 65));
        }

        [Theory]
        [InlineData("1.0\n")]
        [InlineData("версия")]
        [InlineData("1.0.0-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        public void Reader_RejectsMalformedAppVersion(string appVersion)
        {
            AssertInvalid(RawHeader(appVersion: appVersion));
        }

        [Fact]
        public void Reader_AcceptsPrintableAppVersion()
        {
            Assert.Equal("1.0.0 (42)", ReplayReader.Read(RawHeader(appVersion: "1.0.0 (42)")).Header.AppVersion);
        }

        internal static long HeaderLength(byte[] file)
        {
            var ms = new MemoryStream(file);
            ReplayReader.ReadHeader(ms);
            return ms.Position;
        }

        static byte[] Slice(byte[] b, int offset, int count)
        {
            var r = new byte[count];
            Array.Copy(b, offset, r, 0, count);
            return r;
        }
    }
}
