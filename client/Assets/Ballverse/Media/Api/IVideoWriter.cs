using System;
using System.Threading.Tasks;
using Unity.Collections;

namespace Ballverse.Media
{
    /// <summary>
    /// H.264 (High, Baseline fallback) + AAC-LC 48 kHz encoder writing one MP4 file (spec §3.8, §7.5 step e).
    /// Created by <see cref="IMediaBridge.OpenWriter"/>, finished by <see cref="IMediaBridge.FinalizeAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Input.</b> Frames are RGBA32, top-down, tightly packed (exactly <c>Width × Height × 4</c> bytes, row bytes =
    /// width × 4), opaque. The bytes are copied before the append call returns, so the caller may reuse its buffer at
    /// once (e.g. an <c>AsyncGPUReadback</c> NativeArray). iOS swizzles RGBA→BGRA natively; Android uploads RGBA to a GL
    /// texture and the encoder's input surface converts to YUV.</para>
    /// <para><b>Timestamps.</b> <c>ptsUs</c> must start at ≥ 0 and strictly increase; use the export plan's frame times
    /// (CFR). The file's video duration ends at last pts + 1 / fps.</para>
    /// <para><b>Back-pressure.</b> At most <see cref="MaxFramesInFlight"/> (3) frames are queued to the encoder.
    /// <see cref="TryAppendVideoFrame(NativeArray{byte}, long)"/> returns false when the queue is full;
    /// <see cref="AppendVideoFrame(NativeArray{byte}, long)"/> blocks until a slot frees (up to
    /// <see cref="VideoWriterOptions.AppendTimeoutMs"/>).</para>
    /// <para><b>Threading.</b> Main thread only. Encoding, audio encoding and muxing run on native threads.</para>
    /// <para><b>Files.</b> The encoder writes a temp file private to this writer next to <see cref="OutputPath"/>
    /// (<c>&lt;output&gt;.bv&lt;id&gt;.part.mp4</c>); only a successful <see cref="IMediaBridge.FinalizeAsync"/> renames it to
    /// <see cref="OutputPath"/>. Disposing, cancelling or failing deletes only that temp file, so a late native cleanup can
    /// never delete the output of a newer writer for the same path.</para>
    /// <para><b>Release.</b> <see cref="IDisposable.Dispose"/> returns at once and stops the native encoder in the
    /// background; <see cref="Released"/> / <see cref="ReleaseAsync"/> complete once the native side has stopped, released
    /// the hardware encoder and deleted its temp file. Until then <see cref="IMediaBridge.OpenWriter"/> refuses the same
    /// output path; <see cref="MediaBridgeExtensions.OpenWriterAsync"/> waits for it. A redo/retry (§3.8 segment redo) should
    /// therefore <c>await writer.ReleaseAsync()</c> (or use <c>OpenWriterAsync</c>) before opening the next writer.</para>
    /// </remarks>
    public interface IVideoWriter : IDisposable
    {
        VideoWriterOptions Options { get; }

        /// <summary>Destination MP4 path. Created only by a successful <see cref="IMediaBridge.FinalizeAsync"/> (rename of the writer's temp file).</summary>
        string OutputPath { get; }

        /// <summary>Frames queued but not yet consumed by the encoder.</summary>
        int FramesInFlight { get; }

        int MaxFramesInFlight { get; }

        /// <summary>Frames accepted so far.</summary>
        int FramesAppended { get; }

        /// <summary>True after <see cref="IMediaBridge.FinalizeAsync"/> started; appends then throw.</summary>
        bool IsFinalizing { get; }

        /// <summary>
        /// Completes (on the main thread, never faulted) once this writer's native side has stopped: the encoder thread /
        /// queue has finished, the codec is released and an unfinished temp file is deleted. Pending until the writer is
        /// disposed or finalized.
        /// </summary>
        Task Released { get; }

        /// <summary>Disposes the writer (aborting it if unfinished) and returns <see cref="Released"/>.</summary>
        Task ReleaseAsync();

        /// <summary>Copies the frame to the encoder queue; false when 3 frames are already in flight.</summary>
        /// <exception cref="MediaException">The encoder failed earlier (sticky).</exception>
        /// <exception cref="ArgumentException">Wrong buffer size or non-increasing pts.</exception>
        bool TryAppendVideoFrame(NativeArray<byte> rgba, long ptsUs);

        /// <inheritdoc cref="TryAppendVideoFrame(NativeArray{byte}, long)"/>
        bool TryAppendVideoFrame(byte[] rgba, long ptsUs);

        /// <summary>Copies the frame to the encoder queue, waiting for a free slot.</summary>
        /// <exception cref="MediaException"><see cref="MediaErrorCode.Timeout"/> when no slot freed in time, or an encoder failure.</exception>
        void AppendVideoFrame(NativeArray<byte> rgba, long ptsUs);

        /// <inheritdoc cref="AppendVideoFrame(NativeArray{byte}, long)"/>
        void AppendVideoFrame(byte[] rgba, long ptsUs);
    }
}
