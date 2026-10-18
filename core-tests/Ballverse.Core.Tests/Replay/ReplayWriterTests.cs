using System;
using System.IO;
using System.Text;
using Ballverse.Core.Replay;
using Xunit;

namespace Ballverse.Core.Tests.Replay
{
    public class ReplayWriterTests
    {
        [Fact]
        public void HeaderLayoutMatchesSpec()
        {
            var header = new ReplayHeader("ab", 0x0102, "v", PhysicsBackend.Box2DV3, 0x1122334455667788UL, "{}");
            var ms = new MemoryStream();
            new ReplayWriter(ms, header).Finish(0x0A0B0C0D);
            byte[] b = ms.ToArray();

            var expected = new MemoryStream();
            expected.Write(Encoding.ASCII.GetBytes("BVR1"), 0, 4);
            expected.Write(new byte[] { 1, 0 }, 0, 2);                       // formatVersion u16 LE
            expected.Write(new byte[] { 2, (byte)'a', (byte)'b' }, 0, 3);    // modeId
            expected.Write(new byte[] { 0x02, 0x01 }, 0, 2);                 // modeSimVersion
            expected.Write(new byte[] { 1, (byte)'v' }, 0, 2);               // appVersion
            expected.WriteByte(1);                                           // physicsBackend
            expected.Write(new byte[] { 60, 0 }, 0, 2);                      // tickRate
            expected.Write(new byte[] { 0x88, 0x77, 0x66, 0x55, 0x44, 0x33, 0x22, 0x11 }, 0, 8);
            expected.Write(new byte[] { 2, (byte)'{', (byte)'}' }, 0, 3);    // paramsJson
            expected.Write(BitConverter.GetBytes(XxHash64.Hash(Encoding.UTF8.GetBytes("{}"))), 0, 8);
            expected.Write(new byte[] { 0x0D, 0x0C, 0x0B, 0x0A }, 0, 4);     // durationTicks
            Assert.Equal(expected.ToArray(), b);
        }

        [Fact]
        public void RecordsAreDeltaEncodedInChunks()
        {
            var ms = new MemoryStream();
            var w = new ReplayWriter(ms, ReplayTestUtil.Header());
            int headerLength = (int)ms.Length;
            w.AddHash(60, 1);
            w.AddHash(200, 2);
            w.AddInput(130, 4, new[] { -1, 64 });
            w.AddEvent(5, ReplayEventType.Score, 300, 0x0102, -2);
            w.SetResult(new ReplayResult("x", -1, new[] { 1 }, 250));
            w.Finish(300);
            byte[] all = ms.ToArray();
            byte[] chunks = new byte[all.Length - headerLength];
            Array.Copy(all, headerLength, chunks, 0, chunks.Length);

            var expected = new MemoryStream();
            void Chunk(string tag, params int[] payload)
            {
                byte[] c = ReplayTestUtil.Chunk(tag, ReplayTestUtil.Bytes(payload));
                expected.Write(c, 0, c.Length);
            }
            // Flush order at Finish: INPT, PARM (none), HASH, EVNT, RSLT.
            Chunk("INPT", 0x82, 0x01, 4, 2, 1, 0x80, 0x01);                 // Δ130, type 4, count 2, zz(-1)=1, zz(64)=128
            Chunk("HASH", 60, 1, 0, 0, 0, 0, 0, 0, 0, 140, 0x01, 2, 0, 0, 0, 0, 0, 0, 0); // Δ60, Δ140
            Chunk("EVNT", 5, 6, 0xAC, 0x02, 0x02, 0x01, 3);                // Δ5, Score, body 300, impulse LE, zz(-2)=3
            Chunk("RSLT", 1, (byte)'x', 1, 1, 2, 250, 0x01);               // "x", zz(-1)=1, 1 score zz(1)=2, finish 250
            Assert.Equal(expected.ToArray(), chunks);
        }

