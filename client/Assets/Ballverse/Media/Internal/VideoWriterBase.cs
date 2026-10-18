using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Ballverse.Media.Internal
{
    /// <summary>Raw finish result of a platform writer.</summary>
    internal readonly struct WriterFinish
    {
        public readonly int Frames;
        public readonly long DurationUs;

        public WriterFinish(int frames, long durationUs)
        {
            Frames = frames;
            DurationUs = durationUs;
        }
    }

    /// <summary>
    /// Shared <see cref="IVideoWriter"/> logic: argument checks, monotonic timestamps, frame counting, sticky errors,
    /// disposal and finalizer safety. Platform writers implement the Core methods. The native side owns its threads and
    /// buffers; this object only holds a handle, so its finalizer releases the native writer on the main thread.
    /// </summary>
    internal abstract class VideoWriterBase : IVideoWriter
    {
        long _lastPts = -1;
        int _appended;
        bool _finalizing;
        int _released;
        MediaException _error;

        protected VideoWriterBase(string outputPath, VideoWriterOptions options)
        {
            OutputPath = outputPath;
            Options = options;
        }

        ~VideoWriterBase()
        {
            if (Volatile.Read(ref _released) == 0) MainThread.RunOrInline(ReleaseOnce);
        }

        public VideoWriterOptions Options { get; }
        public string OutputPath { get; }
        public int FramesAppended => _appended;
        public bool IsFinalizing => _finalizing;
        public int MaxFramesInFlight => 3;
        public abstract int FramesInFlight { get; }

        /// <summary>Copies <paramref name="rgba"/> (FrameBytes) into a native slot; false if no slot freed within <paramref name="timeoutMs"/> (0 = do not wait). Throws <see cref="MediaException"/> on failure.</summary>
        protected abstract bool AppendCore(IntPtr rgba, long ptsUs, int timeoutMs);

        /// <summary>Starts the native finish; completes with frame count and duration. Cancellation must abort and delete the file.</summary>
        protected abstract Task<WriterFinish> FinishCore(CancellationToken ct);

        /// <summary>Releases the native writer; if it is not finished, aborts and deletes the partial file. Main thread, idempotent via <see cref="ReleaseOnce"/>.</summary>
        protected abstract void ReleaseCore();

        public unsafe bool TryAppendVideoFrame(NativeArray<byte> rgba, long ptsUs)
        {
            CheckAppend(rgba.IsCreated ? rgba.Length : -1, ptsUs);
            return Append((IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(rgba), ptsUs, 0);
        }

        public unsafe bool TryAppendVideoFrame(byte[] rgba, long ptsUs)
        {
            CheckAppend(rgba == null ? -1 : rgba.Length, ptsUs);
            fixed (byte* p = rgba) return Append((IntPtr)p, ptsUs, 0);
        }

        public unsafe void AppendVideoFrame(NativeArray<byte> rgba, long ptsUs)
        {
            CheckAppend(rgba.IsCreated ? rgba.Length : -1, ptsUs);
            if (!Append((IntPtr)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(rgba), ptsUs, Options.AppendTimeoutMs)) ThrowStalled();
        }

        public unsafe void AppendVideoFrame(byte[] rgba, long ptsUs)
        {
            CheckAppend(rgba == null ? -1 : rgba.Length, ptsUs);
            bool ok;
            fixed (byte* p = rgba) ok = Append((IntPtr)p, ptsUs, Options.AppendTimeoutMs);
            if (!ok) ThrowStalled();
        }

        void ThrowStalled()
        {
            throw new MediaException(MediaErrorCode.Timeout, "encoder did not accept a frame within " + Options.AppendTimeoutMs + " ms");
        }

        void CheckAppend(int length, long ptsUs)
        {
            MainThread.Assert(nameof(AppendVideoFrame));
            if (Volatile.Read(ref _released) != 0) throw new ObjectDisposedException(nameof(IVideoWriter));
            if (_finalizing) throw new InvalidOperationException("writer is finalizing");
            if (_error != null) throw _error;
            if (length < 0) throw new ArgumentNullException("rgba");
            if (length < Options.FrameBytes) throw new ArgumentException("frame buffer has " + length + " bytes, expected " + Options.FrameBytes + " (RGBA32 " + Options.Width + "x" + Options.Height + ")", "rgba");
            if (ptsUs < 0) throw new ArgumentOutOfRangeException(nameof(ptsUs), ptsUs, "pts must be >= 0");
            if (ptsUs <= _lastPts) throw new ArgumentException("pts must strictly increase (got " + ptsUs + " after " + _lastPts + ")", nameof(ptsUs));
        }

        bool Append(IntPtr p, long ptsUs, int timeoutMs)
        {
            bool accepted;
            try
            {
                accepted = AppendCore(p, ptsUs, timeoutMs);
            }
            catch (MediaException e)
            {
                _error = e;
                throw;
            }
            if (accepted)
            {
                _lastPts = ptsUs;
                _appended++;
            }
            return accepted;
        }

        /// <summary>Called by the bridge's FinalizeAsync. Always releases the native writer afterwards.</summary>
        internal async Task<WriterFinish> FinishAsync(CancellationToken ct)
        {
            MainThread.Assert("FinalizeAsync");
            if (Volatile.Read(ref _released) != 0) throw new ObjectDisposedException(nameof(IVideoWriter));
            if (_finalizing) throw new InvalidOperationException("FinalizeAsync was already called");
            if (_error != null) throw _error;
            if (_appended == 0) throw new InvalidOperationException("no frames were appended");
            _finalizing = true;
            try
            {
                return await FinishCore(ct);
            }
            finally
            {
                ReleaseOnce();
            }
        }

        public void Dispose()
        {
            MainThread.Assert(nameof(Dispose));
            ReleaseOnce();
            GC.SuppressFinalize(this);
        }

        void ReleaseOnce()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            ReleaseCore();
        }
    }
}
