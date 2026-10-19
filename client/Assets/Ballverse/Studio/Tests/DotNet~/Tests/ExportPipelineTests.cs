using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core;
using Ballverse.Core.Integration;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Ballverse.Media;
using Ballverse.Media.Tests;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Xunit;

namespace Ballverse.Studio.Tests
{
    /// <summary>Export host for tests: frames are pumped by the Media test synchronization context; time moves 16 ms per frame.</summary>
    sealed class TestExportHost : IExportHost
    {
        readonly SynchronizationContext _pump;
        long _now;

        public TestExportHost(SynchronizationContext pump)
        {
            _pump = pump;
        }

        public int Frames { get; private set; }
        public bool Killed { get; set; }
        public long Free { get; set; } = -1;
        public ExportThermalLevel Heat { get; set; } = ExportThermalLevel.Nominal;
        public bool Paused { get; set; }
        public int Pauses { get; set; }
        public int AwakeTokens { get; private set; }
        /// <summary>Called at every frame (tests inject pauses etc.).</summary>
        public Action<TestExportHost> OnFrame { get; set; }

        public Task NextFrame()
        {
            var tcs = new TaskCompletionSource<bool>();
            if (Killed) return tcs.Task; // the "process" died: nothing runs any more
            _pump.Post(_ =>
            {
                Frames++;
                _now += 16;
                OnFrame?.Invoke(this);
                if (!Killed) tcs.TrySetResult(true);
            }, null);
            return tcs.Task;
        }

        public long NowMs => _now;
        public string NowIso => "2026-10-18T12:00:00Z";
        public ExportThermalLevel Thermal => Heat;
        public long FreeSpaceBytes => Free;
        public DeviceTier Tier => DeviceTier.A;
        public bool IsPaused => Paused;
        public int PauseCount => Pauses;

        public IDisposable KeepAwake(string reason)
        {
            AwakeTokens++;
            return new Token(this);
        }

        sealed class Token : IDisposable
        {
            readonly TestExportHost _host;
            bool _done;

            public Token(TestExportHost host)
            {
                _host = host;
            }

            public void Dispose()
            {
                if (_done) return;
                _done = true;
                _host.AwakeTokens--;
            }
        }
    }

    /// <summary>CPU stand-in for the GPU renderer: every frame's bytes encode its index; readbacks finish one frame later.</summary>
    sealed unsafe class CpuFrameRenderer : IExportFrameRenderer
    {
        readonly TestExportHost _host;
        readonly Queue<(int index, int slot, int readyAt)> _pending = new Queue<(int, int, int)>();
        readonly bool[] _busy = new bool[3];
        NativeArray<byte>[] _buffers;
        void*[] _memory;

        public CpuFrameRenderer(TestExportHost host)
        {
            _host = host;
        }

        public List<int> Rendered { get; } = new List<int>();
        public int NotReadyEvery { get; set; }
        public bool Disposed { get; private set; }
        int _tries;

        public Task PrepareAsync(ExportJob job, CancellationToken ct)
        {
            int bytes = job.Settings.Width * job.Settings.Height * 4;
            _buffers = new NativeArray<byte>[3];
            _memory = new void*[3];
            for (int i = 0; i < 3; i++)
            {
                _memory[i] = UnsafeUtility.Malloc(bytes, 16, Allocator.Persistent);
                _buffers[i] = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<byte>(_memory[i], bytes, Allocator.Persistent);
            }
            return Task.CompletedTask;
        }

        public int MaxInFlight => 3;
        public int InFlight => _pending.Count;

        public bool TryRender(in ExportFrame frame)
        {
            if (NotReadyEvery > 0 && (++_tries % NotReadyEvery) == 0) return false; // video frame still decoding
            int slot = Array.IndexOf(_busy, false);
            if (slot < 0) return false;
            _busy[slot] = true;
            byte* p = (byte*)_memory[slot];
            p[0] = (byte)(frame.Index & 0xFF);
            p[1] = (byte)(frame.Index >> 8);
            _pending.Enqueue((frame.Index, slot, _host.Frames + 1));
            Rendered.Add(frame.Index);
            return true;
        }

