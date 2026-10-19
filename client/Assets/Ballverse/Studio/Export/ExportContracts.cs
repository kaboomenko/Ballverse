using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core;
using Ballverse.Core.Integration;
using Ballverse.Core.Studio;
using Ballverse.Media;
using Unity.Collections;

namespace Ballverse.Studio
{
    /// <summary>What the rewarded gate is asked for (spec §3.8 step 2, §5.3.1).</summary>
    public sealed class ExportGateRequest
    {
        public ExportGateRequest(string projectId, string jobId, int resolution, int fps)
        {
            ProjectId = projectId;
            JobId = jobId;
            Resolution = resolution;
            Fps = fps;
        }

        public string ProjectId { get; }
        public string JobId { get; }
        public int Resolution { get; }
        public int Fps { get; }
    }

    /// <summary>
    /// Hook for the rewarded-export gate (spec §3.8 step 2): the App connects Services' RewardedExport here (Core
    /// <c>RewardedExportGate</c> decides whether an ad is needed: <c>NO_EXPORT_ADS</c>, retry credit RW4, …). Invoked on the main
    /// thread once per new export, after the free-space check and before anything is rendered; never for a resumed export
    /// (the gate was passed when it started, spec RW4).
    /// </summary>
    public interface IExportGate
    {
        /// <summary>Completes with true when the export may start (ad watched, entitlement, credit), false when the user declined.</summary>
        Task<bool> RequestAsync(ExportGateRequest request, CancellationToken ct);

        /// <summary>The export run ended (success, failure or cancellation) — for credit consumption / retry credit (RW4, RW5).</summary>
        void OnExportFinished(string projectId, StudioExportResult result);
    }

    /// <summary>Progress of a running export for the progress sheet (spec §3.8 step 3).</summary>
    public readonly struct ExportProgressInfo
    {
        public ExportProgressInfo(string jobId, ExportPhase phase, double fraction, long? remainingMs, int framesDone, int frameCount, bool throttled, bool paused)
        {
            JobId = jobId;
            Phase = phase;
            Fraction = fraction;
            RemainingMs = remainingMs;
            FramesDone = framesDone;
            FrameCount = frameCount;
            Throttled = throttled;
            Paused = paused;
        }

        public string JobId { get; }
        public ExportPhase Phase { get; }
        /// <summary>0..1 for the bar.</summary>
        public double Fraction { get; }
        /// <summary>Estimated remaining time, ms, or null.</summary>
        public long? RemainingMs { get; }
        public int FramesDone { get; }
        public int FrameCount { get; }
        /// <summary>The device is hot and the export pauses between segments (spec §3.8 "пользователь видит подсказку").</summary>
        public bool Throttled { get; }
        /// <summary>Waiting for the app to come back to the foreground.</summary>
        public bool Paused { get; }
    }

    /// <summary>
    /// Renders export frames on the GPU and reads them back (spec §7.5 steps a–e). The Unity implementation is
    /// <see cref="GpuExportFrameRenderer"/>; tests use a CPU stand-in. Main thread.
    /// </summary>
    public interface IExportFrameRenderer : IDisposable
    {
        /// <summary>Opens the clip, the user video decoder and the render targets for <paramref name="job"/>.</summary>
        Task PrepareAsync(ExportJob job, CancellationToken ct);

        /// <summary>Readbacks that may be in flight at once.</summary>
        int MaxInFlight { get; }

        /// <summary>Frames rendered whose data was not released yet.</summary>
        int InFlight { get; }

        /// <summary>
        /// Renders <paramref name="frame"/> and starts its readback. False when it cannot be done now (its video frame is still
        /// being decoded, or no readback slot is free): call again next Unity frame.
        /// </summary>
        bool TryRender(in ExportFrame frame);

        /// <summary>Hint: the next frame to render (starts decoding its video frame).</summary>
        void Prefetch(in ExportFrame next);

        /// <summary>The oldest rendered frame, when its readback finished: RGBA32 top-down bytes, valid until <see cref="ReleaseCompleted"/>.</summary>
        bool TryPeekCompleted(out NativeArray<byte> rgba, out int frameIndex);

