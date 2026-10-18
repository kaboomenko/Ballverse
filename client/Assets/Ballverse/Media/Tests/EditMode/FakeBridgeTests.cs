using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Ballverse.Media.Tests
{
    public sealed class FakeBridgeTests
    {
        const int Fps = 30;

        MainThreadPump _pump;
        TempDir _dir;
        IMediaBridge _bridge;

        [SetUp]
        public void SetUp()
        {
            _pump = new MainThreadPump();
            _dir = new TempDir();
            _bridge = MediaBridgeFactory.CreateFake(new FakeMediaOptions { PngFrameStride = 1 });
        }

        [TearDown]
        public void TearDown()
        {
            _bridge.Dispose();
            _pump.Dispose();
            _dir.Dispose();
        }

        /// <summary>Work directories ("&lt;name&gt;.bv&lt;id&gt;.part.mp4.frames") of writers/joins for output <paramref name="name"/>.</summary>
        string[] WorkDirs(string name)
        {
            return Directory.GetDirectories(_dir.Path, name + ".bv*.part.mp4.frames");
        }

        string SingleWorkDir(string name)
        {
            string[] dirs = WorkDirs(name);
            Assert.AreEqual(1, dirs.Length, "work directories of " + name + ": " + string.Join(", ", dirs));
            return dirs[0];
        }

        static VideoWriterOptions SegmentOptions()
        {
            return new VideoWriterOptions(16, 16, Fps, 1000000);
        }

        string WriteSegment(string name, int frames)
        {
            IVideoWriter w = _bridge.OpenWriter(_dir.File(name), SegmentOptions());
            var rgba = new byte[16 * 16 * 4];
            for (int i = 0; i < frames; i++)
            {
                rgba[0] = (byte)i;
                w.AppendVideoFrame(rgba, SegmentConcatOptions.StartUs(i, Fps));
            }
            VideoWriterResult r = _pump.Wait(_bridge.FinalizeAsync(w, CancellationToken.None));
            Assert.AreEqual(frames, r.FrameCount);
            Assert.IsTrue(r.IsSynthetic);
            return r.Path;
        }

        string WritePcm(double seconds)
        {
            string path = _dir.File("mix.pcm");
            var samples = new float[(int)(seconds * PcmFile.SampleRate) * 2];
            for (int i = 0; i < samples.Length; i++) samples[i] = 0.1f;
            PcmFile.Write(path, samples, 0, samples.Length);
            return path;
        }

        [Test]
        public void ConcatSegments_JoinsFramesAndAudio_WithMetadata()
        {
            var segments = new List<string> { WriteSegment("s0.mp4", 60), WriteSegment("s1.mp4", 60), WriteSegment("s2.mp4", 15) };
            string pcm = WritePcm(10);
            var metadata = new Mp4Metadata("Helix Drop", new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc));
            VideoWriterResult r = _pump.Wait(_bridge.ConcatSegmentsAsync(segments, pcm, _dir.File("out.mp4"),
                new SegmentConcatOptions(Fps, metadata: metadata), CancellationToken.None));

            Assert.AreEqual(135, r.FrameCount);
            Assert.AreEqual(4500000, r.DurationUs); // 135 frames / 30 fps
            Assert.IsTrue(Directory.Exists(r.Path));
            Assert.IsTrue(File.Exists(Path.Combine(r.Path, "frame_000000.png")));
            Assert.IsTrue(File.Exists(Path.Combine(r.Path, "frame_000134.png")));
            Assert.IsFalse(File.Exists(Path.Combine(r.Path, "frame_000135.png")));
            string manifest = File.ReadAllText(Path.Combine(r.Path, "manifest.json"));
            StringAssert.Contains("\"Helix Drop\"", manifest);
            StringAssert.Contains("2026-10-18T12:00:00Z", manifest);
            // audio.wav trimmed to the video: 44-byte header + 4.5 s × 48000 × 2 ch × 2 bytes (16-bit) or 4 bytes (float)
            long wavBytes = new FileInfo(Path.Combine(r.Path, "audio.wav")).Length;
            long frames48k = 4500000L * PcmFile.SampleRate / 1000000;
            Assert.That(wavBytes == 44 + frames48k * 4 || wavBytes == 44 + frames48k * 8, "audio.wav size " + wavBytes);
            foreach (string s in segments) Assert.IsTrue(Directory.Exists(s), "segments are kept");
        }

        [Test]
        public void ConcatSegments_DifferentFps_FailsAndDeletesOutput()
        {
            var segments = new List<string> { WriteSegment("s0.mp4", 30) };
            Task<VideoWriterResult> task = _bridge.ConcatSegmentsAsync(segments, null, _dir.File("out.mp4"), new SegmentConcatOptions(60), CancellationToken.None);
            _pump.RunUntil(() => task.IsCompleted);
            var e = Assert.Throws<MediaException>(() => task.GetAwaiter().GetResult());
            Assert.AreEqual(MediaErrorCode.InvalidState, e.Code);
            Assert.IsFalse(Directory.Exists(_dir.File("out.mp4") + ".frames"));
            CollectionAssert.IsEmpty(WorkDirs("out.mp4"));
        }

        [Test]
        public void ConcatSegments_PreCancelled_DoesNothing()
        {
            var segments = new List<string> { WriteSegment("s0.mp4", 30) };
            var cts = new CancellationTokenSource();
            cts.Cancel();
            Task<VideoWriterResult> task = _bridge.ConcatSegmentsAsync(segments, null, _dir.File("out.mp4"), new SegmentConcatOptions(Fps), cts.Token);
            _pump.RunUntil(() => task.IsCompleted);
            Assert.IsTrue(task.IsCanceled);
            Assert.IsFalse(Directory.Exists(_dir.File("out.mp4") + ".frames"));
        }

        [Test]
        public void ConcatSegments_ValidatesArguments()
        {
            Task<VideoWriterResult> empty = _bridge.ConcatSegmentsAsync(new List<string>(), null, _dir.File("o.mp4"), new SegmentConcatOptions(Fps), CancellationToken.None);
            _pump.RunUntil(() => empty.IsCompleted);
            Assert.Throws<ArgumentException>(() => empty.GetAwaiter().GetResult());
            Task<VideoWriterResult> missing = _bridge.ConcatSegmentsAsync(new List<string> { _dir.File("nope.mp4.frames") }, null, _dir.File("o.mp4"),
                new SegmentConcatOptions(Fps), CancellationToken.None);
            _pump.RunUntil(() => missing.IsCompleted);
            Assert.AreEqual(MediaErrorCode.FileNotFound, Assert.Throws<MediaException>(() => missing.GetAwaiter().GetResult()).Code);
        }

        // QA finding 5: FinalizeAsync never checked a pre-cancelled token.
        [Test]
        public void FinalizeAsync_PreCancelled_AbortsWriterAndDeletesOutput()
        {
            IVideoWriter w = _bridge.OpenWriter(_dir.File("c.mp4"), SegmentOptions());
            w.AppendVideoFrame(new byte[16 * 16 * 4], 0);
            string frames = SingleWorkDir("c.mp4");
            var cts = new CancellationTokenSource();
            cts.Cancel();
            Task<VideoWriterResult> task = _bridge.FinalizeAsync(w, cts.Token);
            Assert.Throws<ObjectDisposedException>(() => w.AppendVideoFrame(new byte[16 * 16 * 4], 100000));
            _pump.RunUntil(() => task.IsCompleted);
            Assert.IsTrue(task.IsCanceled);
            // Completes only once the writer has stopped and deleted its own work output.
            Assert.IsTrue(w.Released.IsCompleted);
            Assert.IsFalse(Directory.Exists(frames));
            Assert.IsFalse(Directory.Exists(_dir.File("c.mp4") + ".frames"));
        }

        // QA round 2: a second FinalizeAsync on a finished writer deleted the finished output.
        [Test]
        public void FinalizeAsync_Twice_KeepsFinishedOutput()
        {
            string output = WriteSegment("twice.mp4", 3);
            Assert.IsTrue(Directory.Exists(output));
            IVideoWriter w = _bridge.OpenWriter(_dir.File("twice2.mp4"), SegmentOptions());
            w.AppendVideoFrame(new byte[16 * 16 * 4], 0);
            VideoWriterResult r = _pump.Wait(_bridge.FinalizeAsync(w, CancellationToken.None));
            Task<VideoWriterResult> again = _bridge.FinalizeAsync(w, CancellationToken.None);
            _pump.RunUntil(() => again.IsCompleted);
            Assert.IsTrue(again.IsFaulted);
            Assert.IsInstanceOf<InvalidOperationException>(again.Exception.InnerException);
            _pump.Drain(100);
            Assert.IsTrue(Directory.Exists(r.Path), "the finished output must survive a second FinalizeAsync");
            Assert.IsTrue(File.Exists(Path.Combine(r.Path, "manifest.json")));
        }

        // QA round 2: a concurrent second call deleted the file while the first finish was still writing it.
        [Test]
        public void FinalizeAsync_Concurrent_SecondCallFailsWithoutTouchingOutput()
        {
            IVideoWriter w = _bridge.OpenWriter(_dir.File("conc.mp4"), SegmentOptions());
            for (int i = 0; i < 5; i++) w.AppendVideoFrame(new byte[16 * 16 * 4], SegmentConcatOptions.StartUs(i, Fps));
            Task<VideoWriterResult> first = _bridge.FinalizeAsync(w, CancellationToken.None);
            Task<VideoWriterResult> second = _bridge.FinalizeAsync(w, CancellationToken.None);
            Assert.IsTrue(second.IsFaulted);
            Assert.IsInstanceOf<InvalidOperationException>(second.Exception.InnerException);
            VideoWriterResult r = _pump.Wait(first);
            Assert.AreEqual(5, r.FrameCount);
            Assert.IsTrue(File.Exists(Path.Combine(r.Path, "manifest.json")));
        }

        // QA round 2: after an encoder failure FinalizeAsync deleted the file but did not release the writer.
        [Test]
        public void FinalizeAsync_AfterEncoderFailure_ReleasesWriter()
        {
            IVideoWriter w = _bridge.OpenWriter(_dir.File("sticky.mp4"), SegmentOptions());
            var frame = new byte[16 * 16 * 4];
            w.AppendVideoFrame(frame, 0);
            string frames = SingleWorkDir("sticky.mp4");
            _pump.RunUntil(() => File.Exists(Path.Combine(frames, "frame_000000.png")));
            Directory.Delete(frames, true); // the next PNG write fails → sticky encoder error
            w.AppendVideoFrame(frame, 33334);
            MediaException sticky = null;
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            long pts = 66667;
            while (sticky == null)
            {
                Assert.Less(DateTime.UtcNow, deadline, "the fake encoder never failed");
                try
                {
                    w.AppendVideoFrame(frame, pts);
                    pts += 33334;
                    _pump.Drain(10);
                }
                catch (MediaException e)
                {
                    sticky = e;
                }
            }
            Task<VideoWriterResult> task = _bridge.FinalizeAsync(w, CancellationToken.None);
            _pump.RunUntil(() => task.IsCompleted);
            Assert.IsTrue(task.IsFaulted);
            Assert.AreSame(sticky, task.Exception.InnerException);
            Assert.Throws<ObjectDisposedException>(() => w.AppendVideoFrame(frame, pts + 100000), "writer must be released");
            Assert.IsTrue(w.Released.IsCompleted, "FinalizeAsync must complete only after the native release");
            Assert.IsFalse(Directory.Exists(frames));
        }

        // QA round 2: FinalizeAsync without frames deleted the output under a still-open writer.
        [Test]
        public void FinalizeAsync_NoFrames_DisposesWriter()
        {
            IVideoWriter w = _bridge.OpenWriter(_dir.File("empty.mp4"), SegmentOptions());
            string frames = SingleWorkDir("empty.mp4");
            Task<VideoWriterResult> task = _bridge.FinalizeAsync(w, CancellationToken.None);
            Assert.Throws<ObjectDisposedException>(() => w.AppendVideoFrame(new byte[16 * 16 * 4], 0));
            _pump.RunUntil(() => task.IsCompleted);
            Assert.IsTrue(task.IsFaulted);
            Assert.IsInstanceOf<InvalidOperationException>(task.Exception.InnerException);
            Assert.IsTrue(w.Released.IsCompleted);
            Assert.IsFalse(Directory.Exists(frames));
        }

        [Test]
        public void FinalizeAsync_DisposedWriter_ThrowsObjectDisposed()
        {
            IVideoWriter w = _bridge.OpenWriter(_dir.File("disposed.mp4"), SegmentOptions());
            w.AppendVideoFrame(new byte[16 * 16 * 4], 0);
            w.Dispose();
            Task<VideoWriterResult> task = _bridge.FinalizeAsync(w, CancellationToken.None);
            Assert.IsTrue(task.IsFaulted);
            Assert.IsInstanceOf<ObjectDisposedException>(task.Exception.InnerException);
            _pump.Wait(w.Released); // tests must not leave releases pending (OpenWriterAsync waits for them)
        }

        [Test]
        public void ForSegment_VideoOnly_NoMetadata_NoFastStart()
        {
            var settings = new Ballverse.Core.Timeline.ExportSettings(Ballverse.Core.ExportResolution.P1080, 60);
            VideoWriterOptions o = VideoWriterOptions.ForSegment(settings);
            Assert.AreEqual(1080, o.Width);
            Assert.AreEqual(1920, o.Height);
            Assert.AreEqual(60, o.Fps);
            Assert.IsNull(o.AudioPcmPath);
            Assert.IsNull(o.Metadata);
            Assert.IsFalse(o.FastStart);
            Assert.IsTrue(VideoWriterOptions.FromExportSettings(settings, null).FastStart);
            var metadata = new Mp4Metadata("x", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            Assert.Throws<ArgumentException>(() => new VideoWriterOptions(16, 16, Fps, 1000000, metadata: metadata, fastStart: false));
        }

        [Test]
        public void ConcatSegments_DeleteSegmentsBeforeFastStart_RemovesSegmentsOnSuccess()
        {
            var segments = new List<string> { WriteSegment("d0.mp4", 30), WriteSegment("d1.mp4", 30) };
            VideoWriterResult r = _pump.Wait(_bridge.ConcatSegmentsAsync(segments, null, _dir.File("joined.mp4"),
                new SegmentConcatOptions(Fps, deleteSegmentsBeforeFastStart: true), CancellationToken.None));
            Assert.AreEqual(60, r.FrameCount);
            Assert.IsTrue(Directory.Exists(r.Path));
            foreach (string s in segments) Assert.IsFalse(Directory.Exists(s), "segment not deleted: " + s);

            var kept = new List<string> { WriteSegment("k0.mp4", 30) };
            _pump.Wait(_bridge.ConcatSegmentsAsync(kept, null, _dir.File("joined2.mp4"), new SegmentConcatOptions(Fps), CancellationToken.None));
            Assert.IsTrue(Directory.Exists(kept[0]), "segments are kept by default");
        }

        [Test]
        public void SegmentStartUs_MatchesExportPlanFrameTimes()
        {
            var options = new SegmentConcatOptions(60);
            Assert.AreEqual(0, options.SegmentStartUs(0));
            Assert.AreEqual(2000000, options.SegmentStartUs(120));
            Assert.AreEqual(16667, options.SegmentStartUs(1));
            // Same rounding as StudioTimeline.FrameTimeUs (i·10⁶/fps rounded up).
            for (int f = 0; f < 10800; f += 7) Assert.AreEqual((f * 1000000L + 59) / 60, options.SegmentStartUs(f));
        }
    }
}
