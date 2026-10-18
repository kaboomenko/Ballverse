#if BALLVERSE_ADMOB
using System;
using System.Collections.Generic;
using GoogleMobileAds.Ump.Api;

namespace Ballverse.Services
{
    /// <summary>Development-only UMP settings: force the EEA geography for test devices to see the form.</summary>
    public sealed class ConsentDebugOptions
    {
        public bool ForceEeaGeography { get; set; }

        /// <summary>Hashed device ids printed by the UMP SDK in the device log.</summary>
        public List<string> TestDeviceHashedIds { get; } = new List<string>();
    }

    /// <summary>
    /// Google User Messaging Platform (spec §5.6, §7.9): <c>ConsentInformation.Update</c> then
    /// <c>ConsentForm.LoadAndShowConsentFormIfRequired</c>, before the ads SDK starts. All UMP callbacks are moved to the main
    /// thread through <see cref="MainThreadQueue"/>. The information update gives up after <see cref="UpdateTimeoutMs"/>
    /// (no network answer): the previous session's decision (<see cref="ConsentInformation.CanRequestAds"/>) then applies.
    /// The user's time on the form itself is never limited.
    /// </summary>
    public sealed class UmpConsentService : IConsentService, ITickable
    {
        public const int UpdateTimeoutMs = 10_000;

        const string Tag = "Consent";

        readonly MainThreadQueue _queue;
        readonly IClock _clock;
        readonly IServiceLog _log;
        readonly ConsentDebugOptions _debug;
        readonly TrackingDecisionFlow _tracking;
        Action<ConsentGatherResult> _pending;
        int _attempt;
        bool _awaitingUpdate;
        long _updateStartedMs;
        string _lastError;

        public UmpConsentService(MainThreadQueue queue, IClock clock, ITrackingAuthorization tracking, ConsentDebugOptions debug, IServiceLog log)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _log = log ?? NullServiceLog.Instance;
            _debug = debug;
            _tracking = new TrackingDecisionFlow(tracking ?? new NoTrackingAuthorization());
            RefreshFromSdk();
        }

        public ConsentGatherState GatherState { get; private set; }

        public ConsentRequirement Requirement { get; private set; }

        public bool CanRequestAds { get; private set; }

        public PrivacyOptionsRequirement PrivacyOptions { get; private set; }

        public event Action ConsentChanged;

        public TrackingAuthorizationStatus TrackingStatus => _tracking.Status;

        public IAttPrePrompt AttPrePrompt { get; set; }

        public void Gather(Action<ConsentGatherResult> done)
        {
            _pending += done;
            if (GatherState == ConsentGatherState.Gathering) return;
            GatherState = ConsentGatherState.Gathering;
            _lastError = null;
            int attempt = ++_attempt;
            _awaitingUpdate = true;
            _updateStartedMs = _clock.MonotonicMs;

            var request = new ConsentRequestParameters { TagForUnderAgeOfConsent = false };
            if (_debug != null && (_debug.ForceEeaGeography || _debug.TestDeviceHashedIds.Count > 0))
            {
                var settings = new ConsentDebugSettings
                {
                    DebugGeography = _debug.ForceEeaGeography ? DebugGeography.EEA : DebugGeography.Disabled,
                };
                settings.TestDeviceHashedIds.AddRange(_debug.TestDeviceHashedIds);
                request.ConsentDebugSettings = settings;
            }

            try
            {
                ConsentInformation.Update(request, error => _queue.Post(() => OnInfoUpdated(attempt, error)));
            }
            catch (Exception e)
            {
                _awaitingUpdate = false;
                _log.Error(Tag, "ConsentInformation.Update threw.", e);
                Finish(ConsentGatherState.Failed, "update_exception");
            }
        }

        public void Tick()
        {
            if (_awaitingUpdate && _clock.MonotonicMs - _updateStartedMs >= UpdateTimeoutMs)
            {
                _awaitingUpdate = false;
                _log.Warning(Tag, "Consent information update timed out; using the stored decision.");
                Finish(ConsentGatherState.Failed, "update_timeout");
            }
        }

        public void ShowPrivacyOptions(Action<string> done)
        {
            try
            {
                ConsentForm.ShowPrivacyOptionsForm(error => _queue.Post(() =>
                {
                    RefreshFromSdk();
                    done?.Invoke(error != null ? "form_" + error.ErrorCode : null);
                    ConsentChanged?.Invoke();
                }));
            }
            catch (Exception e)
            {
                _log.Error(Tag, "ShowPrivacyOptionsForm threw.", e);
                done?.Invoke("form_exception");
            }
        }

        public void EnsureTrackingDecision(Action done)
        {
            _tracking.Ensure(AttPrePrompt, done);
        }

        void OnInfoUpdated(int attempt, FormError error)
        {
            if (attempt != _attempt || !_awaitingUpdate) return; // timed out earlier; ignore the late answer
            _awaitingUpdate = false;
            if (error != null)
            {
                _log.Warning(Tag, "Consent information update failed: " + error.ErrorCode);
                Finish(ConsentGatherState.Failed, "update_" + error.ErrorCode);
                return;
            }
            try
            {
                ConsentForm.LoadAndShowConsentFormIfRequired(formError => _queue.Post(() =>
                {
                    if (attempt != _attempt) return;
                    if (formError != null) _log.Warning(Tag, "Consent form failed: " + formError.ErrorCode);
                    Finish(ConsentGatherState.Completed, formError != null ? "form_" + formError.ErrorCode : null);
                }));
            }
            catch (Exception e)
            {
                _log.Error(Tag, "LoadAndShowConsentFormIfRequired threw.", e);
                Finish(ConsentGatherState.Completed, "form_exception");
            }
        }

        void Finish(ConsentGatherState state, string error)
        {
            RefreshFromSdk();
            GatherState = state;
            _lastError = error;
            var result = new ConsentGatherResult(CanRequestAds, Requirement, _lastError);
            var callbacks = _pending;
            _pending = null;
            callbacks?.Invoke(result);
            ConsentChanged?.Invoke();
        }

        void RefreshFromSdk()
        {
            try
            {
                CanRequestAds = ConsentInformation.CanRequestAds();
                Requirement = Map(ConsentInformation.ConsentStatus);
                PrivacyOptions = Map(ConsentInformation.PrivacyOptionsRequirementStatus);
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Reading UMP consent state failed.", e);
            }
        }

        static ConsentRequirement Map(ConsentStatus status)
        {
            switch (status)
            {
                case ConsentStatus.NotRequired: return ConsentRequirement.NotRequired;
                case ConsentStatus.Required: return ConsentRequirement.Required;
                case ConsentStatus.Obtained: return ConsentRequirement.Obtained;
                default: return ConsentRequirement.Unknown;
            }
        }

        static PrivacyOptionsRequirement Map(PrivacyOptionsRequirementStatus status)
        {
            switch (status)
            {
                case PrivacyOptionsRequirementStatus.NotRequired: return PrivacyOptionsRequirement.NotRequired;
                case PrivacyOptionsRequirementStatus.Required: return PrivacyOptionsRequirement.Required;
                default: return PrivacyOptionsRequirement.Unknown;
            }
        }
    }
}
#endif