        public void Prefetch(in ExportFrame next)
        {
        }

        public bool TryPeekCompleted(out NativeArray<byte> rgba, out int frameIndex)
        {
            rgba = default(NativeArray<byte>);
            frameIndex = -1;
            if (_pending.Count == 0) return false;
            var head = _pending.Peek();
            if (_host.Frames < head.readyAt) return false;
            rgba = _buffers[head.slot];
            frameIndex = head.index;
            return true;
        }

        public void ReleaseCompleted()
        {
            var head = _pending.Dequeue();
            _busy[head.slot] = false;
        }

        public void DiscardInFlight()
        {
            while (_pending.Count > 0) _busy[_pending.Dequeue().slot] = false;
        }

        public void Dispose()
        {
            if (Disposed) return;
            Disposed = true;
            if (_memory == null) return;
            for (int i = 0; i < _memory.Length; i++) UnsafeUtility.Free(_memory[i], Allocator.Persistent);
            _memory = null;
        }
    }

    /// <summary>Media bridge decorator that fails chosen segment finalizations with an encoder error.</summary>
    sealed class FlakyMediaBridge : IMediaBridge
    {
        readonly IMediaBridge _inner;
        int _finalizes;

        public FlakyMediaBridge(IMediaBridge inner)
        {
            _inner = inner;
        }

        public HashSet<int> FailFinalizeCalls { get; } = new HashSet<int>();
        public int Finalizes => _finalizes;
        public int Concats { get; private set; }
        public int GallerySaves { get; private set; }

        public MediaBackend Backend => _inner.Backend;
        public DeviceSignals Signals => _inner.Signals;
        public Task<PickResult> PickVideoAsync(string destinationDirectory, IProgress<double> progress, CancellationToken ct) => _inner.PickVideoAsync(destinationDirectory, progress, ct);
        public Task<MediaInfo> ProbeAsync(string path, CancellationToken ct) => _inner.ProbeAsync(path, ct);
        public IVideoReader OpenVideoReader(string path, VideoReaderOptions options) => _inner.OpenVideoReader(path, options);
        public Task<IVideoReader> OpenVideoReaderAsync(string path, VideoReaderOptions options, CancellationToken ct) => _inner.OpenVideoReaderAsync(path, options, ct);
        public Task<PcmBuffer> DecodeAudioAsync(string path, AudioDecodeRange range, CancellationToken ct) => _inner.DecodeAudioAsync(path, range, ct);
        public Task<PcmFileInfo> DecodeAudioToFileAsync(string path, string outputPcmPath, AudioDecodeRange range, CancellationToken ct) => _inner.DecodeAudioToFileAsync(path, outputPcmPath, range, ct);
        public IVideoWriter OpenWriter(string outputPath, VideoWriterOptions options) => _inner.OpenWriter(outputPath, options);

        public Task<VideoWriterResult> FinalizeAsync(IVideoWriter writer, CancellationToken ct)
        {
            int call = ++_finalizes;
            if (FailFinalizeCalls.Contains(call))
            {
                writer.Dispose(); // contract: a failed finalize disposes the writer and deletes the partial file
                return Task.FromException<VideoWriterResult>(new MediaException(MediaErrorCode.EncoderFailed, "injected encoder failure"));
            }
            return _inner.FinalizeAsync(writer, ct);
        }

        public Task<VideoWriterResult> ConcatSegmentsAsync(IReadOnlyList<string> segments, string pcmPath, string outFile, SegmentConcatOptions options, CancellationToken ct)
        {
            Concats++;
            return _inner.ConcatSegmentsAsync(segments, pcmPath, outFile, options, ct);
        }

        public Task<GallerySaveResult> SaveToGalleryAsync(string file, string album, CancellationToken ct)
        {
            GallerySaves++;
            return _inner.SaveToGalleryAsync(file, album, ct);
        }

        public void Share(string file, string caption) => _inner.Share(file, caption);
        public void Dispose() => _inner.Dispose();
    }

