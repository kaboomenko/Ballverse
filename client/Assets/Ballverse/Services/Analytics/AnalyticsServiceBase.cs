using System.Collections.Generic;

namespace Ballverse.Services
{
    /// <summary>
    /// Shared behaviour of the analytics implementations: sanitizing, buffering until the consent decision and until the
    /// backend (Firebase) is ready, replay of user properties and consent once it is.
    /// </summary>
    public abstract class AnalyticsServiceBase : IAnalyticsService
    {
        /// <summary>Events kept while collection is undecided or the backend is not ready; older ones are dropped first.</summary>
        public const int MaxBufferedEvents = 200;

        const string Tag = "Analytics";

        readonly Queue<KeyValuePair<string, List<AnalyticsParam>>> _buffer = new Queue<KeyValuePair<string, List<AnalyticsParam>>>();
        readonly Dictionary<string, string> _userProperties = new Dictionary<string, string>();
        readonly List<string> _userPropertyOrder = new List<string>();
        AnalyticsConsent? _consent;
        bool _backendReady;

        protected readonly IServiceLog Log;

        protected AnalyticsServiceBase(IServiceLog log)
        {
            Log = log ?? NullServiceLog.Instance;
        }

        public bool? CollectionEnabled { get; private set; }

        /// <summary>Events currently buffered.</summary>
        public int BufferedCount => _buffer.Count;

        public void LogEvent(string name, params AnalyticsParam[] parameters)
        {
            if (!AnalyticsSanitizer.TrySanitizeEvent(name, parameters, out var clean, out var dropped))
            {
                Log.Warning(Tag, "Dropped event with an invalid name: '" + name + "'.");
                return;
            }
            if (dropped != null) Log.Warning(Tag, name + ": removed or redacted parameters " + string.Join(",", dropped) + ".");

            if (CollectionEnabled == false) return;
            if (CollectionEnabled == null || !_backendReady)
            {
                if (_buffer.Count >= MaxBufferedEvents) _buffer.Dequeue();
                _buffer.Enqueue(new KeyValuePair<string, List<AnalyticsParam>>(name, clean));
                return;
            }
            SendEvent(name, clean);
        }

        public void SetUserProperty(string name, string value)
        {
            if (!AnalyticsSanitizer.TrySanitizeUserProperty(name, value, out string clean))
            {
                Log.Warning(Tag, "Dropped unknown user property '" + name + "'.");
                return;
            }
            if (!_userProperties.ContainsKey(name)) _userPropertyOrder.Add(name);
            _userProperties[name] = clean;
            if (_backendReady) SendUserProperty(name, clean);
        }

        public void SetCollectionEnabled(bool enabled)
        {
            CollectionEnabled = enabled;
            if (!enabled) _buffer.Clear();
            if (_backendReady)
            {
                ApplyCollectionEnabled(enabled);
                if (enabled) Flush();
            }
        }

        public void SetConsent(AnalyticsConsent consent)
        {
            _consent = consent;
            if (_backendReady) ApplyConsent(consent);
        }

        /// <summary>Called by the implementation once its backend can accept calls; replays the stored state.</summary>
        protected void MarkBackendReady()
        {
            if (_backendReady) return;
            _backendReady = true;
            // Undecided: keep the backend's own collection off until the consent decision (spec §9.1); our buffer holds the events.
            ApplyCollectionEnabled(CollectionEnabled ?? false);
            if (_consent.HasValue) ApplyConsent(_consent.Value);
            foreach (string key in _userPropertyOrder) SendUserProperty(key, _userProperties[key]);
            if (CollectionEnabled == true) Flush();
        }

        void Flush()
        {
            while (_buffer.Count > 0)
            {
                var e = _buffer.Dequeue();
                SendEvent(e.Key, e.Value);
            }
        }

        protected abstract void SendEvent(string name, IReadOnlyList<AnalyticsParam> parameters);
        protected abstract void SendUserProperty(string name, string value);
        protected abstract void ApplyCollectionEnabled(bool enabled);
        protected abstract void ApplyConsent(AnalyticsConsent consent);
    }
}
