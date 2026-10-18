using System;
using System.IO;
using Ballverse.Core.RenderStream;
using Xunit;

namespace Ballverse.Core.Tests.RenderStream
{
    public class RenderStreamValidationTests
    {
        static RenderFrame OneBody(int id = 1)
        {
            var f = new RenderFrame();
            f.AddBody(new BodyState(id, BodyKind.Circle, 0, 0, 0, 100, 0, BodyFlags.Visible));
            return f;
        }

        static RenderStreamWriter NewWriter(MemoryStream ms)
        {
            return new RenderStreamWriter(ms, new RenderStreamHeader("m"), leaveOpen: true);
        }

        [Fact]
        public void TicksMustStartAtZeroAndBeConsecutive()
        {
            using (var w = NewWriter(new MemoryStream()))
            {
                Assert.Throws<ArgumentException>(() => w.AppendFrame(1, OneBody()));
                w.AppendFrame(0, OneBody());
                Assert.Throws<ArgumentException>(() => w.AppendFrame(0, OneBody()));
                Assert.Throws<ArgumentException>(() => w.AppendFrame(2, OneBody()));
                w.AppendFrame(1, OneBody());
                Assert.Equal(2, w.TickCount);
            }
        }

        [Fact]
        public void InvalidFramesAreRejectedWithoutCorruptingTheStream()
        {
            var ms = new MemoryStream();
            using (var w = NewWriter(ms))
            {
                w.AppendFrame(0, OneBody());

                var dup = OneBody(5);
                dup.AddBody(new BodyState(5, BodyKind.Polygon, 1, 1, 1, 1, 1, BodyFlags.None));
                Assert.Throws<ArgumentException>(() => w.AppendFrame(1, dup));

                var badKind = OneBody();
                badKind.AddBody(new BodyState(2, (BodyKind)4, 0, 0, 0, 0, 0, BodyFlags.None));
                Assert.Throws<ArgumentException>(() => w.AppendFrame(1, badKind));

                var badKey = OneBody();
                badKey.AddHud(HudEntry.MaxKey + 1, 0);
                Assert.Throws<ArgumentException>(() => w.AppendFrame(1, badKey));

                var negKey = OneBody();
                negKey.AddHud(-1, 0);
                Assert.Throws<ArgumentException>(() => w.AppendFrame(1, negKey));

                var dupKey = OneBody();
                dupKey.AddHud(3, 0);
                dupKey.AddHud(3, 1);
                Assert.Throws<ArgumentException>(() => w.AppendFrame(1, dupKey));

                Assert.Throws<ArgumentNullException>(() => w.AppendFrame(1, null));

                w.AppendFrame(1, OneBody(7));
                w.Finish();
            }
            ms.Position = 0;
            using (var r = new RenderStreamReader(ms))
            {
                Assert.Equal(2, r.TickCount);
                Assert.Equal(7, r.ReadFrame(1).GetBody(0).Id);
            }
        }

        [Fact]
        public void WriterLifecycle()
        {
            var ms = new MemoryStream();
            var w = NewWriter(ms);
            w.AppendFrame(0, OneBody());
            w.Finish();
            Assert.True(w.IsFinished);
            Assert.Equal(ms.Length, w.BytesWritten);
            Assert.Throws<InvalidOperationException>(() => w.AppendFrame(1, OneBody()));
            Assert.Throws<InvalidOperationException>(() => w.Finish());
            w.Dispose();
            w.Dispose();
            Assert.True(ms.CanRead, "leaveOpen keeps the stream open");

            var owned = new MemoryStream();
            var w2 = new RenderStreamWriter(owned, new RenderStreamHeader("m"));
            w2.Dispose();
            Assert.False(owned.CanRead);
            Assert.Throws<ObjectDisposedException>(() => w2.AppendFrame(0, OneBody()));
        }

        [Fact]
        public void UnfinishedStreamIsNotReadable()
        {
            var ms = new MemoryStream();
            using (var w = NewWriter(ms))
            {
                for (int t = 0; t < 100; t++) w.AppendFrame(t, OneBody());
            }
            ms.Position = 0;
            Assert.Throws<InvalidDataException>(() => new RenderStreamReader(ms));
        }

        [Fact]
        public void WriterArgumentValidation()
        {
            Assert.Throws<ArgumentNullException>(() => new RenderStreamWriter(null, new RenderStreamHeader("m")));
            Assert.Throws<ArgumentNullException>(() => new RenderStreamWriter(new MemoryStream(), null));
            Assert.Throws<ArgumentException>(() => new RenderStreamWriter(new MemoryStream(new byte[10], writable: false), new RenderStreamHeader("m")));
        }

