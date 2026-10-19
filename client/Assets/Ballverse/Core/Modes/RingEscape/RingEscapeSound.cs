using System;
using Ballverse.Core.Audio;
using Ballverse.Core.Integration;
using Ballverse.Core.Replay;

namespace Ballverse.Core.Modes.RingEscape
{
    /// <summary>
    /// M01 sound progression (spec §2.9 M01 «Звук», «Модель»): a ring hit is a pentatonic note whose step rises with the ring
    /// number, a break rises with every ring and climbs further during a combo (several rings passed in one flight).
    /// <para>
    /// Carrier: the mixer's size-dependent pitch (Audio <see cref="SoundPitch.PentatonicMidi"/>, bigger = lower) reads
    /// <see cref="SoundEvent.BodySize"/>. For ring hits and breaks M01 puts a <em>pitch size</em> there — the size that the
    /// built-in packs' size range (<see cref="PitchSizeMin"/>…<see cref="PitchSizeMax"/> m) maps to the wanted step — instead
    /// of the ball diameter. Ball–ball hits keep the ball size. Every size-dependent pack (Notes, Glass, Pop, Chiptune…)
    /// then rises with progress; the Melody pack ignores sizes as before. The step is a pure function of the EVNT record
    /// (ring number in <c>extra</c>, combo packed into the Break extra), so the export, which rebuilds sound from EVNT
    /// records, wraps its stream-based <see cref="ISoundBodySource"/> in <see cref="BodySource"/> and gets the same events
    /// as the live run.
    /// </para>
    /// </summary>
    public static class RingEscapeSound
    {
        /// <summary>Size range of the built-in sound packs (Audio <c>SoundPack.SizeMin</c> / <c>SizeMax</c> defaults), m.</summary>
        public const float PitchSizeMin = 0.1f;
        public const float PitchSizeMax = 1.5f;

        /// <summary>Scale steps of the built-in Notes pack: hits C3…C6 (15 steps), breaks C4…C6 (10 steps).</summary>
        public const int HitSteps = 15;
        public const int BreakSteps = 10;

        /// <summary>Break EVNT extra = ring + <see cref="ComboFactor"/> × (combo − 1) (ring numbers are below 128).</summary>
        public const long ComboFactor = 256;

        /// <summary>Largest combo step stored in a Break extra.</summary>
        public const int MaxComboStep = 255;

        /// <summary>Pentatonic step of a hit on ring <paramref name="ring"/>: one step per ring, wrapping every three octaves.</summary>
        public static int HitStep(int ring)
        {
            return Mod(ring, HitSteps + 1);
        }

        /// <summary>
        /// Step of the break of ring <paramref name="ring"/> as the <paramref name="combo"/>-th ring of one flight (1 = first):
        /// one step per ring plus one per combo ring, wrapping every two octaves.
        /// </summary>
        public static int BreakStep(int ring, int combo)
        {
            return Mod(ring + ComboStep(combo), BreakSteps + 1);
        }

        /// <summary>Size that a pack with the built-in size range maps to step <paramref name="step"/> of <paramref name="steps"/>.</summary>
        public static float SizeForStep(int step, int steps)
        {
            if (steps <= 0) return PitchSizeMax;
            if (step < 0) step = 0;
            if (step > steps) step = steps;
            return PitchSizeMax - (PitchSizeMax - PitchSizeMin) * step / steps;
        }

        public static float HitPitchSize(int ring) => SizeForStep(HitStep(ring), HitSteps);

        public static float BreakPitchSize(int ring, int combo) => SizeForStep(BreakStep(ring, combo), BreakSteps);

        /// <summary>EVNT extra of a Break: the ring number with the combo step packed above it.</summary>
        public static long BreakExtra(int ring, int combo)
        {
            return ring + ComboFactor * ComboStep(combo);
        }

        public static int RingOfBreakExtra(long extra) => (int)(extra < 0 ? 0 : extra % ComboFactor);

        /// <summary>Combo (1 = first ring of the flight) stored in a Break extra.</summary>
        public static int ComboOfBreakExtra(long extra) => (int)(extra < 0 ? 0 : extra / ComboFactor) + 1;

        static int ComboStep(int combo)
        {
            int s = combo - 1;
            if (s < 0) return 0;
            return s > MaxComboStep ? MaxComboStep : s;
        }

        static int Mod(int x, int m)
        {
            int r = x % m;
            return r < 0 ? r + m : r;
        }

        /// <summary>
        /// Export-side mapping (spec §7.8: export and app must sound alike): pan and the ball size from the wrapped source (the
        /// render stream), pitch size of ring hits and breaks from the EVNT record exactly as the live run computes it.
        /// </summary>
        public sealed class BodySource : ISoundBodySource
        {
            readonly ISoundBodySource _inner;

            public BodySource(ISoundBodySource inner)
            {
                _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            }

            public bool TryGetBody(in ReplayEvent e, out float bodySize, out float pan)
            {
                bool hasBody = _inner.TryGetBody(in e, out bodySize, out pan);
                if (e.Type == ReplayEventType.Hit && e.Extra >= 0) bodySize = HitPitchSize((int)Math.Min(e.Extra, int.MaxValue));
                else if (e.Type == ReplayEventType.Break) bodySize = BreakPitchSize(RingOfBreakExtra(e.Extra), ComboOfBreakExtra(e.Extra));
                return hasBody;
            }
        }
    }
}
