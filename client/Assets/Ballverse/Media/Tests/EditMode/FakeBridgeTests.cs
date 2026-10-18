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
            string frames = _dir.File("c.mp4") + ".frames";
            Assert.IsTrue(Directory.Exists(frames));
            var cts = new CancellationTokenSource();
            cts.Cancel();
            Task<VideoWriterResult> task = _bridge.FinalizeAsync(w, cts.Token);
            _pump.RunUntil(() => task.IsCompleted);
            Assert.IsTrue(task.IsCanceled);
            _pump.RunUntil(() => !Directory.Exists(frames), 5000);
            Assert.Throws<ObjectDisposedException>(() => w.AppendVideoFrame(new byte[16 * 16 * 4], 100000));
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
