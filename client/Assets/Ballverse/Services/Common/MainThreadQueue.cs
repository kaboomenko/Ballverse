using System;
using System.Collections.Generic;

namespace Ballverse.Services
{
    /// <summary>
    /// Hands SDK callbacks to the Unity main thread. Google Mobile Ads, UMP, ATT and Firebase may call back on other threads;
    /// every SDK adapter posts its callbacks here and <see cref="ServicesRunner"/> drains the queue once per frame, so all
    /// service logic runs single-threaded. Thread-safe.
    /// </summary>
    public sealed class MainThreadQueue
    {
        readonly object _lock = new object();
        List<Action> _pending = new List<Action>();
        List<Action> _running = new List<Action>();
        readonly IServiceLog _log;

        public MainThreadQueue(IServiceLog log = null)
        {
            _log = log ?? NullServiceLog.Instance;
        }

        /// <summary>Queues <paramref name="action"/> for the next <see cref="Drain"/>. Safe from any thread.</summary>
        public void Post(Action action)
        {
            if (action == null) return;
            lock (_lock)
            {
                _pending.Add(action);
            }
        }

        /// <summary>Number of queued actions.</summary>
        public int Count
        {
            get
            {
                lock (_lock) return _pending.Count;
            }
        }

        /// <summary>
        /// Runs the queued actions in posting order (main thread). Actions posted while draining run in the next drain.
        /// An exception in one action is logged and does not stop the others.
        /// </summary>
        /// <returns>Number of actions run.</returns>
        public int Drain()
        {
            List<Action> batch;
            lock (_lock)
            {
                if (_pending.Count == 0) return 0;
                batch = _pending;
                _pending = _running;
                _running = batch;
            }
            int n = batch.Count;
            for (int i = 0; i < n; i++)
            {
                try
                {
                    batch[i]();
                }
                catch (Exception e)
                {
                    _log.Error("MainThreadQueue", "Queued callback threw.", e);
                }
            }
            batch.Clear();
            return n;
        }
    }
}
