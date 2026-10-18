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
    /// to native (which deletes partial output); a late native completion for a finished id is ignored.
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
        }

        long _nextId;
        readonly ConcurrentDictionary<long, Op> _ops = new ConcurrentDictionary<long, Op>();

        /// <summary>Number of operations not completed yet (diagnostics and tests).</summary>
        public int PendingCount => _ops.Count;

        /// <summary>
        /// Starts a native operation: returns a cancelled task without calling <paramref name="start"/> if
        /// <paramref name="ct"/> is already cancelled; otherwise registers the operation, calls
        /// <paramref name="start"/>(id) and only then arms <paramref name="ct"/> (so <paramref name="cancelNative"/>(id)
        /// always follows the start). An exception from <paramref name="start"/> fails the task with
        /// <paramref name="wrapStartError"/>(exception). Main thread.
        /// </summary>
        public Task<string> Run(IProgress<double> progress, CancellationToken ct, Action<long> start, Action<long> cancelNative,
            Func<Exception, MediaException> wrapStartError)
        {
            if (ct.IsCancellationRequested) return Task.FromCanceled<string>(ct);
            long id = Begin(out Task<string> task, progress);
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
        public long Begin(out Task<string> task, IProgress<double> progress)
        {
            long id = Interlocked.Increment(ref _nextId);
            var op = new Op
            {
                Tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously),
                Progress = progress,
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
                    if (_ops.TryRemove(id, out Op removed))
                    {
                        removed.Registration.Dispose();
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

        /// <summary>Fails an operation synchronously (e.g. the native start call threw).</summary>
        public void Fail(long id, Exception error)
        {
            if (_ops.TryRemove(id, out Op op))
            {
                op.Registration.Dispose();
                op.Tcs.TrySetException(error);
            }
        }

        /// <summary>Native completion; any thread.</summary>
        public void Complete(long id, int status, string json)
        {
            MainThread.Post(() =>
            {
                if (!_ops.TryRemove(id, out Op op)) return;
                op.Registration.Dispose();
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

        /// <summary>Cancels every pending task (bridge disposal).</summary>
        public void CancelAll()
        {
            foreach (var pair in _ops)
            {
                if (_ops.TryRemove(pair.Key, out Op op))
                {
                    op.Registration.Dispose();
                    op.Tcs.TrySetCanceled();
                }
            }
        }
    }
}
