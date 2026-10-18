#if UNITY_IOS
using System;
using System.Threading;
using System.Threading.Tasks;
using AOT;
using Ballverse.Media.Internal;
using UnityEngine;

namespace Ballverse.Media.Ios
{
    /// <summary>
    /// iOS implementation over the Objective-C++ plugin in Assets/Plugins/iOS/BallverseMedia (AVFoundation, PhotosUI,
    /// Photos, UIKit). Native completions arrive on GCD threads through static [MonoPInvokeCallback] functions and are
    /// marshalled to the main thread by <see cref="NativeOps"/>. Frame buffers are C#-owned NativeArrays; the native
    /// reader writes RGBA into them directly and the writer copies (RGBA→BGRA swizzle) out of them synchronously.
    /// </summary>
    internal sealed class IosMediaBridge : MediaBridgeBase
    {
        // One registry for the process: callbacks are static functions.
        static readonly NativeOps s_ops = new NativeOps();
        // Delegates kept alive for the lifetime of the process (native holds the function pointers).
        static readonly IosNative.ResultCallback s_onResult = OnResult;
        static readonly IosNative.ProgressCallback s_onProgress = OnProgress;
        static bool s_callbacksSet;

        public IosMediaBridge()
        {
            MainThread.Assert(nameof(IosMediaBridge));
            if (!s_callbacksSet)
            {
                IosNative.BVMedia_SetCallbacks(s_onResult, s_onProgress);
                s_callbacksSet = true;
            }
        }

        public override MediaBackend Backend => MediaBackend.Ios;

        [MonoPInvokeCallback(typeof(IosNative.ResultCallback))]
        static void OnResult(long requestId, int status, IntPtr utf8Json)
        {
            string json;
            try
            {
                json = IosNative.Utf8(utf8Json);
            }
            catch (Exception)
            {
                json = string.Empty;
            }
            s_ops.Complete(requestId, status, json);
        }

        [MonoPInvokeCallback(typeof(IosNative.ProgressCallback))]
        static void OnProgress(long requestId, double progress)
        {
            s_ops.Progress(requestId, progress);
        }

        static Task<string> Start(IProgress<double> progress, CancellationToken ct, Action<long> start)
        {
            long id = s_ops.Begin(out Task<string> task, progress, ct, IosNative.BVMedia_Cancel);
            try
            {
                start(id);
            }
            catch (Exception e)
            {
                s_ops.Fail(id, new MediaException(MediaErrorCode.Unknown, "native call failed: " + e.Message, e));
            }
            return task;
        }

        protected override async Task<string> PickNativeAsync(string destinationDirectory, string baseName, IProgress<double> progress, CancellationToken ct)
        {
            string json = await Start(progress, ct, id => IosNative.BVMedia_PickVideo(id, destinationDirectory, baseName));
            return NativeJson.Parse<NativePickDto>(json).path;
        }

        protected override async Task<MediaInfo> ProbeNativeAsync(string path, CancellationToken ct)
        {
            string json = await Start(null, ct, id => IosNative.BVMedia_Probe(id, path));
            return NativeJson.ToMediaInfo(path, NativeJson.Parse<NativeMediaInfoDto>(json));
        }

        protected override IFrameSource CreateFrameSource(string path, VideoReaderOptions options)
        {
            return new IosFrameSource(path, options);
        }

        protected override async Task<NativeAudioDto> DecodeAudioNativeAsync(string path, string rawPath, AudioDecodeRange range, CancellationToken ct)
        {
            string json = await Start(null, ct, id => IosNative.BVMedia_DecodeAudio(id, path, rawPath, range.StartUs, range.DurationUs));
            return NativeJson.Parse<NativeAudioDto>(json);
        }

        protected override VideoWriterBase CreateWriter(string outputPath, VideoWriterOptions options)
        {
            return new IosVideoWriter(outputPath, options);
        }

        protected override async Task<GallerySaveResult> SaveNativeAsync(string file, string album, CancellationToken ct)
        {
            string json = await Start(null, ct, id => IosNative.BVMedia_SaveToGallery(id, file, album));
            return NativeJson.ToSaveResult(NativeJson.Parse<NativeSaveDto>(json));
        }

        protected override void ShareNative(string file, string caption)
        {
            IosNative.BVMedia_Share(file, caption);
        }

