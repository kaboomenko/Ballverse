#if UNITY_ANDROID
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Media.Internal;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.Scripting;

namespace Ballverse.Media.Android
{
    /// <summary>
    /// Android implementation over the Java plugin in Assets/Plugins/Android/BallverseMedia (package com.ballverse.media).
    /// Asynchronous operations go through <c>MediaBridge</c> static methods and complete via <see cref="AndroidCallbackProxy"/>;
    /// frames move through Java direct ByteBuffers whose addresses are obtained once with
    /// <see cref="AndroidJNI.GetDirectBufferAddress"/> (one memcpy per frame, no per-frame JNI array allocations).
    /// </summary>
    internal sealed class AndroidMediaBridge : MediaBridgeBase
    {
        const string BridgeClass = "com.ballverse.media.MediaBridge";

        // One registry and one Java callback for the process (Java keeps a single static callback): operations that
        // outlive a bridge instance (writer releases) still complete after the bridge was disposed and re-created, and
        // request ids never collide between bridge instances.
        static readonly NativeOps s_ops = new NativeOps();
        static AndroidCallbackProxy s_proxy;

        readonly NativeOps _ops = s_ops;
        readonly AndroidJavaClass _bridge;

        public AndroidMediaBridge()
        {
            MainThread.Assert(nameof(AndroidMediaBridge));
            _bridge = new AndroidJavaClass(BridgeClass);
            if (s_proxy == null) s_proxy = new AndroidCallbackProxy(s_ops);
            using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                _bridge.CallStatic("init", activity, s_proxy);
            }
        }

        public override MediaBackend Backend => MediaBackend.Android;

        Task<string> Start(string method, IProgress<double> progress, CancellationToken ct, params object[] args)
        {
            return Start(method, false, progress, ct, args);
        }

        /// <param name="cancelWaitsForNative">A cancel completes the task only once Java reports that the request stopped.</param>
        Task<string> Start(string method, bool cancelWaitsForNative, IProgress<double> progress, CancellationToken ct, params object[] args)
        {
            return _ops.Run(progress, ct, id =>
            {
                var full = new object[args.Length + 1];
                full[0] = id;
                Array.Copy(args, 0, full, 1, args.Length);
                _bridge.CallStatic(method, full);
            }, CancelNative, e => new MediaException(MediaErrorCode.Unknown, "Java call " + method + " failed: " + e.Message, e),
                false, cancelWaitsForNative);
        }

        void CancelNative(long id)
        {
            _bridge.CallStatic("cancel", id);
        }

        protected override async Task<string> PickNativeAsync(string destinationDirectory, string baseName, IProgress<double> progress, CancellationToken ct)
        {
            string json = await Start("pickVideo", progress, ct, destinationDirectory, baseName);
            return NativeJson.Parse<NativePickDto>(json).path;
        }

        protected override async Task<MediaInfo> ProbeNativeAsync(string path, CancellationToken ct)
        {
            string json = await Start("probe", null, ct, path);
            return NativeJson.ToMediaInfo(path, NativeJson.Parse<NativeMediaInfoDto>(json));
        }

        protected override IFrameSource CreateFrameSource(string path, VideoReaderOptions options)
        {
            return new AndroidFrameSource(path, options);
        }

        protected override async Task<NativeAudioDto> DecodeAudioNativeAsync(string path, string rawPath, AudioDecodeRange range, CancellationToken ct)
        {
            string json = await Start("decodeAudio", null, ct, path, rawPath, range.StartUs, range.DurationUs);
            return NativeJson.Parse<NativeAudioDto>(json);
        }

        protected override VideoWriterBase CreateWriter(string outputPath, string workPath, VideoWriterOptions options)
        {
            return new AndroidVideoWriter(outputPath, workPath, options, _ops);
        }

        protected override async Task<WriterFinish> ConcatNativeAsync(IReadOnlyList<string> segments, string pcmPath, string outFile,
            SegmentConcatOptions options, CancellationToken ct)
        {
            // A cancelled join completes only after SegmentConcat stopped (muxer and AAC codec released, its own work file
            // deleted): MediaBridge.submit reports the cancellation after SegmentConcat.run returned.
            string json = await Start("concatSegments", true, null, ct, string.Join("\n", segments), pcmPath ?? string.Empty, outFile,
                options.Fps, options.AudioBitrateBps);
            NativeWriterDto dto = NativeJson.Parse<NativeWriterDto>(json);
            return new WriterFinish(dto.frames, dto.durationUs);
        }