    sealed class TestGate : IExportGate
    {
        public bool Answer = true;
        public int Requests;
        public readonly List<ExportErrorCode> Finished = new List<ExportErrorCode>();

        public Task<bool> RequestAsync(ExportGateRequest request, CancellationToken ct)
        {
            Requests++;
            return Task.FromResult(Answer);
        }

        public void OnExportFinished(string projectId, StudioExportResult result)
        {
            Finished.Add(result.Error);
        }
    }

    public sealed class ExportPipelineTests : IDisposable
    {
        const int ClipTicks = 150; // 2.5 s → 75 frames at 30 fps → segments of 60 + 15
        internal const long HarnessStallTimeoutMs = 24L * 3600 * 1000;
        readonly TempFolder _dir = new TempFolder();
        readonly MainThreadPump _pump = new MainThreadPump();
        readonly StudioStorage _storage;
        readonly FlakyMediaBridge _media;
        readonly TestGate _gate = new TestGate();
        readonly List<CpuFrameRenderer> _renderers = new List<CpuFrameRenderer>();
        readonly ProjectDocument _doc;

        public ExportPipelineTests()
        {
            _storage = new StudioStorage(_dir.Sub("data"), _dir.Sub("runs"), _dir.Sub("export"));
            _media = new FlakyMediaBridge(MediaBridgeFactory.CreateFake(new FakeMediaOptions { PngFrameStride = 100000 }));
            _doc = ProjectDefaults.NewProject("p1", "My Reel", "2026-10-18T12:00:00Z", "ring_escape", ClipTicks);
            new ProjectRepository(_storage.Library).Create(_doc);
        }

        public void Dispose()
        {
            _media.Dispose();
            _pump.Dispose();
            _dir.Dispose();
        }

        ExportPipeline Pipeline(TestExportHost host)
        {
            var mixer = new StudioAudioMixer(_media, null, _storage.Exports.FullPath("audio-cache"));
            string projectDir = _storage.Library.FullPath("projects/p1");
            return new ExportPipeline(_media, _storage.Exports, _storage.Library, host,
                job =>
                {
                    var r = new CpuFrameRenderer(host);
                    _renderers.Add(r);
                    return r;
                },
                new StudioExportAudio(mixer, _ => projectDir), _gate)
            {
                // Harness time is simulated (16 ms per pumped frame, thousands of frames per real second) while the fake
                // encoder runs on real threads: its push-back must not look like a stall. Stalls: ExportFailureTests.
                StallTimeoutMs = HarnessStallTimeoutMs,
            };
        }

        ExportJobRequest Request(bool keepInProject = false, string jobId = "job1")
        {
            return new ExportJobRequest
            {
                JobId = jobId,
                Project = _doc,
                Settings = new ExportSettings(ExportResolution.P720, 30),
                StreamTicks = ClipTicks,
                KeepInProject = keepInProject,
                NowIso = "2026-10-18T12:06:00Z",
            };
        }

        [Fact]
        public void ExportsSegmentsJoinsAndVerifies()
        {
            var host = new TestExportHost(_pump);
            ExportPipeline pipeline = Pipeline(host);
            var phases = new List<ExportPhase>();
            pipeline.Progress += p => phases.Add(p.Phase);
            StudioExportResult r = _pump.Wait(pipeline.StartAsync(Request(), CancellationToken.None), 60000);
            Assert.True(r.Succeeded, r.Error + ": " + r.Message);
            Assert.Equal(75, r.FrameCount);
            Assert.Equal(2500000, r.DurationUs);
            Assert.True(r.IsSynthetic);
            Assert.Equal(ExportGalleryStatus.NotRequested, r.Gallery); // the fake output is not a playable file
            Assert.True(Directory.Exists(r.OutputPath));
            Assert.StartsWith(_storage.Exports.FullPath(StudioStorage.OutputsDir), r.OutputPath);
            Assert.False(_storage.Exports.DirectoryExists("job1"));
            Assert.Equal(2, _media.Finalizes);
            Assert.Equal(1, _media.Concats);
            Assert.Equal(Enumerable.Range(0, 75), _renderers.Single().Rendered);
            Assert.True(_renderers.Single().Disposed);
            Assert.Equal(1, _gate.Requests);
            Assert.Equal(new[] { ExportErrorCode.None }, _gate.Finished);
            Assert.Equal(0, host.AwakeTokens);
            Assert.Contains(ExportPhase.Audio, phases);
            Assert.Contains(ExportPhase.Frames, phases);
            Assert.Equal(ExportPhase.Done, phases.Last());
            Assert.Equal(720, r.Resolution);
            Assert.Equal(30, r.Fps);
            Assert.False(pipeline.IsRunning);
        }

