using System;
using System.IO;
using Ballverse.Core.Replay;
using Xunit;

namespace Ballverse.Core.Tests.Replay
{
    /// <summary>Output errors, Finish retries and reused output streams.</summary>
    public class ReplayWriterFailureTests
    {
        static void Fill(ReplayWriter w)
        {
            var sample = ReplayTestUtil.Sample();
            foreach (var x in sample.Inputs) w.AddInput(x);
            foreach (var x in sample.ParamChanges) w.AddParamChange(x);
            foreach (var x in sample.Hashes) w.AddHash(x);
            foreach (var x in sample.Events) w.AddEvent(x);
            w.SetResult(sample.Result);
        }

        static byte[] Clean(int recordsPerChunk)
        {
            var ms = new MemoryStream();
            var w = new ReplayWriter(ms, ReplayTestUtil.Header(), recordsPerChunk);
            Fill(w);
            w.Finish(800);
            return ms.ToArray();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void FinishRetryAfterFlushIOException_DoesNotDuplicateResult(bool seekable)
        {
            var output = new FaultyStream(seekable);
            var w = new ReplayWriter(output, ReplayTestUtil.Header(), 3);
            Fill(w);
            output.FlushFailures = 1;
            Assert.Throws<IOException>(() => w.Finish(800));
            Assert.False(w.IsFinished);
            Assert.Throws<InvalidOperationException>(() => w.AddHash(800, 1));
            Assert.Throws<ArgumentException>(() => w.Finish(801));

            w.Finish(800);
            Assert.True(w.IsFinished);
            Assert.Equal(Clean(3), output.Inner.ToArray());
            ReplayTestUtil.AssertEqual(ReplayTestUtil.Sample(), ReplayReader.Read(output.Inner.ToArray()));
        }

        [Fact]
        public void FinishRetryAfterTrailerWriteFailure_Seekable()
        {
            var output = new FaultyStream(true);
            var w = new ReplayWriter(output, ReplayTestUtil.Header(), 3);
            Fill(w);
            output.WriteFailures = 1;
            Assert.Throws<IOException>(() => w.Finish(800));
            output.WriteFailures = 2;
            output.PartialWriteBytes = 2;
            Assert.Throws<IOException>(() => w.Finish(800));
            output.WriteFailures = 0;
            w.Finish(800);
            Assert.Equal(Clean(3), output.Inner.ToArray());
        }

        [Fact]
        public void CopyFailureOnNonSeekableOutputFaultsTheWriter()
        {
            var output = new FaultyStream(false);
            var w = new ReplayWriter(output, ReplayTestUtil.Header());
            Fill(w);
            output.WriteFailures = 1;
            output.PartialWriteBytes = 10;
            Assert.Throws<IOException>(() => w.Finish(800));
            Assert.False(w.IsFinished);
            Assert.Throws<InvalidOperationException>(() => w.Finish(800));
            Assert.Throws<InvalidOperationException>(() => w.AddHash(800, 1));
        }

        [Fact]
        public void MidRunChunkWriteFailureFaultsTheWriter()
        {
            var output = new FaultyStream(true);
            var w = new ReplayWriter(output, ReplayTestUtil.Header(), 2);
            w.AddHash(0, 1);
            output.WriteFailures = 1;
            Assert.Throws<IOException>(() => w.AddHash(1, 2));
            Assert.Throws<InvalidOperationException>(() => w.AddHash(2, 3));
            Assert.Throws<InvalidOperationException>(() => w.Finish(10));
        }

        [Fact]
        public void WriterOverExistingLongerContent_Truncates()
        {
            var ms = new MemoryStream();
            ReplayWriter.Write(ms, ReplayTestUtil.Sample(), 2);
            long oldLength = ms.Length;
            ms.Position = 0;
            var w = new ReplayWriter(ms, ReplayTestUtil.Header());
            w.AddHash(0, 2222);
            w.Finish(10);
            Assert.True(ms.Length < oldLength);
            Assert.Equal(ms.Position, ms.Length);
            var read = ReplayReader.Read(ms.ToArray());
            Assert.Single(read.Hashes);
            Assert.Empty(read.Events);
            Assert.Null(read.Result);
        }

        [Fact]
        public void WriterOverUntruncatableLongerContent_Throws()
        {
            var output = new FaultyStream(true) { SetLengthSupported = false };
            ReplayWriter.Write(output, ReplayTestUtil.Sample());
            output.Position = 0;
            var w = new ReplayWriter(output, ReplayTestUtil.Header());
            Assert.Throws<IOException>(() => w.Finish(10));
            Assert.False(w.IsFinished);
        }

        [Fact]
        public void AddInputDoesNotAllocatePerCall()
        {
            // Pre-sized output so only the writer's own allocations are measured, not the destination growing.
            var w = new ReplayWriter(new MemoryStream(1 << 20), ReplayTestUtil.Header());
            var values = new[] { 1200, -3400, 65535, 7 };
            for (uint t = 0; t < 2000; t++) w.AddInput(t, 1, values);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (uint t = 2000; t < 12000; t++) w.AddInput(t, 1, values);
            long perCall = (GC.GetAllocatedBytesForCurrentThread() - before) / 10000;
            Assert.True(perCall < 8, "AddInput allocates " + perCall + " B/call");
        }
    }

    /// <summary>MemoryStream wrapper that fails on demand; optionally hides seeking or truncation.</summary>
    internal sealed class FaultyStream : Stream
    {
        readonly bool _seekable;

        internal FaultyStream(bool seekable)
        {
            _seekable = seekable;
        }

        internal MemoryStream Inner { get; } = new MemoryStream();

        /// <summary>Number of upcoming Flush calls that throw.</summary>
        internal int FlushFailures;

        /// <summary>Number of upcoming Write calls that throw.</summary>
        internal int WriteFailures;

        /// <summary>Bytes a failing Write still passes through before throwing.</summary>
        internal int PartialWriteBytes;

        internal bool SetLengthSupported = true;

        public override bool CanRead => true;
        public override bool CanSeek => _seekable;
        public override bool CanWrite => true;
        public override long Length => _seekable ? Inner.Length : throw new NotSupportedException();

        public override long Position
        {
            get => _seekable ? Inner.Position : throw new NotSupportedException();
            set
            {
                if (!_seekable) throw new NotSupportedException();
                Inner.Position = value;
            }
        }

        public override void Flush()
        {
            if (FlushFailures > 0)
            {
                FlushFailures--;
                throw new IOException("Disk full (flush)");
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return Inner.Read(buffer, offset, count);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            if (!_seekable) throw new NotSupportedException();
            return Inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            if (!_seekable || !SetLengthSupported) throw new NotSupportedException();
            Inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (WriteFailures > 0)
            {
                WriteFailures--;
                Inner.Write(buffer, offset, Math.Min(count, PartialWriteBytes));
                throw new IOException("Disk full (write)");
            }
            Inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            byte[] copy = buffer.ToArray();
            Write(copy, 0, copy.Length);
        }
    }
}
