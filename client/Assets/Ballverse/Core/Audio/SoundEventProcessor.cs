using System;
using System.Collections.Generic;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// The decision half of the mixer (spec §2.7): maps an event through the sound pack, impulse volume curve, rate limits
    /// and melody cursor to a <see cref="VoiceRequest"/>. Pure event logic: the result depends only on the pack, the melody
    /// and the sequence of events with their frames — never on audio buffer sizes or the sample bank.
    /// <para>Order of checks: pack rule → impulse threshold (silent hits consume nothing) → per-body 50 ms → overall
    /// 40/s → for melody notes also the 20 notes/s melody window. Reading of spec §2.7 «не более 40 звуков в секунду
    /// суммарно (кроме пакета Melody… лимит 20 нот/с)»: the 40/s total holds for every pack, and in the Melody pack the
    /// notes are additionally limited to 20/s; a note refused by either limit does not advance the melody.</para>
    /// <para>Times are 48 kHz frames on the OUTPUT timeline (what the listener hears), so the limits hold at any playback
    /// speed (spec §2.2 timeScale, §3.4 studio speed). Event times must not decrease; an earlier time is treated as the
    /// latest time seen.</para>
    /// <para>Pitch shifts beyond ±48 semitones (the resampler's range) are folded by whole octaves into range, so the pitch
    /// class stays right; <see cref="MelodyNotesOutOfRange"/> reports melody notes affected.</para>
    /// Allocation-free per event after construction.
    /// </summary>
    public sealed class SoundEventProcessor
    {
        struct Rule
        {
            public bool Present;
            public string[] Samples;
            public SampleSelection Selection;
            public PitchMode Pitch;
            public int RootMidi;
            public int LowMidi;
            public int Steps;
            public int SemitoneRange;
            public int Semitones;
            public float Gain;
            public bool ImpulseVolume;
        }

        readonly Rule[] _rules = new Rule[(int)SoundEventType.Custom + 1];
        readonly SoundPackId _packId;
        readonly float _i0;
        readonly float _i1;
        readonly float _packGain;
        readonly float _sizeMin;
        readonly float _sizeMax;
        readonly MelodyCursor _melody;
        readonly SlidingRateWindow _global;
        readonly BodyRateLimiter _bodies;
        readonly int _melodyOutOfRange;
        long _latest = long.MinValue;

        /// <summary>
        /// Snapshots <paramref name="pack"/> (throws <see cref="ArgumentException"/> if it fails validation).
        /// <paramref name="melody"/> is used by rules with <see cref="PitchMode.Melody"/>; when null those rules fall back to
        /// <see cref="PitchMode.PentatonicBySize"/> and count toward the 40/s window.
        /// </summary>
        public SoundEventProcessor(SoundPack pack, Melody melody = null)
        {
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            List<string> errors = pack.Validate();
            if (errors.Count > 0) throw new ArgumentException("Invalid sound pack: " + string.Join("; ", errors), nameof(pack));

            _packId = pack.Id;
            _i0 = pack.I0;
            _i1 = pack.I1;
            _packGain = pack.Gain;
            _sizeMin = pack.SizeMin;
            _sizeMax = pack.SizeMax;
            foreach (var r in pack.Rules)
            {
                _rules[(int)r.EventType] = new Rule
                {
                    Present = true,
                    Samples = (string[])r.Samples.Clone(),
                    Selection = r.Selection,
                    Pitch = r.Pitch,
                    RootMidi = r.RootMidi,
                    LowMidi = r.LowMidi,
                    Steps = r.Steps,
                    SemitoneRange = r.SemitoneRange,
                    Semitones = r.Semitones,
                    Gain = r.Gain,
                    ImpulseVolume = r.ImpulseVolume,
                };
            }
            if (melody != null)
            {
                _melody = new MelodyCursor(melody);
                _melodyOutOfRange = CountOutOfRange(melody);
            }
            _global = new SlidingRateWindow(AudioConstants.MaxSoundsPerWindow, AudioConstants.RateWindowFrames);
            _bodies = new BodyRateLimiter();
        }

        public SoundPackId PackId => _packId;
        public float I0 => _i0;
        public float I1 => _i1;

        /// <summary>The melody cursor, or null without a melody.</summary>
        public MelodyCursor MelodyCursor => _melody;

        /// <summary>
        /// Melody notes whose shift from a melody rule's sample root exceeds ±48 semitones; they play octave-folded
        /// (right pitch class, wrong octave). A content warning for the studio or the melody pipeline; 0 without a melody.
        /// </summary>
        public int MelodyNotesOutOfRange => _melodyOutOfRange;

        /// <summary>Processes an event at its nominal frame <c>tick × 800</c>.</summary>
        public SoundDecision Process(in SoundEvent e, out VoiceRequest request)
        {
            return Process(in e, AudioConstants.TickToFrame(e.Tick), out request);
        }

        /// <summary>Processes an event at <paramref name="frame"/> (48 kHz frames on the caller's timeline).</summary>
        public SoundDecision Process(in SoundEvent e, long frame, out VoiceRequest request)
        {
            request = default;
            request.MelodyIndex = -1;
            if (frame < _latest) frame = _latest;
            _latest = frame;

            int type = (int)e.Type;
            if (type < 0 || type >= _rules.Length || !_rules[type].Present) return SoundDecision.NoRule;
            ref Rule rule = ref _rules[type];

            float volume = 1f;
            if (rule.ImpulseVolume)
            {
                volume = AudioMath.ImpulseVolume(e.Impulse, _i0, _i1);
                if (volume <= 0f) return SoundDecision.BelowThreshold;
            }

            if (_bodies.IsLimited(e.BodyId, frame)) return SoundDecision.BodyRateLimited;

            int midi;
            bool melodyNote = rule.Pitch == PitchMode.Melody && _melody != null;
            if (melodyNote)
            {
                if (!_global.CanAcquire(frame)) return SoundDecision.GlobalRateLimited;
                int index = _melody.NextIndex;
                if (!_melody.TryAdvance(frame, out MelodyNote note)) return SoundDecision.MelodyRateLimited;
                _global.TryAcquire(frame);
                request.MelodyIndex = index;
                midi = note.Pitch;
                volume *= note.Velocity;
            }
            else
            {
                if (!_global.TryAcquire(frame)) return SoundDecision.GlobalRateLimited;
                midi = ResolveMidi(ref rule, in e);
            }
            _bodies.Record(e.BodyId, frame);

            int semis = FoldSemitones(midi - rule.RootMidi + rule.Semitones);

            float gain = _packGain * rule.Gain * volume;
            request.SampleId = rule.Samples[SoundPitch.SelectSample(rule.Selection, rule.Samples.Length, in e, _sizeMin, _sizeMax)];
            request.Semitones = semis;
            request.Gain = gain;
            request.Pan = SanitizePan(e.Pan);
            request.Frame = frame;
            return gain > 0f ? SoundDecision.Played : SoundDecision.Muted;
        }

        /// <summary>Clears rate limits and rewinds the melody.</summary>
        public void Reset()
        {
            _global.Reset();
            _bodies.Reset();
            _melody?.Reset();
            _latest = long.MinValue;
        }

        int ResolveMidi(ref Rule rule, in SoundEvent e)
        {
            switch (rule.Pitch)
            {
                case PitchMode.PentatonicBySize:
                case PitchMode.Melody:
                    return SoundPitch.PentatonicMidi(e.BodySize, _sizeMin, _sizeMax, rule.LowMidi, rule.Steps);
                case PitchMode.SizeSemitones:
                    return rule.RootMidi + SoundPitch.SizeSemitoneOffset(e.BodySize, _sizeMin, _sizeMax, rule.SemitoneRange);
                default:
                    return rule.RootMidi;
            }
        }

        /// <summary>Folds a shift by whole octaves into ±<see cref="AudioMath.MaxSemitoneShift"/>.</summary>
        public static int FoldSemitones(int semitones)
        {
            while (semitones > AudioMath.MaxSemitoneShift) semitones -= 12;
            while (semitones < -AudioMath.MaxSemitoneShift) semitones += 12;
            return semitones;
        }

        int CountOutOfRange(Melody melody)
        {
            int count = 0;
            for (int i = 0; i < melody.Count; i++)
            {
                int pitch = melody[i].Pitch;
                for (int t = 0; t < _rules.Length; t++)
                {
                    if (!_rules[t].Present || _rules[t].Pitch != PitchMode.Melody) continue;
                    int semis = pitch - _rules[t].RootMidi + _rules[t].Semitones;
                    if (semis > AudioMath.MaxSemitoneShift || semis < -AudioMath.MaxSemitoneShift)
                    {
                        count++;
                        break;
                    }
                }
            }
            return count;
        }

        static float SanitizePan(float pan)
        {
            if (float.IsNaN(pan)) return 0f;
            if (pan < -1f) return -1f;
            if (pan > 1f) return 1f;
            return pan;
        }
    }
}
