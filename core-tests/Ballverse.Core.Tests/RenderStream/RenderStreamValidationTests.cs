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
            f.AddBody(new BodyState(id, 0, 0, 0, 0, 100, 0, BodyFlags.Visible));
            return f;
        }

        static RenderStreamWriter NewWriter(MemoryStream ms)
        {
            var w = new RenderStreamWriter(ms, new RenderStreamHeader("m"), leaveOpen: true);
            StreamScenes.DefineStandardTables(w);
            return w;
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
                dup.AddBody(new BodyState(5, 1, 1, 1, 1, 1, 1, BodyFlags.None));
                Assert.Throws<ArgumentException>(() => w.AppendFrame(1, dup));

                var badShape = OneBody();
                badShape.AddBody(new BodyState(2, StreamScenes.StandardShapes.Length, 0, 0, 0, 0, 0, BodyFlags.None));
                Assert.Throws<ArgumentException>(() => w.AppendFrame(1, badShape));

                var negShape = OneBody();
                negShape.AddBody(new BodyState(2, -1, 0, 0, 0, 0, 0, BodyFlags.None));
                Assert.Throws<ArgumentException>(() => w.AppendFrame(1, negShape));

                var badLabel = OneBody();
                badLabel.AddBody(new BodyState(2, 0, 0, 0, 0, 0, 0, BodyFlags.None, StreamScenes.StandardLabels.Length + 1));
                Assert.Throws<ArgumentException>(() => w.AppendFrame(1, badLabel));

                var negLabel = OneBody();
                negLabel.AddBody(new BodyState(2, 0, 0, 0, 0, 0, 0, BodyFlags.None, -1));
                Assert.Throws<ArgumentException>(() => w.AppendFrame(1, negLabel));

                var unexpectedGrid = OneBody();
                unexpectedGrid.SetGridSize(2, 2);
                Assert.Throws<ArgumentException>(() => w.AppendFrame(1, unexpectedGrid));

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
        public void ReaderDisposesStreamWhenOpenFails()
        {
            var owned = new MemoryStream(new byte[100]);
            Assert.Throws<InvalidDataException>(() => new RenderStreamReader(owned));
            Assert.False(owned.CanRead, "stream not disposed after a failed open");

            var borrowed = new MemoryStream(new byte[100]);
            Assert.Throws<InvalidDataException>(() => new RenderStreamReader(borrowed, leaveOpen: true));
            Assert.True(borrowed.CanRead, "leaveOpen stream was disposed");
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
            // 30 frames: write #2 is the partial block flushed by Finish, #3 the tables, #4 the index, #5 the footer.
            for (int failOn = 2; failOn <= 5; failOn++)
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
        public void FailedFlushInFinishFaultsTheWriter()
        {
            // Flush runs after the footer write: the writer is faulted and not finished even though the bytes may be complete.
            var ms = new FlakyStream(int.MaxValue, failFlush: true);
            using (var w = NewWriter(ms))
            {
                for (int t = 0; t < 30; t++) w.AppendFrame(t, OneBody());
                Assert.Throws<IOException>(() => w.Finish());
                Assert.True(w.IsFaulted);
                Assert.False(w.IsFinished);
                Assert.Throws<InvalidOperationException>(() => w.Finish());
                Assert.Throws<InvalidOperationException>(() => w.DefineShape(ShapeDefinition.Circle));
                Assert.Throws<InvalidOperationException>(() => w.DefineLabel("x"));
            }
            // Documented: the in-memory bytes happen to be complete here, which is why callers must discard the file anyway.
            ms.Position = 0;
            using (var r = new RenderStreamReader(ms))
            {
                Assert.Equal(30, r.TickCount);
            }
        }

        [Fact]
        public void ConstructorDisposesStreamWhenHeaderWriteFails()
        {
            var owned = new FlakyStream(1);
            Assert.Throws<IOException>(() => new RenderStreamWriter(owned, new RenderStreamHeader("m")));
            Assert.False(owned.CanWrite, "stream not disposed after a failed header write");

            var borrowed = new FlakyStream(1);
            Assert.Throws<IOException>(() => new RenderStreamWriter(borrowed, new RenderStreamHeader("m"), leaveOpen: true));
            Assert.True(borrowed.CanWrite, "leaveOpen stream was disposed");
        }

        [Fact]
        public void ArgumentErrorsDoNotFaultTheWriter()
        {
            using (var w = NewWriter(new MemoryStream()))
            {
                Assert.Throws<ArgumentException>(() => w.AppendFrame(3, OneBody()));
                var dup = OneBody(1);
                dup.AddBody(new BodyState(1, 0, 0, 0, 0, 0, 0, BodyFlags.None));
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
            readonly bool _failFlush;
            int _writes;

            public FlakyStream(int failOn, bool failFlush = false)
            {
                _failOn = failOn;
                _failFlush = failFlush;
            }

            public override void Flush()
            {
                if (_failFlush) throw new IOException("Simulated flush failure");
                base.Flush();
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
