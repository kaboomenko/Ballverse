#if !BALLVERSE_STORE // fakes are not compiled into Store builds (spec §8.9)
using System;

namespace Ballverse.Services
{
    /// <summary>
    /// Consent without UMP (editor, tests, builds without the Google Mobile Ads package). By default consent is not required
    /// and ads may be requested; tests can script a required form, a refusal or an error.
    /// </summary>
    public sealed class FakeConsentService : IConsentService
    {
        readonly TrackingDecisionFlow _tracking;
        Action<ConsentGatherResult> _pending;

        public FakeConsentService(ITrackingAuthorization tracking = null, IClock clock = null, IServiceLog log = null)
        {
            _tracking = new TrackingDecisionFlow(tracking ?? new NoTrackingAuthorization(), clock, log);
        }

        /// <summary>Result <see cref="Gather"/> will produce.</summary>
        public ConsentRequirement ScriptedRequirement { get; set; } = ConsentRequirement.NotRequired;

        /// <summary>Whether ads are allowed after gathering.</summary>
        public bool ScriptedCanRequestAds { get; set; } = true;

        /// <summary>Non-null makes gathering fail with this error.</summary>
        public string ScriptedError { get; set; }

        /// <summary>When true, <see cref="Gather"/> waits for <see cref="CompleteGather"/> (simulates the network and the form).</summary>
        public bool ManualCompletion { get; set; }

        public PrivacyOptionsRequirement ScriptedPrivacyOptions { get; set; } = PrivacyOptionsRequirement.NotRequired;

        /// <summary>TCF values "written" by a completed gather or by the privacy options form.</summary>
        public TcfSignals ScriptedTcf { get; set; }

        public int PrivacyOptionsCalls { get; private set; }

        public int GatherCalls { get; private set; }

        public ConsentGatherState GatherState { get; private set; }

        public ConsentRequirement Requirement { get; private set; }

        public bool CanRequestAds { get; set; }

        public PrivacyOptionsRequirement PrivacyOptions { get; private set; }

        public TcfSignals Tcf { get; private set; }

        public event Action ConsentChanged;

        public TrackingAuthorizationStatus TrackingStatus => _tracking.Status;

        public IAttPrePrompt AttPrePrompt { get; set; }

        public void Gather(Action<ConsentGatherResult> done)
        {
            GatherCalls++;
            if (GatherState == ConsentGatherState.Gathering)
            {
                _pending += done;
                return;
            }
            GatherState = ConsentGatherState.Gathering;
            _pending += done;
            if (!ManualCompletion) CompleteGather();
        }

        /// <summary>Finishes a pending <see cref="Gather"/> with the scripted values.</summary>
        public void CompleteGather()
        {
            if (GatherState != ConsentGatherState.Gathering) return;
            if (ScriptedError != null)
            {
                GatherState = ConsentGatherState.Failed;
            }
            else
            {
                GatherState = ConsentGatherState.Completed;
                Requirement = ScriptedRequirement;
                CanRequestAds = ScriptedCanRequestAds;
                PrivacyOptions = ScriptedPrivacyOptions;
                Tcf = ScriptedTcf;
            }
            var result = new ConsentGatherResult(CanRequestAds, Requirement, ScriptedError);
            var callbacks = _pending;
            _pending = null;
            callbacks?.Invoke(result);
            ConsentChanged?.Invoke();
        }

        /// <summary>The "form" applies the scripted values (the user's new choice) and closes.</summary>
        public void ShowPrivacyOptions(Action<string> done)
        {
            PrivacyOptionsCalls++;
            Requirement = ScriptedRequirement;
            CanRequestAds = ScriptedCanRequestAds;
            Tcf = ScriptedTcf;
            done?.Invoke(null);
            ConsentChanged?.Invoke();
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
#endif