        protected override DeviceSignals GetSignalsNative()
        {
            IosNative.BVMedia_GetSignals(out IosNative.Signals s);
            ThermalState thermal = s.thermal >= 0 && s.thermal <= 3 ? (ThermalState)s.thermal : ThermalState.Unknown;
            return new DeviceSignals(thermal, s.lowPower != 0, s.reduceMotion != 0, s.reduceTransparency != 0, s.freeSpaceBytes);
        }

        public override void Dispose()
        {
            if (Disposed) return;
            base.Dispose();
            s_ops.CancelAll();
        }

        /// <summary>Native AVAssetReader driven from the reader worker thread; writes RGBA into C#-owned buffers.</summary>
        sealed class IosFrameSource : IFrameSource
        {
            readonly string _path;
            readonly VideoReaderOptions _options;
            IntPtr _handle;

            public IosFrameSource(string path, VideoReaderOptions options)
            {
                _path = path;
                _options = options;
            }

            public int Width { get; private set; }
            public int Height { get; private set; }
            public bool ToneMapped { get; private set; }
            public long DurationUs { get; private set; }

            public void BeginThread()
            {
            }

            public void Open()
            {
                _handle = IosNative.BVReader_Open(_path, _options.MaxWidth, _options.MaxHeight, _options.ToneMapHdrToSdr ? 1 : 0,
                    out int w, out int h, out int toneMapped, out long duration);
                if (_handle == IntPtr.Zero) throw MediaException.FromNative(IosNative.LastError(), MediaErrorCode.DecoderFailed);
                Width = w;
                Height = h;
                ToneMapped = toneMapped != 0;
                DurationUs = duration;
            }

            public IFrameMemory CreateBuffer()
            {
                return new UnmanagedFrameMemory(Width * Height * 4);
            }

            public long ReadFrame(long ptsUs, IFrameMemory destination)
            {
                long pts = IosNative.BVReader_Read(_handle, ptsUs, destination.Pointer, destination.Length);
                if (pts == long.MinValue) throw MediaException.FromNative(IosNative.LastError(), MediaErrorCode.DecoderFailed);
                return pts;
            }

            public void Close()
            {
                if (_handle != IntPtr.Zero) IosNative.BVReader_Close(_handle);
                _handle = IntPtr.Zero;
            }

            public void EndThread()
            {
            }
        }

        /// <summary>AVAssetWriter handle; appends copy (and swizzle) synchronously into a pooled CVPixelBuffer.</summary>
        sealed class IosVideoWriter : VideoWriterBase
        {
            IntPtr _handle;

            public IosVideoWriter(string outputPath, VideoWriterOptions o) : base(outputPath, o)
            {
                _handle = IosNative.BVWriter_Open(outputPath, o.Width, o.Height, o.Fps, o.VideoBitrateBps, o.KeyframeIntervalSeconds,
                    o.AudioPcmPath ?? string.Empty, o.AudioBitrateBps);
                if (_handle == IntPtr.Zero)
                {
                    GC.SuppressFinalize(this);
                    throw MediaException.FromNative(IosNative.LastError(), MediaErrorCode.EncoderFailed);
                }
            }

            public override int FramesInFlight => _handle == IntPtr.Zero ? 0 : IosNative.BVWriter_FramesInFlight(_handle);

            protected override bool AppendCore(IntPtr rgba, long ptsUs, int timeoutMs)
            {
                int rc = IosNative.BVWriter_Append(_handle, rgba, Options.Width * 4, ptsUs, timeoutMs);
                if (rc > 0) return true;
                if (rc == 0) return false;
                throw MediaException.FromNative(IosNative.LastError(), MediaErrorCode.EncoderFailed);
            }

            protected override async Task<WriterFinish> FinishCore(CancellationToken ct)
            {
                IntPtr handle = _handle;
                string json = await Start(null, ct, id => IosNative.BVWriter_Finish(handle, id));
                NativeWriterDto dto = NativeJson.Parse<NativeWriterDto>(json);
                return new WriterFinish(dto.frames, dto.durationUs);
            }

            protected override void ReleaseCore()
            {
                if (_handle == IntPtr.Zero) return;
                IosNative.BVWriter_Close(_handle);
                _handle = IntPtr.Zero;
            }
        }
    }
}
#endif
