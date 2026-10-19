using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core;
using Ballverse.Core.Integration;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Ballverse.Media;
using Unity.Collections;

namespace Ballverse.Studio
{
    /// <summary>
    /// The MP4 export (spec §3.8, §7.5; release 1: RGBA readback, 2-second segments joined into one fast-start MP4):
    /// <list type="number">
    /// <item>preflight: free space ≥ 2 × estimate + 200 MB (Core <see cref="ExportPolicy.Preflight"/>);</item>
    /// <item>the rewarded gate (<see cref="IExportGate"/>, new exports only);</item>
    /// <item>audio first: the offline mix is written to the job's PCM file (<see cref="IExportAudioRenderer"/>);</item>
    /// <item>frame loop over the Core export plan: render + compose + async RGBA readback (≤ 3 in flight, back-pressure) →
    /// <see cref="IVideoWriter.TryAppendVideoFrame(NativeArray{byte}, long)"/>; as many frames per Unity frame as fit in ~25 ms;
    /// every 2 s a segment writer is finalized and the resume state saved atomically (Core <see cref="ExportResumeStore"/>);</item>
    /// <item>join without re-encoding + one AAC track from the PCM mix, <c>moov</c> first (<see cref="IMediaBridge.ConcatSegmentsAsync"/>);</item>
    /// <item>verification of frame count and duration (spec §3.8 acceptance), gallery save (album "Ballverse") per setting,
    /// cleanup of the job folder.</item>
    /// </list>
    /// Interruptions: app in the background → the current segment is dropped and redone on return; app killed → the job stays
    /// resumable (<see cref="ListResumable"/> / <see cref="ResumeAsync"/>, no second ad); hot device → pause between segments;
    /// encoder error → one retry of the segment, then <see cref="ExportErrorCode.EncoderFailed"/> (UI offers 720p/30); cancel →
    /// partial files deleted at once. Main thread: every await resumes on the Unity main thread; native work runs on the media
    /// bridge's threads, the mix on the thread pool.
    /// </summary>
    public sealed class ExportPipeline
    {
        /// <summary>
        /// No progress of any kind (a frame rendered, a frame accepted by the encoder, a segment finished) for this long while the
        /// export should be running counts as a stall (hung encoder queue, a GPU readback that never completes, a video frame that
        /// never decodes): the segment is restarted once like an encoder error, then the export fails. ms.
        /// </summary>
        public const long DefaultStallTimeoutMs = 15000;

        readonly IMediaBridge _media;
        readonly UnityFileStore _exports;
        readonly UnityFileStore _library;
        readonly IExportHost _host;
        readonly Func<ExportJob, IExportFrameRenderer> _rendererFactory;
        readonly IExportAudioRenderer _audio;
        IExportGate _gate;

        /// <param name="media">Media bridge (writers, join, gallery).</param>
        /// <param name="exports">Export scratch store (job folders, outputs).</param>
        /// <param name="library">Persistent store (projects/{id}/exports for "keep in project").</param>
        /// <param name="host">Frame pacing, device signals, lifecycle.</param>
        /// <param name="rendererFactory">Creates the frame renderer of a job.</param>
        /// <param name="audio">Mix renderer.</param>
        /// <param name="gate">Rewarded gate, or null (no gate).</param>
        public ExportPipeline(IMediaBridge media, UnityFileStore exports, UnityFileStore library, IExportHost host,
            Func<ExportJob, IExportFrameRenderer> rendererFactory, IExportAudioRenderer audio, IExportGate gate)
        {
            _media = media ?? throw new ArgumentNullException(nameof(media));
            _exports = exports ?? throw new ArgumentNullException(nameof(exports));
            _library = library ?? throw new ArgumentNullException(nameof(library));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            _rendererFactory = rendererFactory ?? throw new ArgumentNullException(nameof(rendererFactory));
            _audio = audio ?? throw new ArgumentNullException(nameof(audio));
            _gate = gate;
        }

