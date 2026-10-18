namespace Ballverse.Core.Audio
{
    /// <summary>
    /// What one event type sounds like inside a sound pack (spec §2.7): sample ids, pitch rule and gain.
    /// Plain data (CoreJson-serializable) so packs can be shipped as content (spec §7.6 "data instead of code").
    /// </summary>
    public sealed class SoundRule
    {
        public SoundEventType EventType { get; set; }

        /// <summary>Sample bank ids (48 kHz PCM, spec §7.8). At least one.</summary>
        public string[] Samples { get; set; }

        public SampleSelection Selection { get; set; } = SampleSelection.Hash;

        public PitchMode Pitch { get; set; } = PitchMode.Fixed;

        /// <summary>MIDI note at which the samples were recorded (played back unshifted).</summary>
        public int RootMidi { get; set; } = 60;

        /// <summary>Pentatonic / melody fallback: MIDI note for the largest body (scale tonic).</summary>
        public int LowMidi { get; set; } = 48;

        /// <summary>Pentatonic: number of scale steps above <see cref="LowMidi"/> (15 = three octaves).</summary>
        public int Steps { get; set; } = 15;

        /// <summary>SizeSemitones: ± range in semitones.</summary>
        public int SemitoneRange { get; set; }

        /// <summary>Transposition added to every pitch mode, semitones.</summary>
        public int Semitones { get; set; }

        /// <summary>Rule gain, 0…4.</summary>
        public float Gain { get; set; } = 1f;

        /// <summary>
        /// When true the volume follows the impulse curve and hits below i0 are silent (spec §2.7); when false
        /// (finish, score…) the event always plays at the rule gain.
        /// </summary>
        public bool ImpulseVolume { get; set; } = true;

        public SoundRule()
        {
        }

        public SoundRule(SoundEventType eventType, params string[] samples)
        {
            EventType = eventType;
            Samples = samples;
        }
    }
}
