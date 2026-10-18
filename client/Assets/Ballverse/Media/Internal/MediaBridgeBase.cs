using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Ballverse.Media.Internal
{
    /// <summary>
    /// Platform-independent part of <see cref="IMediaBridge"/>: thread checks, argument validation, pick → probe
    /// chaining, audio post-processing (downmix/resample to 48 kHz stereo), fast-start post-processing and cleanup.
    /// Platform subclasses implement the Native* primitives.
    /// </summary>
    internal abstract class MediaBridgeBase : IMediaBridge
    {
        protected bool Disposed { get; private set; }

        public abstract MediaBackend Backend { get; }

        /// <summary>Shows the picker and copies the file; returns the copy's path or null if the user cancelled.</summary>
        protected abstract Task<string> PickNativeAsync(string destinationDirectory, string baseName, IProgress<double> progress, CancellationToken ct);
        protected abstract Task<MediaInfo> ProbeNativeAsync(string path, CancellationToken ct);
        protected abstract IFrameSource CreateFrameSource(string path, VideoReaderOptions options);
        /// <summary>Decodes audio to f32le interleaved at the source rate/channels (or already 48 kHz stereo) into <paramref name="rawPath"/>.</summary>
        protected abstract Task<NativeAudioDto> DecodeAudioNativeAsync(string path, string rawPath, AudioDecodeRange range, CancellationToken ct);
        /// <summary>Creates the platform writer; the native encoder writes <paramref name="workPath"/> (unique per writer), never <paramref name="outputPath"/>.</summary>
        protected abstract VideoWriterBase CreateWriter(string outputPath, string workPath, VideoWriterOptions options);
        /// <summary>
        /// Remuxes the segments (validated, in order) into <paramref name="outFile"/> (a work file unique to this call)
        /// with one AAC track from <paramref name="pcmPath"/> (null = silent). Cancellation must stop native work, which
        /// deletes its own output; the caller deletes it once more.
        /// </summary>
        protected abstract Task<WriterFinish> ConcatNativeAsync(IReadOnlyList<string> segments, string pcmPath, string outFile,
            SegmentConcatOptions options, CancellationToken ct);
        protected abstract Task<GallerySaveResult> SaveNativeAsync(string file, string album, CancellationToken ct);
        protected abstract void ShareNative(string file, string caption);
        protected abstract DeviceSignals GetSignalsNative();

        public async Task<PickResult> PickVideoAsync(string destinationDirectory, IProgress<double> progress, CancellationToken ct)
        {
            Enter(nameof(PickVideoAsync));
            if (string.IsNullOrEmpty(destinationDirectory)) throw new ArgumentNullException(nameof(destinationDirectory));
            ct.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destinationDirectory);
            string baseName = "import_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff", System.Globalization.CultureInfo.InvariantCulture);
            string path = await PickNativeAsync(destinationDirectory, baseName, progress, ct);
            if (string.IsNullOrEmpty(path)) return PickResult.Cancelled;
            try
            {
                MediaInfo info = await ProbeNativeAsync(path, ct);
                if (!info.HasVideo) throw new MediaException(MediaErrorCode.NoVideoTrack, "the picked file has no video track");
                return new PickResult(PickStatus.Picked, path, info);
            }
            catch
            {
                TryDelete(path);
                throw;
            }
        }

        public Task<MediaInfo> ProbeAsync(string path, CancellationToken ct)
        {
            Enter(nameof(ProbeAsync));
            RequireFile(path);
            ct.ThrowIfCancellationRequested();
            return ProbeNativeAsync(path, ct);
        }

        public IVideoReader OpenVideoReader(string path, VideoReaderOptions options)
        {
            Enter(nameof(OpenVideoReader));
            RequireFile(path);
            if (options == null) throw new ArgumentNullException(nameof(options));
            return new PrefetchVideoReader(CreateFrameSource(path, options));
        }

        public async Task<IVideoReader> OpenVideoReaderAsync(string path, VideoReaderOptions options, CancellationToken ct)
        {
            IVideoReader reader = OpenVideoReader(path, options);
            try
            {
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (ct.Register(() => cancelled.TrySetResult(true)))
                {
                    Task first = await Task.WhenAny(reader.Ready, cancelled.Task);
                    if (first != reader.Ready) ct.ThrowIfCancellationRequested();
                    await reader.Ready;
                }
                return reader;
            }
            catch
            {
                reader.Dispose();
                throw;
            }
        }

        public async Task<PcmBuffer> DecodeAudioAsync(string path, AudioDecodeRange range, CancellationToken ct)
        {
            Enter(nameof(DecodeAudioAsync));
            string tmp = Path.Combine(Application.temporaryCachePath, "decode_" + Guid.NewGuid().ToString("N") + ".f32");
            try
            {
                PcmFileInfo file = await DecodeAudioToFileAsync(path, tmp, range, ct);
                if (file.FrameCount == 0) return PcmBuffer.Empty48kStereo();
                return await Task.Run(() => PcmFile.Read(tmp), ct);
            }
            finally
            {
                TryDelete(tmp);
            }
        }

        public async Task<PcmFileInfo> DecodeAudioToFileAsync(string path, string outputPcmPath, AudioDecodeRange range, CancellationToken ct)
        {
            Enter(nameof(DecodeAudioToFileAsync));
            RequireFile(path);
            if (string.IsNullOrEmpty(outputPcmPath)) throw new ArgumentNullException(nameof(outputPcmPath));
            ct.ThrowIfCancellationRequested();
            string dir = Path.GetDirectoryName(outputPcmPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string raw = outputPcmPath + ".native";
            try
            {
                NativeAudioDto dto = await DecodeAudioNativeAsync(path, raw, range, ct);
                long frames = await Task.Run(() =>
                {
                    if (dto.frames <= 0 || dto.sampleRate <= 0 || dto.channels <= 0 || !File.Exists(raw))
                    {
                        PcmFile.Write(outputPcmPath, Array.Empty<float>(), 0, 0);
                        return 0L;
                    }
                    if (dto.sampleRate == PcmFile.SampleRate && dto.channels == PcmFile.Channels)
                    {
                        if (File.Exists(outputPcmPath)) File.Delete(outputPcmPath);
                        File.Move(raw, outputPcmPath);
                        return PcmFile.CountFrames(outputPcmPath);
                    }
                    return PcmConverter.ConvertFile(raw, dto.sampleRate, dto.channels, outputPcmPath, ct);
                }, ct);
                return new PcmFileInfo(outputPcmPath, frames);
            }
            catch
            {
                TryDelete(outputPcmPath);
                TryDelete(outputPcmPath + PcmFile.TempSuffix);
                throw;
            }
            finally
            {
                TryDelete(raw);
            }
        }

        // Writers and joins of the whole process (all bridges) that have not settled yet; main thread only. Weak, so that
        // a leaked (never disposed) writer can still be collected and released by its finalizer.
        static readonly List<WeakReference<VideoWriterBase>> s_writers = new List<WeakReference<VideoWriterBase>>();
        static readonly List<string> s_concatOutputs = new List<string>();

        public IVideoWriter OpenWriter(string outputPath, VideoWriterOptions options)
        {
            Enter(nameof(OpenWriter));
            ValidateWriterArgs(outputPath, options);
            // Never blocks: the native release completes through the main thread, so waiting here would deadlock.
            if (FindUnsettledWriter(outputPath) != null || IsConcatRunning(outputPath))
                throw new InvalidOperationException("a writer or join for " + outputPath + " is still open, finalizing or releasing; "
                    + "dispose it and await IVideoWriter.ReleaseAsync() (or use OpenWriterAsync) before opening it again");
            return OpenWriterCore(outputPath, options);
        }

        /// <summary>
        /// Completes once a writer for <paramref name="outputPath"/> may be opened: every earlier writer for that path has
        /// settled and every writer that is being released (any path) has stopped (frees the hardware encoder). Main
        /// thread; resumes on it. Backs <see cref="MediaBridgeExtensions.OpenWriterAsync"/>.
        /// </summary>
        /// <exception cref="InvalidOperationException">A writer for the same path is still open (never disposed), or a
        /// <see cref="IMediaBridge.ConcatSegmentsAsync"/> is writing it: waiting would not end.</exception>
        internal static async Task WaitUntilWriterCanOpenAsync(string outputPath, CancellationToken ct)
        {
            while (true)
            {
                MainThread.Assert(nameof(MediaBridgeExtensions.OpenWriterAsync));
                ct.ThrowIfCancellationRequested();
                var pending = new List<Task>();
                string full = Path.GetFullPath(outputPath);
                foreach (VideoWriterBase w in UnsettledWriters())
                {
                    bool samePath = SamePath(w.OutputPath, full);
                    if (w.IsOpen)
                    {
                        if (samePath) throw new InvalidOperationException("a writer for " + outputPath + " is still open; dispose it first");
                        continue; // another live writer: allowed, not waited for
                    }
                    // Released or finalizing: wait for the same path (its output) and for every release in progress (its encoder).
                    if (samePath || !w.IsFinalizing) pending.Add(w.Settled);
                }
                if (pending.Count == 0 && !IsConcatRunning(outputPath)) return;
                if (pending.Count == 0) throw new InvalidOperationException("ConcatSegmentsAsync is writing " + outputPath);
                await WhenAllOrCancelled(pending, ct); // resumes on the main thread; re-check, new releases may have started
            }
        }

        static void ValidateWriterArgs(string outputPath, VideoWriterOptions options)
        {
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentNullException(nameof(outputPath));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.AudioPcmPath != null && !File.Exists(options.AudioPcmPath))
                throw new MediaException(MediaErrorCode.FileNotFound, "audio PCM file not found: " + options.AudioPcmPath);
        }

        IVideoWriter OpenWriterCore(string outputPath, VideoWriterOptions options)
        {
            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            // No writer or join of this process targets the path (checked by the caller): leftovers are a crashed
            // run's work files or the previous output, which this writer replaces.
            DeleteOrphanWorkFiles(outputPath);
            DeleteOutput(ResultPathFor(outputPath));
            VideoWriterBase writer = CreateWriter(outputPath, NewWorkPath(outputPath), options);
            s_writers.Add(new WeakReference<VideoWriterBase>(writer));
            return writer;
        }

        static async Task WhenAllOrCancelled(List<Task> tasks, CancellationToken ct)
        {
            Task all = Task.WhenAll(tasks);
            if (!ct.CanBeCanceled)
            {
                await all;
                return;
            }
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => cancelled.TrySetResult(true)))
            {
                await Task.WhenAny(all, cancelled.Task);
            }
            ct.ThrowIfCancellationRequested();
        }

        static List<VideoWriterBase> UnsettledWriters()
        {
            var live = new List<VideoWriterBase>();
            s_writers.RemoveAll(r => !r.TryGetTarget(out VideoWriterBase w) || w.Settled.IsCompleted);
            foreach (WeakReference<VideoWriterBase> r in s_writers)
            {
                if (r.TryGetTarget(out VideoWriterBase w)) live.Add(w);
            }
            return live;
        }

        static VideoWriterBase FindUnsettledWriter(string outputPath)
        {
            string full = Path.GetFullPath(outputPath);
            foreach (VideoWriterBase w in UnsettledWriters())
            {
                if (SamePath(w.OutputPath, full)) return w;
            }
            return null;
        }

        static bool IsConcatRunning(string outFile)
        {
            string full = Path.GetFullPath(outFile);
            foreach (string o in s_concatOutputs)
            {
                if (SamePath(o, full)) return true;
            }
            return false;
        }

        static bool SamePath(string path, string fullPath)
        {
            return string.Equals(Path.GetFullPath(path), fullPath, StringComparison.Ordinal);
        }

        /// <summary>Marker between an output file name and the random id of its work files.</summary>
        internal const string WorkInfix = ".bv";
        /// <summary>Ending of every work file (keeps an .mp4 extension for AVAssetWriter / MediaMuxer).</summary>
        internal const string WorkSuffix = ".part.mp4";

        /// <summary>A new work file path next to <paramref name="outputPath"/>, unique to one writer or join.</summary>
        internal static string NewWorkPath(string outputPath)
        {
            return outputPath + WorkInfix + Guid.NewGuid().ToString("N").Substring(0, 16) + WorkSuffix;
        }

        /// <summary>
        /// Deletes work files (and their fast-start temps / fake frame directories) of <paramref name="outputPath"/> left
        /// by a run that crashed. Only called when no writer or join of this process targets the path.
        /// </summary>
        void DeleteOrphanWorkFiles(string outputPath)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;
            string pattern = Path.GetFileName(outputPath) + WorkInfix + "*" + WorkSuffix + "*";
            string[] entries;
            try
            {
                entries = Directory.GetFileSystemEntries(dir, pattern);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Media] could not list " + dir + ": " + e.Message);
                return;
            }
            foreach (string entry in entries)
            {
                if (Directory.Exists(entry)) DeleteDirectory(entry);
                else TryDelete(entry);
            }
        }

        public async Task<VideoWriterResult> FinalizeAsync(IVideoWriter writer, CancellationToken ct)
        {
            Enter(nameof(FinalizeAsync));
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (!(writer is VideoWriterBase w)) throw new ArgumentException("writer was not created by this bridge", nameof(writer));
            // Preconditions are checked BEFORE the delete-on-failure region below:
            // - already finished, disposed or finalizing: the output belongs to the earlier call (a finished export, or
            //   one still being written) — throw without touching it;
            w.ThrowIfFinishedOrFinalizing();
            // - pre-cancelled token: never start the native finish; abort the writer (the native side deletes its work
            //   file) and complete only once it has stopped;
            // - sticky encoder error or no frames: the writer can never finish — release it now (frees the hardware
            //   encoder for the §3.8 segment retry; the native abort deletes the work file) and report why once released.
            if (ct.IsCancellationRequested)
            {
                await w.ReleaseAsync();
                ct.ThrowIfCancellationRequested();
            }
            Exception unfinishable = w.UnfinishableReason();
            if (unfinishable != null)
            {
                await w.ReleaseAsync();
                ExceptionDispatchInfo.Capture(unfinishable).Throw();
            }
            // From here on this call owns the work output: any failure or cancellation deletes it. The output path is
            // written only by the final rename.
            string work = ResultPathFor(w.WorkPath);
            string path = ResultPathFor(w.OutputPath);
            w.BeginCommit();
            try
            {
                WriterFinish finish = await w.FinishAsync(ct); // completes after the native release
                Mp4Metadata metadata = w.Options.Metadata;
                bool fastStart = w.Options.FastStart && await Task.Run(() => FinishFile(work, metadata, ct), ct);
                ct.ThrowIfCancellationRequested();
                CommitOutput(work, path);
                return new VideoWriterResult(path, finish.Frames, finish.DurationUs, fastStart, Backend == MediaBackend.Fake);
            }
            catch
            {
                DeleteOutput(work);
                throw;
            }
            finally
            {
                w.EndCommit();
            }
        }

        public async Task<VideoWriterResult> ConcatSegmentsAsync(IReadOnlyList<string> segments, string pcmPath, string outFile,
            SegmentConcatOptions options, CancellationToken ct)
        {
            Enter(nameof(ConcatSegmentsAsync));
            if (segments == null) throw new ArgumentNullException(nameof(segments));
            if (segments.Count == 0) throw new ArgumentException("no segments", nameof(segments));
            if (string.IsNullOrEmpty(outFile)) throw new ArgumentNullException(nameof(outFile));
            if (options == null) throw new ArgumentNullException(nameof(options));
            var list = new List<string>(segments.Count);
            foreach (string segment in segments)
            {
                if (string.IsNullOrEmpty(segment)) throw new ArgumentException("null or empty segment path", nameof(segments));
                if (segment.IndexOf('\n') >= 0 || segment.IndexOf('\r') >= 0) throw new ArgumentException("segment path contains a line break: " + segment, nameof(segments));
                if (string.Equals(Path.GetFullPath(segment), Path.GetFullPath(outFile), StringComparison.Ordinal))
                    throw new ArgumentException("the output must not be one of the segments", nameof(outFile));
                RequireSegment(segment);
                list.Add(segment);
            }
            if (!string.IsNullOrEmpty(pcmPath) && !File.Exists(pcmPath))
                throw new MediaException(MediaErrorCode.FileNotFound, "audio PCM file not found: " + pcmPath);
            if (FindUnsettledWriter(outFile) != null || IsConcatRunning(outFile))
                throw new InvalidOperationException("a writer or join for " + outFile + " is still running");
            string pcm = PcmFile.CountFrames(pcmPath) > 0 ? pcmPath : null;
            ct.ThrowIfCancellationRequested();
            string dir = Path.GetDirectoryName(outFile);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            DeleteOrphanWorkFiles(outFile);
            string path = ResultPathFor(outFile);
            DeleteOutput(path);
            // The native join writes a work file unique to this call; a cancelled join that is still stopping in the
            // background can therefore never delete or overwrite the output of a retry.
            string workFile = NewWorkPath(outFile);
            string work = ResultPathFor(workFile);
            string key = Path.GetFullPath(outFile);
            s_concatOutputs.Add(key);
            try
            {
                WriterFinish finish = await ConcatNativeAsync(list, pcm, workFile, options, ct);
                if (options.DeleteSegmentsBeforeFastStart)
                {
                    // Free ≈ 1× the export size before the rewrite's temp copy (peak ≈ 2E instead of 3E).
                    foreach (string segment in list) DeleteOutput(segment);
                }
                Mp4Metadata metadata = options.Metadata;
                bool fastStart = await Task.Run(() => FinishFile(work, metadata, ct), ct);
                ct.ThrowIfCancellationRequested();
                CommitOutput(work, path);
                return new VideoWriterResult(path, finish.Frames, finish.DurationUs, fastStart, Backend == MediaBackend.Fake);
            }
            catch
            {
                DeleteOutput(work);
                throw;
            }
            finally
            {
                s_concatOutputs.Remove(key);
            }
        }

        /// <summary>
        /// Where the output of an MP4 path lives: the path itself; the fake backend returns its frame directory
        /// (<c>path + ".frames"</c>). Used for both the output path and the work path.
        /// </summary>
        protected virtual string ResultPathFor(string mp4Path)
        {
            return mp4Path;
        }

        /// <summary>
        /// Moves the finished work output to the output path (replacing it). Main thread, after the native side has
        /// stopped. Default: file delete + rename (same directory, so the rename is atomic on the device file systems).
        /// </summary>
        protected virtual void CommitOutput(string workResult, string resultPath)
        {
            try
            {
                PcmFile.ReplaceWith(workResult, resultPath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new MediaException(MediaErrorCode.Io, "cannot move the finished file to " + resultPath + ": " + e.Message, e);
            }
        }

        /// <summary>Checks that a segment exists (an MP4 file; the fake backend: a frame directory).</summary>
        protected virtual void RequireSegment(string path)
        {
            RequireFile(path);
        }

        /// <summary>Deletes a failed/cancelled output and its post-processing temp file (worker or main thread).</summary>
        protected virtual void DeleteOutput(string path)
        {
            TryDelete(path);
            TryDelete(path + Mp4FastStart.TempSuffix);
        }

        /// <summary>True when the platform writer stores <see cref="Mp4Metadata"/> itself (iOS AVAssetWriter).</summary>
        protected virtual bool WritesMetadataNatively => false;

        /// <summary>
        /// Post-processing on a worker thread; default: move moov to the front and add the metadata box unless the
        /// native writer stored it. Returns the fast-start flag.
        /// </summary>
        protected virtual bool FinishFile(string path, Mp4Metadata metadata, CancellationToken ct)
        {
            Mp4FastStart.Apply(path, WritesMetadataNatively ? null : metadata, ct);
            return true;
        }

        public Task<GallerySaveResult> SaveToGalleryAsync(string file, string album, CancellationToken ct)
        {
            Enter(nameof(SaveToGalleryAsync));
            RequireFile(file);
            ct.ThrowIfCancellationRequested();
            return SaveNativeAsync(file, SanitizeAlbum(album), ct);
        }

        public void Share(string file, string caption)
        {
            Enter(nameof(Share));
            RequireFile(file);
            ShareNative(file, caption ?? string.Empty);
        }

        public DeviceSignals Signals
        {
            get
            {
                Enter(nameof(Signals));
                return GetSignalsNative();
            }
        }

        public virtual void Dispose()
        {
            Disposed = true;
        }

        protected void Enter(string api)
        {
            MainThread.Assert(api);
            if (Disposed) throw new ObjectDisposedException(nameof(IMediaBridge));
        }

        protected static void RequireFile(string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            if (!File.Exists(path)) throw new MediaException(MediaErrorCode.FileNotFound, "file not found: " + path);
        }

        /// <summary>Album/folder name without path separators or control characters; "Ballverse" when empty.</summary>
        internal static string SanitizeAlbum(string album)
        {
            if (string.IsNullOrWhiteSpace(album)) return "Ballverse";
            var chars = album.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                if (c == '/' || c == '\\' || c == ':' || c < ' ') chars[i] = '_';
            }
            string s = new string(chars);
            return s == "." || s == ".." ? "Ballverse" : s;
        }

        internal static void DeleteDirectory(string dir)
        {
            try
            {
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Media] could not delete " + dir + ": " + e.Message);
            }
        }

        internal static void TryDelete(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Media] could not delete " + path + ": " + e.Message);
            }
        }
    }
}
