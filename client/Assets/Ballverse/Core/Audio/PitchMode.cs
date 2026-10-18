namespace Ballverse.Core.Audio
{
    /// <summary>How a sound rule chooses the pitch of a voice (spec §2.7 sound packs). All pitches are whole semitones.</summary>
    public enum PitchMode
    {
        /// <summary>Sample root pitch plus <see cref="SoundRule.Semitones"/>.</summary>
        Fixed = 0,
        /// <summary>
        /// Notes pack: a major-pentatonic note chosen from body size, bigger = lower.
        /// Size is mapped linearly from [SizeMax … SizeMin] onto scale steps 0 … <see cref="SoundRule.Steps"/> above
        /// <see cref="SoundRule.LowMidi"/>, so any sequence sounds consonant.
        /// </summary>
        PentatonicBySize = 1,
        /// <summary>Continuous size-to-semitone mapping: smallest body +range, largest −range (rounded to whole semitones).</summary>
        SizeSemitones = 2,
        /// <summary>Melody pack: the next note of the selected melody (falls back to <see cref="PentatonicBySize"/> without a melody).</summary>
        Melody = 3,
    }
}
