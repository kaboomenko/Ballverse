namespace Ballverse.Services
{
    /// <summary>
    /// The IAB TCF v2 values the UMP SDK writes into the platform's default preferences after a GDPR consent decision
    /// (Android default SharedPreferences, iOS NSUserDefaults): <c>IABTCF_gdprApplies</c> and <c>IABTCF_PurposeConsents</c>.
    /// They carry the user's actual choice, which the UMP consent status (<see cref="ConsentRequirement.Obtained"/>, "the
    /// user has answered") does not.
    /// </summary>
    public readonly struct TcfSignals
    {
        public const string GdprAppliesKey = "IABTCF_gdprApplies";
        public const string PurposeConsentsKey = "IABTCF_PurposeConsents";

        public TcfSignals(bool? gdprApplies, string purposeConsents)
        {
            GdprApplies = gdprApplies;
            PurposeConsents = string.IsNullOrEmpty(purposeConsents) ? null : purposeConsents;
        }

        /// <summary>True: GDPR applies to this user; false: it does not; null: not written or unreadable.</summary>
        public bool? GdprApplies { get; }

        /// <summary>Binary string, character n-1 is purpose n ("1" = consent); null when not written or unreadable.</summary>
        public string PurposeConsents { get; }

        /// <summary>True when purpose <paramref name="purpose"/> (1-based TCF purpose id) has consent.</summary>
        public bool HasPurposeConsent(int purpose)
        {
            return PurposeConsents != null && purpose >= 1 && purpose <= PurposeConsents.Length && PurposeConsents[purpose - 1] == '1';
        }

        public override string ToString()
        {
            return "gdprApplies=" + (GdprApplies.HasValue ? (GdprApplies.Value ? "1" : "0") : "?") + " purposes=" + (PurposeConsents ?? "?");
        }
    }

    /// <summary>Reads <see cref="TcfSignals"/> from the platform preferences. Never throws.</summary>
    public interface ITcfStorage
    {
        TcfSignals Read();
    }

    /// <summary>Editor, tests, builds without UMP: nothing written.</summary>
    public sealed class NoTcfStorage : ITcfStorage
    {
        public static readonly NoTcfStorage Instance = new NoTcfStorage();

        public TcfSignals Read()
        {
            return default;
        }
    }

#if !BALLVERSE_STORE // fakes are not compiled into Store builds (spec §8.9)
    /// <summary>Scriptable TCF values for tests.</summary>
    public sealed class FakeTcfStorage : ITcfStorage
    {
        public TcfSignals Signals { get; set; }

        public TcfSignals Read()
        {
            return Signals;
        }
    }
#endif

    /// <summary>Outcome of <see cref="ConsentModeRules.Decide"/>.</summary>
    public readonly struct AnalyticsConsentDecision
    {
        public AnalyticsConsentDecision(bool hasDecision, AnalyticsConsent consent)
        {
            HasDecision = hasDecision;
            Consent = consent;
        }

        /// <summary>False: the user has not decided yet; analytics keeps buffering (spec §9.1).</summary>
        public bool HasDecision { get; }

        /// <summary>The Consent Mode values to pass to Firebase; meaningful only with <see cref="HasDecision"/>.</summary>
        public AnalyticsConsent Consent { get; }
    }

    /// <summary>
    /// Spec §9.1 "Сбор аналитики начинается после решения пользователя в UMP (где требуется); режим согласия Google Consent
    /// Mode передаётся в Firebase", as a pure function of the UMP status and the TCF values:
    /// <list type="bullet">
    /// <item><see cref="ConsentRequirement.NotRequired"/>: collection on, everything granted.</item>
    /// <item><see cref="ConsentRequirement.Obtained"/> (the user answered): collection on. <c>analytics_storage</c> is granted
    /// only when GDPR does not apply (US-state messages are opt-out and do not restrict first-party analytics), or when it
    /// applies and the user consented to TCF purposes 1 (store/access information on a device), 8 (measure content
    /// performance) and 9 (understand audiences through statistics). Unreadable TCF values deny it. The ad signals are not
    /// set: Firebase and the ads SDK read them from the TCF string themselves.</item>
    /// <item><see cref="ConsentRequirement.Required"/> / <see cref="ConsentRequirement.Unknown"/>: no decision; nothing is sent
    /// (events stay in the bounded buffer) until a later gather or the privacy options form delivers one.</item>
    /// </list>
    /// </summary>
    public static class ConsentModeRules
    {
        public const int PurposeStoreAndAccess = 1;
        public const int PurposeMeasureContent = 8;
        public const int PurposeAudienceStatistics = 9;

        public static AnalyticsConsentDecision Decide(ConsentRequirement requirement, TcfSignals tcf)
        {
            switch (requirement)
            {
                case ConsentRequirement.NotRequired:
                    return new AnalyticsConsentDecision(true, AnalyticsConsent.AllGranted);
                case ConsentRequirement.Obtained:
                    return new AnalyticsConsentDecision(true, new AnalyticsConsent(AnalyticsStorageGranted(tcf), null, null, null));
                default:
                    return new AnalyticsConsentDecision(false, default);
            }
        }

        /// <summary>The <c>analytics_storage</c> value for a user who answered the UMP message.</summary>
        public static bool AnalyticsStorageGranted(TcfSignals tcf)
        {
            if (tcf.GdprApplies == false) return true;
            if (tcf.GdprApplies != true) return false;
            return tcf.HasPurposeConsent(PurposeStoreAndAccess)
                   && tcf.HasPurposeConsent(PurposeMeasureContent)
                   && tcf.HasPurposeConsent(PurposeAudienceStatistics);
        }
    }
}
