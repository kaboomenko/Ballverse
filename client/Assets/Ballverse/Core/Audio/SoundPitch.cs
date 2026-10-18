using System;

namespace Ballverse.Core.Audio
{
    /// <summary>Pure pitch and sample-choice functions of the sound pack rules (spec §2.7).</summary>
    public static class SoundPitch
    {
        /// <summary>Major pentatonic intervals within one octave: 0, 2, 4, 7, 9 semitones.</summary>
        public static readonly int[] PentatonicIntervals = { 0, 2, 4, 7, 9 };

        /// <summary>
        /// Normalised size position: 0 at <paramref name="sizeMin"/> (smallest), 1 at <paramref name="sizeMax"/> (largest),
        /// clamped; NaN size gives 0.5.
        /// </summary>
        public static double SizePosition(float size, float sizeMin, float sizeMax)
        {
            if (float.IsNaN(size)) return 0.5;
            double t = ((double)size - sizeMin) / ((double)sizeMax - sizeMin);
            if (double.IsNaN(t)) return 0.5;
            if (t < 0) return 0;
            if (t > 1) return 1;
            return t;
        }

        /// <summary>
        /// Notes pack (spec §2.7): pentatonic MIDI note from body size, bigger = lower.
        /// step = round((1 − t) × steps), note = lowMidi + 12·(step / 5) + interval[step % 5], clamped to 0…127.
        /// With the built-in Notes pack (lowMidi 48, steps 15): size ≥ 1.5 m → C3 (48), size ≤ 0.1 m → C6 (84).
        /// </summary>
        public static int PentatonicMidi(float size, float sizeMin, float sizeMax, int lowMidi, int steps)
        {
            if (steps < 0) steps = 0;
            double t = SizePosition(size, sizeMin, sizeMax);
            int step = (int)Math.Floor((1.0 - t) * steps + 0.5);
            int midi = lowMidi + 12 * (step / 5) + PentatonicIntervals[step % 5];
            return ClampMidi(midi);
        }

        /// <summary>Continuous size pitch: smallest body +range, largest −range semitones, rounded half up.</summary>
        public static int SizeSemitoneOffset(float size, float sizeMin, float sizeMax, int range)
        {
            double t = SizePosition(size, sizeMin, sizeMax);
            return (int)Math.Floor((1.0 - 2.0 * t) * range + 0.5);
        }

        /// <summary>
        /// Index of the sample to play among <paramref name="count"/> ids. Pure function of the event, so re-rendering the
        /// same events with the same pack always picks the same samples (spec §3.6 pack change without re-simulation).
        /// </summary>
        public static int SelectSample(SampleSelection selection, int count, in SoundEvent e, float sizeMin, float sizeMax)
        {
            if (count <= 1) return 0;
            if (selection == SampleSelection.BySize)
            {
                double t = SizePosition(e.BodySize, sizeMin, sizeMax);
                int idx = (int)Math.Floor((1.0 - t) * count);
                return idx >= count ? count - 1 : idx;
            }
            ulong x = ((ulong)(uint)e.Tick << 32) ^ (uint)e.BodyId ^ ((ulong)(uint)e.Type << 56);
            ulong h = Rng.SplitMix64(ref x);
            return (int)(h % (ulong)count);
        }

        public static int ClampMidi(int midi)
        {
            if (midi < 0) return 0;
            if (midi > 127) return 127;
            return midi;
        }
    }
}
