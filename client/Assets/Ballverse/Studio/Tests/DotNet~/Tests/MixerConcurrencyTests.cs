#pragma warning disable xUnit1031 // tasks complete on the pumped main-thread context; .Result after RunUntil does not block
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Ballverse.Media;
using Ballverse.Media.Tests;
using Xunit;

namespace Ballverse.Studio.Tests
{
    /// <summary>Overlapping preview remixes and exports on one <see cref="StudioAudioMixer"/> (QA round 1).</summary>
    public sealed class MixerConcurrencyTests : IDisposable
    {
        readonly TempFolder _dir = new TempFolder();
        readonly MainThreadPump _pump = new MainThreadPump();
        readonly IMediaBridge _media;

        public MixerConcurrencyTests()
        {
            _media = MediaBridgeFactory.CreateFake(new FakeMediaOptions { PngFrameStride = 100000 });
        }

        public void Dispose()
        {
            _media.Dispose();
            _pump.Dispose();
            _dir.Dispose();
        }

        static float Peak(StudioMixResult r)
        {
            float peak = 0f;
            foreach (float v in r.Samples) peak = Math.Max(peak, Math.Abs(v));
            return peak;
        }

        (ProjectDocument doc, StudioTimeline timeline, string dir) VideoProject()
        {
            string proj = _dir.Sub("proj");
            Directory.CreateDirectory(Path.Combine(proj, "media"));
            File.WriteAllText(Path.Combine(proj, "media", "user.bvtestpattern"),
                "{\"width\":1080,\"height\":1920,\"rotation\":0,\"fps\":30,\"durationSeconds\":60,\"hasAudio\":true}");
            var doc = ProjectDefaults.NewProject("p4", "Video", "2026-10-18T12:00:00Z", "ring_escape", 1800);
            doc.UserVideo = UserVideoDefaults.Create("media/user.bvtestpattern", 60_000_000);
            return (doc, ProjectComposition.BuildTimeline(doc, 30, 1800), proj);
        }

        [Fact]
        public void OverlappingUserAudioDecodesShareOneDecode()
        {
            var (doc, tl, proj) = VideoProject();
            string cache = _dir.Sub("cache");
            var mixer = new StudioAudioMixer(_media, null, cache);
            StudioMixResult solo = _pump.Wait(mixer.RenderAsync(doc, tl, proj, proj, CancellationToken.None), 60000);
            float expected = Peak(solo);
            Assert.True(expected > 0.05f);

            for (int round = 0; round < 3; round++)
            {
                mixer.ClearCache();
                // A: a superseded preview remix (cancelled right away); B: the next remix; C: an export started meanwhile.
                var ctsA = new CancellationTokenSource();
                Task<StudioMixResult> a = mixer.RenderAsync(doc, tl, proj, proj, ctsA.Token);
                _pump.Drain(5);
                ctsA.Cancel();
                Task<StudioMixResult> b = mixer.RenderAsync(doc, tl, proj, proj, CancellationToken.None);
                Task<StudioMixResult> c = mixer.RenderAsync(doc, tl, proj, proj, CancellationToken.None);
                _pump.RunUntil(() => a.IsCompleted && b.IsCompleted && c.IsCompleted, 60000);
                Assert.Equal(TaskStatus.RanToCompletion, b.Status);
                Assert.Equal(TaskStatus.RanToCompletion, c.Status);
                Assert.Equal(expected, Peak(b.Result), 3);
                Assert.Equal(expected, Peak(c.Result), 3);
            }
            // Only the cached decode is left (no stray attempt files).
            Assert.Single(Directory.GetFiles(cache, "user_*.pcm"));
        }

        [Fact]
        public void SupersededMixesStopEarlyAndRunOneAtATime()
        {
            var doc = ProjectDefaults.NewProject("p2", "Long", "2026-10-18T12:00:00Z", "ring_escape", 10800);
            StudioTimeline tl = ProjectComposition.BuildTimeline(doc, 60, 10800);
            var mixer = new StudioAudioMixer(_media, null, _dir.Sub("cache2"));
            string clipDir = _dir.Sub("clip");

            long before = GC.GetTotalAllocatedBytes(true);
            _pump.Wait(mixer.RenderAsync(doc, tl, clipDir, null, CancellationToken.None), 120000);
            long oneMix = GC.GetTotalAllocatedBytes(true) - before;

            var tasks = new List<Task<StudioMixResult>>();
            before = GC.GetTotalAllocatedBytes(true);
            for (int i = 0; i < 6; i++)
            {
                var cts = new CancellationTokenSource();
                tasks.Add(mixer.RenderAsync(doc, tl, clipDir, null, cts.Token));
                _pump.Drain(16);
                if (i < 5) cts.Cancel(); // superseded by the next edit, like a slider drag
            }
            _pump.RunUntil(() => tasks.All(x => x.IsCompleted), 120000);
            long allocated = GC.GetTotalAllocatedBytes(true) - before;
            // The first may finish (it was already mastering when cancelled); the ones queued behind it never start.
            for (int i = 1; i < 5; i++) Assert.True(tasks[i].IsCanceled, "mix " + i + " is " + tasks[i].Status);
            Assert.Equal(TaskStatus.RanToCompletion, tasks[5].Status);
            Assert.Equal(ExportSoundScheduleFrames(tl) * 2, tasks[5].Result.Samples.Length);
            // At most two full mixes worth of buffers (QA saw 6 overlapping mixes allocate 396 MB).
            Assert.True(allocated < oneMix * 5 / 2, "allocated " + allocated / (1024 * 1024) + " MB, one mix " + oneMix / (1024 * 1024) + " MB");
        }

        static long ExportSoundScheduleFrames(StudioTimeline tl) => Ballverse.Core.Integration.ExportSoundSchedule.AudioFrameCount(tl);
    }
}
