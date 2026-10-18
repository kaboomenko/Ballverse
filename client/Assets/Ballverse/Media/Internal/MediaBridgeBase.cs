using System;
using System.IO;
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
        protected abstract VideoWriterBase CreateWriter(string outputPath, VideoWriterOptions options);
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
                throw;
            }
            finally
            {
                TryDelete(raw);
            }
        }

        public IVideoWriter OpenWriter(string outputPath, VideoWriterOptions options)
        {
            Enter(nameof(OpenWriter));
            if (string.IsNullOrEmpty(outputPath)) throw new ArgumentNullException(nameof(outputPath));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.AudioPcmPath != null && !File.Exists(options.AudioPcmPath))
                throw new MediaException(MediaErrorCode.FileNotFound, "audio PCM file not found: " + options.AudioPcmPath);
            string dir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            TryDelete(outputPath);
            return CreateWriter(outputPath, options);
        }

        public async Task<VideoWriterResult> FinalizeAsync(IVideoWriter writer, CancellationToken ct)
        {
            Enter(nameof(FinalizeAsync));
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            if (!(writer is VideoWriterBase w)) throw new ArgumentException("writer was not created by this bridge", nameof(writer));
            string path = ResultPathOf(w);
            try
            {
                WriterFinish finish = await w.FinishAsync(ct);
                bool fastStart = await Task.Run(() => FinishFile(path, ct), ct);
                return new VideoWriterResult(path, finish.Frames, finish.DurationUs, fastStart, Backend == MediaBackend.Fake);
            }
            catch
            {
                TryDelete(w.OutputPath);
                throw;
            }
        }

        /// <summary>Where the finished output lives (the MP4 path; the fake backend returns its frame directory).</summary>
        protected virtual string ResultPathOf(VideoWriterBase writer)
        {
            return writer.OutputPath;
        }

        /// <summary>Post-processing on a worker thread; default: ensure moov is at the front. Returns the fast-start flag.</summary>
        protected virtual bool FinishFile(string path, CancellationToken ct)
        {
            if (Mp4FastStart.IsFastStart(path)) return true;
            Mp4FastStart.Apply(path, ct);
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

        static void RequireFile(string path)
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