        /// <summary>Stall timeout of the frame loop, ms (<see cref="DefaultStallTimeoutMs"/>).</summary>
        public long StallTimeoutMs { get; set; } = DefaultStallTimeoutMs;

        /// <summary>The rewarded gate (the App may connect it after construction).</summary>
        public IExportGate Gate
        {
            get => _gate;
            set => _gate = value;
        }

        /// <summary>Raised on the main thread whenever the progress changes (a few times per second at most per Unity frame).</summary>
        public event Action<ExportProgressInfo> Progress;

        /// <summary>The frame renderer of the running export (the progress sheet shows its current frame), or null.</summary>
        public IExportFrameRenderer CurrentRenderer { get; private set; }

        public bool IsRunning { get; private set; }

        /// <summary>Unfinished exports that can continue (spec §3.8 "Продолжить экспорт"), newest first.</summary>
        public IReadOnlyList<ExportResumeState> ListResumable()
        {
            return ExportResumeStore.List(_exports);
        }

        /// <summary>Deletes an unfinished export the user does not want to continue.</summary>
        public void DiscardResumable(string jobId)
        {
            ExportResumeStore.DeleteJob(_exports, jobId);
        }

        /// <summary>Starts a new export. Never throws for export failures: they come back as <see cref="StudioExportResult.Error"/>.</summary>
        public async Task<StudioExportResult> StartAsync(ExportJobRequest request, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (IsRunning) return StudioExportResult.Failure(ExportErrorCode.Busy, "another export is running", request.Project?.Id, request.JobId);
            IsRunning = true;
            string projectId = request.Project?.Id;
            try
            {
                ExportResumeState state;
                ExportJob job;
                try
                {
                    state = ExportJob.CreateState(request);
                    job = ExportJob.FromState(state);
                }
                catch (ArgumentException e)
                {
                    return StudioExportResult.Failure(ExportErrorCode.ProjectInvalid, e.Message, projectId, request.JobId);
                }
                Report(job, ExportPhase.Preflight, null, false, false);
                ExportPreflight preflight = ExportPolicy.Preflight(job.Plan, _host.FreeSpaceBytes);
                if (!preflight.Ok)
                {
                    return StudioExportResult.Failure(ExportErrorCode.InsufficientSpace,
                        "free " + Mb(preflight.FreeBytes) + " MB, need " + Mb(preflight.RequiredBytes) + " MB", projectId, request.JobId);
                }
                Report(job, ExportPhase.Gate, null, false, false);
                if (_gate != null)
                {
                    bool passed;
                    try
                    {
                        passed = await _gate.RequestAsync(new ExportGateRequest(projectId, state.JobId, state.Resolution, state.Fps), ct);
                    }
                    catch (OperationCanceledException)
                    {
                        return StudioExportResult.Failure(ExportErrorCode.Cancelled, "cancelled at the gate", projectId, state.JobId);
                    }
                    catch (Exception e) when (!(e is OutOfMemoryException))
                    {
                        // A failing ad SDK must not crash the studio: the export simply does not start.
                        UnityEngine.Debug.LogException(e);
                        return StudioExportResult.Failure(ExportErrorCode.GateDeclined, "rewarded gate failed: " + e.Message, projectId, state.JobId);
                    }
                    if (!passed) return StudioExportResult.Failure(ExportErrorCode.GateDeclined, "rewarded gate declined", projectId, state.JobId);
                }
                state.GatePassed = true;
                _exports.CreateDirectory(state.JobId);
                ExportResumeStore.Save(_exports, state);
                return await RunJobAsync(job, ct);
            }
            finally
            {
                IsRunning = false;
            }
        }

