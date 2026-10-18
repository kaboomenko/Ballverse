#if BALLVERSE_FIREBASE
using System;
using System.Collections.Generic;

namespace Ballverse.Services
{
    /// <summary>
    /// Firebase Crashlytics (spec §7.12). Uncaught managed exceptions are reported as fatal. Calls made before Firebase is
    /// ready are kept (bounded) and replayed.
    /// </summary>
    public sealed class FirebaseCrashReporter : ICrashReporter
    {
        const int MaxQueued = 100;

        readonly FirebaseBootstrap _firebase;
        readonly LogCrashReporter _local;
        readonly Queue<Action> _queued = new Queue<Action>();
        bool _ready;
        bool _failed;

        public FirebaseCrashReporter(FirebaseBootstrap firebase, IServiceLog log)
        {
            _firebase = firebase ?? throw new ArgumentNullException(nameof(firebase));
            _local = new LogCrashReporter(log);
        }

        /// <summary>Local breadcrumbs (spec §7.12 ring buffer).</summary>
        public string[] RecentLines()
        {
            return _local.RecentLines();
        }

        public void Initialize()
        {
            _local.Initialize();
            _firebase.Start();
            _firebase.WhenReady(ok =>
            {
                if (!ok)
                {
                    _failed = true;
                    _queued.Clear();
                    return;
                }
                Firebase.Crashlytics.Crashlytics.ReportUncaughtExceptionsAsFatal = true;
                _ready = true;
                while (_queued.Count > 0) _queued.Dequeue()();
            });
        }

        public void Log(string message)
        {
            _local.Log(message);
            Run(() => Firebase.Crashlytics.Crashlytics.Log(message ?? string.Empty));
        }

        public void RecordException(Exception exception, string errorCode = null)
        {
            if (exception == null) return;
            _local.RecordException(exception, errorCode);
            Run(() =>
            {
                if (!string.IsNullOrEmpty(errorCode)) Firebase.Crashlytics.Crashlytics.SetCustomKey("error_code", errorCode);
                Firebase.Crashlytics.Crashlytics.LogException(exception);
            });
        }

        public void SetCustomKey(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            _local.SetCustomKey(key, value);
            Run(() => Firebase.Crashlytics.Crashlytics.SetCustomKey(key, value ?? string.Empty));
        }

        public void SetCollectionEnabled(bool enabled)
        {
            _local.SetCollectionEnabled(enabled);
            Run(() => Firebase.Crashlytics.Crashlytics.IsCrashlyticsCollectionEnabled = enabled);
        }

        void Run(Action action)
        {
            if (_failed) return;
            if (_ready)
            {
                action();
                return;
            }
            if (_queued.Count >= MaxQueued) _queued.Dequeue();
            _queued.Enqueue(action);
        }
    }
}
#endif
