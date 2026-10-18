using System.Collections.Generic;

namespace Ballverse.Services
{
    /// <summary>
    /// Fake analytics for the editor, tests and builds without Firebase: keeps what would have been sent and writes it to
    /// the service log. Behaves like the real service (sanitizing, consent buffering).
    /// </summary>
    public sealed class RecordingAnalyticsService : AnalyticsServiceBase
    {
        public sealed class Event
        {
            public Event(string name, IReadOnlyList<AnalyticsParam> parameters)
            {
                Name = name;
                Parameters = parameters;
            }

            public string Name { get; }
            public IReadOnlyList<AnalyticsParam> Parameters { get; }

            public bool TryGet(string param, out AnalyticsParam value)
            {
                foreach (var p in Parameters)
                {
                    if (p.Name == param)
                    {
                        value = p;
                        return true;
                    }
                }
                value = default;
                return false;
            }

            public override string ToString()
            {
                var parts = new List<string>();
                foreach (var p in Parameters) parts.Add(p.ToString());
                return Name + "(" + string.Join(", ", parts) + ")";
            }
        }

        readonly List<Event> _sent = new List<Event>();
        readonly Dictionary<string, string> _sentUserProperties = new Dictionary<string, string>();

        /// <summary>Retained sent events (oldest dropped beyond this many).</summary>
        public const int MaxRetained = 1000;

        public RecordingAnalyticsService(IServiceLog log = null) : base(log)
        {
            MarkBackendReady();
        }

        public IReadOnlyList<Event> Sent => _sent;

        public IReadOnlyDictionary<string, string> SentUserProperties => _sentUserProperties;

        public AnalyticsConsent? AppliedConsent { get; private set; }

        public bool? AppliedCollectionEnabled { get; private set; }

        /// <summary>Sent events with this name, in order.</summary>
        public List<Event> EventsNamed(string name)
        {
            var list = new List<Event>();
            foreach (var e in _sent)
            {
                if (e.Name == name) list.Add(e);
            }
            return list;
        }

        protected override void SendEvent(string name, IReadOnlyList<AnalyticsParam> parameters)
        {
            var e = new Event(name, parameters);
            if (_sent.Count >= MaxRetained) _sent.RemoveAt(0);
            _sent.Add(e);
            Log.Info("Analytics", e.ToString());
        }

        protected override void SendUserProperty(string name, string value)
        {
            if (value == null) _sentUserProperties.Remove(name);
            else _sentUserProperties[name] = value;
        }

        protected override void ApplyCollectionEnabled(bool enabled)
        {
            AppliedCollectionEnabled = enabled;
        }

        protected override void ApplyConsent(AnalyticsConsent consent)
        {
            AppliedConsent = consent;
        }
    }
}
