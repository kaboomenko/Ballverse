namespace Ballverse.Services
{
    /// <summary>
    /// Consent Mode v2 signals passed to Firebase (spec §9.1). A null signal is left as it is: for users who answered the UMP
    /// form the TCF string written by UMP governs the ad signals, so they are not overridden here.
    /// </summary>
    public readonly struct AnalyticsConsent
    {
        public bool? AnalyticsStorage { get; }
        public bool? AdStorage { get; }
        public bool? AdUserData { get; }
        public bool? AdPersonalization { get; }

        public AnalyticsConsent(bool? analyticsStorage, bool? adStorage, bool? adUserData, bool? adPersonalization)
        {
            AnalyticsStorage = analyticsStorage;
            AdStorage = adStorage;
            AdUserData = adUserData;
            AdPersonalization = adPersonalization;
        }

        public static AnalyticsConsent AllGranted => new AnalyticsConsent(true, true, true, true);
    }

    /// <summary>
    /// Product analytics (spec §9). Events and parameters are sanitized by <see cref="AnalyticsSanitizer"/> (no PII, Firebase
    /// limits). Until the consent decision (<see cref="SetCollectionEnabled"/>) events are buffered, not sent (spec §9.1).
    /// Main thread only.
    /// </summary>
    public interface IAnalyticsService
    {
        /// <summary>Null until decided; then the last value passed to <see cref="SetCollectionEnabled"/>.</summary>
        bool? CollectionEnabled { get; }

        void LogEvent(string name, params AnalyticsParam[] parameters);

        /// <summary>Sets a §9.2 user property; null clears it.</summary>
        void SetUserProperty(string name, string value);

        /// <summary>True flushes the buffered events; false drops them and everything that follows.</summary>
        void SetCollectionEnabled(bool enabled);

        void SetConsent(AnalyticsConsent consent);
    }
}
