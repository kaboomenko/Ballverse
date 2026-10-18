using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Ballverse.Media
{
    /// <summary>
    /// Platform media services for the studio (spec §3.2, §3.8, §7.5): video import, probing, decoding, encoding,
    /// saving to the gallery, sharing and device signals. Create with <see cref="MediaBridgeFactory.Create"/>.
    /// </summary>
    /// <remarks>
    /// <para><b>Threading.</b> Every member must be called from the Unity main thread (checked; violations throw
    /// <see cref="InvalidOperationException"/>). Native completions arrive on platform threads and are marshalled back
    /// to the main thread through the Unity synchronization context, so every returned task completes — and every
    /// <c>await</c> resumes — on the main thread. Progress callbacks are also invoked on the main thread.</para>
    /// <para><b>Cancellation.</b> Cancelling a token completes the task as cancelled immediately and asks the native side
    /// to stop; partial output of the cancelled operation is deleted (natively, and by the C# side for its own
    /// post-processing temp files such as <c>*.faststart.tmp</c> / <c>*.tmp</c>). A token that is already cancelled when
    /// a method is called never starts the native operation. Exception: once the system accepted a gallery save it
    /// cannot be undone.</para>
    /// <para><b>Errors.</b> Failures surface as <see cref="MediaException"/> with a <see cref="MediaErrorCode"/>; expected
    /// user outcomes (picker dismissed, gallery permission denied) are results, not exceptions.</para>
    /// <para><b>Native handles.</b> Readers and writers own native decoders/encoders: dispose them. A finalizer releases
    /// a leaked one on the main thread, but codec instances are a scarce system resource, so do not rely on it.</para>
    /// </remarks>
    public interface IMediaBridge : IDisposable
    {
        /// <summary>Which implementation is running. <see cref="MediaBackend.Fake"/> produces synthetic media.</summary>
        MediaBackend Backend { get; }

        /// <summary>
        /// Opens the system video picker (no library permission needed) and copies the chosen file into
        /// <paramref name="destinationDirectory"/> inside the app sandbox (downloading it from iCloud first if needed),
        /// then probes it. The copy keeps the original container; its extension follows the source type.
        /// </summary>
        /// <param name="destinationDirectory">Existing or creatable directory in the app sandbox (e.g. the project folder).</param>
        /// <param name="progress">Optional copy/download progress 0..1, invoked on the main thread.</param>
        /// <returns><see cref="PickStatus.Cancelled"/> when the user dismissed the picker.</returns>
        /// <exception cref="MediaException">Copy failed, not enough space, or the file cannot be decoded (the copy is deleted).</exception>
        Task<PickResult> PickVideoAsync(string destinationDirectory, IProgress<double> progress, CancellationToken ct);

        /// <summary>Reads duration, size, frame rate, rotation, HDR, codecs and audio presence of a local file.</summary>
        /// <exception cref="MediaException">Missing file, unsupported codec, DRM, corrupt file.</exception>
        Task<MediaInfo> ProbeAsync(string path, CancellationToken ct);

        /// <summary>
        /// Opens a sequential frame reader that outputs top-down RGBA32 frames with rotation applied, scaled to fit
        /// <see cref="VideoReaderOptions.MaxWidth"/> × <see cref="VideoReaderOptions.MaxHeight"/>, HDR tone-mapped to SDR
        /// BT.709 when possible. Returns at once; the native decoder opens on the reader's worker thread
        /// (await <see cref="IVideoReader.Ready"/> or use <see cref="OpenVideoReaderAsync"/>).
        /// </summary>
        IVideoReader OpenVideoReader(string path, VideoReaderOptions options);

        /// <summary>Like <see cref="OpenVideoReader"/> but completes once the decoder is open and the output size is known.</summary>
        Task<IVideoReader> OpenVideoReaderAsync(string path, VideoReaderOptions options, CancellationToken ct);

        /// <summary>
        /// Decodes the audio track (or a range of it) to 48 kHz stereo float PCM in memory. A file without audio gives
        /// an empty buffer. For long ranges prefer <see cref="DecodeAudioToFileAsync"/> (≈ 23 MB per minute in memory).
        /// </summary>
        Task<PcmBuffer> DecodeAudioAsync(string path, AudioDecodeRange range, CancellationToken ct);

        /// <summary>Decodes audio to a raw PCM file in the <see cref="PcmFile"/> format (f32le, interleaved, 48 kHz, stereo).</summary>
        Task<PcmFileInfo> DecodeAudioToFileAsync(string path, string outputPcmPath, AudioDecodeRange range, CancellationToken ct);

        /// <summary>
        /// Starts an H.264 + AAC MP4 writer at <paramref name="outputPath"/>. Append frames in increasing pts order, then
        /// call <see cref="FinalizeAsync"/>. Disposing an unfinalized writer cancels it and deletes the partial file.
        /// </summary>
        /// <exception cref="MediaException">No suitable encoder, cannot create the file.</exception>
        IVideoWriter OpenWriter(string outputPath, VideoWriterOptions options);

        /// <summary>
        /// Flushes the encoders, writes the remaining audio, closes the MP4 (index <c>moov</c> at the front unless
        /// <see cref="VideoWriterOptions.FastStart"/> is false) and disposes the writer. On failure or cancellation the
        /// partial file is deleted and the writer is disposed.
        /// </summary>
        /// <exception cref="InvalidOperationException">FinalizeAsync was already called for this writer (finished or still
        /// running); the output of that call is not touched.</exception>
        /// <exception cref="ObjectDisposedException">The writer was disposed (its partial file was deleted then).</exception>
        /// <exception cref="MediaException">The encoder failed earlier (sticky error) or fails while finishing: the writer
        /// is disposed (the hardware encoder is free for a retry) and the partial file deleted. A writer without frames
        /// is disposed the same way and fails with <see cref="InvalidOperationException"/>.</exception>
        Task<VideoWriterResult> FinalizeAsync(IVideoWriter writer, CancellationToken ct);

        /// <summary>
        /// Segmented export (spec §3.8, §7.5): joins finished video-only segment files without re-encoding and encodes
        /// the PCM mix into ONE AAC-LC track (no priming clicks between segments), producing a single MP4 with
        /// <c>moov</c> at the front and the given metadata. Android: MediaExtractor + MediaMuxer remux + MediaCodec AAC;
        /// iOS: AVAssetReader passthrough → AVAssetWriter passthrough video + AAC input.
        /// </summary>
        /// <param name="segments">
        /// <see cref="VideoWriterResult.Path"/> of each segment, in order. Each segment is one writer's output
        /// (<see cref="VideoWriterOptions.AudioPcmPath"/> = null), starts with an IDR frame and has the same size, fps and
        /// encoder settings as the others (checked: different codec configurations fail with
        /// <see cref="MediaErrorCode.InvalidState"/>). Timing rule: <see cref="SegmentConcatOptions"/>. The segment files
        /// are not deleted.
        /// </param>
        /// <param name="pcmPath">The mix in the <see cref="PcmFile"/> format, or null / empty file for a silent video. Audio past the video end is trimmed.</param>
        /// <param name="outFile">Destination MP4 (replaced). Deleted again on failure or cancellation.</param>
        /// <returns>Frame count (sum of the segments' frames) and duration (frames / fps).</returns>
        /// <remarks>Spec §7.5 lists <c>Task&lt;string&gt; ConcatSegmentsAsync(segments, pcmPath, outFile, ct)</c>; this
        /// version also takes the fps (for exact segment offsets), audio bitrate and metadata, and returns the frame count
        /// for the §3.8 acceptance check. The fake backend joins PNG-sequence segment directories.</remarks>
        Task<VideoWriterResult> ConcatSegmentsAsync(IReadOnlyList<string> segments, string pcmPath, string outFile,
            SegmentConcatOptions options, CancellationToken ct);

        /// <summary>
        /// Saves a finished video to the device gallery: Android 10+ MediaStore <c>Movies/&lt;album&gt;</c> without
        /// permissions; Android 8–9 public Movies folder with WRITE_EXTERNAL_STORAGE (requested here); iOS Photos with
        /// add-only access (see <see cref="GallerySaveResult.AddedToAlbum"/> for the album caveat).
        /// </summary>
        Task<GallerySaveResult> SaveToGalleryAsync(string file, string album, CancellationToken ct);

        /// <summary>Opens the system share sheet with the file and an optional caption text.</summary>
        void Share(string file, string caption);

        /// <summary>Fresh snapshot of thermal state, low power, accessibility flags and free space (cheap; poll at most a few times per second).</summary>
        DeviceSignals Signals { get; }
    }
}