        [Fact]
        public void TicksMustBeNonDecreasingPerStream()
        {
            var w = new ReplayWriter(new MemoryStream(), ReplayTestUtil.Header());
            w.AddHash(60, 1);
            w.AddHash(60, 2);
            Assert.Throws<ArgumentException>(() => w.AddHash(59, 3));
            w.AddEvent(10, ReplayEventType.Hit, 0, 0, 0);
            Assert.Throws<ArgumentException>(() => w.AddEvent(9, ReplayEventType.Hit, 0, 0, 0));
            w.AddInput(3, 0, null);
            Assert.Throws<ArgumentException>(() => w.AddInput(2, 0, null));
            w.AddParamChange(4, "a", "1");
            Assert.Throws<ArgumentException>(() => w.AddParamChange(3, "a", "1"));
            // Streams are independent: an event earlier than the last hash is fine.
            w.AddEvent(10, ReplayEventType.Hit, 0, 0, 0);
            w.Finish(60);
        }

        [Fact]
        public void RejectedRecordLeavesWriterUnchanged()
        {
            var a = new MemoryStream();
            var w = new ReplayWriter(a, ReplayTestUtil.Header());
            w.AddHash(10, 1);
            Assert.Throws<ArgumentException>(() => w.AddHash(5, 1));
            Assert.Throws<ArgumentException>(() => w.AddParamChange(20, "p", "{not json"));
            w.AddParamChange(20, "p", "true");
            w.Finish(20);

            var b = new MemoryStream();
            var w2 = new ReplayWriter(b, ReplayTestUtil.Header());
            w2.AddHash(10, 1);
            w2.AddParamChange(20, "p", "true");
            w2.Finish(20);
            Assert.Equal(b.ToArray(), a.ToArray());
        }

        [Fact]
        public void FinishRejectsDurationShorterThanRecords()
        {
            var ms = new MemoryStream();
            var w = new ReplayWriter(ms, ReplayTestUtil.Header());
            w.AddEvent(100, ReplayEventType.Hit, 0, 0, 0);
            Assert.Throws<ArgumentException>(() => w.Finish(99));
            Assert.False(w.IsFinished);
            var h = w.Finish(100);
            Assert.Equal(100u, h.DurationTicks);
            Assert.True(w.IsFinished);
        }

        [Fact]
        public void FinishRejectsResultBeyondDuration()
        {
            var w = new ReplayWriter(new MemoryStream(), ReplayTestUtil.Header());
            w.SetResult(new ReplayResult("a", 0, null, 50));
            Assert.Throws<ArgumentException>(() => w.Finish(49));
            w.Finish(50);
        }

        [Fact]
        public void WritesAfterFinishThrow()
        {
            var w = new ReplayWriter(new MemoryStream(), ReplayTestUtil.Header());
            w.Finish(0);
            Assert.Throws<InvalidOperationException>(() => w.AddHash(0, 0));
            Assert.Throws<InvalidOperationException>(() => w.AddInput(0, 0, null));
            Assert.Throws<InvalidOperationException>(() => w.AddEvent(default(ReplayEvent)));
            Assert.Throws<InvalidOperationException>(() => w.AddParamChange(0, "a", "1"));
            Assert.Throws<InvalidOperationException>(() => w.SetResult(new ReplayResult("", -1, null, 0)));
            Assert.Throws<InvalidOperationException>(() => w.Finish(0));
        }

        [Fact]
        public void ResultCanBeSetOnlyOnce()
        {
            var w = new ReplayWriter(new MemoryStream(), ReplayTestUtil.Header());
            w.SetResult(new ReplayResult("", -1, null, 0));
            Assert.Throws<InvalidOperationException>(() => w.SetResult(new ReplayResult("", -1, null, 0)));
        }

        [Fact]
        public void NonSeekableOutputIsWrittenOnlyAtFinish()
        {
            var ms = new MemoryStream();
            var w = new ReplayWriter(new NonSeekableStream(ms), ReplayTestUtil.Header());
            w.AddHash(0, 1);
            Assert.Equal(0, ms.Length);
            w.Finish(0);
            Assert.True(ms.Length > 0);
            Assert.Single(ReplayReader.Read(ms.ToArray()).Hashes);
        }