        [Fact]
        public void HeaderValidation()
        {
            Assert.Throws<ArgumentNullException>(() => new RenderStreamHeader(null));
            Assert.Throws<ArgumentException>(() => new RenderStreamHeader(new string('x', 65)));
            Assert.Throws<ArgumentException>(() => new RenderStreamHeader(new string('ж', 33))); // 66 UTF-8 bytes
            new RenderStreamHeader(new string('x', 64));
            new RenderStreamHeader("");
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderStreamHeader("m", 0, 60, RenderQuantization.Default));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderStreamHeader("m", 1001, 60, RenderQuantization.Default));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderStreamHeader("m", 60, 0, RenderQuantization.Default));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderStreamHeader("m", 60, 3601, RenderQuantization.Default));
            Assert.Throws<ArgumentNullException>(() => new RenderStreamHeader("m", 60, 60, null));

            var h = new RenderStreamHeader("ring_escape");
            Assert.Equal(60, h.TickRate);
            Assert.Equal(60, h.BlockTicks);
            Assert.Same(RenderQuantization.Default, h.Quantization);
        }

        [Fact]
        public void ReaderArgumentValidation()
        {
            Assert.Throws<ArgumentNullException>(() => new RenderStreamReader(null));
            Assert.Throws<ArgumentException>(() => new RenderStreamReader(new NonSeekableStream()));

            byte[] data = StreamScenes.Write(StreamScenes.Churn(10, 1));
            var reader = StreamScenes.Open(data);
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadFrame(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => reader.ReadFrame(10));
            Assert.Throws<ArgumentNullException>(() => reader.ReadFrame(0, null));
            reader.Dispose();
            Assert.Throws<ObjectDisposedException>(() => reader.ReadFrame(0));
        }

        [Fact]
        public void UnsupportedVersionIsInvalidData()
        {
            byte[] data = StreamScenes.Write(StreamScenes.Churn(10, 1));
            data[4] = 2; // formatVersion u16 at offset 4
            Assert.Throws<InvalidDataException>(() => StreamScenes.Open(data));
        }

        [Fact]
        public void WriterRejectsUseAfterStreamWriteFailure()
        {
            // Write #1 is the header, write #2 the first block flush (completed by tick 59).
            var ms = new FlakyStream(2);
            using (var w = NewWriter(ms))
            {
                for (int t = 0; t < 59; t++) w.AppendFrame(t, OneBody());
                Assert.False(w.IsFaulted);
                Assert.Throws<IOException>(() => w.AppendFrame(59, OneBody()));
                Assert.True(w.IsFaulted);
                Assert.Throws<InvalidOperationException>(() => w.AppendFrame(59, OneBody()));
                Assert.Throws<InvalidOperationException>(() => w.AppendFrame(60, OneBody()));
                Assert.Throws<InvalidOperationException>(() => w.Finish());
                Assert.False(w.IsFinished);
            }
            ms.Position = 0;
            Assert.Throws<InvalidDataException>(() => new RenderStreamReader(ms));
        }

        [Fact]
        public void FailedFinishFaultsTheWriter()
        {
            // 30 frames: write #2 is the partial block flushed by Finish, #3 the index, #4 the footer.
            for (int failOn = 2; failOn <= 4; failOn++)
            {
                var ms = new FlakyStream(failOn);
                using (var w = NewWriter(ms))
                {
                    for (int t = 0; t < 30; t++) w.AppendFrame(t, OneBody());
                    Assert.Throws<IOException>(() => w.Finish());
                    Assert.True(w.IsFaulted, "failOn " + failOn);
                    Assert.False(w.IsFinished);
                    Assert.Throws<InvalidOperationException>(() => w.Finish());
                }
                ms.Position = 0;
                Assert.Throws<InvalidDataException>(() => new RenderStreamReader(ms));
            }
        }

        [Fact]
        public void ArgumentErrorsDoNotFaultTheWriter()
        {
            using (var w = NewWriter(new MemoryStream()))
            {
                Assert.Throws<ArgumentException>(() => w.AppendFrame(3, OneBody()));
                var dup = OneBody(1);
                dup.AddBody(new BodyState(1, BodyKind.Circle, 0, 0, 0, 0, 0, BodyFlags.None));
                Assert.Throws<ArgumentException>(() => w.AppendFrame(0, dup));
                Assert.False(w.IsFaulted);
                w.AppendFrame(0, OneBody());
                w.Finish();
            }
        }

        [Fact]
        public void HudEntryCapIsEnforced()
        {
            var ms = new MemoryStream();
            using (var w = NewWriter(ms))
            {
                var full = OneBody();
                for (int k = 0; k < HudEntry.MaxEntriesPerFrame; k++) full.AddHud(k * 200, k);
                w.AppendFrame(0, full);

                var over = OneBody();
                for (int k = 0; k <= HudEntry.MaxEntriesPerFrame; k++) over.AddHud(k, k);
                Assert.Throws<ArgumentException>(() => w.AppendFrame(1, over));
                Assert.False(w.IsFaulted);
                w.Finish();
            }
            ms.Position = 0;
            using (var r = new RenderStreamReader(ms))
            {
                Assert.Equal(HudEntry.MaxEntriesPerFrame, r.ReadFrame(0).HudCount);
            }
        }

        [Fact]
        public void BlocksIsReadOnly()
        {
            using (var reader = StreamScenes.Open(StreamScenes.Write(StreamScenes.Churn(70, 3))))
            {
                Assert.Null(reader.Blocks as RenderStreamBlockInfo[]);
                Assert.Throws<NotSupportedException>(() => ((System.Collections.Generic.IList<RenderStreamBlockInfo>)reader.Blocks)[1] = default);
                Assert.Equal(2, reader.Blocks.Count);
                Assert.Equal(60, reader.Blocks[1].FirstTick);
                Assert.Same(reader.Blocks, reader.Blocks);
                reader.ReadFrame(65);
            }
        }

        sealed class NonSeekableStream : MemoryStream
        {
            public override bool CanSeek => false;
        }

        /// <summary>Throws <see cref="IOException"/> on the N-th Write call (simulates disk full / a failed write-behind).</summary>
        sealed class FlakyStream : MemoryStream
        {
            readonly int _failOn;
            int _writes;

            public FlakyStream(int failOn)
            {
                _failOn = failOn;
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                _writes++;
                if (_writes == _failOn) throw new IOException("Simulated write failure");
                base.Write(buffer, offset, count);
            }
        }
    }
}