        /// <summary>Releases the frame returned by <see cref="TryPeekCompleted"/>.</summary>
        void ReleaseCompleted();

        /// <summary>Drops every frame in flight (segment restart after a pause or an encoder error).</summary>
        void DiscardInFlight();
    }

    /// <summary>Renders the export's PCM mix (spec §7.5 step 3: audio first, one PCM file for the whole export).</summary>
    public interface IExportAudioRenderer
    {
        /// <summary>Writes the mix of <paramref name="job"/> to <paramref name="pcmPath"/> (Media PcmFile format); returns its frame count.</summary>
        Task<long> RenderMixAsync(ExportJob job, string pcmPath, CancellationToken ct);
    }

    /// <summary>Environment of the export loop: frame pacing, clocks, device signals and the app lifecycle.</summary>
    public interface IExportHost
    {
        /// <summary>Completes on the next Unity frame (main thread).</summary>
        Task NextFrame();

        /// <summary>Monotonic milliseconds.</summary>
        long NowMs { get; }

        /// <summary>UTC now (file metadata, history).</summary>
        string NowIso { get; }

        ExportThermalLevel Thermal { get; }

        /// <summary>Free bytes on the app volume; −1 when unknown.</summary>
        long FreeSpaceBytes { get; }

        /// <summary>Device tier for the time estimate.</summary>
        DeviceTier Tier { get; }

        /// <summary>The app is in the background.</summary>
        bool IsPaused { get; }

        /// <summary>Incremented on every pause (a segment that saw one is redone).</summary>
        int PauseCount { get; }

        /// <summary>Keeps the screen on while held (spec §3.8 step 3).</summary>
        IDisposable KeepAwake(string reason);
    }

    /// <summary>An export step failed with a known code.</summary>
    public sealed class ExportFailureException : Exception
    {
        public ExportFailureException(ExportErrorCode code, string message, Exception inner = null)
            : base(message, inner)
        {
            Code = code;
        }

        public ExportErrorCode Code { get; }
    }

    /// <summary>Maps media and file errors to <see cref="ExportErrorCode"/> (spec §7.12 single error catalogue).</summary>
    public static class ExportErrors
    {
        /// <summary>Code of a media error raised while writing, joining or saving the export.</summary>
        public static ExportErrorCode FromMedia(MediaErrorCode code)
        {
            switch (code)
            {
                case MediaErrorCode.InsufficientSpace: return ExportErrorCode.InsufficientSpace;
                case MediaErrorCode.EncoderFailed: return ExportErrorCode.EncoderFailed;
                case MediaErrorCode.MuxerFailed: return ExportErrorCode.MuxerFailed;
                case MediaErrorCode.Timeout: return ExportErrorCode.Timeout;
                case MediaErrorCode.Busy: return ExportErrorCode.Busy;
                case MediaErrorCode.Io:
                case MediaErrorCode.FileNotFound:
                case MediaErrorCode.PermissionDenied:
                    return ExportErrorCode.Io;
                case MediaErrorCode.UnsupportedFormat:
                case MediaErrorCode.DrmProtected:
                case MediaErrorCode.Corrupt:
                case MediaErrorCode.NoVideoTrack:
                case MediaErrorCode.HdrUnsupported:
                case MediaErrorCode.DecoderFailed:
                case MediaErrorCode.Download:
                    return ExportErrorCode.UserVideoFailed;
                default:
                    return ExportErrorCode.Unknown;
            }
        }

        /// <summary>True for encoder-side failures that get one automatic retry of the segment (spec §3.8).</summary>
        public static bool IsRetryableEncoderError(MediaException e)
        {
            return e != null && (e.Code == MediaErrorCode.EncoderFailed || e.Code == MediaErrorCode.MuxerFailed || e.Code == MediaErrorCode.Unknown
                || e.Code == MediaErrorCode.Timeout);
        }

        /// <summary>Disk-full I/O errors (ENOSPC on POSIX, ERROR_DISK_FULL / ERROR_HANDLE_DISK_FULL on Windows).</summary>
        public static bool IsDiskFull(IOException e)
        {
            if (e == null) return false;
            int h = e.HResult & 0xFFFF;
            return h == 28 || h == 0x70 || h == 0x27 || e.Message.IndexOf("space", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
