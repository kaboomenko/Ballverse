using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Ballverse.Core.Integration;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Ballverse.Media;
using Ballverse.Media.Tests;
using Unity.Collections;
using Xunit;

namespace Ballverse.Studio.Tests
{
    /// <summary>Sound provider with an (empty) Pop pack: makes the mixer read the clip's replay.</summary>
    sealed class PopSounds : IStudioSoundProvider
    {
        public Task<StudioSoundAssets> LoadSoundsAsync(string soundPackId, string melodyId, CancellationToken ct)
            => Task.FromResult(new StudioSoundAssets(SoundPackCatalog.Get(SoundPackId.Pop), new InMemorySampleBank(), null));

        public Task<float[]> LoadMusicAsync(string trackId, CancellationToken ct) => Task.FromResult<float[]>(null);
    }

    /// <summary>Writer whose encoder queue stops draining after a few frames (hung hardware encoder).</summary>
    sealed class StuckWriter : IVideoWriter
    {
        readonly IVideoWriter _inner;

        public StuckWriter(IVideoWriter inner)
        {
            _inner = inner;
        }

        public VideoWriterOptions Options => _inner.Options;
        public string OutputPath => _inner.OutputPath;
        public int FramesInFlight => 3;
        public int MaxFramesInFlight => 3;
        public int FramesAppended => _inner.FramesAppended;
        public bool IsFinalizing => _inner.IsFinalizing;
        public bool TryAppendVideoFrame(NativeArray<byte> rgba, long ptsUs) => _inner.FramesAppended < 5 && _inner.TryAppendVideoFrame(rgba, ptsUs);
        public bool TryAppendVideoFrame(byte[] rgba, long ptsUs) => false;
        public void AppendVideoFrame(NativeArray<byte> rgba, long ptsUs) => throw new NotSupportedException();
        public void AppendVideoFrame(byte[] rgba, long ptsUs) => throw new NotSupportedException();
        public Task Released => _inner.Released;
        public Task ReleaseAsync() => _inner.ReleaseAsync();
        public void Dispose() => _inner.Dispose();
    }

    /// <summary>Media bridge decorator for failure injection: stuck writers, failing joins, a real-file join result, a slow gallery.</summary>
    sealed class FaultyMediaBridge : IMediaBridge
    {
        readonly IMediaBridge _inner;

        public FaultyMediaBridge(IMediaBridge inner)
        {
            _inner = inner;
        }

        public bool StuckWriters;
        /// <summary>Join calls (1-based) that fail with <see cref="MediaErrorCode.MuxerFailed"/>.</summary>
        public readonly HashSet<int> FailConcatCalls = new HashSet<int>();
        /// <summary>Runs inside a failing join (e.g. the app goes to the background).</summary>
        public Action OnConcatFailure;
        /// <summary>Report the joined output as a real MP4 so the gallery path runs.</summary>
        public bool RealConcat;
        /// <summary>Runs when the gallery save starts (e.g. the user taps cancel).</summary>
        public Action OnGallery;
        public int Concats;
        public CancellationToken GalleryToken;

        public MediaBackend Backend => _inner.Backend;
        public DeviceSignals Signals => _inner.Signals;
        public Task<PickResult> PickVideoAsync(string d, IProgress<double> p, CancellationToken ct) => _inner.PickVideoAsync(d, p, ct);
        public Task<MediaInfo> ProbeAsync(string path, CancellationToken ct) => _inner.ProbeAsync(path, ct);
        public IVideoReader OpenVideoReader(string path, VideoReaderOptions o) => _inner.OpenVideoReader(path, o);
        public Task<IVideoReader> OpenVideoReaderAsync(string path, VideoReaderOptions o, CancellationToken ct) => _inner.OpenVideoReaderAsync(path, o, ct);
        public Task<PcmBuffer> DecodeAudioAsync(string path, AudioDecodeRange r, CancellationToken ct) => _inner.DecodeAudioAsync(path, r, ct);
        public Task<PcmFileInfo> DecodeAudioToFileAsync(string path, string o, AudioDecodeRange r, CancellationToken ct) => _inner.DecodeAudioToFileAsync(path, o, r, ct);

        /// <summary>Every writer opened so far (to check the redo hand-over).</summary>
        public readonly List<IVideoWriter> Opened = new List<IVideoWriter>();
        /// <summary>Set when a writer was opened while an earlier one was still releasing its encoder.</summary>
        public bool OpenedWhileReleasing;

