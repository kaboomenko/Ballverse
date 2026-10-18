using System;

namespace Ballverse.Services
{
    /// <summary>Platform access to App Tracking Transparency.</summary>
    public interface ITrackingAuthorization
    {
        TrackingAuthorizationStatus Status { get; }

        /// <summary>Shows the system dialog when undetermined; <paramref name="done"/> runs on the main thread.</summary>
        void Request(Action<TrackingAuthorizationStatus> done);
    }

    /// <summary>Android, editor, tests: ATT does not exist.</summary>
    public sealed class NoTrackingAuthorization : ITrackingAuthorization
    {
        public TrackingAuthorizationStatus Status => TrackingAuthorizationStatus.NotApplicable;

        public void Request(Action<TrackingAuthorizationStatus> done)
        {
            done?.Invoke(TrackingAuthorizationStatus.NotApplicable);
        }
    }

    /// <summary>Scriptable ATT for tests and the editor.</summary>
    public sealed class FakeTrackingAuthorization : ITrackingAuthorization
    {
        public TrackingAuthorizationStatus Status { get; set; } = TrackingAuthorizationStatus.NotDetermined;

        /// <summary>What the "system dialog" answers.</summary>
        public TrackingAuthorizationStatus Answer { get; set; } = TrackingAuthorizationStatus.Denied;

        public int RequestCount { get; private set; }

        public void Request(Action<TrackingAuthorizationStatus> done)
        {
            RequestCount++;
            if (Status == TrackingAuthorizationStatus.NotDetermined) Status = Answer;
            done?.Invoke(Status);
        }
    }

    /// <summary>
    /// The ATT sequence of spec §5.6, shared by the consent implementations: own explanation screen → system dialog, only
    /// when undetermined, at most once per session; concurrent callers wait for the same flow.
    /// </summary>
    public sealed class TrackingDecisionFlow
    {
        readonly ITrackingAuthorization _platform;
        Action _waiters;
        bool _running;
        bool _doneThisSession;

        public TrackingDecisionFlow(ITrackingAuthorization platform)
        {
            _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        }

        public TrackingAuthorizationStatus Status => _platform.Status;

        public void Ensure(IAttPrePrompt prePrompt, Action done)
        {
            if (_doneThisSession || _platform.Status != TrackingAuthorizationStatus.NotDetermined)
            {
                done?.Invoke();
                return;
            }
            _waiters += done;
            if (_running) return;
            _running = true;

            if (prePrompt == null)
            {
                RequestSystem();
                return;
            }
            bool answered = false;
            prePrompt.Show(proceed =>
            {
                if (answered) return;
                answered = true;
                if (proceed) RequestSystem();
                else Finish();
            });
        }

        void RequestSystem()
        {
            _platform.Request(_ => Finish());
        }

        void Finish()
        {
            _running = false;
            _doneThisSession = true;
            var w = _waiters;
            _waiters = null;
            w?.Invoke();
        }
    }
}
