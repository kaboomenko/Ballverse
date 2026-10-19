using System;
using System.Threading;
using UnityEngine;

namespace Ballverse.Media.Internal
{
    /// <summary>
    /// Small main-thread dispatcher for native callbacks. Captures Unity's <see cref="SynchronizationContext"/>
    /// (UnitySynchronizationContext, drained once per frame by the player loop) and the main thread id at startup;
    /// <see cref="Post"/> may be called from any thread (native codec threads, Java threads, GCD queues, finalizers).
    /// Posted actions run on the main thread in the next player-loop update; exceptions are logged, never rethrown.
    /// </summary>
    internal static class MainThread
    {
        static SynchronizationContext s_context;
        static int s_threadId = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void CaptureAtStartup()
        {
            Capture();
        }

        /// <summary>Captures the context if called on a thread that has one (idempotent). Called by the bridge factory too.</summary>
        internal static void Capture()
        {
            SynchronizationContext current = SynchronizationContext.Current;
            if (current == null) return;
            s_context = current;
            s_threadId = Thread.CurrentThread.ManagedThreadId;
        }

        public static bool IsAvailable => s_context != null;

        /// <summary>The captured main-thread dispatcher (Unity's context; a test pump in tests), or null before capture.</summary>
        internal static SynchronizationContext Context => s_context;

        public static bool IsMainThread => s_threadId == Thread.CurrentThread.ManagedThreadId;

        /// <summary>Throws unless called on the main thread.</summary>
        public static void Assert(string api)
        {
            if (s_context == null) Capture();
            if (!IsMainThread) throw new InvalidOperationException(api + " must be called from the Unity main thread.");
        }

        /// <summary>
        /// Queues <paramref name="action"/> to the main thread. Returns false if no context is captured: the action is dropped
        /// with a warning.
        /// </summary>
        /// <remarks>
        /// A missing context is a warning, not an error. In a player the context is captured at startup, before any native
        /// operation can exist, so this cannot happen there. It happens for callbacks that arrive after their dispatcher went
        /// away — native reports, token callbacks or timers that outlive an editor test whose <see cref="OverrideForTests"/>
        /// scope ended (EditMode never runs <c>RuntimeInitializeOnLoadMethod</c>, so no context is captured between tests).
        /// Those callbacks are late by definition and have nobody left to deliver to. An error log there was worse than
        /// useless: Unity's test runner fails whichever test happens to be running when an unexpected error is logged,
        /// even from another thread, so a late callback failed an unrelated test (CI: UiShellTests after NativeOpsTests).
        /// </remarks>
        public static bool Post(Action action)
        {
            if (action == null) return false;
            SynchronizationContext ctx = s_context;
            if (ctx == null)
            {
                Debug.LogWarning("[Media] MainThread.Post without a captured main-thread context (late callback after teardown?); callback dropped.");
                return false;
            }
            ctx.Post(RunPosted, action);
            return true;
        }

        /// <summary>Runs inline when already on the main thread, otherwise posts.</summary>
        public static void Run(Action action)
        {
            if (IsMainThread)
            {
                RunPosted(action);
            }
            else
            {
                Post(action);
            }
        }

        /// <summary>Runs inline on the main thread, posts from other threads, and runs inline as a last resort when no context exists.</summary>
        public static void RunOrInline(Action action)
        {
            if (IsMainThread || s_context == null)
            {
                RunPosted(action);
                return;
            }
            s_context.Post(RunPosted, action);
        }

        /// <summary>
        /// Tests only: treats the calling thread as the main thread with <paramref name="context"/> as its dispatcher until
        /// the returned scope is disposed (restores the previous state). Lets tests pump posted callbacks themselves.
        /// </summary>
        internal static IDisposable OverrideForTests(SynchronizationContext context)
        {
            var scope = new Scope(s_context, s_threadId);
            s_context = context;
            s_threadId = Thread.CurrentThread.ManagedThreadId;
            return scope;
        }

        sealed class Scope : IDisposable
        {
            readonly SynchronizationContext _context;
            readonly int _threadId;
            bool _disposed;

            public Scope(SynchronizationContext context, int threadId)
            {
                _context = context;
                _threadId = threadId;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                s_context = _context;
                s_threadId = _threadId;
            }
        }

        static void RunPosted(object state)
        {
            try
            {
                ((Action)state)();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