        public IVideoWriter OpenWriter(string outputPath, VideoWriterOptions options)
        {
            foreach (IVideoWriter earlier in Opened)
            {
                if (!earlier.IsFinalizing && !earlier.Released.IsCompleted) OpenedWhileReleasing = true;
            }
            IVideoWriter w = _inner.OpenWriter(outputPath, options);
            Opened.Add(w);
            return StuckWriters ? new StuckWriter(w) : w;
        }

        public Task<VideoWriterResult> FinalizeAsync(IVideoWriter writer, CancellationToken ct) => _inner.FinalizeAsync(writer, ct);

        public async Task<VideoWriterResult> ConcatSegmentsAsync(IReadOnlyList<string> segments, string pcmPath, string outFile, SegmentConcatOptions options, CancellationToken ct)
        {
            int call = ++Concats;
            if (FailConcatCalls.Contains(call))
            {
                OnConcatFailure?.Invoke();
                throw new MediaException(MediaErrorCode.MuxerFailed, "injected join failure");
            }
            VideoWriterResult r = await _inner.ConcatSegmentsAsync(segments, pcmPath, outFile, options, ct);
            return RealConcat ? new VideoWriterResult(r.Path, r.FrameCount, r.DurationUs, r.FastStart, false) : r;
        }

        public async Task<GallerySaveResult> SaveToGalleryAsync(string file, string album, CancellationToken ct)
        {
            GalleryToken = ct;
            OnGallery?.Invoke();
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
            return new GallerySaveResult(GallerySaveStatus.Saved, "content://saved", true, null);
        }

        public void Share(string file, string caption) => _inner.Share(file, caption);
        public void Dispose() => _inner.Dispose();
    }

    /// <summary>Renderer whose GPU readbacks never complete.</summary>
    sealed class NeverDoneRenderer : IExportFrameRenderer
    {
        int _inFlight;
        public int Discards;
        public Task PrepareAsync(ExportJob job, CancellationToken ct) => Task.CompletedTask;
        public int MaxInFlight => 3;
        public int InFlight => _inFlight;

        public bool TryRender(in ExportFrame frame)
        {
            if (_inFlight >= 3) return false;
            _inFlight++;
            return true;
        }

        public void Prefetch(in ExportFrame next)
        {
        }

        public bool TryPeekCompleted(out NativeArray<byte> rgba, out int frameIndex)
        {
            rgba = default(NativeArray<byte>);
            frameIndex = -1;
            return false;
        }

        public void ReleaseCompleted()
        {
        }

