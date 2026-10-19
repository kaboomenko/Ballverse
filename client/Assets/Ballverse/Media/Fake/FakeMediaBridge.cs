using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Media.Internal;
using UnityEngine;

namespace Ballverse.Media.Fake
{
    /// <summary>Descriptor of a synthetic video (".bvtestpattern" JSON file) produced by the fake picker.</summary>
    [Serializable]
    internal sealed class TestPatternSpec
    {
        public int width = 1920;
        public int height = 1080;
        public int rotation = 90;
        public int fps = 30;
        public double durationSeconds = 15;
        public bool hasAudio = true;

        public long DurationUs => (long)Math.Round(durationSeconds * 1000000.0);
        public int FrameCount => Math.Max(1, (int)Math.Floor(durationSeconds * fps));
    }

    /// <summary>
    /// FAKE <see cref="IMediaBridge"/> for the Unity editor and desktop players — lets the studio and export pipeline run
    /// without a device. Produces synthetic media only (see <see cref="FakeMediaOptions"/>); it is NOT a video codec.
    /// Threading and lifetime rules are the same as on devices (main thread API, worker-thread decoding, 3-frame pools).
    /// </summary>
    internal sealed class FakeMediaBridge : MediaBridgeBase
    {
        public const string DescriptorExtension = ".bvtestpattern";

        readonly FakeMediaOptions _options;

        public FakeMediaBridge(FakeMediaOptions options)
        {
            _options = options;
            Debug.LogWarning("[Media] FAKE media backend active: synthetic test-pattern video, PNG-sequence output (editor/desktop only).");
        }

        public override MediaBackend Backend => MediaBackend.Fake;

        protected override async Task<string> PickNativeAsync(string destinationDirectory, string baseName, IProgress<double> progress, CancellationToken ct)
        {
            var spec = new TestPatternSpec
            {
                width = _options.PatternWidth,
                height = _options.PatternHeight,
                rotation = MediaInfo.NormalizeRotation(_options.PatternRotation),
                fps = Math.Max(1, _options.PatternFps),
                durationSeconds = Math.Max(0.1, _options.PatternDurationSeconds),
                hasAudio = _options.PatternHasAudio,
            };
            string path = Path.Combine(destinationDirectory, baseName + DescriptorExtension);
            progress?.Report(0.5);
            await Task.Run(() => File.WriteAllText(path, JsonUtility.ToJson(spec, true)), ct);
            progress?.Report(1.0);
            return path;
        }

        internal static TestPatternSpec ReadSpec(string path)
        {
            if (path.EndsWith(DescriptorExtension, StringComparison.OrdinalIgnoreCase))
            {
                TestPatternSpec spec = JsonUtility.FromJson<TestPatternSpec>(File.ReadAllText(path));
                if (spec == null || spec.width < 16 || spec.height < 16 || spec.fps <= 0 || spec.durationSeconds <= 0)
                    throw new MediaException(MediaErrorCode.Corrupt, "invalid test-pattern descriptor: " + path);
                spec.rotation = MediaInfo.NormalizeRotation(spec.rotation);
                return spec;
            }
            // Any other file: the fake cannot decode it; stand in with a portrait 1080x1920 30 fps 10 s pattern.
            Debug.LogWarning("[Media] FAKE backend cannot decode " + Path.GetFileName(path) + "; using a test pattern instead.");
            return new TestPatternSpec { width = 1080, height = 1920, rotation = 0, fps = 30, durationSeconds = 10, hasAudio = true };
        }

        protected override async Task<MediaInfo> ProbeNativeAsync(string path, CancellationToken ct)
        {
            TestPatternSpec spec = await Task.Run(() => ReadSpec(path), ct);
            long size = new FileInfo(path).Length;
            return new MediaInfo(path, size, spec.DurationUs, true, spec.width, spec.height, spec.rotation, spec.fps,
                "fake/testpattern", HdrTransfer.Sdr, 8, spec.hasAudio, spec.hasAudio ? "fake/tone" : string.Empty,
                spec.hasAudio ? PcmFile.SampleRate : 0, spec.hasAudio ? PcmFile.Channels : 0);
        }

        protected override IFrameSource CreateFrameSource(string path, VideoReaderOptions options)
        {
            return new FakeFrameSource(path, options);
        }

