using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Ballverse.Media.Tests
{
    /// <summary>
    /// QA finding (MediaBridge follow-up, MAJOR): disposing a writer — and the FinalizeAsync rejection paths that dispose
    /// it — finished the native release in the background and deleted the output BY PATH, so a redo/retry at the same
    /// path lost the new file and the old encoder could still be held. Now every writer encodes into its own work file,
    /// only a successful FinalizeAsync renames it to the output, Released / ReleaseAsync report the native stop, OpenWriter
    /// refuses a path whose previous writer has not settled and OpenWriterAsync waits for it. The fake backend models a
    /// slow native teardown with <see cref="FakeMediaOptions.SimulatedReleaseDelayMs"/>.
    /// </summary>
    public sealed class WriterReleaseTests
    {
        const int Fps = 30;
        const int ReleaseDelayMs = 300;
        static readonly byte[] Frame = new byte[16 * 16 * 4];

        MainThreadPump _pump;
        TempDir _dir;
        IMediaBridge _bridge;

        [SetUp]
        public void SetUp()
        {
            _pump = new MainThreadPump();
            _dir = new TempDir();
            _bridge = MediaBridgeFactory.CreateFake(new FakeMediaOptions { PngFrameStride = 1, SimulatedReleaseDelayMs = ReleaseDelayMs });
        }

        [TearDown]
        public void TearDown()
        {
            _bridge.Dispose();
            _pump.Dispose();
            _dir.Dispose();
        }

        static VideoWriterOptions Options()
        {
            return new VideoWriterOptions(16, 16, Fps, 1000000);
        }

        string[] WorkEntries(string name)
        {
            return Directory.GetFileSystemEntries(_dir.Path, name + ".bv*.part.mp4*");
        }

        static void Append(IVideoWriter w, int frames, int first = 0)
        {
            for (int i = first; i < first + frames; i++) w.AppendVideoFrame(Frame, SegmentConcatOptions.StartUs(i, Fps));
        }

        [Test]
        public void Dispose_SamePathIsRefusedUntilReleased_ThenOpenWorks()
        {
            string path = _dir.File("seg.mp4");
            IVideoWriter a = _bridge.OpenWriter(path, Options());
            Append(a, 3);
            Assert.Throws<InvalidOperationException>(() => _bridge.OpenWriter(path, Options()), "an open writer owns the path");
            a.Dispose();
            Assert.IsFalse(a.Released.IsCompleted, "the simulated native teardown is still running");
            Assert.Throws<InvalidOperationException>(() => _bridge.OpenWriter(path, Options()), "the old writer is still stopping");
            _pump.Wait(a.Released);
            CollectionAssert.IsEmpty(WorkEntries("seg.mp4"), "the released writer deleted its own work output");
            IVideoWriter b = _bridge.OpenWriter(path, Options());
            Append(b, 2);
            VideoWriterResult r = _pump.Wait(_bridge.FinalizeAsync(b, CancellationToken.None));
            Assert.AreEqual(2, r.FrameCount);
            Assert.AreEqual(path + ".frames", r.Path);
        }

        // The QA scenario: the segment is redone at the same path while the interrupted writer is still stopping.
        [Test]
        public void Redo_WithOpenWriterAsync_WaitsForOldRelease_AndKeepsNewOutput()
        {
            string path = _dir.File("redo.mp4");
            IVideoWriter a = _bridge.OpenWriter(path, Options());
            Append(a, 5);
            a.Dispose(); // backgrounding interrupted the segment

            Task<IVideoWriter> open = _bridge.OpenWriterAsync(path, Options(), CancellationToken.None);
            Assert.IsFalse(open.IsCompleted, "must wait for the old writer's native release");
            IVideoWriter b = _pump.Wait(open);
            Assert.IsTrue(a.Released.IsCompleted, "the new writer started before the old one stopped");
            Append(b, 4);
            VideoWriterResult r = _pump.Wait(_bridge.FinalizeAsync(b, CancellationToken.None));
            _pump.Drain(ReleaseDelayMs * 2); // anything late from writer A would strike now
            Assert.AreEqual(4, r.FrameCount);
            Assert.IsTrue(File.Exists(Path.Combine(r.Path, "manifest.json")), "the redo's output was lost");
            Assert.IsTrue(File.Exists(Path.Combine(r.Path, "frame_000003.png")));
            CollectionAssert.IsEmpty(WorkEntries("redo.mp4"));
        }

        [Test]
        public void ReleaseAsync_CompletesAfterNativeStop_AndDeletesOnlyItsWorkOutput()
        {
            string path = _dir.File("own.mp4");
            Directory.CreateDirectory(path + ".frames.keep");
            IVideoWriter w = _bridge.OpenWriter(path, Options());
            Append(w, 2);
            string[] work = WorkEntries("own.mp4");
            Assert.AreEqual(1, work.Length);
            Assert.IsFalse(Directory.Exists(path + ".frames"), "the output path is written only by a successful finalize");
            Task released = w.ReleaseAsync();
            Assert.AreSame(w.Released, released);
            _pump.Wait(released);
            Assert.IsFalse(Directory.Exists(work[0]));
            Assert.IsTrue(Directory.Exists(path + ".frames.keep"), "a foreign directory was deleted");
            Assert.Throws<ObjectDisposedException>(() => w.AppendVideoFrame(Frame, 1000000));
            Assert.AreSame(released, w.ReleaseAsync(), "idempotent");
        }

        [Test]
        public void FinalizeAsync_CancelledWhileFinishing_CompletesOnlyAfterRelease()
        {
            string path = _dir.File("cancel.mp4");
            IVideoWriter w = _bridge.OpenWriter(path, Options());
            Append(w, 30);
            var cts = new CancellationTokenSource();
            Task<VideoWriterResult> task = _bridge.FinalizeAsync(w, cts.Token);
            cts.Cancel();
            _pump.RunUntil(() => task.IsCompleted);
            Assert.IsTrue(task.IsCanceled || task.IsFaulted, "status " + task.Status);
            Assert.IsTrue(w.Released.IsCompleted, "FinalizeAsync completed before the native side stopped");
            CollectionAssert.IsEmpty(WorkEntries("cancel.mp4"));
            Assert.IsFalse(Directory.Exists(path + ".frames"));
            // The retry may start at once.
            IVideoWriter retry = _bridge.OpenWriter(path, Options());
            Append(retry, 1);
            Assert.AreEqual(1, _pump.Wait(_bridge.FinalizeAsync(retry, CancellationToken.None)).FrameCount);
        }

        [Test]
        public void FinalizeAsync_Success_ReleasedBeforeCompletion_AndOutputRenamed()
        {
            string path = _dir.File("ok.mp4");
            IVideoWriter w = _bridge.OpenWriter(path, Options());
            Append(w, 3);
            VideoWriterResult r = _pump.Wait(_bridge.FinalizeAsync(w, CancellationToken.None));
            Assert.IsTrue(w.Released.IsCompleted);
            Assert.AreEqual(path + ".frames", r.Path);
            Assert.IsTrue(File.Exists(Path.Combine(r.Path, "manifest.json")));
            CollectionAssert.IsEmpty(WorkEntries("ok.mp4"));
            // A new writer for the same path is allowed at once and replaces the result only when it succeeds.
            IVideoWriter again = _bridge.OpenWriter(path, Options());
            Append(again, 1);
            Assert.AreEqual(1, _pump.Wait(_bridge.FinalizeAsync(again, CancellationToken.None)).FrameCount);
            Assert.IsFalse(File.Exists(Path.Combine(r.Path, "frame_000001.png")), "old frames must be replaced, not merged");
        }

        [Test]
        public void OpenWriterAsync_SamePathStillOpen_Throws()
        {
            string path = _dir.File("open.mp4");
            IVideoWriter a = _bridge.OpenWriter(path, Options());
            Task<IVideoWriter> second = _bridge.OpenWriterAsync(path, Options(), CancellationToken.None);
            _pump.RunUntil(() => second.IsCompleted);
            Assert.IsInstanceOf<InvalidOperationException>(second.Exception.InnerException);
            _pump.Wait(a.ReleaseAsync());
        }

        [Test]
        public void OpenWriterAsync_Cancelled_WhileWaiting()
        {
            string path = _dir.File("wait.mp4");
            IVideoWriter a = _bridge.OpenWriter(path, Options());
            Append(a, 1);
            a.Dispose();
            var cts = new CancellationTokenSource();
            Task<IVideoWriter> open = _bridge.OpenWriterAsync(path, Options(), cts.Token);
            cts.Cancel();
            _pump.RunUntil(() => open.IsCompleted);
            Assert.IsTrue(open.IsCanceled);
            _pump.Wait(a.Released);
        }

        [Test]
        public void OpenWriter_DeletesOrphanWorkFilesOfThatOutputOnly()
        {
            string path = _dir.File("orphan.mp4");
            string orphanDir = path + ".bv0123456789abcdef.part.mp4.frames";
            string orphanFile = path + ".bvfedcba9876543210.part.mp4";
            string orphanTmp = orphanFile + ".faststart.tmp";
            string otherOutput = _dir.File("other.mp4") + ".bv0123456789abcdef.part.mp4";
            string unrelated = path + ".keep";
            Directory.CreateDirectory(orphanDir);
            foreach (string f in new[] { orphanFile, orphanTmp, otherOutput, unrelated }) File.WriteAllText(f, "x");
            IVideoWriter w = _bridge.OpenWriter(path, Options());
            Assert.IsFalse(Directory.Exists(orphanDir));
            Assert.IsFalse(File.Exists(orphanFile));
            Assert.IsFalse(File.Exists(orphanTmp));
            Assert.IsTrue(File.Exists(otherOutput), "work file of another output deleted");
            Assert.IsTrue(File.Exists(unrelated));
            Assert.AreEqual(1, WorkEntries("orphan.mp4").Length, "only the new writer's own work directory");
            _pump.Wait(w.ReleaseAsync());
        }

        [Test]
        public void Concat_WritesWorkFileAndRenamesOnSuccess_RefusesWhileWriterRuns()
        {
            var segments = new List<string>();
            for (int s = 0; s < 2; s++)
            {
                IVideoWriter w = _bridge.OpenWriter(_dir.File("s" + s + ".mp4"), Options());
                Append(w, 30);
                segments.Add(_pump.Wait(_bridge.FinalizeAsync(w, CancellationToken.None)).Path);
            }
            string outFile = _dir.File("joined.mp4");
            IVideoWriter blocker = _bridge.OpenWriter(outFile, Options());
            Task<VideoWriterResult> refused = _bridge.ConcatSegmentsAsync(segments, null, outFile, new SegmentConcatOptions(Fps), CancellationToken.None);
            _pump.RunUntil(() => refused.IsCompleted);
            Assert.IsInstanceOf<InvalidOperationException>(refused.Exception.InnerException);
            _pump.Wait(blocker.ReleaseAsync());

            VideoWriterResult r = _pump.Wait(_bridge.ConcatSegmentsAsync(segments, null, outFile, new SegmentConcatOptions(Fps), CancellationToken.None));
            Assert.AreEqual(outFile + ".frames", r.Path);
            Assert.AreEqual(60, r.FrameCount);
            CollectionAssert.IsEmpty(WorkEntries("joined.mp4"));
        }
    }
}
