using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Ballverse.Media.Internal
{
    /// <summary>Native completion status codes shared by the Java and Objective-C++ plugins.</summary>
    internal static class NativeStatus
    {
        public const int Ok = 0;
        public const int Cancelled = 1;
        public const int Error = 2;
    }

    /// <summary>
    /// Registry of in-flight asynchronous native operations keyed by a 64-bit request id. Native code reports
    /// completion as (id, status, json) and progress as (id, 0..1) from any thread; this class marshals both to the
    /// main thread and completes the task there. Cancellation completes the task immediately and forwards the cancel
    /// to native (which deletes partial output); a late native completion for a finished id is ignored. Operations
    /// started with <c>cancelWaitsForNative</c> (segment joins) instead stay pending after the cancel until native reports
    /// that it stopped (then always as cancelled), bounded by <see cref="NativeCancelTimeoutMs"/>.
    /// </summary>
    /// <remarks>
    /// Ordering: the cancellation token is armed only AFTER the native operation was started (<see cref="Run"/>), so the
    /// native cancel for an id can never run before the native start for that id (a token cancelled before the start
    /// does not start the native operation at all).
    /// </remarks>
    internal sealed class NativeOps
    {
        sealed class Op
        {
            public TaskCompletionSource<string> Tcs;
            public IProgress<double> Progress;
            public CancellationTokenRegistration Registration;
            public bool KeepOnCancelAll;
            public bool CancelWaitsForNative;
            public Action<long> CancelNative;
            public bool CancelRequested;
            public CancellationToken CancelToken;
            /// <summary>Fallback timer of a forwarded cancel (cancelWaitsForNative), or null. Stopped when the op finishes.</summary>
            public CancellationTokenSource CancelTimer;
        }

        /// <summary>Default of <see cref="NativeCancelTimeoutMs"/>.</summary>
        internal const int DefaultNativeCancelTimeoutMs = 20000;

        /// <summary>
        /// Upper bound for a <c>cancelWaitsForNative</c> operation to report after its cancel (a native call that never
        /// returns). After it the task is cancelled anyway with an error log. Read when the cancel is forwarded.
        /// </summary>
        internal static int NativeCancelTimeoutMs = DefaultNativeCancelTimeoutMs;

        long _nextId;
        int _armedCancelTimers;
        readonly ConcurrentDictionary<long, Op> _ops = new ConcurrentDictionary<long, Op>();

        /// <summary>Number of operations not completed yet (diagnostics and tests).</summary>
        public int PendingCount => _ops.Count;

        /// <summary>
        /// Number of cancel fallback timers that are still running (diagnostics and tests). Every way an operation finishes
        /// (native report, failure, cancel, <see cref="CancelAll"/>, the timeout itself) stops its timer, so this is never
        /// larger than the number of pending cancelWaitsForNative operations whose cancel was forwarded.
        /// </summary>
        internal int ArmedCancelTimerCount => Volatile.Read(ref _armedCancelTimers);

        /// <summary>
        /// Starts a native operation: returns a cancelled task without calling <paramref name="start"/> if
        /// <paramref name="ct"/> is already cancelled; otherwise registers the operation, calls
        /// <paramref name="start"/>(id) and only then arms <paramref name="ct"/> (so <paramref name="cancelNative"/>(id)
        /// always follows the start). An exception from <paramref name="start"/> fails the task with
        /// <paramref name="wrapStartError"/>(exception). <paramref name="keepOnCancelAll"/>: <see cref="CancelAll"/> (bridge
        /// disposal) leaves the operation pending so that it still completes with the native result (writer releases,
        /// whose completion means "the native side has stopped"). <paramref name="cancelWaitsForNative"/>: a cancel (token
        /// or <see cref="CancelAll"/>) forwards to native but the task completes — as cancelled — only when native reports
        /// (the native side reports after it stopped and deleted its own output), or after <see cref="NativeCancelTimeoutMs"/>.
        /// Main thread.
        /// </summary>
        public Task<string> Run(IProgress<double> progress, CancellationToken ct, Action<long> start, Action<long> cancelNative,
            Func<Exception, MediaException> wrapStartError, bool keepOnCancelAll = false, bool cancelWaitsForNative = false)
        {
            if (ct.IsCancellationRequested) return Task.FromCanceled<string>(ct);
            long id = Begin(out Task<string> task, progress, keepOnCancelAll);
            if (cancelWaitsForNative && _ops.TryGetValue(id, out Op op))
            {
                op.CancelWaitsForNative = true;
                op.CancelNative = cancelNative;
            }
            try
            {
                start(id);
            }
            catch (Exception e)
            {
                Fail(id, wrapStartError != null ? wrapStartError(e) : new MediaException(MediaErrorCode.Unknown, e.Message, e));
                return task;
            }
            Arm(id, ct, cancelNative);
            return task;
        }

        /// <summary>
        /// Registers an operation and returns its id; call the native start function with the id afterwards, then
        /// <see cref="Arm"/>. Prefer <see cref="Run"/>.
        /// </summary>
        public long Begin(out Task<string> task, IProgress<double> progress, bool keepOnCancelAll = false)
        {
            long id = Interlocked.Increment(ref _nextId);
            var op = new Op
            {
                Tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously),
                Progress = progress,
                KeepOnCancelAll = keepOnCancelAll,
            };
            _ops[id] = op;
            task = op.Tcs.Task;
            return id;
        }

        /// <summary>
        /// Connects <paramref name="ct"/> to a started operation (main thread). When the token fires, the task is
        /// cancelled at once and <paramref name="cancelNative"/>(id) runs on the main thread. A token that is already
        /// cancelled cancels synchronously. No-op if the operation already completed.
        /// </summary>
        public void Arm(long id, CancellationToken ct, Action<long> cancelNative)
        {
            if (!ct.CanBeCanceled || !_ops.TryGetValue(id, out Op op)) return;
            CancellationTokenRegistration registration = ct.Register(() =>
            {
                MainThread.Run(() =>
                {
                    if (op.CancelWaitsForNative)
                    {
                        RequestNativeCancel(id, op, ct);
                        return;
                    }
                    if (TryFinish(id, out Op removed))
                    {
                        removed.Tcs.TrySetCanceled(ct);
                        if (cancelNative != null)
                        {
                            try { cancelNative(id); }
                            catch (Exception e) { Debug.LogException(e); }
                        }
                    }
                });
            });
            if (_ops.TryGetValue(id, out Op current) && ReferenceEquals(current, op)) op.Registration = registration;
            else registration.Dispose(); // completed or cancelled inline meanwhile
        }

        /// <summary>
        /// cancelWaitsForNative: forwards the cancel once and keeps the operation pending until native reports (main
        /// thread). A fallback timer cancels the task if native never reports within <see cref="NativeCancelTimeoutMs"/>.
        /// </summary>
        /// <remarks>
        /// The timer belongs to the operation: whichever path finishes the operation first stops it (<see cref="TryFinish"/>),
        /// so a native report in time leaves no timer behind. A timer that fires anyway (it raced with the finish) finds
        /// the operation gone and does nothing — no post to the main thread, no log. Before this, a 20 s
        /// <c>Task.Delay</c> outlived every operation whose native side reported in time and posted from the thread pool
        /// long after its caller (in CI: an EditMode test) had finished.
        /// </remarks>
        void RequestNativeCancel(long id, Op op, CancellationToken ct)
        {
            if (op.CancelRequested || !IsPending(id, op)) return;
            op.CancelRequested = true;
            op.CancelToken = ct;
            if (op.CancelNative != null)
            {
                try { op.CancelNative(id); }
                catch (Exception e) { Debug.LogException(e); }
            }
            // cancelNative may have completed the operation synchronously (Complete → posted; Fail → removed already).
            if (!IsPending(id, op)) return;
            int timeoutMs = NativeCancelTimeoutMs;
            if (timeoutMs < 0) return; // Timeout.Infinite: wait for native forever
            var timer = new CancellationTokenSource();
            op.CancelTimer = timer;
            Interlocked.Increment(ref _armedCancelTimers);
            CancellationToken timerToken = timer.Token;
            Task.Delay(timeoutMs, timerToken).ContinueWith(
                _ => OnCancelTimeout(id, op, timerToken, timeoutMs),
                CancellationToken.None, TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        /// <summary>The cancel fallback timer fired (thread pool). Does nothing unless the operation is still pending.</summary>
        void OnCancelTimeout(long id, Op op, CancellationToken timerToken, int timeoutMs)
        {
            if (timerToken.IsCancellationRequested || !IsPending(id, op)) return;
            MainThread.Post(() =>
            {
                if (timerToken.IsCancellationRequested || !IsPending(id, op) || !TryFinish(id, out _)) return;
                Debug.LogError("[Media] native operation " + id + " did not stop within " + timeoutMs + " ms of its cancel; continuing");
                op.Tcs.TrySetCanceled(op.CancelToken);
            });
        }

        bool IsPending(long id, Op op)
        {
            return _ops.TryGetValue(id, out Op current) && ReferenceEquals(current, op);
        }

        /// <summary>
        /// Removes a pending operation and releases what it holds (token registration, cancel fallback timer). Every path
        /// that completes an operation's task goes through here; false if it already finished.
        /// </summary>
        bool TryFinish(long id, out Op op)
        {
            if (!_ops.TryRemove(id, out op)) return false;
            op.Registration.Dispose();
            StopCancelTimer(op);
            return true;
        }

        void StopCancelTimer(Op op)
        {
            CancellationTokenSource timer = Interlocked.Exchange(ref op.CancelTimer, null);
            if (timer == null) return;
            timer.Cancel(); // cancels the pending Task.Delay: its OnlyOnRanToCompletion continuation never runs
            timer.Dispose();
            Interlocked.Decrement(ref _armedCancelTimers);
        }

        /// <summary>Fails an operation synchronously (e.g. the native start call threw).</summary>
        public void Fail(long id, Exception error)
        {
            if (TryFinish(id, out Op op))
            {
                op.Tcs.TrySetException(error);
            }
        }

        /// <summary>Native completion; any thread.</summary>
        public void Complete(long id, int status, string json)
        {
            MainThread.Post(() =>
            {
                if (!TryFinish(id, out Op op)) return;
                if (op.CancelRequested)
                {
                    // Cancelled by C#: native has stopped now; whatever it produced is discarded by the caller.
                    op.Tcs.TrySetCanceled(op.CancelToken);
                    return;
                }
                switch (status)
                {
                    case NativeStatus.Ok:
                        op.Tcs.TrySetResult(json ?? string.Empty);
                        break;
                    case NativeStatus.Cancelled:
                        op.Tcs.TrySetCanceled();
                        break;
                    default:
                        op.Tcs.TrySetException(NativeJson.ParseError(json));
                        break;
                }
            });
        }

        /// <summary>Native progress; any thread.</summary>
        public void Progress(long id, double value)
        {
            if (!_ops.TryGetValue(id, out Op peek) || peek.Progress == null) return;
            MainThread.Post(() =>
            {
                if (_ops.TryGetValue(id, out Op op) && op.Progress != null)
                {
                    op.Progress.Report(value < 0 ? 0 : value > 1 ? 1 : value);
                }
            });
        }

        /// <summary>
        /// Cancels every pending task (bridge disposal) except those started with keepOnCancelAll; those started with
        /// cancelWaitsForNative get a native cancel and complete when native reports. Main thread.
        /// </summary>
        public void CancelAll()
        {
            foreach (var pair in _ops)
            {
                if (pair.Value.KeepOnCancelAll) continue;
                if (pair.Value.CancelWaitsForNative)
                {
                    RequestNativeCancel(pair.Key, pair.Value, CancellationToken.None);
                    continue;
                }
                if (TryFinish(pair.Key, out Op op))
                {
                    op.Tcs.TrySetCanceled();
                }
            }
        }
    }
}
