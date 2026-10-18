using System;

namespace Ballverse.Services
{
    /// <summary>Mirror of the UMP consent status (spec §5.6).</summary>
    public enum ConsentRequirement
    {
        Unknown = 0,
        /// <summary>No consent form is required for this user (outside GDPR/US-state regions).</summary>
        NotRequired = 1,
        /// <summary>A form must be shown before personalised ads.</summary>
        Required = 2,
        /// <summary>The user has made a choice.</summary>
        Obtained = 3,
    }

    public enum ConsentGatherState
    {
        NotStarted = 0,
        Gathering = 1,
        Completed = 2,
        /// <summary>The consent information update failed (offline, timeout); the previous session's decision applies.</summary>
        Failed = 3,
    }

    public enum PrivacyOptionsRequirement
    {
        Unknown = 0,
        NotRequired = 1,
        /// <summary>Settings must offer "Privacy options" that reopens the form (spec §5.6 "Повторный вызов — из настроек").</summary>
        Required = 2,
    }

    /// <summary>iOS App Tracking Transparency status; <see cref="NotApplicable"/> on Android, in the editor and below iOS 14.</summary>
    public enum TrackingAuthorizationStatus
    {
        NotApplicable = -1,
        NotDetermined = 0,
        Restricted = 1,
        Denied = 2,
        Authorized = 3,
    }

    /// <summary>Result of <see cref="IConsentService.Gather"/>.</summary>
    public readonly struct ConsentGatherResult
    {
        public ConsentGatherResult(bool canRequestAds, ConsentRequirement requirement, string error)
        {
            CanRequestAds = canRequestAds;
            Requirement = requirement;
            Error = error;
        }

        public bool CanRequestAds { get; }
        public ConsentRequirement Requirement { get; }

        /// <summary>Null on success; otherwise a short error code/message (no personal data).</summary>
        public string Error { get; }
    }

    /// <summary>
    /// The app's own explanation screen shown before the system ATT dialog (spec §5.6: «Реклама помогает Ballverse оставаться
    /// бесплатным…» → system dialog). Implemented by the UI and assigned to <see cref="IConsentService.AttPrePrompt"/>.
    /// </summary>
    public interface IAttPrePrompt
    {
        /// <summary>Shows the screen; call <paramref name="onContinue"/> with true to proceed to the system dialog.</summary>
        void Show(Action<bool> onContinue);
    }

    /// <summary>
    /// Privacy consent (spec §5.6, §7.9): Google UMP (GDPR / US states) before the ads SDK starts, a "Privacy options" entry
    /// in settings, and iOS ATT deferred to the first ad show with the app's pre-prompt. Main thread only.
    /// </summary>
    public interface IConsentService
    {
        ConsentGatherState GatherState { get; }

        ConsentRequirement Requirement { get; }

        /// <summary>
        /// Whether ads may be requested now. Can already be true at start-up from the previous session's decision, so ads
        /// can start without waiting for the network (UMP guidance).
        /// </summary>
        bool CanRequestAds { get; }

        PrivacyOptionsRequirement PrivacyOptions { get; }

        /// <summary>
        /// The TCF values UMP wrote with the user's actual choice (refreshed after every gather and privacy options form);
        /// default when not written or unreadable. Drives Consent Mode (<see cref="ConsentModeRules"/>).
        /// </summary>
        TcfSignals Tcf { get; }

        /// <summary>Raised after <see cref="Gather"/> completes or the privacy options form closes.</summary>
        event Action ConsentChanged;

        /// <summary>
        /// Requests the consent information and shows the consent form if required (spec §7.9 frame 1+). Calls
        /// <paramref name="done"/> once, on the main thread. Repeated calls while gathering share the same result.
        /// </summary>
        void Gather(Action<ConsentGatherResult> done);

        /// <summary>Opens the UMP privacy options form from settings; <paramref name="done"/> gets an error or null.</summary>
        void ShowPrivacyOptions(Action<string> done);

        TrackingAuthorizationStatus TrackingStatus { get; }

        /// <summary>The UI's pre-prompt; when null the system dialog is shown directly.</summary>
        IAttPrePrompt AttPrePrompt { get; set; }

        /// <summary>
        /// iOS: if ATT is undetermined, shows the pre-prompt and then the system dialog, once per session; elsewhere returns at
        /// once. Called by the ads service right before the first ad is shown (spec §5.6). A refusal never limits the app.
        /// </summary>
        void EnsureTrackingDecision(Action done);
    }
}