        [Fact]
        public void KeepInProjectMovesTheFileIntoExports()
        {
            var host = new TestExportHost(_pump);
            StudioExportResult r = _pump.Wait(Pipeline(host).StartAsync(Request(keepInProject: true), CancellationToken.None), 60000);
            Assert.True(r.Succeeded, r.Message);
            Assert.StartsWith("exports/Ballverse_My_Reel_20261018_120600", r.ProjectFile);
            Assert.True(Directory.Exists(_storage.Library.FullPath("projects/p1/" + r.ProjectFile)));
        }

        [Fact]
        public void VideoFramesNotReadyAreRetriedNextFrame()
        {
            var host = new TestExportHost(_pump);
            ExportPipeline pipeline = Pipeline(host);
            pipeline.Progress += _ => { if (_renderers.Count > 0) _renderers[0].NotReadyEvery = 3; };
            StudioExportResult r = _pump.Wait(pipeline.StartAsync(Request(), CancellationToken.None), 60000);
            Assert.True(r.Succeeded, r.Message);
            Assert.Equal(Enumerable.Range(0, 75), _renderers.Single().Rendered);
        }

        [Fact]
        public void GateDeclinedAndLowSpaceStopBeforeRendering()
        {
            _gate.Answer = false;
            var host = new TestExportHost(_pump);
            StudioExportResult declined = _pump.Wait(Pipeline(host).StartAsync(Request(), CancellationToken.None));
            Assert.Equal(ExportErrorCode.GateDeclined, declined.Error);
            Assert.Empty(_renderers);
            Assert.False(_storage.Exports.DirectoryExists("job1"));

            _gate.Answer = true;
            var poor = new TestExportHost(_pump) { Free = 10 * 1024 * 1024 };
            StudioExportResult space = _pump.Wait(Pipeline(poor).StartAsync(Request(), CancellationToken.None));
            Assert.Equal(ExportErrorCode.InsufficientSpace, space.Error);
            Assert.Contains("need", space.Message);
            Assert.Equal(1, _gate.Requests); // the space check comes before the ad
        }

        [Fact]
        public void CancelDeletesTheJob()
        {
            var host = new TestExportHost(_pump);
            var cts = new CancellationTokenSource();
            host.OnFrame = h => { if (h.Frames == 10) cts.Cancel(); };
            StudioExportResult r = _pump.Wait(Pipeline(host).StartAsync(Request(), cts.Token), 60000);
            Assert.Equal(ExportErrorCode.Cancelled, r.Error);
            Assert.False(_storage.Exports.DirectoryExists("job1"));
            Assert.Equal(ExportErrorCode.Cancelled, _gate.Finished.Single());
            Assert.Equal(0, host.AwakeTokens);
        }

        [Fact]
        public void PauseRedoesTheCurrentSegment()
        {
            var host = new TestExportHost(_pump);
            bool paused = false;
            host.OnFrame = h =>
            {
                CpuFrameRenderer r = _renderers.FirstOrDefault();
                if (!paused && r != null && r.Rendered.Count >= 20)
                {
                    paused = true;
                    h.Paused = true;
                    h.Pauses++;
                }
                else if (h.Paused && h.Frames % 5 == 0)
                {
                    h.Paused = false;
                }
            };
            StudioExportResult result = _pump.Wait(Pipeline(host).StartAsync(Request(), CancellationToken.None), 60000);
            Assert.True(result.Succeeded, result.Message);
            List<int> rendered = _renderers.Single().Rendered;
            Assert.True(rendered.Count > 75);
            Assert.Equal(2, rendered.Count(i => i == 0)); // segment 0 was started again from frame 0
            Assert.Equal(75, result.FrameCount);
        }

