#pragma warning disable xUnit1031 // the fake rebuilder completes synchronously or on the thread pool; waits are bounded
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core.Projects;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Studio;
using Ballverse.Media.Tests;
using Xunit;

namespace Ballverse.Studio.Tests
{
    /// <summary>Rebuilder stand-in: writes a small valid render stream (or garbage) like a re-simulation would.</summary>
    sealed class FakeRebuilder : IRenderStreamRebuilder
    {
        public int Calls;
        public bool WriteGarbage;
        public TaskCompletionSource<bool> Gate;
        public CancellationToken LastToken;

        public async Task RebuildAsync(string replayPath, string renderStreamPath, string eventsPath, IProgress<double> progress, CancellationToken ct)
        {
            Calls++;
            LastToken = ct;
            if (!File.Exists(replayPath)) throw new FileNotFoundException("replay", replayPath);
            if (Gate != null)
            {
                var stop = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (ct.Register(() => stop.TrySetResult(true))) await Task.WhenAny(Gate.Task, stop.Task);
            }
            ct.ThrowIfCancellationRequested();
            if (WriteGarbage)
            {
                File.WriteAllBytes(renderStreamPath, new byte[] { 1, 2, 3 });
                return;
            }
            using (var fs = new FileStream(renderStreamPath, FileMode.Create, FileAccess.ReadWrite))
            {
                var q = RenderQuantization.Default;
                var writer = new RenderStreamWriter(fs, new RenderStreamHeader("ring_escape"), true);
                int circle = writer.DefineShape(ShapeDefinition.Circle);
                for (int tick = 0; tick < 30; tick++)
                {
                    var f = new RenderFrame();
                    f.AddBody(new BodyState(1, circle, q.QuantizePosition(tick * 0.1f), 0, 0, q.QuantizeScale(0.25f), 0, BodyFlags.None));
                    writer.AppendFrame(tick, f);
                    progress?.Report((tick + 1) / 30.0);
                }
                writer.Finish();
            }
            File.WriteAllBytes(eventsPath, new byte[] { 0 });
        }
    }

    public sealed class RenderStreamRepairTests : IDisposable
    {
        readonly TempFolder _dir = new TempFolder();

        public void Dispose() => _dir.Dispose();

        string ClipDir(bool replay = true)
        {
            string dir = _dir.Sub("clip" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            if (replay) File.WriteAllBytes(Path.Combine(dir, ProjectPaths.ClipReplayFile), new byte[] { 9, 9 });
            return dir;
        }

        [Fact]
        public void EvictedStreamIsRebuiltFromTheReplay()
        {
            var rebuilder = new FakeRebuilder();
            var repair = new RenderStreamRepair(rebuilder);
            string dir = ClipDir();
            double last = 0;
            var progress = new Progress<double>(p => last = p);
            Assert.True(repair.EnsureAsync(dir, null, CancellationToken.None).Wait(10000));
            Assert.True(RenderStreamRepair.HasReadableStream(dir));
            Assert.True(File.Exists(Path.Combine(dir, StudioFiles.ClipEventsFile)));
            Assert.Empty(Directory.GetFiles(dir, "*" + StudioFiles.TempSuffix));
            Assert.True(repair.EnsureAsync(dir, progress, CancellationToken.None).Result);
            Assert.Equal(1, rebuilder.Calls); // present now: no second rebuild
        }

        [Fact]
        public void ConcurrentCallersShareOneRebuild()
        {
            var rebuilder = new FakeRebuilder { Gate = new TaskCompletionSource<bool>() };
            var repair = new RenderStreamRepair(rebuilder);
            string dir = ClipDir();
            Task<bool> a = repair.EnsureAsync(dir, null, CancellationToken.None);
            Task<bool> b = repair.EnsureAsync(dir, null, CancellationToken.None);
            rebuilder.Gate.SetResult(true);
            Assert.True(a.Wait(10000) && a.Result);
            Assert.True(b.Wait(10000) && b.Result);
            Assert.Equal(1, rebuilder.Calls);
        }

        sealed class SyncProgress : IProgress<double>
        {
            public double Last = -1;
            public void Report(double value) => Last = value;
        }

        [Fact]
        public void FirstCallerCancellingDoesNotCancelASecondWaiter()
        {
            // QA round 2: the shared rebuild ran with the first caller's token, so leaving "Подготовка клипа" cancelled the export
            // that awaited the same rebuild, and only the first caller's progress was reported.
            var rebuilder = new FakeRebuilder { Gate = new TaskCompletionSource<bool>() };
            var repair = new RenderStreamRepair(rebuilder);
            string dir = ClipDir();
            var previewCts = new CancellationTokenSource();
            var previewProgress = new SyncProgress();
            var exportProgress = new SyncProgress();
            Task<bool> preview = repair.EnsureAsync(dir, previewProgress, previewCts.Token);
            Task<bool> export = repair.EnsureAsync(dir, exportProgress, CancellationToken.None);
            previewCts.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => preview.GetAwaiter().GetResult());
            Assert.False(rebuilder.LastToken.IsCancellationRequested);
            rebuilder.Gate.SetResult(true);
            Assert.True(export.Wait(10000));
            Assert.True(export.Result);
            Assert.True(RenderStreamRepair.HasReadableStream(dir));
            Assert.Equal(1.0, exportProgress.Last);
            Assert.Equal(-1, previewProgress.Last); // the cancelled waiter's sink was removed
            Assert.Equal(1, rebuilder.Calls);
        }