        [Fact]
        public void ConstructorValidatesArguments()
        {
            var h = ReplayTestUtil.Header();
            Assert.Throws<ArgumentNullException>(() => new ReplayWriter(null, h));
            Assert.Throws<ArgumentNullException>(() => new ReplayWriter(new MemoryStream(), null));
            Assert.Throws<ArgumentException>(() => new ReplayWriter(new MemoryStream(new byte[4], false), h));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayWriter(new MemoryStream(), h, 0));
            var badJson = new ReplayHeader("m", 1, "", PhysicsBackend.None, 0, "{\"a\":");
            Assert.Throws<ArgumentException>(() => new ReplayWriter(new MemoryStream(), badJson));
            var trailing = new ReplayHeader("m", 1, "", PhysicsBackend.None, 0, "{} {}");
            Assert.Throws<ArgumentException>(() => new ReplayWriter(new MemoryStream(), trailing));
        }

        [Fact]
        public void ParamValueMustBeJson()
        {
            var w = new ReplayWriter(new MemoryStream(), ReplayTestUtil.Header());
            Assert.Throws<ArgumentException>(() => w.AddParamChange(0, "p", "abc"));
            Assert.Throws<ArgumentException>(() => w.AddParamChange(0, "p", "1 2"));
            Assert.Throws<ArgumentException>(() => w.AddParamChange(0, "", "1"));
            Assert.Throws<ArgumentNullException>(() => w.AddParamChange(0, "p", null));
            w.AddParamChange(0, "p", "[1,2,3]");
            w.AddParamChange(0, "p", "null");
        }

        [Fact]
        public void AddParamValueSerializesCompactCoreJson()
        {
            var ms = new MemoryStream();
            var w = new ReplayWriter(ms, ReplayTestUtil.Header());
            w.AddParamValue(1, "speed", 1.5);
            w.AddParamValue(2, "layout", LayoutType.L2);
            w.AddParamValue(3, "pos", new[] { 1, 2 });
            w.Finish(3);
            var p = ReplayReader.Read(ms.ToArray()).ParamChanges;
            Assert.Equal("1.5", p[0].ValueJson);
            Assert.Equal("\"L2\"", p[1].ValueJson);
            Assert.Equal("[1,2]", p[2].ValueJson);
        }

        [Fact]
        public void LargeStreamFlushesBySize()
        {
            var ms = new MemoryStream();
            var w = new ReplayWriter(ms, ReplayTestUtil.Header(), 100000);
            string big = "\"" + new string('x', 100 * 1024) + "\"";
            for (uint t = 0; t < 5; t++) w.AddParamChange(t, "p", big);
            w.Finish(5);
            byte[] bytes = ms.ToArray();
            int parmChunks = CountTag(bytes, "PARM");
            Assert.True(parmChunks >= 2, "expected size-based flush, got " + parmChunks);
            Assert.Equal(5, ReplayReader.Read(bytes).ParamChanges.Count);
        }

        [Fact]
        public void RecordsPerChunkSplitsStreams()
        {
            var ms = new MemoryStream();
            var w = new ReplayWriter(ms, ReplayTestUtil.Header(), 4);
            for (uint t = 0; t < 10; t++) w.AddHash(t, t);
            w.Finish(10);
            Assert.Equal(3, CountTag(ms.ToArray(), "HASH"));
        }

