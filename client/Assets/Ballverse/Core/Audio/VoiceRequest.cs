namespace Ballverse.Core.Audio
{
    /// <summary>A voice to start, produced by <see cref="SoundEventProcessor"/> from an accepted event (spec §7.8 "voices: sample, pitch, volume, pan").</summary>
    public struct VoiceRequest
    {
        /// <summary>Sample bank id.</summary>
        public string SampleId;
        /// <summary>Pitch shift relative to the sample's recorded pitch, whole semitones (±48).</summary>
        public int Semitones;
        /// <summary>Linear gain: pack gain × rule gain × impulse volume (× melody velocity).</summary>
        public float Gain;
        /// <summary>Pan −1…1.</summary>
        public float Pan;
        /// <summary>Nominal start frame of the event (tick × 800 relative to the mixer origin).</summary>
        public long Frame;
        /// <summary>Index of the melody note played, or −1.</summary>
        public int MelodyIndex;
    }
}
