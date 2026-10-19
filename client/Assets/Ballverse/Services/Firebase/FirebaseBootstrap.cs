#if BALLVERSE_FIREBASE
using System;
using System.Collections.Generic;
using Firebase;

namespace Ballverse.Services
{
    /// <summary>
    /// One-time Firebase start (spec §7.9 frame 0: Crashlytics first). <c>CheckAndFixDependenciesAsync</c> runs in the
    /// background; nothing waits for it on the main thread. Analytics, Crashlytics and Remote Config register with
    /// <see cref="WhenReady"/>. Requires the Firebase Unity SDK and google-services.json / GoogleService-Info.plist.
    /// </summary>
    public sealed class FirebaseBootstrap
    {
        const string Tag = "Firebase";

        readonly MainThreadQueue _queue;
        readonly IServiceLog _log;
        readonly List<Action<bool>> _waiters = new List<Action<bool>>();
        bool _started;

        public FirebaseBootstrap(MainThreadQueue queue, IServiceLog log)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _log = log ?? NullServiceLog.Instance;
        }

        /// <summary>Null while checking; then whether Firebase can be used this session.</summary>
        public bool? IsReady { get; private set; }

        public void Start()
        {
            if (_started) return;
            _started = true;
            try
            {
                FirebaseApp.CheckAndFixDependenciesAsync().ContinueWith(task =>
                {
                    bool ok = false;
                    string problem = null;
                    if (task.IsFaulted || task.IsCanceled)
                    {
                        problem = task.Exception != null ? task.Exception.GetBaseException().Message : "cancelled";
                    }
                    else if (task.Result == DependencyStatus.Available)
                    {
                        ok = true;
                    }
                    else
                    {
                        problem = task.Result.ToString();
                    }
                    _queue.Post(() => Complete(ok, problem));
                });
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Firebase dependency check could not start.", e);
                Complete(false, e.Message);
            }
        }

        /// <summary>Calls <paramref name="callback"/> on the main thread once the outcome is known (immediately if known).</summary>
        public void WhenReady(Action<bool> callback)
        {
            if (callback == null) return;
            if (IsReady.HasValue)
            {
                callback(IsReady.Value);
                return;
            }
            _waiters.Add(callback);
        }

        void Complete(bool ok, string problem)
        {
            if (IsReady.HasValue) return;
            if (ok)
            {
                try
                {
                    // Creates the default app from google-services.json / GoogleService-Info.plist.
                    var app = FirebaseApp.DefaultInstance;
                    ok = app != null;
                }
                catch (Exception e)
                {
                    ok = false;
                    problem = e.Message;
                }
            }
            IsReady = ok;
            if (ok) _log.Info(Tag, "Firebase ready.");
            else _log.Warning(Tag, "Firebase unavailable: " + problem);
            var waiters = _waiters.ToArray();
            _waiters.Clear();
            foreach (var w in waiters)
            {
                try
                {
                    w(ok);
                }
                catch (Exception e)
                {
                    _log.Error(Tag, "Firebase ready callback threw.", e);
                }
            }
        }
    }
}
#endif