        [Fact]
        public void RebuildStopsWhenEveryWaiterCancelsAndANewCallerStartsAgain()
        {
            var rebuilder = new FakeRebuilder { Gate = new TaskCompletionSource<bool>() };
            var repair = new RenderStreamRepair(rebuilder);
            string dir = ClipDir();
            var a = new CancellationTokenSource();
            var b = new CancellationTokenSource();
            Task<bool> ta = repair.EnsureAsync(dir, null, a.Token);
            Task<bool> tb = repair.EnsureAsync(dir, null, b.Token);
            a.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => ta.GetAwaiter().GetResult());
            Assert.False(rebuilder.LastToken.IsCancellationRequested); // b still waits
            b.Cancel();
            Assert.ThrowsAny<OperationCanceledException>(() => tb.GetAwaiter().GetResult());
            Assert.True(rebuilder.LastToken.IsCancellationRequested);

            rebuilder.Gate = null;
            Task<bool> again = repair.EnsureAsync(dir, null, CancellationToken.None);
            Assert.True(again.Wait(10000));
            Assert.True(again.Result);
            Assert.Equal(2, rebuilder.Calls);
            Assert.Empty(Directory.GetFiles(dir, "*" + StudioFiles.TempSuffix));
        }

        [Fact]
        public void WithoutRebuilderOrReplayTheClipIsUnavailable()
        {
            var none = new RenderStreamRepair(null);
            Assert.False(none.EnsureAsync(ClipDir(), null, CancellationToken.None).Result);
            var repair = new RenderStreamRepair(new FakeRebuilder());
            Assert.False(repair.EnsureAsync(ClipDir(replay: false), null, CancellationToken.None).Result);

            var record = new ClipRecord { Id = "r1", ModeId = "ring_escape", DurationTicks = 30 };
            Assert.Equal(ClipReadiness.Ready, none.Readiness(new ClipEntry(record, "runs/r1", true, true, true, 1, 1)));
            Assert.Equal(ClipReadiness.Unavailable, none.Readiness(new ClipEntry(record, "runs/r1", true, false, false, 1, 0)));
            Assert.Equal(ClipReadiness.NeedsRebuild, repair.Readiness(new ClipEntry(record, "runs/r1", true, false, false, 1, 0)));
            Assert.Equal(ClipReadiness.Unavailable, repair.Readiness(new ClipEntry(record, "runs/r1", false, false, false, 0, 0)));
            Assert.Equal(ClipReadiness.Missing, repair.Readiness(null));
        }

        [Fact]
        public void UnreadableRebuildFailsWithoutLeavingAStream()
        {
            var repair = new RenderStreamRepair(new FakeRebuilder { WriteGarbage = true });
            string dir = ClipDir();
            var e = Assert.ThrowsAny<Exception>(() => repair.EnsureAsync(dir, null, CancellationToken.None).GetAwaiter().GetResult());
            Assert.IsType<InvalidDataException>(e);
            Assert.False(File.Exists(Path.Combine(dir, ProjectPaths.ClipRenderFile)));
            Assert.Empty(Directory.GetFiles(dir, "*" + StudioFiles.TempSuffix));
        }

        [Fact]
        public void CorruptStreamOnDiskIsReplaced()
        {
            var repair = new RenderStreamRepair(new FakeRebuilder());
            string dir = ClipDir();
            File.WriteAllBytes(Path.Combine(dir, ProjectPaths.ClipRenderFile), new byte[] { 0, 1, 2, 3 });
            Assert.False(RenderStreamRepair.HasReadableStream(dir));
            Assert.True(repair.EnsureAsync(dir, null, CancellationToken.None).Result);
            Assert.True(RenderStreamRepair.HasReadableStream(dir));
        }
    }
}
