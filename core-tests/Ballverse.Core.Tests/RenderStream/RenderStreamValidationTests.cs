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

        sealed class NonSeekableStream : MemoryStream
        {
            public override bool CanSeek => false;
        }
    }
}
