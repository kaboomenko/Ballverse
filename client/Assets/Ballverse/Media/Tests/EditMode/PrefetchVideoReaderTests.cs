using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Media.Internal;
using NUnit.Framework;

namespace Ballverse.Media.Tests
{
    public sealed class PrefetchVideoReaderTests
    {
        const long Second = 1000000;

        MainThreadPump _pump;
        TestFrameSource _source;
        PrefetchVideoReader _reader;

        [SetUp]
        public void SetUp()
        {
            _pump = new MainThreadPump();
            _source = new TestFrameSource();
            _reader = new PrefetchVideoReader(_source);
            Assert.IsTrue(_reader.Ready.Wait(5000), "reader did not open");
        }

        [TearDown]
        public void TearDown()
        {
            _source.Release.Set();
            _reader.Dispose();
            _pump.Dispose();
        }

        static unsafe long PtsIn(VideoFrame frame)
        {
            return Marshal.ReadInt64((IntPtr)Unity.Collections.LowLevel.Unsafe.NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(frame.Data));
        }

        void AssertFrame(VideoFrame frame, long requested)
        {
            Assert.NotNull(frame);
            long expected = _source.ExpectedPts(requested);
            Assert.AreEqual(expected, frame.PtsUs, "PtsUs for " + requested);
            Assert.AreEqual(requested, frame.RequestedPtsUs);
            Assert.AreEqual(expected, PtsIn(frame), "pixels belong to another frame");
        }

        // QA finding 1: two frames held + a speculative prefetch in progress beyond the requested time used to take the
        // last pool buffer, so the requested frame never decoded (async read hung forever).
        [Test]
        public void AsyncRead_WithTwoFramesHeld_AndSpeculativeDecodeInProgress_Completes()
        {
            VideoFrame f1 = _reader.ReadFrameAt(1 * Second, 5000);
            _reader.Prefetch(3 * Second);
            _source.BlockPts = 10 * Second;
            _reader.Prefetch(10 * Second);
            VideoFrame f2 = _reader.ReadFrameAt(3 * Second, 5000);
            Assert.IsTrue(_source.Entered.Wait(5000), "the speculative decode of 10 s did not start");

            Task<VideoFrame> read = _reader.ReadFrameAtAsync(5 * Second, CancellationToken.None);
            _source.Release.Set();
            Assert.IsTrue(read.Wait(3000), "ReadFrameAtAsync(5 s) did not complete while holding 2 frames");
            AssertFrame(read.Result, 5 * Second);
            read.Result.Dispose();
            f1.Dispose();
            f2.Dispose();
        }

        [Test]
        public void BlockingRead_WithTwoFramesHeld_AndSpeculativeDecodeInProgress_Completes()
        {
            VideoFrame f1 = _reader.ReadFrameAt(1 * Second, 5000);
            _reader.Prefetch(3 * Second);
            _source.BlockPts = 10 * Second;
            _reader.Prefetch(10 * Second);
            VideoFrame f2 = _reader.ReadFrameAt(3 * Second, 5000);
            Assert.IsTrue(_source.Entered.Wait(5000));

            var release = new Thread(() =>
            {
                Thread.Sleep(200);
                _source.Release.Set();
            });
            release.Start();
            VideoFrame f3 = _reader.ReadFrameAt(5 * Second, 3000); // threw Timeout before the fix
            AssertFrame(f3, 5 * Second);
            release.Join();
            f3.Dispose();
            f1.Dispose();
            f2.Dispose();
        }

        // The QA "starve" probe: 2 frames held, a ready speculative frame and a pending prefetch beyond the request.
        [Test]
        public void Read_WithTwoFramesHeld_AndReadyPlusPendingPrefetches_Completes()
        {
            VideoFrame f1 = _reader.ReadFrameAt(1 * Second, 5000);
            VideoFrame f2 = _reader.ReadFrameAt(2 * Second, 5000);
            _reader.Prefetch(3 * Second);
            Thread.Sleep(50);
            _reader.Prefetch(10 * Second);
            Thread.Sleep(50);
            Task<VideoFrame> read = _reader.ReadFrameAtAsync(5 * Second, CancellationToken.None);
            Assert.IsTrue(read.Wait(3000));
            AssertFrame(read.Result, 5 * Second);
            read.Result.Dispose();
            VideoFrame f4 = _reader.ReadFrameAt(6 * Second, 3000);
            AssertFrame(f4, 6 * Second);
            f4.Dispose();
            f1.Dispose();
            f2.Dispose();
        }

