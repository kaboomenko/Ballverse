using UnityEngine;

namespace Ballverse.Services
{
    /// <summary>
    /// Drives <see cref="BallverseServices"/> from the Unity player loop: drains SDK callbacks and ticks the services every
    /// frame, re-reads purchases when the app returns to the foreground or regains focus, and pauses app audio while a full-screen ad is on
    /// screen (RW6). Lives on a hidden DontDestroyOnLoad object.
    /// </summary>
    public sealed class ServicesRunner : MonoBehaviour
    {
        BallverseServices _services;
        bool _muteAudioDuringAds;

        public BallverseServices Services => _services;

        /// <param name="muteAudioDuringAds">Pause <see cref="AudioListener"/> while an ad is up; turn off if the app mutes itself.</param>
        public static ServicesRunner Attach(BallverseServices services, bool muteAudioDuringAds = true)
        {
            var go = new GameObject("BallverseServices");
            go.hideFlags = HideFlags.HideInHierarchy;
            DontDestroyOnLoad(go);
            var runner = go.AddComponent<ServicesRunner>();
            runner._services = services;
            runner._muteAudioDuringAds = muteAudioDuringAds;
            services.Ads.FullScreenChanged += runner.OnFullScreenChanged;
            return runner;
        }

        /// <summary>Stops ticking and destroys the runner object (App module shutdown). The services themselves stay usable.</summary>
        public void Detach()
        {
            if (_services != null) _services.Ads.FullScreenChanged -= OnFullScreenChanged;
            _services = null;
            if (_muteAudioDuringAds) AudioListener.pause = false;
            Destroy(gameObject);
        }

        void Update()
        {
            if (_services != null) _services.Tick();
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused && _services != null) _services.OnApplicationResumed();
        }

        void OnApplicationFocus(bool hasFocus)
        {
            if (_services != null) _services.OnApplicationFocusChanged(hasFocus);
        }

        void OnFullScreenChanged(bool showing)
        {
            if (_muteAudioDuringAds) AudioListener.pause = showing;
        }

        void OnDestroy()
        {
            if (_services != null) _services.Ads.FullScreenChanged -= OnFullScreenChanged;
            _services = null;
            if (_muteAudioDuringAds) AudioListener.pause = false;
        }
    }
}
