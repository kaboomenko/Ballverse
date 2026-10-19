#if BALLVERSE_FIREBASE
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ballverse.Core.Config;
using Firebase.RemoteConfig;

namespace Ballverse.Services
{
    /// <summary>
    /// Firebase Remote Config (spec §7.9): register the build defaults, activate the values fetched in an earlier session,
    /// then fetch in the background. The start-up fetch is waited for at most 3 s (<see cref="RemoteConfigServiceBase.FetchTimeoutMs"/>);
    /// a later completion still activates and applies the new values.
    /// </summary>
    public sealed class FirebaseRemoteConfigService : RemoteConfigServiceBase, ITickable
    {
        const string Tag = "RemoteConfig";

        readonly FirebaseBootstrap _firebase;
        readonly MainThreadQueue _queue;
        readonly IClock _clock;
        readonly bool _developerMode;
        bool _initialized;
        long _fetchStartedMs = -1;

        /// <param name="developerMode">Development builds fetch with a zero cache expiration so console changes show up at once.</param>
        public FirebaseRemoteConfigService(FirebaseBootstrap firebase, MainThreadQueue queue, IClock clock, bool developerMode, IServiceLog log)
            : base(log)
        {
            _firebase = firebase ?? throw new ArgumentNullException(nameof(firebase));
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _developerMode = developerMode;
        }

        public override void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            _fetchStartedMs = _clock.MonotonicMs;
            _firebase.Start();
            _firebase.WhenReady(ok =>
            {
                if (!ok)
                {
                    MarkFetchSettled();
                    return;
                }
                StartWithFirebase();
            });
        }

        public void Tick()
        {
            if (!FetchSettled && _fetchStartedMs >= 0 && _clock.MonotonicMs - _fetchStartedMs >= FetchTimeoutMs)
            {
                Log.Info(Tag, "Start-up fetch not done after 3 s; continuing with the activated values.");
                MarkFetchSettled();
            }
        }

        void StartWithFirebase()
        {
            FirebaseRemoteConfig rc;
            try
            {
                rc = FirebaseRemoteConfig.DefaultInstance;
            }
            catch (Exception e)
            {
                Log.Error(Tag, "Remote Config unavailable.", e);
                MarkFetchSettled();
                return;
            }

            var defaults = new Dictionary<string, object>();
            foreach (var kv in RemoteConfigWriter.ToRaw(RemoteConfigValues.Defaults)) defaults[kv.Key] = kv.Value;

            rc.SetDefaultsAsync(defaults)
                .ContinueWith(_ => rc.ActivateAsync()).Unwrap()
                .ContinueWith(activate => _queue.Post(() =>
                {
                    if (activate.IsFaulted || activate.IsCanceled) Log.Warning(Tag, "Activating cached values failed.");
                    ApplyFrom(rc, RemoteConfigSource.Cached);
                    Fetch(rc);
                }));
        }

        void Fetch(FirebaseRemoteConfig rc)
        {
            Task fetch;
            try
            {
                fetch = _developerMode ? rc.FetchAsync(TimeSpan.Zero) : rc.FetchAsync();
            }
            catch (Exception e)
            {
                Log.Warning(Tag, "Fetch could not start: " + e.Message);
                MarkFetchSettled();
                return;
            }
            fetch.ContinueWith(f =>
            {
                if (f.IsFaulted || f.IsCanceled)
                {
                    _queue.Post(() =>
                    {
                        Log.Info(Tag, "Fetch failed (offline?); keeping activated values.");
                        MarkFetchSettled();
                    });
                    return;
                }
                rc.ActivateAsync().ContinueWith(a => _queue.Post(() =>
                {
                    if (!a.IsFaulted && !a.IsCanceled && a.Result) ApplyFrom(rc, RemoteConfigSource.Fetched);
                    MarkFetchSettled();
                }));
            });
        }

        void ApplyFrom(FirebaseRemoteConfig rc, RemoteConfigSource source)
        {
            var raw = new Dictionary<string, string>();
            try
            {
                foreach (var kv in rc.AllValues)
                {
                    // Defaults are registered from the build values; only remote ones change anything.
                    if (kv.Value.Source == ValueSource.RemoteValue) raw[kv.Key] = kv.Value.StringValue;
                }
            }
            catch (Exception e)
            {
                Log.Error(Tag, "Reading values failed.", e);
                return;
            }
            Apply(raw, raw.Count == 0 ? RemoteConfigSource.Defaults : source);
        }
    }
}
#endif
