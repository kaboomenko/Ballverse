#if BALLVERSE_FIREBASE
using System.Collections.Generic;
using Firebase.Analytics;

namespace Ballverse.Services
{
    /// <summary>Firebase Analytics (spec §9.1). Calls are held until <see cref="FirebaseBootstrap"/> reports Firebase ready.</summary>
    public sealed class FirebaseAnalyticsService : AnalyticsServiceBase
    {
        public FirebaseAnalyticsService(FirebaseBootstrap firebase, IServiceLog log) : base(log)
        {
            firebase.WhenReady(ok =>
            {
                if (ok) MarkBackendReady();
                else Log.Warning("Analytics", "Firebase unavailable: analytics events are buffered and then dropped.");
            });
        }

        protected override void SendEvent(string name, IReadOnlyList<AnalyticsParam> parameters)
        {
            if (parameters.Count == 0)
            {
                FirebaseAnalytics.LogEvent(name);
                return;
            }
            var array = new Parameter[parameters.Count];
            for (int i = 0; i < array.Length; i++)
            {
                var p = parameters[i];
                switch (p.Type)
                {
                    case AnalyticsParamType.Long: array[i] = new Parameter(p.Name, p.LongValue); break;
                    case AnalyticsParamType.Double: array[i] = new Parameter(p.Name, p.DoubleValue); break;
                    default: array[i] = new Parameter(p.Name, p.StringValue); break;
                }
            }
            FirebaseAnalytics.LogEvent(name, array);
        }

        protected override void SendUserProperty(string name, string value)
        {
            FirebaseAnalytics.SetUserProperty(name, value);
        }

        protected override void ApplyCollectionEnabled(bool enabled)
        {
            FirebaseAnalytics.SetAnalyticsCollectionEnabled(enabled);
        }

        protected override void ApplyConsent(AnalyticsConsent consent)
        {
            var settings = new Dictionary<ConsentType, ConsentStatus>();
            Add(settings, ConsentType.AnalyticsStorage, consent.AnalyticsStorage);
            Add(settings, ConsentType.AdStorage, consent.AdStorage);
            Add(settings, ConsentType.AdUserData, consent.AdUserData);
            Add(settings, ConsentType.AdPersonalization, consent.AdPersonalization);
            if (settings.Count > 0) FirebaseAnalytics.SetConsent(settings);
        }

        static void Add(Dictionary<ConsentType, ConsentStatus> settings, ConsentType type, bool? granted)
        {
            // Omitted types keep their previous status (Firebase SetConsent contract).
            if (granted.HasValue) settings[type] = granted.Value ? ConsentStatus.Granted : ConsentStatus.Denied;
        }
    }
}
#endif
