using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ballverse.Media
{
    /// <summary>
    /// Sequential video frame reader (spec §7.5 step b). Frames are RGBA32, top-down, tightly packed
    /// (row bytes = width × 4), rotation applied, SDR.
    /// </summary>
    /// <remarks>
    /// <para><b>Frame selection.</b> A request for time <c>t</c> returns the last source frame whose pts ≤ t (the frame
    /// on screen at t; works for VFR). Before the first frame the first frame is returned; after the last frame the
    /// last frame. Reading forward is cheap; a backward jump or a jump of more than a few seconds seeks to the previous
    /// keyframe and decodes forward.</para>
    /// <para><b>Threading.</b> Call from the Unity main thread. Decoding and colour conversion run on a dedicated worker
    /// thread; <see cref="TryReadFrameAt"/> never blocks.</para>
    /// <para><b>Memory.</b> Frames live in a pool of <see cref="MaxFramesInFlight"/> (3) native buffers per reader.
    /// A returned <see cref="VideoFrame"/> holds one buffer until it is disposed: dispose every frame as soon as its data
    /// is uploaded/copied, otherwise the worker stalls once all 3 buffers are held. Ready frames for times earlier than
    /// a newer request are dropped automatically (forward reading), and a backward request drops all prefetched frames.</para>
    /// </remarks>
    public interface IVideoReader : IDisposable
    {
        /// <summary>Completes when the decoder is open (faults with <see cref="MediaException"/> if it cannot open).</summary>
        Task Ready { get; }

        /// <summary>True once <see cref="Ready"/> completed successfully.</summary>
        bool IsOpen { get; }

        /// <summary>Output frame width in pixels (after rotation and scaling); 0 until open.</summary>
        int Width { get; }

        /// <summary>Output frame height in pixels; 0 until open.</summary>
        int Height { get; }

        /// <summary>True when the source is HDR and frames are tone-mapped to SDR (by the platform or the built-in fallback).</summary>
        bool ToneMappedToSdr { get; }

        /// <summary>Source duration, µs (0 until open).</summary>
        long DurationUs { get; }

        /// <summary>
        /// Non-blocking read. Returns true and a frame if the frame for <paramref name="ptsUs"/> is decoded; otherwise
        /// queues the request (if not queued yet) and returns false — call again next frame.
        /// </summary>
        /// <exception cref="MediaException">The decoder failed (sticky: every later call throws the same error).</exception>
        bool TryReadFrameAt(long ptsUs, out VideoFrame frame);

        /// <summary>Queues a decode request ahead of time (e.g. the next export frame) so that a later read is instant.</summary>
        void Prefetch(long ptsUs);

        /// <summary>
        /// Blocking read for offline export: waits up to <paramref name="timeoutMs"/> for the frame.
        /// Blocks the calling (main) thread; use it only in the export loop.
        /// </summary>
        /// <exception cref="MediaException">Decoder failure or <see cref="MediaErrorCode.Timeout"/> (often: frames not disposed).</exception>
        VideoFrame ReadFrameAt(long ptsUs, int timeoutMs);

        /// <summary>Asynchronous read; completes on the main thread.</summary>
        Task<VideoFrame> ReadFrameAtAsync(long ptsUs, CancellationToken ct);

        /// <summary>Upper bound of frames that can exist at once per reader (ready + held by the caller + being decoded).</summary>
        int MaxFramesInFlight { get; }
    }
}
