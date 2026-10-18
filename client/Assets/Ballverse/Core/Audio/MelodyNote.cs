namespace Ballverse.Core.Audio
{
    /// <summary>One note of a melody (spec §2.7 "simple JSON list of notes: pitch, duration weight, volume").</summary>
    public struct MelodyNote
    {
        /// <summary>MIDI note number 0…127 (60 = C4).</summary>
        public int Pitch;

        /// <summary>
        /// Relative duration (&gt; 0). Informational for previews and editors: in the simulation each hit plays the next
        /// note, so timing comes from the hits, and the mixer always plays the whole sample.
        /// </summary>
        public float DurWeight;

        /// <summary>Note volume 0…1, multiplied with the impulse volume. A 0-velocity note is a rest: it advances but is silent.</summary>
        public float Velocity;

        public MelodyNote(int pitch, float durWeight, float velocity)
        {
            Pitch = pitch;
            DurWeight = durWeight;
            Velocity = velocity;
        }
    }
}