        [Fact]
        public void RecordValidation()
        {
            Assert.Throws<ArgumentException>(() => new ReplayInput(0, 0, new int[BvrFormat.MaxInputValues + 1]));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayEvent(0, (ReplayEventType)8, 0, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayEvent(0, ReplayEventType.Hit, -1, 0, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ReplayResult("", -2, null, 0));
            Assert.Throws<ArgumentException>(() => new ReplayResult("", 0, new int[BvrFormat.MaxResultScores + 1], 0));
            Assert.Throws<ArgumentException>(() => new ReplayHeader("", 0, "", PhysicsBackend.None, 0, "{}"));
            Assert.Throws<ArgumentNullException>(() => new ReplayHeader("m", 0, null, PhysicsBackend.None, 0, "{}"));
            Assert.Throws<ArgumentException>(() => new ReplayHeader("m", 0, "", PhysicsBackend.None, 0,
                "\"" + new string('a', BvrFormat.MaxParamsJsonBytes) + "\""));
            Assert.Throws<ArgumentException>(() => new ReplayHeader("m\uD800", 0, "", PhysicsBackend.None, 0, "{}"));
            Assert.Equal("", new ReplayResult(null, -1, null, 0).WinnerId);
        }

        [Fact]
        public void InputValuesAreCopied()
        {
            var values = new[] { 1, 2 };
            var input = new ReplayInput(0, 0, values);
            values[0] = 99;
            Assert.Equal(1, input.Values[0]);
        }

        [Theory]
        [InlineData("ring_escape")]
        [InlineData("m")]
        [InlineData("mode_15")]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        public void HeaderAcceptsModeIdGrammar(string modeId)
        {
            Assert.Equal(modeId, new ReplayHeader(modeId, 0, "", PhysicsBackend.None, 0, "{}").ModeId);
        }

        [Theory]
        [InlineData("../../projects/x")]
        [InlineData("Ring_Escape")]
        [InlineData("ring-escape")]
        [InlineData("ring escape")]
        [InlineData("режим")]
        [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
        public void HeaderRejectsMalformedModeId(string modeId)
        {
            Assert.Throws<ArgumentException>(() => new ReplayHeader(modeId, 0, "", PhysicsBackend.None, 0, "{}"));
        }

        [Fact]
        public void HeaderRejectsControlCharactersInIds()
        {
            Assert.Throws<ArgumentException>(() => new ReplayHeader("x\0", 0, "", PhysicsBackend.None, 0, "{}"));
            Assert.Throws<ArgumentException>(() => new ReplayHeader("x\u202E", 0, "", PhysicsBackend.None, 0, "{}"));
            Assert.Throws<ArgumentException>(() => new ReplayHeader("m", 0, "1.0\0", PhysicsBackend.None, 0, "{}"));
            Assert.Throws<ArgumentException>(() => new ReplayHeader("m", 0, "1.0\n", PhysicsBackend.None, 0, "{}"));
            Assert.Throws<ArgumentException>(() => new ReplayHeader("m", 0, "версия", PhysicsBackend.None, 0, "{}"));
            Assert.Throws<ArgumentException>(() => new ReplayHeader("m", 0, new string('1', 65), PhysicsBackend.None, 0, "{}"));
            Assert.Equal(new string('1', 64), new ReplayHeader("m", 0, new string('1', 64), PhysicsBackend.None, 0, "{}").AppVersion);
            Assert.Equal(" ~", new ReplayHeader("m", 0, " ~", PhysicsBackend.None, 0, "{}").AppVersion);
        }

        [Fact]
        public void ParamsHashIsXxHash64OfUtf8Json()
        {
            var h = ReplayTestUtil.Header();
            Assert.Equal(XxHash64.Hash(Encoding.UTF8.GetBytes(ReplayTestUtil.ParamsJson)), h.ParamsHash);
            Assert.Equal(h.ParamsHash, ReplayHeader.ComputeParamsHash(ReplayTestUtil.ParamsJson));
            Assert.Equal(h.ParamsHash, h.WithDurationTicks(5).ParamsHash);
        }

        internal static int CountTag(byte[] file, string tag)
        {
            var ms = new MemoryStream(file);
            ReplayReader.ReadHeader(ms);
            int count = 0;
            var buf = new byte[8];
            while (ms.Read(buf, 0, 8) == 8)
            {
                if (Encoding.ASCII.GetString(buf, 0, 4) == tag) count++;
                ms.Position += BitConverter.ToUInt32(buf, 4);
            }
            return count;
        }
    }
}