        protected override async Task<GallerySaveResult> SaveNativeAsync(string file, string album, CancellationToken ct)
        {
            string json = await Start("saveToGallery", null, ct, file, album);
            return NativeJson.ToSaveResult(NativeJson.Parse<NativeSaveDto>(json));
        }

        protected override void ShareNative(string file, string caption)
        {
            _bridge.CallStatic("share", file, caption);
        }

        protected override DeviceSignals GetSignalsNative()
        {
            try
            {
                return NativeJson.ToSignals(NativeJson.Parse<NativeSignalsDto>(_bridge.CallStatic<string>("getSignals")));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Media] getSignals failed: " + e.Message);
                return DeviceSignals.Unknown;
            }
        }

        public override void Dispose()
        {
            if (Disposed) return;
            base.Dispose();
            _ops.CancelAll(); // writer releases (keepOnCancelAll) stay pending until Java reports them
            _bridge.Dispose();
        }
    }

    /// <summary>Receives Java completions (any Java thread) and forwards them to <see cref="NativeOps"/>.</summary>
    [Preserve]
    internal sealed class AndroidCallbackProxy : AndroidJavaProxy
    {
        readonly NativeOps _ops;

        public AndroidCallbackProxy(NativeOps ops) : base("com.ballverse.media.MediaCallback")
        {
            _ops = ops;
        }

        // Invoked by name through AndroidJavaProxy reflection; [Preserve] keeps IL2CPP from stripping them.
        [Preserve]
        public void onResult(long requestId, int status, string json)
        {
            _ops.Complete(requestId, status, json);
        }

        [Preserve]
        public void onProgress(long requestId, double progress)
        {
            _ops.Progress(requestId, progress);
        }
    }

    /// <summary>
    /// Java <c>VideoReader</c> driven from the C# reader worker thread. The Java object is constructed on the main thread
    /// (class lookup needs the app class loader); all other calls happen on the worker, attached to the JVM.
    /// </summary>
    internal sealed class AndroidFrameSource : IFrameSource
    {
        readonly AndroidJavaObject _reader;
        bool _attached;

        public AndroidFrameSource(string path, VideoReaderOptions options)
        {
            _reader = new AndroidJavaObject("com.ballverse.media.VideoReader", path, options.MaxWidth, options.MaxHeight, options.ToneMapHdrToSdr);
        }

        public int Width { get; private set; }
        public int Height { get; private set; }
        public bool ToneMapped { get; private set; }
        public long DurationUs { get; private set; }

        public void BeginThread()
        {
            int rc = AndroidJNI.AttachCurrentThread();
            if (rc != 0) throw new MediaException(MediaErrorCode.DecoderFailed, "AttachCurrentThread failed: " + rc);
            _attached = true;
        }

        public void Open()
        {
            if (!_reader.Call<bool>("open")) throw MediaException.FromNative(_reader.Call<string>("lastError"), MediaErrorCode.DecoderFailed);
            Width = _reader.Call<int>("width");
            Height = _reader.Call<int>("height");
            ToneMapped = _reader.Call<bool>("toneMapped");
            DurationUs = _reader.Call<long>("durationUs");
        }

        public IFrameMemory CreateBuffer()
        {
            int index = _reader.Call<int>("allocateFrameBuffer");
            AndroidJavaObject buffer = _reader.Call<AndroidJavaObject>("frameBuffer", index);
            return AndroidDirectMemory.Wrap(buffer, Width * Height * 4, index);
        }

        public long ReadFrame(long ptsUs, IFrameMemory destination)
        {
            long pts = _reader.Call<long>("readFrame", ptsUs, ((AndroidDirectMemory)destination).Index);
            if (pts == long.MinValue) throw MediaException.FromNative(_reader.Call<string>("lastError"), MediaErrorCode.DecoderFailed);
            return pts;
        }

        public void Close()
        {
            _reader.Call("close");
            _reader.Dispose();
        }

        public void EndThread()
        {
            if (_attached) AndroidJNI.DetachCurrentThread();
            _attached = false;
        }
    }

    /// <summary>A Java direct ByteBuffer kept alive by a global reference; its native address is stable.</summary>
    internal sealed class AndroidDirectMemory : FrameMemoryBase
    {
        AndroidJavaObject _buffer;

        AndroidDirectMemory(AndroidJavaObject buffer, IntPtr address, int length, int index) : base(address, length)
        {
            _buffer = buffer;
            Index = index;
        }

        public int Index { get; }

        public static unsafe AndroidDirectMemory Wrap(AndroidJavaObject buffer, int length, int index)
        {
            sbyte* address = AndroidJNI.GetDirectBufferAddress(buffer.GetRawObject());
            if (address == null)
            {
                buffer.Dispose();
                throw new MediaException(MediaErrorCode.DecoderFailed, "not a direct ByteBuffer");
            }
            return new AndroidDirectMemory(buffer, (IntPtr)address, length, index);
        }

        protected override void FreeMemory()
        {
            _buffer?.Dispose();
            _buffer = null;
        }
    }

    /// <summary>
    /// Java <c>VideoWriter</c>: 3 direct slot buffers filled with one memcpy per frame. The Java writer encodes into the
    /// work path; <c>releaseAsync</c> reports once its thread has exited (codecs released, unfinished file deleted).
    /// </summary>
    internal sealed class AndroidVideoWriter : VideoWriterBase
    {
        readonly NativeOps _ops;
        AndroidJavaObject _writer;
        AndroidJavaObject[] _slots;
        IntPtr[] _slotPointers;

        public unsafe AndroidVideoWriter(string outputPath, string workPath, VideoWriterOptions o, NativeOps ops) : base(outputPath, workPath, o)
        {
            _ops = ops;
            _writer = new AndroidJavaObject("com.ballverse.media.VideoWriter", workPath, o.Width, o.Height, o.Fps,
                o.VideoBitrateBps, o.KeyframeIntervalSeconds, o.AudioPcmPath ?? string.Empty, o.AudioBitrateBps);
            if (!_writer.Call<bool>("open"))
            {
                MediaException error = MediaException.FromNative(_writer.Call<string>("lastError"), MediaErrorCode.EncoderFailed);
                _writer.Call("release");
                _writer.Dispose();
                _writer = null;
                GC.SuppressFinalize(this);
                throw error;
            }
            int count = _writer.Call<int>("slotCount");
            _slots = new AndroidJavaObject[count];
            _slotPointers = new IntPtr[count];
            for (int i = 0; i < count; i++)
            {
                _slots[i] = _writer.Call<AndroidJavaObject>("slot", i);
                sbyte* p = AndroidJNI.GetDirectBufferAddress(_slots[i].GetRawObject());
                if (p == null) throw new MediaException(MediaErrorCode.EncoderFailed, "slot is not a direct ByteBuffer");
                _slotPointers[i] = (IntPtr)p;
            }
            Debug.Log("[Media] Android writer " + o.Width + "x" + o.Height + "@" + o.Fps + " H.264 " + _writer.Call<string>("profile"));
        }

        public override int FramesInFlight => _writer == null ? 0 : _writer.Call<int>("framesInFlight");

        protected override unsafe bool AppendCore(IntPtr rgba, long ptsUs, int timeoutMs)
        {
            int slot = _writer.Call<int>("acquireSlot", timeoutMs);
            if (slot == -1) return false;
            if (slot < 0) throw MediaException.FromNative(_writer.Call<string>("lastError"), MediaErrorCode.EncoderFailed);
            UnsafeUtility.MemCpy((void*)_slotPointers[slot], (void*)rgba, Options.FrameBytes);
            if (!_writer.Call<bool>("submit", slot, ptsUs))
                throw MediaException.FromNative(_writer.Call<string>("lastError"), MediaErrorCode.EncoderFailed);
            return true;
        }

        protected override async Task<WriterFinish> FinishCore(CancellationToken ct)
        {
            AndroidJavaObject writer = _writer;
            Task<string> task = _ops.Run(null, ct, id => writer.Call("finishAsync", id), _ => writer.Call("cancel"),
                e => new MediaException(MediaErrorCode.EncoderFailed, e.Message, e));
            NativeWriterDto dto = NativeJson.Parse<NativeWriterDto>(await task);
            return new WriterFinish(dto.frames, dto.durationUs);
        }

        protected override async Task ReleaseCoreAsync()
        {
            if (_slots != null)
            {
                foreach (AndroidJavaObject s in _slots) s?.Dispose();
                _slots = null;
            }
            AndroidJavaObject writer = _writer;
            _writer = null;
            if (writer == null) return;
            try
            {
                // Completes when the Java writer thread has exited: encoder/muxer released and, if unfinished, the work
                // file deleted by the writer that owns it. Survives bridge disposal (keepOnCancelAll).
                await _ops.Run(null, CancellationToken.None, id => writer.Call("releaseAsync", id), null,
                    e => new MediaException(MediaErrorCode.EncoderFailed, "VideoWriter.releaseAsync failed: " + e.Message, e), true);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                writer.Dispose();
            }
        }
    }
}
#endif
