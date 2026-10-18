using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine;

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
    /// deterministic release and finalizer safety. Platform writers implement the Core methods. The native side owns its
    /// threads and buffers; this object only holds a handle, so its finalizer releases the native writer on the main thread.
    /// </summary>
    /// <remarks>
    /// <para><b>Files.</b> The native encoder writes <see cref="WorkPath"/>, a temp file unique to this writer next to
    /// <see cref="OutputPath"/> (<c>&lt;output&gt;.bv&lt;id&gt;.part.mp4</c>). Only a successful
    /// <see cref="IMediaBridge.FinalizeAsync"/> renames it to <see cref="OutputPath"/>; every abort path deletes only the
    /// work file, never the output path, so a writer released late can never touch the file of a newer writer.</para>
    /// <para><b>Release.</b> <see cref="Released"/> completes once the native side has stopped (encoder thread or queue
    /// drained, codec released) and the unfinished work file is deleted — natively by the writer that owns it, and once
    /// more here as a safety net.</para>
    /// </remarks>
    internal abstract class VideoWriterBase : IVideoWriter
    {
        /// <summary>
        /// Upper bound for the native release (a codec that hangs inside the platform). After it <see cref="Released"/>
        /// completes anyway with an error log; the hung native writer can then only touch its own work file.
        /// </summary>
        internal static int NativeReleaseTimeoutMs = 20000;

        readonly TaskCompletionSource<bool> _releasedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource<bool> _settledTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        long _lastPts = -1;
        int _appended;
        bool _finalizing;
        bool _committing;
        bool _nativeFinished;
        int _released;
        MediaException _error;

        protected VideoWriterBase(string outputPath, string workPath, VideoWriterOptions options)
        {
            OutputPath = outputPath;
            WorkPath = workPath;
            Options = options;
        }

        ~VideoWriterBase()
        {
            if (Volatile.Read(ref _released) == 0) MainThread.RunOrInline(ReleaseOnce);
        }

        public VideoWriterOptions Options { get; }
        public string OutputPath { get; }
        /// <summary>The temp file the native encoder writes (unique per writer); renamed to <see cref="OutputPath"/> on success.</summary>
        internal string WorkPath { get; }
        public int FramesAppended => _appended;
        public bool IsFinalizing => _finalizing;
        public int MaxFramesInFlight => 3;
        public abstract int FramesInFlight { get; }
        public Task Released => _releasedTcs.Task;

        /// <summary>True once the native finish succeeded (the work file is complete and must not be deleted by the release).</summary>
        protected bool NativeFinished => _nativeFinished;

        /// <summary>True while frames may still be appended (not released, not finalizing).</summary>
        internal bool IsOpen => Volatile.Read(ref _released) == 0 && !_finalizing;

        /// <summary>
        /// Completes when nothing of this writer is in flight any more: <see cref="Released"/> completed and, if
        /// <see cref="IMediaBridge.FinalizeAsync"/> ran, that call has finished (including the rename to the output).
        /// </summary>
        internal Task Settled => _settledTcs.Task;

        /// <summary>Copies <paramref name="rgba"/> (FrameBytes) into a native slot; false if no slot freed within <paramref name="timeoutMs"/> (0 = do not wait). Throws <see cref="MediaException"/> on failure.</summary>
        protected abstract bool AppendCore(IntPtr rgba, long ptsUs, int timeoutMs);

        /// <summary>
        /// Starts the native finish of <see cref="WorkPath"/>; completes with frame count and duration. Cancellation
        /// must abort and delete the work file. The writer is released afterwards in any case.
        /// </summary>
        protected abstract Task<WriterFinish> FinishCore(CancellationToken ct);

        /// <summary>
        /// Releases the native writer (main thread, called once). If it is not finished, aborts it; the native side deletes
        /// its own work file. The returned task completes only when the native side has STOPPED: encoder thread exited /
        /// writer queue drained and the codec released. It must not fault for expected states (already finished, failed).
        /// </summary>
        protected abstract Task ReleaseCoreAsync();

        /// <summary>Deletes the unfinished work output after the native release (safety net; default: the work file and its fast-start temp).</summary>
        protected virtual void DeleteWorkOutput()
        {
            MediaBridgeBase.TryDelete(WorkPath);
            MediaBridgeBase.TryDelete(WorkPath + Mp4FastStart.TempSuffix);
        }
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

        /// <summary>
        /// FinalizeAsync precondition that must not touch the output: throws when the writer already finished, was
        /// disposed, or a FinalizeAsync is running (the output then belongs to that call or is a finished export).
        /// </summary>
        internal void ThrowIfFinishedOrFinalizing()
        {
            MainThread.Assert("FinalizeAsync");
            if (_finalizing) throw new InvalidOperationException("FinalizeAsync was already called");
            if (Volatile.Read(ref _released) != 0) throw new ObjectDisposedException(nameof(IVideoWriter));
        }

        /// <summary>Why an open writer can never finish (sticky encoder error, no frames), or null.</summary>
        internal Exception UnfinishableReason()
        {
            if (_error != null) return _error;
            if (_appended == 0) return new InvalidOperationException("no frames were appended");
            return null;
        }

        /// <summary>
        /// Called by the bridge's FinalizeAsync after <see cref="ThrowIfFinishedOrFinalizing"/> and
        /// <see cref="UnfinishableReason"/> passed. Always releases the native writer afterwards and completes (success,
        /// failure or cancellation) only once the native side has stopped (<see cref="Released"/>).
        /// </summary>
        internal async Task<WriterFinish> FinishAsync(CancellationToken ct)
        {
            ThrowIfFinishedOrFinalizing();
            Exception unfinishable = UnfinishableReason();
            if (unfinishable != null) throw new InvalidOperationException("writer cannot finish: " + unfinishable.Message, unfinishable);
            _finalizing = true;
            try
            {
                WriterFinish finish = await FinishCore(ct);
                _nativeFinished = true;
                return finish;
            }
            finally
            {
                ReleaseOnce();
                await Released;
            }
        }

        /// <summary>FinalizeAsync owns the output from here until <see cref="EndCommit"/> (keeps <see cref="Settled"/> pending).</summary>
        internal void BeginCommit()
        {
            _committing = true;
        }

        /// <summary>End of FinalizeAsync (after the rename or the cleanup).</summary>
        internal void EndCommit()
        {
            _committing = false;
            if (Released.IsCompleted) _settledTcs.TrySetResult(true);
        }

        public void Dispose()
        {
            MainThread.Assert(nameof(Dispose));
            ReleaseOnce();
            GC.SuppressFinalize(this);
        }

        public Task ReleaseAsync()
        {
            MainThread.Assert(nameof(ReleaseAsync));
            ReleaseOnce();
            GC.SuppressFinalize(this);
            return Released;
        }

        void ReleaseOnce()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            _ = ReleaseAndSignalAsync();
        }

        async Task ReleaseAndSignalAsync()
        {
            try
            {
                Task core;
                try
                {
                    core = ReleaseCoreAsync() ?? Task.CompletedTask;
                }
                catch (Exception e)
                {
                    core = Task.FromException(e);
                }
                if (!core.IsCompleted)
                {
                    using (var timeout = new CancellationTokenSource())
                    {
                        Task first = await Task.WhenAny(core, Task.Delay(NativeReleaseTimeoutMs, timeout.Token));
                        timeout.Cancel();
                        if (first != core)
                        {
                            Debug.LogError("[Media] native writer for " + OutputPath + " did not stop within " + NativeReleaseTimeoutMs
                                + " ms; continuing (it can only touch its own work file " + WorkPath + ")");
                        }
                    }
                }
                if (core.IsFaulted) Debug.LogException(core.Exception.GetBaseException());
            }
            finally
            {
                try
                {
                    if (!_nativeFinished) DeleteWorkOutput();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
                _releasedTcs.TrySetResult(true);
                if (!_committing) _settledTcs.TrySetResult(true);
            }
        }
    }
}
