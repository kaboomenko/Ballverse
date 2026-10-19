using Ballverse.Core.Studio;
using UnityEngine;

namespace Ballverse.Studio
{
    /// <summary>
    /// Plays the studio's offline mix during the preview (spec §3.7: sound within one frame of the picture). An
    /// <see cref="AudioSource"/> loops a silent clip so Unity calls <see cref="OnAudioFilterRead"/> on the audio thread, where
    /// <see cref="PcmPlaybackCursor"/> writes the mix samples (resampled to the device rate). The picture follows
    /// <see cref="AudibleUs"/> (cursor position minus the DSP buffer latency) through Core <see cref="PreviewAudioSync"/> and
    /// <see cref="PlaybackClock"/>, which fall back to the frame clock whenever the audio thread stops pulling samples.
    /// <para>The filter only runs when the source does not bypass effects (<c>bypassEffects</c> also bypasses the custom
    /// <see cref="OnAudioFilterRead"/> filter) and an enabled <see cref="AudioListener"/> exists: when the scene has none, one is
    /// added to this object for the preview's lifetime.</para>
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class PreviewAudioOutput : MonoBehaviour
    {
        readonly PcmPlaybackCursor _cursor = new PcmPlaybackCursor();
        AudioSource _source;
        AudioClip _silence;
        volatile int _outputRate = 48000;
        long _latencyUs;
        long _blockUs;
        AudioListener _ownListener;

        public PcmPlaybackCursor Cursor => _cursor;

        /// <summary>True when a mix buffer is loaded.</summary>
        public bool HasBuffer => _cursor.FrameCount > 0;

        /// <summary>Output time the listener hears now, µs (approximate: cursor minus device buffering).</summary>
        public long AudibleUs => System.Math.Max(0, _cursor.PositionUs - _latencyUs);

        /// <summary>Cursor position (after the last block handed to the device), µs; frozen while the audio thread does not run.</summary>
        public long CursorUs => _cursor.PositionUs;

        /// <summary>Device buffering between the cursor and the speaker, µs.</summary>
        public long LatencyUs => _latencyUs;

        /// <summary>Length of one audio callback block, µs.</summary>
        public long BlockUs => _blockUs;

        /// <summary>Raised on the main thread when the output device or its buffer size changed (the sync is rebuilt).</summary>
        public event System.Action ConfigurationChanged;

        public static PreviewAudioOutput Create(Transform parent)
        {
            var go = new GameObject("StudioPreviewAudio");
            if (parent != null) go.transform.SetParent(parent, false);
            go.AddComponent<AudioSource>();
            return go.AddComponent<PreviewAudioOutput>();
        }

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            ReadConfiguration();
            int rate = Mathf.Max(8000, _outputRate);
            _silence = AudioClip.Create("StudioSilence", rate, 2, rate, false);
            _source.clip = _silence;
            _source.loop = true;
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.volume = 1f;
            _source.priority = 0;
            // Not bypassEffects: it would also bypass this component's OnAudioFilterRead, the only thing producing sound.
            _source.bypassListenerEffects = true;
            _source.bypassReverbZones = true;
            AudioSettings.OnAudioConfigurationChanged += OnAudioConfigurationChanged;
            EnsureListener();
        }

        /// <summary>Unity mixes nothing without an enabled AudioListener; the studio may run in a scene without a camera listener.</summary>
        void EnsureListener()
        {
            AudioListener existing = Object.FindAnyObjectByType<AudioListener>();
            if (existing != null && existing.isActiveAndEnabled) return;
            _ownListener = gameObject.AddComponent<AudioListener>();
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
            ConfigurationChanged?.Invoke();
        }

        void ReadConfiguration()
        {
            _outputRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            AudioSettings.GetDSPBufferSize(out int bufferLength, out int numBuffers);
            _latencyUs = (long)bufferLength * System.Math.Max(1, numBuffers) * 1000000L / System.Math.Max(1, _outputRate);
            _blockUs = (long)System.Math.Max(1, bufferLength) * 1000000L / System.Math.Max(1, _outputRate);
        }

        /// <summary>Loads the mix (interleaved stereo 48 kHz) and keeps the position.</summary>
        public void SetMix(float[] interleavedStereo)
        {
            _cursor.SetBuffer(interleavedStereo ?? System.Array.Empty<float>());
        }

        public void Play(long outputUs)
        {
            _cursor.SeekUs(outputUs);
            _cursor.Playing = true;
            if (_ownListener == null) EnsureListener(); // a listener found at start may have been destroyed with its scene
            if (_source != null && !_source.isPlaying) _source.Play();
        }

        public void Pause()
        {
            _cursor.Playing = false;
        }

        public void Seek(long outputUs)
        {
            _cursor.SeekUs(outputUs);
        }

        // Audio thread.
        void OnAudioFilterRead(float[] data, int channels)
        {
            _cursor.Read(data, channels, _outputRate);
        }
    }
}
