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

#if !BALLVERSE_STORE // fakes are not compiled into Store builds (spec §8.9)
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
#endif

    /// <summary>
    /// The ATT sequence of spec §5.6, shared by the consent implementations: own explanation screen → system dialog, only
    /// when undetermined, at most once per session; concurrent callers wait for the same flow.
    /// <para>
    /// Failure handling (Р14, IN4: an ad never holds the user up): a pre-prompt or platform call that throws ends the flow at
    /// once; a flow that has not finished after <see cref="FlowTimeoutMs"/> (pre-prompt closed by navigation without an
    /// answer, native callback lost) is abandoned by the next <see cref="Ensure"/> call, which then continues immediately.
    /// The ads service's watchdog (<see cref="AdsService.BeforeShowTimeoutMs"/>) abandons it through <see cref="Abandon"/>
    /// as soon as it stops waiting. Abandoning dismisses the pre-prompt (<see cref="IAttPrePrompt.Dismiss"/>), and answers
    /// that arrive after the flow ended are ignored, so the system dialog never appears after the ad was skipped.
    /// </para>
    /// </summary>
    public sealed class TrackingDecisionFlow
    {
        /// <summary>A flow older than this is treated as lost.</summary>
        public const long FlowTimeoutMs = 60_000;

        const string Tag = "ATT";

        readonly ITrackingAuthorization _platform;
        readonly IClock _clock;
        readonly IServiceLog _log;
        Action _waiters;
        bool _running;
        bool _doneThisSession;
        int _generation;
        int _systemRequested;
        long _startedMs;
        IAttPrePrompt _prePrompt;

        public TrackingDecisionFlow(ITrackingAuthorization platform, IClock clock = null, IServiceLog log = null)
        {
            _platform = platform ?? throw new ArgumentNullException(nameof(platform));
            _clock = clock ?? SystemClock.Instance;
            _log = log ?? NullServiceLog.Instance;
        }

        public TrackingAuthorizationStatus Status => _platform.Status;

        /// <summary>True while the pre-prompt or the system dialog is pending.</summary>
        public bool IsRunning => _running;

        public void Ensure(IAttPrePrompt prePrompt, Action done)
        {
            if (_running && _clock.MonotonicMs - _startedMs >= FlowTimeoutMs) Abandon("did not finish in time");
            if (_doneThisSession || !IsUndetermined())
            {
                done?.Invoke();
                return;
            }
            _waiters += done;
            if (_running) return;
            _running = true;
            _startedMs = _clock.MonotonicMs;
            int generation = ++_generation;
            _prePrompt = prePrompt;

            if (prePrompt == null)
            {
                RequestSystem(generation);
                return;
            }
            try
            {
                prePrompt.Show(proceed =>
                {
                    if (generation != _generation || !_running) return; // answered twice, or after the flow ended
                    if (proceed) RequestSystem(generation);
                    else Finish(generation);
                });
            }
            catch (Exception e)
            {
                _log.Error(Tag, "ATT pre-prompt threw; continuing without the system dialog.", e);
                Finish(generation);
            }
        }

        /// <summary>
        /// Ends a running flow now: waiters continue, the pre-prompt is dismissed, later answers are ignored, and ATT is not
        /// asked again this session. No-op when no flow is running.
        /// </summary>
        public void Abandon(string reason)
        {
            if (!_running) return;
            _log.Warning(Tag, "ATT flow " + reason + "; abandoned for this session.");
            var prePrompt = _prePrompt;
            Finish(_generation);
            if (prePrompt == null) return;
            try
            {
                prePrompt.Dismiss();
            }
            catch (Exception e)
            {
                _log.Error(Tag, "ATT pre-prompt dismiss threw.", e);
            }
        }

        bool IsUndetermined()
        {
            try
            {
                return _platform.Status == TrackingAuthorizationStatus.NotDetermined;
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Reading the ATT status threw.", e);
                return false;
            }
        }

        void RequestSystem(int generation)
        {
            // The pre-prompt may answer "continue" more than once; only the first one reaches the system dialog.
            if (_systemRequested == generation) return;
            _systemRequested = generation;
            try
            {
                _platform.Request(_ => Finish(generation));
            }
            catch (Exception e)
            {
                _log.Error(Tag, "ATT request threw.", e);
                Finish(generation);
            }
        }

        void Finish(int generation)
        {
            if (generation != _generation || !_running) return;
            _running = false;
            _doneThisSession = true;
            _prePrompt = null;
            _generation++; // late answers of this flow are ignored
            var w = _waiters;
            _waiters = null;
            w?.Invoke();
        }
    }
}