        public void DiscardInFlight()
        {
            _inFlight = 0;
            Discards++;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Renderer whose video frames are never decoded (TryRender always declines).</summary>
    sealed class NeverReadyRenderer : IExportFrameRenderer
    {
        public Task PrepareAsync(ExportJob job, CancellationToken ct) => Task.CompletedTask;
        public int MaxInFlight => 3;
        public int InFlight => 0;
        public bool TryRender(in ExportFrame frame) => false;

        public void Prefetch(in ExportFrame next)
        {
        }

        public bool TryPeekCompleted(out NativeArray<byte> rgba, out int frameIndex)
        {
            rgba = default(NativeArray<byte>);
            frameIndex = -1;
            return false;
        }

        public void ReleaseCompleted()
        {
        }

        public void DiscardInFlight()
        {
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Regression tests of the export failure paths (QA round 1): every failure returns a result, cleans up and tells the gate.</summary>
    public sealed class ExportFailureTests : IDisposable
    {
        const int ClipTicks = 150;
        readonly TempFolder _dir = new TempFolder();
        readonly MainThreadPump _pump = new MainThreadPump();
        readonly StudioStorage _storage;
        readonly FaultyMediaBridge _media;
        readonly FakeMediaOptions _mediaOptions = new FakeMediaOptions { PngFrameStride = 100000 };
        readonly TestGate _gate = new TestGate();
        readonly ProjectDocument _doc;
        readonly List<CpuFrameRenderer> _renderers = new List<CpuFrameRenderer>();

        public ExportFailureTests()
        {
            _storage = new StudioStorage(_dir.Sub("data"), _dir.Sub("runs"), _dir.Sub("export"));
            _media = new FaultyMediaBridge(MediaBridgeFactory.CreateFake(_mediaOptions));
            _doc = ProjectDefaults.NewProject("p1", "My Reel", "2026-10-18T12:00:00Z", "ring_escape", ClipTicks);
            _doc.Clip.SoundPack = "Pop";
            new ProjectRepository(_storage.Library).Create(_doc);
        }

        public void Dispose()
        {
            _pump.SettleWriters(_dir.Path);
            _media.Dispose();
            _pump.Dispose();
            _dir.Dispose();
        }

        ExportPipeline Pipeline(TestExportHost host, Func<ExportJob, IExportFrameRenderer> renderer = null, IStudioSoundProvider sounds = null,
            bool realStallTimeout = false)
        {
            var mixer = new StudioAudioMixer(_media, sounds, _storage.Exports.FullPath("audio-cache"));
            string projectDir = _storage.Library.FullPath("projects/p1");
            return new ExportPipeline(_media, _storage.Exports, _storage.Library, host,
                renderer ?? (job =>
                {
                    var r = new CpuFrameRenderer(host);
                    _renderers.Add(r);
                    return r;
                }),
                new StudioExportAudio(mixer, _ => projectDir), _gate)
            {
                StallTimeoutMs = realStallTimeout ? ExportPipeline.DefaultStallTimeoutMs : ExportPipelineTests.HarnessStallTimeoutMs,
            };
        }

        ExportJobRequest Request(bool keep = false, bool gallery = false) => new ExportJobRequest
        {
            JobId = "job1",
            Project = _doc,
            Settings = new ExportSettings(ExportResolution.P720, 30),
            StreamTicks = ClipTicks,
            KeepInProject = keep,
            SaveToGallery = gallery,
            NowIso = "2026-10-18T12:06:00Z",
        };

        StudioExportResult Run(ExportPipeline pipeline, ExportJobRequest request, CancellationToken ct, TestExportHost host)
        {
            Task<StudioExportResult> t = pipeline.StartAsync(request, ct);
            try { _pump.RunUntil(() => t.IsCompleted, 30000); } catch (TimeoutException) { }
            Assert.True(t.IsCompleted, "export still running after " + host.Frames + " frames");
            Assert.Equal(TaskStatus.RanToCompletion, t.Status); // never throws for export failures
            return t.Result;
        }

        [Fact]
        public void CorruptReplayFailsAsClipDataAndCleansUp()
        {
            File.WriteAllBytes(_storage.Library.FullPath("projects/p1/clip.bvr"), new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });
            var host = new TestExportHost(_pump);
            ExportPipeline p = Pipeline(host, null, new PopSounds());
            StudioExportResult r = Run(p, Request(), CancellationToken.None, host);
            Assert.Equal(ExportErrorCode.ClipDataMissing, r.Error);
            Assert.False(_storage.Exports.DirectoryExists("job1"));
            Assert.Empty(p.ListResumable());
            Assert.Equal(new[] { ExportErrorCode.ClipDataMissing }, _gate.Finished);
            Assert.Equal(0, host.AwakeTokens);
            Assert.False(p.IsRunning);
        }

        [Fact]
        public void UnexpectedRendererExceptionStillReturnsAResult()
        {
            var host = new TestExportHost(_pump);
            ExportPipeline p = Pipeline(host, job => throw new InvalidOperationException("boom"));
            StudioExportResult r = Run(p, Request(), CancellationToken.None, host);
            Assert.Equal(ExportErrorCode.Unknown, r.Error);
            Assert.Contains("boom", r.Message);
            Assert.False(_storage.Exports.DirectoryExists("job1"));
            Assert.Single(_gate.Finished);
        }

        [Fact]
        public void HungEncoderQueueIsRetriedOnceThenFails()
        {
            _media.StuckWriters = true;
            var host = new TestExportHost(_pump);
            StudioExportResult r = Run(Pipeline(host, realStallTimeout: true), Request(), CancellationToken.None, host);
            Assert.Equal(ExportErrorCode.EncoderFailed, r.Error);
            Assert.True(r.SuggestLowerSettings);
            // Two attempts of ~15 s each, not forever.
            Assert.InRange(host.NowMs, 2 * ExportPipeline.DefaultStallTimeoutMs, 3 * ExportPipeline.DefaultStallTimeoutMs);
            Assert.False(_storage.Exports.DirectoryExists("job1"));
            Assert.Equal(new[] { ExportErrorCode.EncoderFailed }, _gate.Finished);
        }

        [Fact]
        public void SegmentRedoOpensTheNextWriterOnlyAfterTheDisposedOneIsReleased()
        {
            // QA round 2: after a redo the next segment writer was opened synchronously one frame after Dispose; on Android the
            // native open then waits (up to 3 s, on the main thread) for the codec of the disposed writer.
            _mediaOptions.SimulatedReleaseDelayMs = 300;
            var host = new TestExportHost(_pump);
            bool paused = false;
            host.OnFrame = h =>
            {
                if (!paused && _media.Opened.Count > 0 && _media.Opened[0].FramesAppended >= 10)
                {
                    paused = true;
                    h.Paused = true;
                    h.Pauses++;
                }
                else if (h.Paused)
                {
                    h.Paused = false; // back after one frame: far shorter than the encoder release
                }
            };
            StudioExportResult r = Run(Pipeline(host), Request(), CancellationToken.None, host);
            Assert.True(r.Succeeded, r.Error + ": " + r.Message);
            Assert.True(paused);
            Assert.True(_media.Opened.Count >= 2);
            Assert.True(_media.Opened[0].Released.IsCompleted);
            Assert.False(_media.OpenedWhileReleasing, "a segment writer was opened while the disposed one was still releasing");
        }

        [Fact]
        public void ReadbackThatNeverCompletesIsRetriedOnceThenFails()
        {
            var host = new TestExportHost(_pump);
            var renderer = new NeverDoneRenderer();
            StudioExportResult r = Run(Pipeline(host, job => renderer, realStallTimeout: true), Request(), CancellationToken.None, host);
            Assert.Equal(ExportErrorCode.EncoderFailed, r.Error);
            Assert.True(renderer.Discards >= 2);
            Assert.InRange(host.NowMs, 2 * ExportPipeline.DefaultStallTimeoutMs, 3 * ExportPipeline.DefaultStallTimeoutMs);
        }

        [Fact]
        public void VideoFrameThatNeverDecodesTimesOut()
        {
            var host = new TestExportHost(_pump);
            StudioExportResult r = Run(Pipeline(host, job => new NeverReadyRenderer(), realStallTimeout: true), Request(), CancellationToken.None, host);
            Assert.Equal(ExportErrorCode.Timeout, r.Error);
            Assert.InRange(host.NowMs, 2 * ExportPipeline.DefaultStallTimeoutMs, 3 * ExportPipeline.DefaultStallTimeoutMs);
        }

        [Fact]
        public void CancelDuringGallerySaveKeepsTheFinishedExport()
        {
            _media.RealConcat = true;
            var cts = new CancellationTokenSource();
            _media.OnGallery = () => cts.Cancel();
            var host = new TestExportHost(_pump);
            StudioExportResult r = Run(Pipeline(host), Request(keep: true, gallery: true), cts.Token, host);
            Assert.True(r.Succeeded, r.Error + ": " + r.Message);
            Assert.Equal(ExportGalleryStatus.Saved, r.Gallery);
            Assert.False(_media.GalleryToken.CanBeCanceled);
            Assert.NotNull(r.OutputPath);
            Assert.NotNull(r.ProjectFile);
            Assert.Equal(new[] { ExportErrorCode.None }, _gate.Finished);
        }

        [Fact]
        public void JoinInterruptedByThePauseIsRetriedAfterReturning()
        {
            var host = new TestExportHost(_pump);
            _media.FailConcatCalls.Add(1);
            _media.OnConcatFailure = () =>
            {
                host.Paused = true;
                host.Pauses++;
            };
            int pausedAt = -1;
            host.OnFrame = h =>
            {
                if (h.Paused && pausedAt < 0) pausedAt = h.Frames;
                if (h.Paused && h.Frames - pausedAt > 5) h.Paused = false;
            };
            StudioExportResult r = Run(Pipeline(host), Request(), CancellationToken.None, host);
            Assert.True(r.Succeeded, r.Error + ": " + r.Message);
            Assert.Equal(2, _media.Concats);
            Assert.Equal(75, r.FrameCount);
            Assert.Single(_renderers); // the frames were not rendered again
        }

        [Fact]
        public void JoinThatKeepsFailingLeavesAResumableJob()
        {
            var host = new TestExportHost(_pump);
            _media.FailConcatCalls.Add(1);
            _media.FailConcatCalls.Add(2);
            ExportPipeline first = Pipeline(host);
            StudioExportResult failed = Run(first, Request(), CancellationToken.None, host);
            Assert.Equal(ExportErrorCode.MuxerFailed, failed.Error);
            Assert.True(failed.Resumable);
            Assert.True(_storage.Exports.DirectoryExists("job1"));
            ExportResumeState kept = first.ListResumable().Single();
            Assert.Equal(2, kept.Segments.Count);
            Assert.True(kept.MixReady);

            var host2 = new TestExportHost(_pump);
            ExportPipeline second = Pipeline(host2);
            StudioExportResult r = _pump.Wait(second.ResumeAsync("job1", CancellationToken.None), 60000);
            Assert.True(r.Succeeded, r.Error + ": " + r.Message);
            Assert.Equal(75, r.FrameCount);
            Assert.Single(_renderers); // the resumed run only joined
            Assert.Equal(1, _gate.Requests);
            Assert.False(_storage.Exports.DirectoryExists("job1"));
        }
    }
}
