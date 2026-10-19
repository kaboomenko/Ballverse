using System;
using System.Threading;
using Ballverse.Core.Audio;

namespace Ballverse.Sim
{
    /// <summary>
    /// Live simulation sound (spec §7.8 "события из симуляции → lock-free очередь → микшер в OnAudioFilterRead"): the main
    /// thread places each sound event on the mixer's output timeline and pushes it into Core <see cref="EventMixer"/>'s
    /// single-producer / single-consumer queue; the audio thread renders the mixer in <see cref="Render"/> (Unity
    /// <c>OnAudioFilterRead</c>). A new sound pack or melody builds a new mixer that the audio thread picks up at its next
    /// callback (one reference swap, no lock). The device rate is converted from 48 kHz by linear interpolation when it differs.
    /// <para>Timing: an event of run frame f (real-time frames since the run started) sounds at mixer frame
    /// anchor + (f − anchorRunFrame). The anchor is set at the run start to the mixer position plus the output latency and moved
    /// forward whenever an event would already be late (the simulation fell behind the audio clock), so the order of queued
    /// frames never goes backwards. No allocation on either thread in steady state.</para>
    /// </summary>
    public sealed class LiveAudioEngine : ILiveSoundSink
    {
        /// <summary>Frames between "now" on the audio thread and where new events are placed (≈ 43 ms at 48 kHz).</summary>
        public const int DefaultLatencyFrames = 2048;

        const int ScratchFrames = 4096;

        // Main thread -> audio thread.
        EventMixer _published;
        // Audio thread only.
        EventMixer _active;
        // Audio thread -> main thread: position of the active mixer and which mixer it is.
        long _activePosition;
        EventMixer _activeSeen;

        readonly float[] _scratch = new float[ScratchFrames * 2];
        float _gain = 1f;
        volatile bool _muted;

        // Resampler state (audio thread).
        double _srcPos;
        float _prevL, _prevR, _curL, _curR;
        int _scratchFrames;
        int _scratchIndex;

        // Main-thread timing.
        long _anchorMixerFrame;
        long _anchorRunFrame;
        long _lastQueuedFrame;
        bool _anchored;

        public LiveAudioEngine(int latencyFrames = DefaultLatencyFrames)
        {
            LatencyFrames = Math.Max(256, latencyFrames);
        }

        public int LatencyFrames { get; set; }

        /// <summary>Master gain 0–1 (setting "Звук" × the run's <c>sfxVolume</c>); written by the main thread.</summary>
        public float Gain
        {
            get => Volatile.Read(ref _gain);
            set => Volatile.Write(ref _gain, float.IsNaN(value) ? 0f : Math.Max(0f, Math.Min(4f, value)));
        }

        /// <summary>Silences the output (events are still consumed so the timeline stays in step).</summary>
        public bool Muted
        {
            get => _muted;
            set => _muted = value;
        }

        /// <summary>Pack of the last <see cref="SetPack"/> (<see cref="SoundPackId.None"/> before any).</summary>
        public SoundPackId PackId { get; private set; } = SoundPackId.None;

        /// <summary>Melody of the last <see cref="SetPack"/>, or null (pentatonic fallback / not the Melody pack).</summary>
        public string MelodyId { get; private set; }

        /// <summary>True once <see cref="SetPack"/> was called.</summary>
        public bool HasPack { get; private set; }

        /// <summary>The mixer new events go to (null before a pack was set).</summary>
        public EventMixer Mixer => Volatile.Read(ref _published);

        /// <summary>Events dropped because the queue was full.</summary>
        public int DroppedEvents => Mixer != null ? Mixer.Queue.Dropped : 0;

        /// <summary>
        /// Uses a sound pack (null or <see cref="SoundPackId.None"/> = silent), its sample bank and the melody for the
        /// Melody pack. Builds a new mixer; queued events of the old one are dropped. Main thread.
        /// </summary>
        public void SetPack(SoundPack pack, ISampleBank bank, Melody melody)
        {
            PackId = pack != null ? pack.Id : SoundPackId.None;
            MelodyId = melody?.Id;
            HasPack = true;
            EventMixer mixer = null;
            if (pack != null && pack.Id != SoundPackId.None && bank != null)
            {
                mixer = new EventMixer(pack, bank, melody, new EventMixerOptions { QueueCapacity = 4096 });
            }
            Volatile.Write(ref _published, mixer);
            _anchored = false;
        }

        public void BeginRun()
        {
            _anchored = false;
        }