        /// <summary>Continues an unfinished export from its last finished segment (no gate).</summary>
        public async Task<StudioExportResult> ResumeAsync(string jobId, CancellationToken ct)
        {
            if (IsRunning) return StudioExportResult.Failure(ExportErrorCode.Busy, "another export is running", null, jobId);
            IsRunning = true;
            try
            {
                ExportResumeState state = ExportResumeStore.Load(_exports, jobId, out string error);
                ExportJob job = null;
                if (state != null)
                {
                    try
                    {
                        job = ExportJob.FromState(state);
                    }
                    catch (ArgumentException e)
                    {
                        error = e.Message;
                    }
                }
                if (job == null)
                {
                    TryDeleteJob(jobId);
                    return StudioExportResult.Failure(ExportErrorCode.ResumeInvalid, error ?? "invalid state", state?.ProjectId, jobId);
                }
                return await RunJobAsync(job, ct);
            }
            finally
            {
                IsRunning = false;
            }
        }

        async Task<StudioExportResult> RunJobAsync(ExportJob job, CancellationToken ct)
        {
            ExportResumeState state = job.State;
            string jobId = state.JobId;
            long startMs = _host.NowMs;
            long elapsedBefore = state.ElapsedMs;
            ModeCode mode = ModeCatalog.TryGetById(state.Project.Clip?.ModeId ?? string.Empty, out ModeInfo info) ? info.Code : ModeCode.M01;
            var progress = new ExportProgressModel(job.FrameCount, Math.Min(job.FrameCount, state.Segments.Count * state.SegmentFrames),
                job.Plan.EstimateExportTimeMs(_host.Tier, mode));
            IExportFrameRenderer renderer = null;
            IVideoWriter writer = null;
            StudioExportResult result;
            bool keepJob = false;
            using (_host.KeepAwake("export"))
            {
                try
                {
                    // 1. Audio first (one PCM file for the whole export).
                    string mixPath = _exports.FullPath(StudioFiles.JobFile(jobId, StudioFiles.MixPcmFile));
                    if (!state.MixReady || !File.Exists(mixPath))
                    {
                        progress.SetPhase(ExportPhase.Audio, 0);
                        Report(job, progress, false, false);
                        state.MixFrames = await RenderMixAsync(job, mixPath, ct);
                        state.MixReady = true;
                        SaveState(state, startMs, elapsedBefore);
                    }
                    progress.SetPhase(ExportPhase.Audio, 1);

                    // 2. Frames (skipped when every segment is already on disk: a resumed join).
                    int usable = ExportResumeStore.CountUsableSegments(_exports, state);
                    if (usable < state.Segments.Count)
                    {
                        state.Segments.RemoveRange(usable, state.Segments.Count - usable);
                        SaveState(state, startMs, elapsedBefore);
                    }
                    if (!new ExportFrameFlow(job.FrameCount, state.SegmentFrames, state.Segments.Count).IsDone)
                    {
                        renderer = _rendererFactory(job);
                        CurrentRenderer = renderer;
                        await renderer.PrepareAsync(job, ct);
                        progress.SetPhase(ExportPhase.Frames);
                        progress.ResetRate(state.Segments.Count * state.SegmentFrames);
                        writer = await FrameLoopAsync(job, renderer, progress, startMs, elapsedBefore, ct);
                        renderer.Dispose();
                        renderer = null;
                        CurrentRenderer = null;
                    }

                    // 3. Join, verify, place, save. From here on every frame is encoded: a failed join keeps the job.
                    progress.SetPhase(ExportPhase.Finalize, 0);
                    Report(job, progress, false, false);
                    result = await JoinWithRetryAsync(job, mixPath, startMs, elapsedBefore, ct);
                    keepJob = result.Resumable;
                    if (result.Succeeded)
                    {
                        progress.SetPhase(ExportPhase.Done);
                        Report(job, progress, false, false);
                    }
                }
                catch (OperationCanceledException)
                {
                    result = StudioExportResult.Failure(ExportErrorCode.Cancelled, "cancelled", state.ProjectId, jobId);
                }
                catch (ExportFailureException e)
                {
                    result = StudioExportResult.Failure(e.Code, e.Message, state.ProjectId, jobId);
                }
                catch (MediaException e)
                {
                    result = StudioExportResult.Failure(ExportErrors.FromMedia(e.Code), e.Message, state.ProjectId, jobId);
                }
                catch (IOException e)
                {
                    result = StudioExportResult.Failure(ExportErrors.IsDiskFull(e) ? ExportErrorCode.InsufficientSpace : ExportErrorCode.Io, e.Message, state.ProjectId, jobId);
                }
                catch (UnauthorizedAccessException e)
                {
                    result = StudioExportResult.Failure(ExportErrorCode.Io, e.Message, state.ProjectId, jobId);
                }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    // StartAsync / ResumeAsync never throw for export failures: anything unexpected still cleans up and tells the gate.
                    UnityEngine.Debug.LogException(e);
                    result = StudioExportResult.Failure(ExportErrorCode.Unknown, e.GetType().Name + ": " + e.Message, state.ProjectId, jobId);
                }
                finally
                {
                    writer?.Dispose(); // an unfinished writer deletes its partial segment
                    renderer?.Dispose();
                    CurrentRenderer = null;
                }
            }
            result.Resolution = state.Resolution;
            result.Fps = state.Fps;
            if (result.ElapsedMs == 0) result.ElapsedMs = elapsedBefore + Math.Max(0, _host.NowMs - startMs);
            // Spec §3.8: temporary files go immediately after a cancel or failure — except a finished-but-unjoined job, which
            // stays resumable (spec §3.8 background row: the export continues when the app returns).
            if (!result.Succeeded && !keepJob) TryDeleteJob(jobId);
            try
            {
                _gate?.OnExportFinished(state.ProjectId, result);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                UnityEngine.Debug.LogException(e);
            }
            return result;
        }

