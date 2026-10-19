using System.Collections.Generic;
using System.Text;

namespace Ballverse.Services
{
    /// <summary>
    /// Production analytics of builds without Firebase (release 1, every variant including Store): applies the shared
    /// behaviour of <see cref="AnalyticsServiceBase"/> (sanitizing, consent buffering) and then only writes each event to the
    /// service log when <c>verbose</c> (development builds). Nothing is sent anywhere and nothing is kept after the call.
    /// </summary>
    public sealed class LogAnalyticsService : AnalyticsServiceBase
    {
        const string Tag = "Analytics";

        readonly bool _verbose;

        /// <param name="log">Service log.</param>
        /// <param name="verbose">Write events and user properties to <paramref name="log"/>; off in release builds, where the
        /// lines would be dropped anyway, so they are not even formatted.</param>
        public LogAnalyticsService(IServiceLog log, bool verbose) : base(log)
        {
            _verbose = verbose;
            MarkBackendReady();
        }

        public bool Verbose => _verbose;

        protected override void SendEvent(string name, IReadOnlyList<AnalyticsParam> parameters)
        {
            if (!_verbose) return;
            var line = new StringBuilder(name).Append('(');
            for (int i = 0; i < parameters.Count; i++)
            {
                if (i > 0) line.Append(", ");
                line.Append(parameters[i].ToString());
            }
            Log.Info(Tag, line.Append(')').ToString());
        }

        protected override void SendUserProperty(string name, string value)
        {
            if (_verbose) Log.Info(Tag, "user property " + name + (value == null ? " cleared" : " = " + value));
        }

        protected override void ApplyCollectionEnabled(bool enabled)
        {
            if (_verbose) Log.Info(Tag, "collection " + (enabled ? "on" : "off"));
        }

        protected override void ApplyConsent(AnalyticsConsent consent)
        {
            // Nothing is collected by a backend, so there is no consent mode to forward; the base class already gates events.
        }
    }
}