        [Fact]
        public void EncoderErrorIsRetriedOnceThenFails()
        {
            _media.FailFinalizeCalls.Add(1);
            var host = new TestExportHost(_pump);
            StudioExportResult ok = _pump.Wait(Pipeline(host).StartAsync(Request(), CancellationToken.None), 60000);
            Assert.True(ok.Succeeded, ok.Message);
            Assert.Equal(3, _media.Finalizes);

            _media.FailFinalizeCalls.Add(4);
            _media.FailFinalizeCalls.Add(5);
            var host2 = new TestExportHost(_pump);
            StudioExportResult failed = _pump.Wait(Pipeline(host2).StartAsync(Request(jobId: "job2"), CancellationToken.None), 60000);
            Assert.Equal(ExportErrorCode.EncoderFailed, failed.Error);
            Assert.True(failed.SuggestLowerSettings);
            Assert.False(_storage.Exports.DirectoryExists("job2"));
        }

        [Fact]
        public void KilledExportResumesFromTheLastSegmentWithoutTheGate()
        {
            var host = new TestExportHost(_pump);
            ExportPipeline first = Pipeline(host);
            host.OnFrame = h =>
            {
                ExportResumeState s = ExportResumeStore.Load(_storage.Exports, "job1", out _);
                if (s != null && s.Segments.Count == 1) h.Killed = true;
            };
            Task<StudioExportResult> abandoned = first.StartAsync(Request(), CancellationToken.None);
            _pump.RunUntil(() => host.Killed, 60000);
            Assert.False(abandoned.IsCompleted);

            var second = Pipeline(new TestExportHost(_pump));
            IReadOnlyList<ExportResumeState> resumable = second.ListResumable();
            Assert.Equal("job1", resumable.Single().JobId);
            Assert.True(resumable[0].GatePassed);
            StudioExportResult r = _pump.Wait(second.ResumeAsync("job1", CancellationToken.None), 60000);
            Assert.True(r.Succeeded, r.Message);
            Assert.Equal(75, r.FrameCount);
            Assert.Equal(1, _gate.Requests);
            Assert.Equal(Enumerable.Range(60, 15), _renderers[1].Rendered); // only the second segment was rendered again
        }

        [Fact]
        public void BrokenResumeStateIsDiscarded()
        {
            _storage.Exports.WriteAllText("bad/export_state.json", "{\"schemaVersion\":1}");
            var pipeline = Pipeline(new TestExportHost(_pump));
            Assert.Empty(pipeline.ListResumable());
            StudioExportResult r = _pump.Wait(pipeline.ResumeAsync("bad", CancellationToken.None));
            Assert.Equal(ExportErrorCode.ResumeInvalid, r.Error);
            Assert.False(_storage.Exports.DirectoryExists("bad"));
        }

        [Fact]
        public void HotDevicePausesBetweenSegments()
        {
            var host = new TestExportHost(_pump) { Heat = ExportThermalLevel.Serious };
            ExportPipeline pipeline = Pipeline(host);
            bool sawThrottle = false;
            pipeline.Progress += p => sawThrottle |= p.Throttled;
            StudioExportResult r = _pump.Wait(pipeline.StartAsync(Request(), CancellationToken.None), 60000);
            Assert.True(r.Succeeded, r.Message);
            Assert.True(sawThrottle);
            Assert.True(host.NowMs >= ExportPolicy.SeriousPauseMs);
        }

        [Fact]
        public void InvalidRequestFailsAsProjectInvalid()
        {
            ExportJobRequest req = Request();
            req.Settings = null;
            StudioExportResult r = _pump.Wait(Pipeline(new TestExportHost(_pump)).StartAsync(req, CancellationToken.None));
            Assert.Equal(ExportErrorCode.ProjectInvalid, r.Error);
        }
    }
}