        /// <summary>The audio phase: failures of the replay read, the user audio decode or the mix map to export codes.</summary>
        async Task<long> RenderMixAsync(ExportJob job, string mixPath, CancellationToken ct)
        {
            try
            {
                return await _audio.RenderMixAsync(job, mixPath, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ExportFailureException)
            {
                throw;
            }
            catch (IOException e) when (ExportErrors.IsDiskFull(e))
            {
                throw new ExportFailureException(ExportErrorCode.InsufficientSpace, "audio mix: " + e.Message, e);
            }
            catch (InvalidDataException e)
            {
                // A corrupt or truncated clip.bvr / clip.bvs.
                throw new ExportFailureException(ExportErrorCode.ClipDataMissing, "clip data cannot be read for the sound: " + e.Message, e);
            }
            catch (EndOfStreamException e)
            {
                throw new ExportFailureException(ExportErrorCode.ClipDataMissing, "clip data truncated: " + e.Message, e);
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                throw new ExportFailureException(ExportErrorCode.AudioFailed, "audio mix failed: " + e.GetType().Name + ": " + e.Message, e);
            }
        }

        /// <summary>
        /// The final join (spec §3.8): on a muxer, timeout or file error — typically iOS suspending the app mid-join — it waits
        /// until the app is in the foreground and retries once; if that fails too, the job (segments + mix) is kept and the result
        /// is <see cref="StudioExportResult.Resumable"/>.
        /// </summary>
        async Task<StudioExportResult> JoinWithRetryAsync(ExportJob job, string mixPath, long startMs, long elapsedBefore, CancellationToken ct)
        {
            ExportResumeState state = job.State;
            for (int attempt = 0; ; attempt++)
            {
                int pausesBefore = _host.PauseCount;
                Exception failure;
                try
                {
                    return await FinalizeAsync(job, mixPath, startMs, elapsedBefore, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (MediaException e) when (IsResumableJoinError(e.Code))
                {
                    failure = e;
                }
                catch (IOException e) when (!ExportErrors.IsDiskFull(e))
                {
                    failure = e;
                }
                bool interrupted = _host.IsPaused || _host.PauseCount != pausesBefore;
                UnityEngine.Debug.LogWarning("[Studio] Join failed (attempt " + (attempt + 1) + (interrupted ? ", app was paused" : "") + "): " + failure.Message);
                if (attempt >= 1 || ct.IsCancellationRequested)
                {
                    ExportErrorCode code = failure is MediaException m ? ExportErrors.FromMedia(m.Code) : ExportErrorCode.Io;
                    StudioExportResult kept = StudioExportResult.Failure(code, "join failed, export can be resumed: " + failure.Message, state.ProjectId, state.JobId);
                    kept.Resumable = true;
                    return kept;
                }
                while (_host.IsPaused)
                {
                    await _host.NextFrame();
                    ct.ThrowIfCancellationRequested();
                }
            }
        }

        static bool IsResumableJoinError(MediaErrorCode code)
        {
            return code == MediaErrorCode.MuxerFailed || code == MediaErrorCode.Timeout || code == MediaErrorCode.Io || code == MediaErrorCode.Unknown
                || code == MediaErrorCode.EncoderFailed || code == MediaErrorCode.Busy;
        }

        /// <summary>Runs the frame loop until all segments are finished. Returns null (no open writer) on success.</summary>
        async Task<IVideoWriter> FrameLoopAsync(ExportJob job, IExportFrameRenderer renderer, ExportProgressModel progress, long startMs,
            long elapsedBefore, CancellationToken ct)
        {
            ExportResumeState state = job.State;
            var flow = new ExportFrameFlow(job.FrameCount, state.SegmentFrames, state.Segments.Count, Math.Min(ExportFrameFlow.DefaultMaxInFlight, renderer.MaxInFlight));
            IVideoWriter writer = null;
            int failedAttempts = 0;
            int pausesSeen = _host.PauseCount;
            long resumeAtMs = 0;
            long lastProgressMs = _host.NowMs;
            double avgFrameMs = 0;
            int attemptSegment = -1;
            int attempt = 0;
            bool throttled = false;
            try
            {
                while (!flow.IsDone)
                {
                    ct.ThrowIfCancellationRequested();
                    if (_host.IsPaused || _host.PauseCount != pausesSeen)
                    {
                        // The app went to the background: the half-written segment is dropped and redone (spec §3.8).
                        DisposeWriter(ref writer);
                        renderer.DiscardInFlight();
                        flow.RestartSegment();
                        progress.ResetRate(flow.FramesAppendedTotal);
                        while (_host.IsPaused)
                        {
                            Report(job, progress, throttled, true);
                            await _host.NextFrame();
                            ct.ThrowIfCancellationRequested();
                        }
                        pausesSeen = _host.PauseCount;
                        lastProgressMs = _host.NowMs;
                        continue;
                    }
                    if (_host.NowMs < resumeAtMs)
                    {
                        Report(job, progress, throttled, false);
                        await _host.NextFrame();
                        lastProgressMs = _host.NowMs; // a thermal pause is not a stall
                        continue;
                    }
                    try
                    {
                        long tickStart = _host.NowMs;
                        int framesThisTick = 0;
                        bool rendered = false;
                        while (true)
                        {
                            if (writer == null && flow.AtSegmentStart)
                            {
                                if (flow.Segment != attemptSegment)
                                {
                                    attemptSegment = flow.Segment;
                                    attempt = 0;
                                }
                                string segPath = _exports.FullPath(StudioFiles.SegmentPath(state.JobId, flow.Segment, attempt++));
                                Directory.CreateDirectory(Path.GetDirectoryName(segPath));
                                // Never the synchronous OpenWriter here: after a redo (pause, encoder error, stall) the disposed
                                // writer's codec may still be stopping, and the Android native open would wait for it (up to
                                // 3 s) on the main thread. OpenWriterAsync awaits every writer being released first (§3.8).
                                writer = await _media.OpenWriterAsync(segPath, VideoWriterOptions.ForSegment(job.Settings), ct);
                                lastProgressMs = _host.NowMs; // waiting for a release is not a stall
                                tickStart = _host.NowMs;
                            }
                            if (writer != null && Drain(job, renderer, writer, flow) > 0) lastProgressMs = _host.NowMs;
                            if (flow.SegmentComplete)
                            {
                                IVideoWriter finishing = writer;
                                writer = null;
                                VideoWriterResult segment = await _media.FinalizeAsync(finishing, ct);
                                lastProgressMs = _host.NowMs;
                                int expected = flow.SegmentEnd - flow.SegmentStart;
                                if (segment.FrameCount != expected)
                                    throw new MediaException(MediaErrorCode.EncoderFailed, "segment " + flow.Segment + " has " + segment.FrameCount + " frames, expected " + expected);
                                state.Segments.Add(new ExportSegmentRecord
                                {
                                    Index = flow.Segment,
                                    File = RelativeToJob(state.JobId, segment.Path),
                                    FrameCount = segment.FrameCount,
                                });
                                SaveState(state, startMs, elapsedBefore);
                                flow.OnSegmentFinished();
                                failedAttempts = 0;
                                int pause = ExportPolicy.PauseBetweenSegmentsMs(_host.Thermal);
                                throttled = pause > 0;
                                resumeAtMs = _host.NowMs + pause;
                                break;
                            }
                            if (!flow.CanRender) break;
                            if (!ExportPolicy.FitsInBudget(_host.NowMs - tickStart, avgFrameMs, framesThisTick)) break;
                            ExportFrame frame = job.Planner.GetFrame(flow.NextRender);
                            long t0 = _host.NowMs;
                            if (!renderer.TryRender(frame)) break;
                            if (frame.Index + 1 < flow.SegmentEnd) renderer.Prefetch(job.Planner.GetFrame(frame.Index + 1));
                            flow.OnRendered();
                            rendered = true;
                            framesThisTick++;
                            double cost = Math.Max(0, _host.NowMs - t0);
                            avgFrameMs = avgFrameMs <= 0 ? cost : avgFrameMs + (cost - avgFrameMs) * 0.2;
                        }
                        if (rendered) lastProgressMs = _host.NowMs;
                        else if (!flow.IsDone && _host.NowMs - lastProgressMs > StallTimeoutMs)
                        {
                            // Frames in flight that never drain: the encoder queue or the GPU readback hangs. None in flight:
                            // the next frame cannot be rendered (its video frame is never decoded).
                            if (flow.InFlight > 0)
                                throw new ExportStallException(ExportErrorCode.EncoderFailed, "no frame accepted for " + StallTimeoutMs + " ms (encoder or readback stalled) at frame " + flow.NextAppend);
                            throw new ExportStallException(ExportErrorCode.Timeout, "frame " + flow.NextRender + " could not be rendered for " + StallTimeoutMs + " ms (video frame not decoded)");
                        }
                    }
                    catch (Exception e) when (e is ExportStallException || (e is MediaException me && ExportErrors.IsRetryableEncoderError(me)))
                    {
                        failedAttempts++;
                        DisposeWriter(ref writer);
                        renderer.DiscardInFlight();
                        flow.RestartSegment();
                        progress.ResetRate(flow.FramesAppendedTotal);
                        lastProgressMs = _host.NowMs;
                        if (!ExportPolicy.CanRetrySegment(failedAttempts))
                        {
                            ExportErrorCode code = e is ExportStallException stall ? stall.Code : ExportErrorCode.EncoderFailed;
                            throw new ExportFailureException(code, "segment " + flow.Segment + " failed twice: " + e.Message, e);
                        }
                        UnityEngine.Debug.LogWarning("[Studio] Encoder error on segment " + flow.Segment + ", retrying once: " + e.Message);
                    }
                    progress.OnFrames(flow.FramesAppendedTotal, _host.NowMs);
                    Report(job, progress, throttled, false);
                    await _host.NextFrame();
                }
                return null;
            }
            catch
            {
                DisposeWriter(ref writer);
                throw;
            }
        }

        /// <summary>Hands finished readbacks to the writer in frame order until it pushes back; returns the frames appended.</summary>
        static int Drain(ExportJob job, IExportFrameRenderer renderer, IVideoWriter writer, ExportFrameFlow flow)
        {
            int appended = 0;
            long segmentStartPts = job.Plan.FramePtsUs(flow.SegmentStart);
            while (flow.InFlight > 0 && renderer.TryPeekCompleted(out NativeArray<byte> rgba, out int index))
            {
                if (index != flow.NextAppend) throw new InvalidOperationException("readback out of order: " + index + " != " + flow.NextAppend);
                long pts = job.Plan.FramePtsUs(index) - segmentStartPts;
                if (!writer.TryAppendVideoFrame(rgba, pts)) break;
                renderer.ReleaseCompleted();
                flow.OnAppended();
                appended++;
            }
            return appended;
        }

        async Task<StudioExportResult> FinalizeAsync(ExportJob job, string mixPath, long startMs, long elapsedBefore, CancellationToken ct)
        {
            ExportResumeState state = job.State;
            var segments = new List<string>(state.Segments.Count);
            foreach (ExportSegmentRecord r in state.Segments) segments.Add(_exports.FullPath(StudioFiles.JobFile(state.JobId, r.File)));
            string outFile = _exports.FullPath(StudioFiles.JobFile(state.JobId, state.OutputName));
            bool deleteEarly = ExportPolicy.DeleteSegmentsBeforeFastStart(_host.FreeSpaceBytes, job.Plan.EstimateFileSizeBytes());
            var metadata = new Mp4Metadata(state.Project.Name, DateTime.UtcNow);
            string pcm = state.MixFrames > 0 && File.Exists(mixPath) ? mixPath : null;
            VideoWriterResult joined = await _media.ConcatSegmentsAsync(segments, pcm, outFile,
                SegmentConcatOptions.FromExportSettings(job.Settings, metadata, deleteEarly), ct);
            if (!job.Plan.IsFrameCountExact(joined.FrameCount) || !job.Plan.IsDurationAcceptable(joined.DurationUs))
            {
                DeletePath(joined.Path);
                throw new ExportFailureException(ExportErrorCode.VerificationFailed,
                    "joined file has " + joined.FrameCount + " frames / " + joined.DurationUs + " µs, expected " + job.FrameCount + " / " + job.Timeline.DurationUs);
            }
            long elapsed = elapsedBefore + Math.Max(0, _host.NowMs - startMs);

            // Place the file: the project's exports/ folder (kept with the project) or the outputs folder (until the next start).
            string fileName = Path.GetFileName(joined.Path);
            string projectRelative = null;
            string destination;
            if (state.KeepInProject && _library.DirectoryExists(ProjectPaths.ProjectDir(state.ProjectId)))
            {
                projectRelative = ProjectPaths.ExportsDir + "/" + fileName;
                destination = _library.FullPath(ProjectPaths.InProject(state.ProjectId, projectRelative));
            }
            else
            {
                destination = _exports.FullPath(StudioStorage.OutputsDir + "/" + fileName);
            }
            MovePath(joined.Path, destination);

            var result = new StudioExportResult
            {
                Error = ExportErrorCode.None,
                ProjectId = state.ProjectId,
                JobId = state.JobId,
                OutputPath = destination,
                ProjectFile = projectRelative,
                FrameCount = joined.FrameCount,
                DurationUs = joined.DurationUs,
                ElapsedMs = elapsed,
                IsSynthetic = joined.IsSynthetic,
                Gallery = ExportGalleryStatus.NotRequested,
            };
            if (state.SaveToGallery && !joined.IsSynthetic)
            {
                var progressPhase = new ExportProgressInfo(state.JobId, ExportPhase.Saving, ExportProgressModel.PrepareWeight + ExportProgressModel.FramesWeight + ExportProgressModel.FinalizeWeight,
                    0, job.FrameCount, job.FrameCount, false, false);
                Progress?.Invoke(progressPhase);
                try
                {
                    // The file is verified and placed: a cancel now would only lose it from the history (an accepted gallery save
                    // cannot be undone anyway), so the save runs to its end and the export counts as done.
                    GallerySaveResult g = await _media.SaveToGalleryAsync(destination, StudioFiles.GalleryAlbum, CancellationToken.None);
                    result.Gallery = g.IsSaved ? ExportGalleryStatus.Saved : g.Status == GallerySaveStatus.PermissionDenied ? ExportGalleryStatus.PermissionDenied : ExportGalleryStatus.Failed;
                    result.GalleryLocation = g.Location;
                }
                catch (Exception e) when (!(e is OutOfMemoryException))
                {
                    // The file exists and can still be shared: a gallery failure does not fail the export.
                    result.Gallery = ExportGalleryStatus.Failed;
                    UnityEngine.Debug.LogWarning("[Studio] Gallery save failed: " + e.Message);
                }
            }
            TryDeleteJob(state.JobId);
            return result;
        }

        void SaveState(ExportResumeState state, long startMs, long elapsedBefore)
        {
            state.ElapsedMs = elapsedBefore + Math.Max(0, _host.NowMs - startMs);
            ExportResumeStore.Save(_exports, state);
        }

        string RelativeToJob(string jobId, string absolutePath)
        {
            string jobDir = _exports.FullPath(jobId);
            string full = Path.GetFullPath(absolutePath);
            string prefix = jobDir.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? jobDir : jobDir + Path.DirectorySeparatorChar;
            if (!full.StartsWith(prefix, StringComparison.Ordinal)) throw new ExportFailureException(ExportErrorCode.Io, "segment written outside its job folder");
            return full.Substring(prefix.Length).Replace(Path.DirectorySeparatorChar, '/');
        }

        void Report(ExportJob job, ExportPhase phase, long? remaining, bool throttled, bool paused)
        {
            Progress?.Invoke(new ExportProgressInfo(job.State.JobId, phase, 0, remaining, 0, job.FrameCount, throttled, paused));
        }

        void Report(ExportJob job, ExportProgressModel p, bool throttled, bool paused)
        {
            Progress?.Invoke(new ExportProgressInfo(job.State.JobId, p.Phase, p.Fraction, p.RemainingMs, p.FramesDone, p.FrameCount, throttled, paused));
        }

        /// <summary>The frame loop made no progress for <see cref="StallTimeoutMs"/>.</summary>
        sealed class ExportStallException : Exception
        {
            public ExportStallException(ExportErrorCode code, string message)
                : base(message)
            {
                Code = code;
            }

            public ExportErrorCode Code { get; }
        }

        static void DisposeWriter(ref IVideoWriter writer)
        {
            writer?.Dispose();
            writer = null;
        }

        void TryDeleteJob(string jobId)
        {
            try
            {
                if (ProjectPaths.IsSafeId(jobId)) ExportResumeStore.DeleteJob(_exports, jobId);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // removed by the startup cleanup
            }
        }

        /// <summary>Moves a file, or a folder (fake media backend output), replacing the destination.</summary>
        static void MovePath(string from, string to)
        {
            string dir = Path.GetDirectoryName(to);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            if (Directory.Exists(from))
            {
                if (Directory.Exists(to)) Directory.Delete(to, true);
                Directory.Move(from, to);
                return;
            }
            if (File.Exists(to)) File.Delete(to);
            File.Move(from, to);
        }

        static void DeletePath(string path)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // job folder cleanup removes it
            }
        }

        static long Mb(long bytes) => bytes < 0 ? -1 : bytes / (1024 * 1024);
    }
}