        protected override Task<NativeAudioDto> DecodeAudioNativeAsync(string path, string rawPath, AudioDecodeRange range, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                TestPatternSpec spec = ReadSpec(path);
                if (!spec.hasAudio) return new NativeAudioDto { path = rawPath, sampleRate = 0, channels = 0, frames = 0 };
                long startFrame = range.StartUs * PcmFile.SampleRate / 1000000;
                long endUs = range.DurationUs > 0 ? Math.Min(spec.DurationUs, range.StartUs + range.DurationUs) : spec.DurationUs;
                long frames = Math.Max(0, endUs * PcmFile.SampleRate / 1000000 - startFrame);
                WriteTone(rawPath, startFrame, frames, ct);
                return new NativeAudioDto { path = rawPath, sampleRate = PcmFile.SampleRate, channels = PcmFile.Channels, frames = frames };
            }, ct);
        }

        /// <summary>440 Hz tone at −12 dBFS with a 10 ms 1 kHz click at every whole second (sync check).</summary>
        static void WriteTone(string path, long startFrame, long frames, CancellationToken ct)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                var samples = new float[4096 * 2];
                var bytes = new byte[samples.Length * sizeof(float)];
                for (long done = 0; done < frames;)
                {
                    ct.ThrowIfCancellationRequested();
                    int n = (int)Math.Min(4096, frames - done);
                    for (int i = 0; i < n; i++)
                    {
                        long f = startFrame + done + i;
                        double t = (double)f / PcmFile.SampleRate;
                        bool click = f % PcmFile.SampleRate < PcmFile.SampleRate / 100;
                        float v = click ? (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * t)) : (float)(0.25 * Math.Sin(2 * Math.PI * 440 * t));
                        samples[2 * i] = v;
                        samples[2 * i + 1] = v;
                    }
                    Buffer.BlockCopy(samples, 0, bytes, 0, n * 2 * sizeof(float));
                    fs.Write(bytes, 0, n * 2 * sizeof(float));
                    done += n;
                }
            }
        }

        protected override VideoWriterBase CreateWriter(string outputPath, string workPath, VideoWriterOptions options)
        {
            return new FakeVideoWriter(outputPath, workPath, options, Math.Max(1, _options.PngFrameStride), Math.Max(0, _options.SimulatedReleaseDelayMs));
        }

        protected override string ResultPathFor(string mp4Path)
        {
            return mp4Path + FakeVideoWriter.FramesSuffix;
        }

        protected override void CommitOutput(string workResult, string resultPath)
        {
            try
            {
                if (Directory.Exists(resultPath)) Directory.Delete(resultPath, true);
                Directory.Move(workResult, resultPath);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                throw new MediaException(MediaErrorCode.Io, "cannot move the finished frames to " + resultPath + ": " + e.Message, e);
            }
        }

        protected override void RequireSegment(string path)
        {
            if (!File.Exists(Path.Combine(path, FakeVideoWriter.ManifestName)))
                throw new MediaException(MediaErrorCode.FileNotFound, "fake segment (frame directory with manifest.json) not found: " + path);
        }

        protected override void DeleteOutput(string path)
        {
            FakeVideoWriter.DeleteDirectory(path);
        }

        protected override bool FinishFile(string path, Mp4Metadata metadata, CancellationToken ct)
        {
            return false; // no MP4 container; the manifest carries the metadata
        }

        protected override Task<WriterFinish> ConcatNativeAsync(IReadOnlyList<string> segments, string pcmPath, string outFile,
            SegmentConcatOptions options, CancellationToken ct)
        {
            string outDir = ResultPathFor(outFile);
            return Task.Run(() => FakeVideoWriter.Concat(segments, pcmPath, outDir, options, ct), ct);
        }

        protected override Task<GallerySaveResult> SaveNativeAsync(string file, string album, CancellationToken ct)
        {
            string dir = Path.Combine(Application.persistentDataPath, "FakeGallery", album); // main-thread-only Unity API
            return Task.Run(() =>
            {
                Directory.CreateDirectory(dir);
                string dest = Path.Combine(dir, Path.GetFileName(file));
                File.Copy(file, dest, true);
                return new GallerySaveResult(GallerySaveStatus.Saved, dest, true, string.Empty);
            }, ct);
        }

        protected override void ShareNative(string file, string caption)
        {
            Debug.Log("[Media] FAKE share: " + file + (string.IsNullOrEmpty(caption) ? string.Empty : " — " + caption));
#if UNITY_EDITOR
            UnityEditor.EditorUtility.RevealInFinder(file);
#endif
        }

        protected override DeviceSignals GetSignalsNative()
        {
            long free = -1;
            try
            {
                string root = Path.GetPathRoot(Path.GetFullPath(Application.persistentDataPath));
                if (!string.IsNullOrEmpty(root)) free = new DriveInfo(root).AvailableFreeSpace;
            }
            catch (Exception)
            {
                free = -1;
            }
            return new DeviceSignals(_options.Thermal, _options.LowPowerMode, _options.ReduceMotion, _options.ReduceTransparency, free);
        }
    }

    /// <summary>Renders the test pattern for the requested time into the reader's buffers.</summary>
    internal sealed unsafe class FakeFrameSource : IFrameSource
    {
        static readonly uint[] Bars =
        {
            0xFFC0C0C0u, 0xFF00C0C0u, 0xFFC0C000u, 0xFF00C000u, 0xFFC000C0u, 0xFF0000C0u, 0xFFC00000u, 0xFF101010u,
        };

        readonly string _path;
        readonly VideoReaderOptions _options;
        TestPatternSpec _spec;
        uint[] _barRow;

        public FakeFrameSource(string path, VideoReaderOptions options)
        {
            _path = path;
            _options = options;
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool ToneMapped => false;
        public long DurationUs => _spec?.DurationUs ?? 0;

        public void BeginThread()
        {
        }

        public void Open()
        {
            _spec = FakeMediaBridge.ReadSpec(_path);
            bool swap = _spec.rotation == 90 || _spec.rotation == 270;
            VideoReaderOptions.ComputeOutputSize(swap ? _spec.height : _spec.width, swap ? _spec.width : _spec.height,
                _options.MaxWidth, _options.MaxHeight, out int w, out int h);
            Width = w;
            Height = h;
            _barRow = new uint[w];
            for (int x = 0; x < w; x++) _barRow[x] = Bars[Math.Min(Bars.Length - 1, x * Bars.Length / w)];
        }

        public IFrameMemory CreateBuffer()
        {
            return new UnmanagedFrameMemory(Width * Height * 4);
        }

        public long ReadFrame(long ptsUs, IFrameMemory destination)
        {
            int index = (int)Math.Max(0, Math.Min(_spec.FrameCount - 1, (long)Math.Floor(ptsUs * (double)_spec.fps / 1000000.0 + 1e-6)));
            long pts = (long)Math.Round(index * 1000000.0 / _spec.fps);
            uint* p = (uint*)destination.Pointer;
            int w = Width, h = Height;
            fixed (uint* bars = _barRow)
            {
                for (int y = 0; y < h; y++) Buffer.MemoryCopy(bars, p + (long)y * w, (long)w * 4, (long)w * 4);
            }
            // Moving white square: one full sweep per second.
            int size = Math.Max(4, Math.Min(w, h) / 6);
            int travel = Math.Max(1, w - size);
            int frameInSecond = index % _spec.fps;
            int sx = (int)((long)travel * frameInSecond / Math.Max(1, _spec.fps - 1));
            int sy = h / 2 - size / 2;
            Fill(p, w, h, sx, sy, size, size, 0xFFFFFFFFu);
            // Frame index as 16 binary blocks (MSB first) along the top; white = 1, black = 0.
            int block = Math.Max(2, w / 16);
            for (int bit = 0; bit < 16; bit++)
            {
                bool on = ((index >> (15 - bit)) & 1) != 0;
                Fill(p, w, h, bit * block, 0, block - 1, block, on ? 0xFFFFFFFFu : 0xFF000000u);
            }
            return pts;
        }

        static void Fill(uint* p, int w, int h, int x0, int y0, int fw, int fh, uint color)
        {
            int x1 = Math.Min(w, x0 + fw), y1 = Math.Min(h, y0 + fh);
            for (int y = Math.Max(0, y0); y < y1; y++)
            {
                uint* row = p + (long)y * w;
                for (int x = Math.Max(0, x0); x < x1; x++) row[x] = color;
            }
        }

        public void Close()
        {
        }

        public void EndThread()
        {
        }
    }

    /// <summary>Writes appended frames as PNGs (every Nth) plus audio.wav and manifest.json into "&lt;output&gt;.frames/".</summary>
    internal sealed class FakeVideoWriter : VideoWriterBase
    {
        [Serializable]
        sealed class Manifest
        {
            public string note = "FAKE media backend output: PNG sequence + WAV, not an MP4";
            public int width;
            public int height;
            public int fps;
            public int frames;
            public long durationUs;
            public int pngFrameStride;
            public bool hasAudio;
            public int segments;
            public string title;
            public string creationTime;
        }

        public const string FramesSuffix = ".frames";
        public const string ManifestName = "manifest.json";
        const string AudioName = "audio.wav";

        readonly int _stride;
        readonly int _releaseDelayMs;
        readonly SemaphoreSlim _slots = new SemaphoreSlim(3, 3);
        readonly ConcurrentBag<byte[]> _buffers = new ConcurrentBag<byte[]>();
        readonly BlockingCollection<(byte[] data, int index)> _queue = new BlockingCollection<(byte[] data, int index)>();
        readonly Task _worker;
        long _lastPts = -1;
        int _frames;
        volatile Exception _workerError;

        public FakeVideoWriter(string outputPath, string workPath, VideoWriterOptions options, int stride, int releaseDelayMs)
            : base(outputPath, workPath, options)
        {
            _stride = stride;
            _releaseDelayMs = releaseDelayMs;
            FramesDirectory = workPath + FramesSuffix;
            if (Directory.Exists(FramesDirectory)) Directory.Delete(FramesDirectory, true);
            Directory.CreateDirectory(FramesDirectory);
            _worker = Task.Run(WorkerLoop);
        }

        /// <summary>The work directory this writer fills (renamed to "&lt;output&gt;.frames" by a successful FinalizeAsync).</summary>
        public string FramesDirectory { get; }

        public override int FramesInFlight => 3 - _slots.CurrentCount;

        void WorkerLoop()
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                try
                {
                    if (_workerError == null && item.index % _stride == 0)
                    {
                        string file = Path.Combine(FramesDirectory, FrameFileName(item.index));
                        PngWriter.WriteRgba(file, item.data, Options.Width, Options.Height);
                    }
                }
                catch (Exception e)
                {
                    _workerError = e;
                }
                finally
                {
                    _buffers.Add(item.data);
                    _slots.Release();
                }
            }
        }

        protected override bool AppendCore(IntPtr rgba, long ptsUs, int timeoutMs)
        {
            if (_workerError != null) throw new MediaException(MediaErrorCode.Io, "PNG writer failed: " + _workerError.Message, _workerError);
            if (!_slots.Wait(timeoutMs)) return false;
            if (!_buffers.TryTake(out byte[] buffer)) buffer = new byte[Options.FrameBytes];
            System.Runtime.InteropServices.Marshal.Copy(rgba, buffer, 0, Options.FrameBytes);
            _queue.Add((buffer, _frames));
            _frames++;
            _lastPts = ptsUs;
            return true;
        }

        protected override async Task<WriterFinish> FinishCore(CancellationToken ct)
        {
            try
            {
                return await FinishFrames(ct);
            }
            catch
            {
                try
                {
                    if (Directory.Exists(FramesDirectory)) Directory.Delete(FramesDirectory, true);
                }
                catch (Exception)
                {
                    // best effort
                }
                throw;
            }
        }

        async Task<WriterFinish> FinishFrames(CancellationToken ct)
        {
            _queue.CompleteAdding();
            await _worker;
            ct.ThrowIfCancellationRequested();
            if (_workerError != null) throw new MediaException(MediaErrorCode.Io, "PNG writer failed: " + _workerError.Message, _workerError);
            long durationUs = _lastPts + Options.FrameDurationUs;
            int frames = _frames;
            string audio = Options.AudioPcmPath;
            Mp4Metadata metadata = Options.Metadata;
            await Task.Run(() =>
            {
                bool hasAudio = audio != null && PcmFile.CountFrames(audio) > 0;
                if (hasAudio) WavWriter.FromPcmFile(audio, Path.Combine(FramesDirectory, AudioName), durationUs * PcmFile.SampleRate / 1000000);
                var manifest = new Manifest
                {
                    width = Options.Width, height = Options.Height, fps = Options.Fps, frames = frames, durationUs = durationUs,
                    pngFrameStride = _stride, hasAudio = hasAudio, segments = 1,
                    title = metadata?.Title ?? string.Empty, creationTime = metadata?.CreationTimeIso8601 ?? string.Empty,
                };
                File.WriteAllText(Path.Combine(FramesDirectory, ManifestName), JsonUtility.ToJson(manifest, true));
            }, ct);
            return new WriterFinish(frames, durationUs);
        }

        static string FrameFileName(int index)
        {
            return "frame_" + index.ToString("D6", CultureInfo.InvariantCulture) + ".png";
        }

        internal static void DeleteDirectory(string dir)
        {
            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Media] could not delete " + dir + ": " + e.Message);
            }
        }

        /// <summary>
        /// Fake <see cref="IMediaBridge.ConcatSegmentsAsync"/>: renumbers the segments' PNG frames into one directory,
        /// writes the PCM mix (trimmed to the video) as audio.wav and a manifest. Worker thread.
        /// </summary>
        internal static WriterFinish Concat(IReadOnlyList<string> segments, string pcmPath, string outDir, SegmentConcatOptions options,
            CancellationToken ct)
        {
            Directory.CreateDirectory(outDir);
            int total = 0;
            int width = 0, height = 0, stride = 1;
            for (int s = 0; s < segments.Count; s++)
            {
                ct.ThrowIfCancellationRequested();
                Manifest m = JsonUtility.FromJson<Manifest>(File.ReadAllText(Path.Combine(segments[s], ManifestName)));
                if (m == null || m.frames <= 0) throw new MediaException(MediaErrorCode.Corrupt, "bad fake segment manifest: " + segments[s]);
                if (m.fps != options.Fps) throw new MediaException(MediaErrorCode.InvalidState, "segment " + s + " has " + m.fps + " fps, expected " + options.Fps);
                if (s == 0)
                {
                    width = m.width;
                    height = m.height;
                    stride = m.pngFrameStride;
                }
                else if (m.width != width || m.height != height)
                {
                    throw new MediaException(MediaErrorCode.InvalidState, "segment " + s + " is " + m.width + "x" + m.height + ", expected " + width + "x" + height);
                }
                for (int i = 0; i < m.frames; i++)
                {
                    string src = Path.Combine(segments[s], FrameFileName(i));
                    if (File.Exists(src)) File.Copy(src, Path.Combine(outDir, FrameFileName(total + i)), true);
                }
                total += m.frames;
            }
            long durationUs = options.SegmentStartUs(total);
            bool hasAudio = PcmFile.CountFrames(pcmPath) > 0;
            if (hasAudio) WavWriter.FromPcmFile(pcmPath, Path.Combine(outDir, AudioName), durationUs * PcmFile.SampleRate / 1000000);
            ct.ThrowIfCancellationRequested();
            var manifest = new Manifest
            {
                width = width, height = height, fps = options.Fps, frames = total, durationUs = durationUs, pngFrameStride = stride,
                hasAudio = hasAudio, segments = segments.Count,
                title = options.Metadata?.Title ?? string.Empty, creationTime = options.Metadata?.CreationTimeIso8601 ?? string.Empty,
            };
            File.WriteAllText(Path.Combine(outDir, ManifestName), JsonUtility.ToJson(manifest, true));
            return new WriterFinish(total, durationUs);
        }

        /// <summary>
        /// The "native" side has stopped once the PNG worker drained; like the device writers, an unfinished writer then
        /// deletes its own work directory before it reports the release. <see cref="FakeMediaOptions.SimulatedReleaseDelayMs"/>
        /// models a slow codec teardown in between.
        /// </summary>
        protected override async Task ReleaseCoreAsync()
        {
            if (!_queue.IsAddingCompleted) _queue.CompleteAdding();
            await _worker.ConfigureAwait(false);
            if (!NativeFinished) DeleteDirectory(FramesDirectory);
            if (_releaseDelayMs > 0) await Task.Delay(_releaseDelayMs).ConfigureAwait(false);
        }

        protected override void DeleteWorkOutput()
        {
            DeleteDirectory(FramesDirectory);
        }
    }
}