        [Test]
        public void SequentialExportReads_At60Fps_Of30FpsSource_ReturnShownFrames()
        {
            for (int i = 0; i < 120; i++)
            {
                long t = (i * Second + 59) / 60;
                VideoFrame f = _reader.ReadFrameAt(t, 3000);
                _reader.Prefetch((( i + 1) * Second + 59) / 60);
                AssertFrame(f, t);
                f.Dispose();
            }
        }

        [Test]
        public void BackwardJump_ReturnsCorrectFrame()
        {
            VideoFrame a = _reader.ReadFrameAt(8 * Second, 3000);
            _reader.Prefetch(9 * Second);
            a.Dispose();
            VideoFrame b = _reader.ReadFrameAt(2 * Second + 1, 3000);
            AssertFrame(b, 2 * Second + 1);
            b.Dispose();
        }

        [Test]
        public void HoldingAllFrames_TimesOut_AndRecoversAfterDispose()
        {
            var held = new List<VideoFrame>();
            for (int i = 0; i < PrefetchVideoReader.FramesInFlight; i++) held.Add(_reader.ReadFrameAt(i * Second, 3000));
            var e = Assert.Throws<MediaException>(() => _reader.ReadFrameAt(5 * Second, 300));
            Assert.AreEqual(MediaErrorCode.Timeout, e.Code);
            held[0].Dispose();
            VideoFrame f = _reader.ReadFrameAt(5 * Second, 3000);
            AssertFrame(f, 5 * Second);
            f.Dispose();
            for (int i = 1; i < held.Count; i++) held[i].Dispose();
        }

        // Random mixed operations while holding up to MaxFramesInFlight − 1 = 2 frames during each read (the documented
        // contract): every read must complete with the right frame. The QA stress run hung at operation 102 with 2
        // frames held; QA round 2: the earlier loop only ever read with ≤ 1 frame held and passed on the buggy reader.
        [Test]
        public void RandomOperations_HoldingUpToTwoFrames_NeverStall()
        {
            uint state = 12345;
            Func<int, int> next = n =>
            {
                state = state * 1664525u + 1013904223u;
                return (int)((state >> 8) % (uint)n);
            };
            var held = new List<VideoFrame>();
            long t = 0;
            long maxT = _source.Duration - Second;
            int ops = 1500;
            for (int i = 0; i < ops; i++)
            {
                // Keep at most MaxFramesInFlight − 1 = 2 frames before each read (often exactly 2: the contract's edge).
                while (held.Count > PrefetchVideoReader.FramesInFlight - 1 || (held.Count > 0 && next(4) == 0))
                {
                    int k = next(held.Count);
                    held[k].Dispose();
                    held.RemoveAt(k);
                }

                int jump = next(100);
                if (jump < 5) t = next((int)(maxT / 1000)) * 1000L;                 // random seek (often backward)
                else t = Math.Min(maxT, t + 1 + next(70000));                     // forward by up to ~2 frames
                if (next(4) == 0) _reader.Prefetch(Math.Min(maxT, t + 1 + next(3 * (int)Second)));

                VideoFrame f;
                switch (next(3))
                {
                    case 0:
                        f = _reader.ReadFrameAt(t, 3000);
                        break;
                    case 1:
                    {
                        Task<VideoFrame> task = _reader.ReadFrameAtAsync(t, CancellationToken.None);
                        Assert.IsTrue(task.Wait(3000), "async read stalled at op " + i + " (t=" + t + ", held " + held.Count + ")");
                        f = task.Result;
                        break;
                    }
                    default:
                    {
                        f = null;
                        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
                        while (!_reader.TryReadFrameAt(t, out f))
                        {
                            Assert.Less(DateTime.UtcNow, deadline, "TryReadFrameAt stalled at op " + i);
                            Thread.Sleep(1);
                        }
                        break;
                    }
                }
                AssertFrame(f, t);
                held.Add(f);
            }
            foreach (VideoFrame f in held) f.Dispose();
        }

        [Test]
        public void DisposedFrame_ReturnsBufferToPool()
        {
            for (int i = 0; i < 20; i++)
            {
                VideoFrame f = _reader.ReadFrameAt(i * Second / 2, 3000);
                f.Dispose();
                Assert.IsTrue(f.IsDisposed);
                Assert.Throws<ObjectDisposedException>(() => { var unused = f.Data; });
            }
        }
    }
}
