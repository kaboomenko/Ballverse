using System;
using System.Collections.Generic;

namespace Ballverse.Services
{
    /// <summary>
    /// Crash and non-fatal error reporting (spec §7.12). Context keys and log lines must not contain personal data or user
    /// file paths. Main thread only.
    /// </summary>
    public interface ICrashReporter
    {
        /// <summary>Starts the reporter (spec §7.9 frame 0, before everything else).</summary>
        void Initialize();

        /// <summary>Breadcrumb attached to the next crash report.</summary>
        void Log(string message);

        /// <summary>Non-fatal error with an <c>ErrorCode</c> from the app's error catalogue (spec §7.12).</summary>
        void RecordException(Exception exception, string errorCode = null);

        /// <summary>Context such as mode, export stage, tier, free memory (spec §7.12).</summary>
        void SetCustomKey(string key, string value);

        void SetCollectionEnabled(bool enabled);
    }

    /// <summary>
    /// Log-based reporter for the editor, tests and builds without Firebase. Also keeps the last
    /// <see cref="RingSize"/> breadcrumbs (spec §7.12 ring buffer for the feedback e-mail).
    /// </summary>
    public sealed class LogCrashReporter : ICrashReporter
    {
        public const int RingSize = 500;

        const string Tag = "Crash";

        readonly IServiceLog _log;
        readonly Queue<string> _ring = new Queue<string>();
        readonly Dictionary<string, string> _keys = new Dictionary<string, string>();

        public LogCrashReporter(IServiceLog log)
        {
            _log = log ?? NullServiceLog.Instance;
        }

        public bool Initialized { get; private set; }

        public bool CollectionEnabled { get; private set; } = true;

        public int RecordedExceptions { get; private set; }

        public IReadOnlyDictionary<string, string> CustomKeys => _keys;

        /// <summary>Breadcrumbs, oldest first.</summary>
        public string[] RecentLines()
        {
            return _ring.ToArray();
        }

        public void Initialize()
        {
            Initialized = true;
        }

        public void Log(string message)
        {
            Remember(message);
            _log.Info(Tag, message);
        }

        public void RecordException(Exception exception, string errorCode = null)
        {
            if (exception == null) return;
            RecordedExceptions++;
            Remember("exception " + (errorCode ?? "-") + " " + exception.GetType().Name);
            _log.Error(Tag, "Non-fatal " + (errorCode ?? "(no code)"), exception);
        }

        public void SetCustomKey(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) return;
            _keys[key] = value ?? string.Empty;
        }

        public void SetCollectionEnabled(bool enabled)
        {
            CollectionEnabled = enabled;
        }

        void Remember(string line)
        {
            if (_ring.Count >= RingSize) _ring.Dequeue();
            _ring.Enqueue(line ?? string.Empty);
        }
    }
}