        /// <summary>Main thread: queues a sound event of the live run.</summary>
        public void OnSound(in SoundEvent e, long runFrame)
        {
            EventMixer mixer = Volatile.Read(ref _published);
            if (mixer == null) return;
            long position = ReadPosition(mixer);
            if (!_anchored)
            {
                _anchorMixerFrame = position + LatencyFrames;
                _anchorRunFrame = runFrame;
                _lastQueuedFrame = 0;
                _anchored = true;
            }
            long frame = _anchorMixerFrame + (runFrame - _anchorRunFrame);
            if (frame < position + LatencyFrames / 4)
            {
                // The simulation fell behind the audio clock: re-anchor so this event is not late.
                _anchorMixerFrame = position + LatencyFrames;
                _anchorRunFrame = runFrame;
                frame = _anchorMixerFrame;
            }
            if (frame < _lastQueuedFrame) frame = _lastQueuedFrame;
            _lastQueuedFrame = frame;
            mixer.Queue.TryEnqueue(in e, frame);
        }

        /// <summary>
        /// Audio thread (Unity <c>OnAudioFilterRead</c>): fills <paramref name="data"/> (interleaved, <paramref name="channels"/>
        /// channels at <paramref name="outputRate"/> Hz) with the mixer output. Never allocates or blocks.
        /// </summary>
        public void Render(float[] data, int channels, int outputRate)
        {
            if (data == null || channels <= 0) return;
            EventMixer published = Volatile.Read(ref _published);
            if (!ReferenceEquals(published, _active))
            {
                _active = published;
                _srcPos = 0;
                _scratchFrames = 0;
                _scratchIndex = 0;
                _prevL = _prevR = _curL = _curR = 0f;
                Volatile.Write(ref _activeSeen, published);
                Interlocked.Exchange(ref _activePosition, 0);
            }
            int frames = data.Length / channels;
            if (_active == null)
            {
                Array.Clear(data, 0, data.Length);
                return;
            }
            float gain = _muted ? 0f : Volatile.Read(ref _gain);
            if (outputRate == AudioConstants.SampleRate)
            {
                int done = 0;
                while (done < frames)
                {
                    int n = Math.Min(ScratchFrames, frames - done);
                    _active.Render(new Span<float>(_scratch, 0, n * 2));
                    WriteFrames(data, channels, done, n, gain);
                    done += n;
                }
            }
            else
            {
                double step = AudioConstants.SampleRate / (double)Math.Max(8000, outputRate);
                for (int i = 0; i < frames; i++)
                {
                    _srcPos += step;
                    while (_srcPos >= 1.0)
                    {
                        _srcPos -= 1.0;
                        _prevL = _curL;
                        _prevR = _curR;
                        NextSourceFrame(out _curL, out _curR);
                    }
                    float t = (float)_srcPos;
                    float l = (_prevL + (_curL - _prevL) * t) * gain;
                    float r = (_prevR + (_curR - _prevR) * t) * gain;
                    WriteFrame(data, channels, i, l, r);
                }
            }
            Interlocked.Exchange(ref _activePosition, _active.Position);
        }

        long ReadPosition(EventMixer mixer)
        {
            // Until the audio thread has switched to this mixer it has rendered nothing of it.
            if (!ReferenceEquals(Volatile.Read(ref _activeSeen), mixer)) return 0;
            return Interlocked.Read(ref _activePosition);
        }

        void NextSourceFrame(out float l, out float r)
        {
            if (_scratchIndex >= _scratchFrames)
            {
                _active.Render(new Span<float>(_scratch, 0, ScratchFrames * 2));
                _scratchFrames = ScratchFrames;
                _scratchIndex = 0;
            }
            l = _scratch[_scratchIndex * 2];
            r = _scratch[_scratchIndex * 2 + 1];
            _scratchIndex++;
        }

        void WriteFrames(float[] data, int channels, int offset, int count, float gain)
        {
            for (int i = 0; i < count; i++)
            {
                WriteFrame(data, channels, offset + i, _scratch[i * 2] * gain, _scratch[i * 2 + 1] * gain);
            }
        }

        static void WriteFrame(float[] data, int channels, int frame, float l, float r)
        {
            int o = frame * channels;
            if (channels == 1)
            {
                data[o] = 0.5f * (l + r);
                return;
            }
            data[o] = l;
            data[o + 1] = r;
            for (int c = 2; c < channels; c++) data[o + c] = 0f;
        }
    }
}
