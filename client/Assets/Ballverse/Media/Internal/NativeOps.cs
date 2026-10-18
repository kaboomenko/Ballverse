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

        /// <summary>
        /// Registers an operation and returns its id; call the native start function with the id afterwards.
        /// <paramref name="cancelNative"/> is invoked on the main thread when <paramref name="ct"/> fires.
        /// </summary>
        public long Begin(out Task<string> task, IProgress<double> progress, CancellationToken ct, Action<long> cancelNative)
        {
            long id = Interlocked.Increment(ref _nextId);
            var op = new Op
            {
                Tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously),
                Progress = progress,
            };
            _ops[id] = op;
            task = op.Tcs.Task;
            if (ct.CanBeCanceled)
            {
                op.Registration = ct.Register(() =>
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
            }
            return id;
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
