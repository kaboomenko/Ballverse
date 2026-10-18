using System;

namespace Ballverse.Services
{
    /// <summary>
    /// Consent in builds without the Google Mobile Ads package (no ads SDK, so no UMP): no form is needed, the decision is
    /// "not required" at once (analytics Consent Mode then grants, spec §9.1), and the privacy options entry is not offered.
    /// The ATT sequence is kept for completeness, although without an ads SDK nothing asks for it.
    /// </summary>
    public sealed class NoConsentService : IConsentService
    {
        readonly TrackingDecisionFlow _tracking;

        public NoConsentService(ITrackingAuthorization tracking, IClock clock, IServiceLog log)
        {
            _tracking = new TrackingDecisionFlow(tracking ?? new NoTrackingAuthorization(), clock, log);
        }

        public ConsentGatherState GatherState { get; private set; }

        public ConsentRequirement Requirement { get; private set; }

        public bool CanRequestAds { get; private set; }

        public PrivacyOptionsRequirement PrivacyOptions => PrivacyOptionsRequirement.NotRequired;

        public TcfSignals Tcf => default;

        public event Action ConsentChanged;

        public TrackingAuthorizationStatus TrackingStatus => _tracking.Status;

        public IAttPrePrompt AttPrePrompt { get; set; }

        public void Gather(Action<ConsentGatherResult> done)
        {
            GatherState = ConsentGatherState.Completed;
            Requirement = ConsentRequirement.NotRequired;
            CanRequestAds = true;
            done?.Invoke(new ConsentGatherResult(true, ConsentRequirement.NotRequired, null));
            ConsentChanged?.Invoke();
        }

        public void ShowPrivacyOptions(Action<string> done)
        {
            done?.Invoke("not_available");
        }

        public void EnsureTrackingDecision(Action done)
        {
            _tracking.Ensure(AttPrePrompt, done);
        }

        public void AbandonTrackingDecision()
        {
            _tracking.Abandon("abandoned by the caller");
        }
    }
}
