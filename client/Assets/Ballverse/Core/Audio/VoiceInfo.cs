namespace Ballverse.Core.Audio
{
    /// <summary>Read-only snapshot of a playing voice (diagnostics and tests).</summary>
    public struct VoiceInfo
    {
        public string SampleId;
        /// <summary>Output frame at which the voice started.</summary>
        public long StartFrame;
        /// <summary>Nominal gain at start (pack × rule × impulse volume).</summary>
        public float Gain;
        /// <summary>Envelope-aware level used for stealing: gain × remaining / length.</summary>
        public double CurrentLevel;
        public int Semitones;
        /// <summary>Playback position in source frames (integer part).</summary>
        public long SourceFrame;
    }
}
