using UnityEngine;

namespace Ballverse.Sim
{
    /// <summary>
    /// Plays <see cref="LiveAudioEngine"/> on the Unity audio thread: an <see cref="AudioSource"/> loops a silent clip so Unity
    /// calls <see cref="OnAudioFilterRead"/>, where the engine renders the event mixer (spec §7.8, latency ≤ 50 ms). The
    /// engine's latency follows the DSP buffer size. Survives scene loads.
    /// <para>Device rules: the source must NOT set <c>bypassEffects</c>. That also bypasses this component's
    /// <c>OnAudioFilterRead</c>, which Unity inserts as a custom filter into the DSP chain of the AudioSource on the same
    /// object. Unity mixes nothing without an enabled <see cref="AudioListener"/>, but this object must never carry one. With
    /// both, Unity attaches the filter to whichever component comes first and logs "GameObject has multiple AudioSources
    /// and/or AudioListeners attached" when the other one tries. On a listener the filter would also overwrite the final mix.
    /// The app's composition root owns the only listener, on its own object (Ballverse.App <c>DeviceAudio</c>), and creates it
    /// before this output.</para>
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class LiveAudioOutput : MonoBehaviour
    {
        LiveAudioEngine _engine;
        AudioSource _source;
        AudioClip _silence;
        volatile int _outputRate = 48000;

        public LiveAudioEngine Engine => _engine;

        public static LiveAudioOutput Create(LiveAudioEngine engine, Transform parent = null)
        {
            var go = new GameObject("BallverseLiveAudio");
            go.SetActive(false);
            if (parent != null) go.transform.SetParent(parent, false);
            else if (Application.isPlaying) DontDestroyOnLoad(go);
            go.AddComponent<AudioSource>();
            var output = go.AddComponent<LiveAudioOutput>();
            output._engine = engine;
            go.SetActive(true);
            return output;
        }

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            ReadConfiguration();
            int rate = Mathf.Max(8000, _outputRate);
            _silence = AudioClip.Create("BallverseLiveSilence", rate, 2, rate, false);
            _source.clip = _silence;
            _source.loop = true;
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.volume = 1f;
            _source.priority = 0;
            // Not bypassEffects: it would also bypass OnAudioFilterRead, the only thing producing sound.
            _source.bypassListenerEffects = true;
            _source.bypassReverbZones = true;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
        }

        void OnEnable()
        {
            if (_source != null && !_source.isPlaying) _source.Play();
        }

        void OnDisable()
        {
            if (_source != null) _source.Stop();
        }

        void OnDestroy()
        {
            AudioSettings.OnAudioConfigurationChanged -= OnAudioConfigurationChanged;
            if (_silence != null) Destroy(_silence);
        }

        void OnAudioConfigurationChanged(bool deviceWasChanged)
        {
            ReadConfiguration();
            if (_source != null && isActiveAndEnabled && !_source.isPlaying) _source.Play();
        }

        void ReadConfiguration()
        {
            _outputRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
            if (_engine != null)
            {
                // Place events one DSP queue (plus a margin) ahead of the audio thread, in 48 kHz frames.
                long latency48 = (long)bufferLength * System.Math.Max(1, numBuffers) * 48000L / System.Math.Max(8000, _outputRate);
                _engine.LatencyFrames = (int)System.Math.Max(512, System.Math.Min(4800, latency48 + 512));
            }
        }

        // Audio thread.
        void OnAudioFilterRead(float[] data, int channels)
        {
            LiveAudioEngine engine = _engine;
            if (engine == null)
            {
                System.Array.Clear(data, 0, data.Length);
                return;
            }
            engine.Render(data, channels, _outputRate);
        }
    }
}
