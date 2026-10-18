using System.IO;
using Ballverse.Core.Replay;
using Xunit;

namespace Ballverse.Core.Tests.Replay
{
    public class ReplayRoundTripTests
    {
        [Fact]
        public void EveryRecordTypeRoundTrips()
        {
            var sample = ReplayTestUtil.Sample();
            var read = ReplayReader.Read(ReplayWriter.WriteToArray(sample));
            ReplayTestUtil.AssertEqual(sample, read);
            Assert.Equal(0, read.SkippedChunkCount);
            Assert.Equal(60, read.Header.TickRate);
            Assert.Equal(BvrFormat.CurrentVersion, read.Header.FormatVersion);
        }

        [Fact]
        public void HeaderOnlyReplayRoundTrips()
        {
            var empty = new ReplayFile(ReplayTestUtil.Header(0), null, null, null, null, null);
            var read = ReplayReader.Read(ReplayWriter.WriteToArray(empty));
            ReplayTestUtil.AssertEqual(empty, read);
            Assert.Null(read.Result);
            Assert.Empty(read.Inputs);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(7)]
        public void MultiChunkStreamsRoundTrip(int recordsPerChunk)
        {
            var sample = ReplayTestUtil.Sample();
            byte[] bytes = ReplayWriter.WriteToArray(sample, recordsPerChunk);
            ReplayTestUtil.AssertEqual(sample, ReplayReader.Read(bytes));
        }

        [Fact]
        public void SmallerChunksProduceMoreChunks()
        {
            var sample = ReplayTestUtil.Random(5, 500);
            byte[] big = ReplayWriter.WriteToArray(sample, 1000);
            byte[] small = ReplayWriter.WriteToArray(sample, 10);
            Assert.True(small.Length > big.Length);
            ReplayTestUtil.AssertEqual(ReplayReader.Read(big), ReplayReader.Read(small));
        }

        [Fact]
        public void InterleavedChunksOfDifferentTagsRoundTrip()
        {
            var ms = new MemoryStream();
            var w = new ReplayWriter(ms, ReplayTestUtil.Header(), 2);
            for (uint t = 0; t < 20; t++)
            {
                w.AddEvent(t, ReplayEventType.Hit, (int)t, (ushort)t, -(long)t);
                if (t % 3 == 0) w.AddHash(t, t * 1000UL);
                if (t % 5 == 0) w.AddInput(t, 1, new[] { (int)t, -(int)t });
            }
            w.Finish(20);
            var read = ReplayReader.Read(ms.ToArray());
            Assert.Equal(20, read.Events.Count);
            Assert.Equal(7, read.Hashes.Count);
            Assert.Equal(4, read.Inputs.Count);
            for (int i = 0; i < 20; i++)
            {
                Assert.Equal((uint)i, read.Events[i].Tick);
                Assert.Equal(-(long)i, read.Events[i].Extra);
            }
        }

        [Theory]
        [InlineData(1UL)]
        [InlineData(2UL)]
        [InlineData(3UL)]
        [InlineData(4UL)]
        [InlineData(5UL)]
        public void RandomReplaysRoundTrip(ulong seed)
        {
            var replay = ReplayTestUtil.Random(seed, 400);
            var rng = Rng.FromSeed(seed, 1);
            int perChunk = rng.NextInt(1, 64);
            ReplayTestUtil.AssertEqual(replay, ReplayReader.Read(ReplayWriter.WriteToArray(replay, perChunk)));
        }

        [Fact]
        public void NonSeekableWriteAndTrickleReadRoundTrip()
        {
            var sample = ReplayTestUtil.Sample();
            var ms = new MemoryStream();
            ReplayWriter.Write(new NonSeekableStream(ms), sample, 2);
            byte[] bytes = ms.ToArray();
            Assert.Equal(ReplayWriter.WriteToArray(sample, 2), bytes);

            var read = ReplayReader.Read(new NonSeekableStream(new MemoryStream(bytes), trickle: true));
            ReplayTestUtil.AssertEqual(sample, read);
        }

        [Fact]
        public void WriterStartingMidStreamPatchesDurationAtTheRightOffset()
        {
            var ms = new MemoryStream();
            ms.Write(new byte[] { 9, 9, 9 }, 0, 3);
            ReplayWriter.Write(ms, ReplayTestUtil.Sample());
            ms.Position = 3;
            var read = ReplayReader.Read(ms);
            Assert.Equal(800u, read.Header.DurationTicks);
            Assert.Equal(new byte[] { 9, 9, 9 }, new[] { ms.GetBuffer()[0], ms.GetBuffer()[1], ms.GetBuffer()[2] });
        }

        [Fact]
        public void UnicodeStringsRoundTrip()
        {
            var header = new ReplayHeader("mode_1", 1, "", PhysicsBackend.None, 0, "{\"name\":\"Какой шар победит? 🎉\"}", 10);
            var replay = new ReplayFile(header, null, new[] { new ReplayParamChange(1, "текст", "\"π≈3.14\"") }, null, null,
                new ReplayResult("", -1, null, 10));
            var read = ReplayReader.Read(ReplayWriter.WriteToArray(replay));
            ReplayTestUtil.AssertEqual(replay, read);
            Assert.False(read.Result.HasWinner);
        }

        [Fact]
        public void ReadHeaderReadsOnlyTheHeader()
        {
            byte[] bytes = ReplayWriter.WriteToArray(ReplayTestUtil.Sample());
            var h = ReplayReader.ReadHeader(new MemoryStream(bytes));
            Assert.Equal("ring_escape", h.ModeId);
            Assert.Equal(800u, h.DurationTicks);
        }

        [Fact]
        public void IdenticalInputsProduceIdenticalBytes()
        {
            Assert.Equal(ReplayWriter.WriteToArray(ReplayTestUtil.Sample()), ReplayWriter.WriteToArray(ReplayTestUtil.Sample()));
            Assert.Equal(ReplayWriter.WriteToArray(ReplayTestUtil.Random(77, 300), 16),
                ReplayWriter.WriteToArray(ReplayTestUtil.Random(77, 300), 16));
            // Re-encoding a read file reproduces the original bytes.
            byte[] bytes = ReplayWriter.WriteToArray(ReplayTestUtil.Sample());
            Assert.Equal(bytes, ReplayWriter.WriteToArray(ReplayReader.Read(bytes)));
        }
    }
}
