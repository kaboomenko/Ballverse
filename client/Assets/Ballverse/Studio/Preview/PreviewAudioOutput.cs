using Ballverse.Core.Studio;
using UnityEngine;

namespace Ballverse.Studio
{
    /// <summary>
    /// Plays the studio's offline mix during the preview (spec §3.7: sound within one frame of the picture). An
    /// <see cref="AudioSource"/> loops a silent clip so Unity calls <see cref="OnAudioFilterRead"/> on the audio thread, where
    /// <see cref="PcmPlaybackCursor"/> writes the mix samples (resampled to the device rate). The picture follows
    /// <see cref="AudibleUs"/> (cursor position minus the DSP buffer latency) through Core <see cref="PlaybackClock"/>.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class PreviewAudioOutput : MonoBehaviour
    {
        readonly PcmPlaybackCursor _cursor = new PcmPlaybackCursor();
        AudioSource _source;
        AudioClip _silence;
        volatile int _outputRate = 48000;
        long _latencyUs;

        public PcmPlaybackCursor Cursor => _cursor;

        /// <summary>True when a mix buffer is loaded.</summary>
        public bool HasBuffer => _cursor.FrameCount > 0;

        /// <summary>Output time the listener hears now, µs (approximate: cursor minus device buffering).</summary>
        public long AudibleUs => System.Math.Max(0, _cursor.PositionUs - _latencyUs);

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
            _source.bypassEffects = true;
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
            _latencyUs = (long)bufferLength * System.Math.Max(1, numBuffers) * 1000000L / System.Math.Max(1, _outputRate);
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
