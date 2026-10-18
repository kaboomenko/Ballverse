using System;
using System.Collections.Generic;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Built-in sound packs (spec §2.7 table). Sample ids name entries of the sample bank (spec §7.8) and must be provided
    /// by the content (Unity) side. Impulse thresholds i0/i1 are initial tuning values in simulation impulse units (N·s).
    /// Every call returns a fresh, independent instance.
    /// </summary>
    public static class SoundPackCatalog
    {
        static readonly SoundPackId[] s_ids =
        {
            SoundPackId.Notes, SoundPackId.Melody, SoundPackId.Pop, SoundPackId.Glass,
            SoundPackId.Drums, SoundPackId.Asmr, SoundPackId.Chiptune, SoundPackId.None,
        };

        /// <summary>All pack ids in table order.</summary>
        public static IReadOnlyList<SoundPackId> Ids => s_ids;

        /// <summary>A new instance of the built-in pack.</summary>
        public static SoundPack Get(SoundPackId id)
        {
            switch (id)
            {
                case SoundPackId.Notes: return Notes();
                case SoundPackId.Melody: return Melody();
                case SoundPackId.Pop: return Pop();
                case SoundPackId.Glass: return Glass();
                case SoundPackId.Drums: return Drums();
                case SoundPackId.Asmr: return Asmr();
                case SoundPackId.Chiptune: return Chiptune();
                case SoundPackId.None: return new SoundPack { Id = SoundPackId.None, I0 = 0f, I1 = 1f };
                default: throw new ArgumentOutOfRangeException(nameof(id));
            }
        }

        /// <summary>
        /// Notes: every hit is a major-pentatonic note (spec §2.7), C3 (largest body, ≥ 1.5 m) … C6 (smallest, ≤ 0.1 m),
        /// three octaves = 15 scale steps; bell sample recorded at C5 (72).
        /// </summary>
        static SoundPack Notes()
        {
            var p = Pack(SoundPackId.Notes, 0.3f, 6f);
            p.Rules.Add(new SoundRule(SoundEventType.Hit, "notes/bell") { Pitch = PitchMode.PentatonicBySize, RootMidi = 72, LowMidi = 48, Steps = 15 });
            p.Rules.Add(new SoundRule(SoundEventType.Break, "notes/chime") { Pitch = PitchMode.PentatonicBySize, RootMidi = 72, LowMidi = 60, Steps = 10, Gain = 0.9f });
            p.Rules.Add(new SoundRule(SoundEventType.Eliminate, "notes/drop") { Gain = 0.8f, ImpulseVolume = false });
            p.Rules.Add(new SoundRule(SoundEventType.Finish, "notes/fanfare") { ImpulseVolume = false });
            p.Rules.Add(new SoundRule(SoundEventType.Spawn, "notes/tick") { Gain = 0.4f, ImpulseVolume = false });
            p.Rules.Add(new SoundRule(SoundEventType.Score, "notes/ding") { Gain = 0.8f, ImpulseVolume = false });
            p.Rules.Add(new SoundRule(SoundEventType.Custom, "notes/bell") { RootMidi = 72, Gain = 0.8f });
            return p;
        }

        /// <summary>Melody: each qualifying hit plays the next melody note on a piano sample recorded at C4 (60).</summary>
        static SoundPack Melody()
        {
            var p = Pack(SoundPackId.Melody, 0.3f, 6f);
            p.Rules.Add(new SoundRule(SoundEventType.Hit, "melody/piano") { Pitch = PitchMode.Melody, RootMidi = 60, LowMidi = 48, Steps = 15 });
            p.Rules.Add(new SoundRule(SoundEventType.Break, "melody/chime") { Gain = 0.7f });
            p.Rules.Add(new SoundRule(SoundEventType.Eliminate, "melody/drop") { Gain = 0.7f, ImpulseVolume = false });
            p.Rules.Add(new SoundRule(SoundEventType.Finish, "melody/fanfare") { ImpulseVolume = false });
            p.Rules.Add(new SoundRule(SoundEventType.Score, "melody/ding") { Gain = 0.7f, ImpulseVolume = false });
            return p;
        }

        static SoundPack Pop()
        {
            var p = Pack(SoundPackId.Pop, 0.2f, 5f);
            p.Rules.Add(new SoundRule(SoundEventType.Hit, "pop/pop1", "pop/pop2", "pop/pop3") { Pitch = PitchMode.SizeSemitones, SemitoneRange = 5 });
            p.Rules.Add(new SoundRule(SoundEventType.Break, "pop/burst"));
            p.Rules.Add(new SoundRule(SoundEventType.Eliminate, "pop/deflate") { ImpulseVolume = false, Gain = 0.8f });
            p.Rules.Add(new SoundRule(SoundEventType.Finish, "pop/party") { ImpulseVolume = false });
            p.Rules.Add(new SoundRule(SoundEventType.Spawn, "pop/bloop") { ImpulseVolume = false, Gain = 0.5f });
            p.Rules.Add(new SoundRule(SoundEventType.Score, "pop/bloop") { ImpulseVolume = false, Semitones = 7, Gain = 0.7f });
            p.Rules.Add(new SoundRule(SoundEventType.Custom, "pop/pop1"));
            return p;
        }

        /// <summary>Glass: clinks for hits, shattering for breaks (M11, spec §2.7).</summary>
        static SoundPack Glass()
        {
            var p = Pack(SoundPackId.Glass, 0.4f, 8f);
            p.Rules.Add(new SoundRule(SoundEventType.Hit, "glass/clink1", "glass/clink2", "glass/clink3") { Pitch = PitchMode.SizeSemitones, SemitoneRange = 4 });
            p.Rules.Add(new SoundRule(SoundEventType.Break, "glass/shatter1", "glass/shatter2"));
            p.Rules.Add(new SoundRule(SoundEventType.Eliminate, "glass/crack") { ImpulseVolume = false, Gain = 0.8f });
            p.Rules.Add(new SoundRule(SoundEventType.Finish, "glass/chimes") { ImpulseVolume = false });
            p.Rules.Add(new SoundRule(SoundEventType.Score, "glass/ting") { ImpulseVolume = false, Gain = 0.7f });
            p.Rules.Add(new SoundRule(SoundEventType.Custom, "glass/clink1"));
            return p;
        }

        /// <summary>Drums: hits choose the drum by size, largest = kick … smallest = hi-hat (M13, spec §2.7).</summary>
        static SoundPack Drums()
        {
            var p = Pack(SoundPackId.Drums, 0.3f, 7f);
            p.Rules.Add(new SoundRule(SoundEventType.Hit, "drums/kick", "drums/tom", "drums/snare", "drums/hat") { Selection = SampleSelection.BySize });
            p.Rules.Add(new SoundRule(SoundEventType.Break, "drums/crash"));
            p.Rules.Add(new SoundRule(SoundEventType.Eliminate, "drums/rim") { ImpulseVolume = false, Gain = 0.8f });
            p.Rules.Add(new SoundRule(SoundEventType.Finish, "drums/crash") { ImpulseVolume = false });
            p.Rules.Add(new SoundRule(SoundEventType.Spawn, "drums/shaker") { ImpulseVolume = false, Gain = 0.4f });
            p.Rules.Add(new SoundRule(SoundEventType.Score, "drums/cowbell") { ImpulseVolume = false, Gain = 0.7f });
            p.Rules.Add(new SoundRule(SoundEventType.Custom, "drums/snare"));
            return p;
        }

        static SoundPack Asmr()
        {
            var p = Pack(SoundPackId.Asmr, 0.1f, 4f);
            p.Gain = 0.8f;
            p.Rules.Add(new SoundRule(SoundEventType.Hit, "asmr/click1", "asmr/click2", "asmr/wood1", "asmr/wood2") { Pitch = PitchMode.SizeSemitones, SemitoneRange = 3 });
            p.Rules.Add(new SoundRule(SoundEventType.Break, "asmr/snap"));
            p.Rules.Add(new SoundRule(SoundEventType.Eliminate, "asmr/rustle") { ImpulseVolume = false, Gain = 0.7f });
            p.Rules.Add(new SoundRule(SoundEventType.Finish, "asmr/tap_roll") { ImpulseVolume = false });
            p.Rules.Add(new SoundRule(SoundEventType.Spawn, "asmr/tap") { ImpulseVolume = false, Gain = 0.5f });
            p.Rules.Add(new SoundRule(SoundEventType.Score, "asmr/tap") { ImpulseVolume = false, Semitones = 5, Gain = 0.6f });
            p.Rules.Add(new SoundRule(SoundEventType.Custom, "asmr/click1"));
            return p;
        }

        /// <summary>Chiptune: 8-bit square notes on the pentatonic scale (theme Retro, spec §2.10).</summary>
        static SoundPack Chiptune()
        {
            var p = Pack(SoundPackId.Chiptune, 0.3f, 6f);
            p.Rules.Add(new SoundRule(SoundEventType.Hit, "chip/square") { Pitch = PitchMode.PentatonicBySize, RootMidi = 72, LowMidi = 48, Steps = 15, Gain = 0.7f });
            p.Rules.Add(new SoundRule(SoundEventType.Break, "chip/noise") { Gain = 0.8f });
            p.Rules.Add(new SoundRule(SoundEventType.Eliminate, "chip/down") { ImpulseVolume = false, Gain = 0.8f });
            p.Rules.Add(new SoundRule(SoundEventType.Finish, "chip/jingle") { ImpulseVolume = false });
            p.Rules.Add(new SoundRule(SoundEventType.Spawn, "chip/blip") { ImpulseVolume = false, Gain = 0.4f });
            p.Rules.Add(new SoundRule(SoundEventType.Score, "chip/coin") { ImpulseVolume = false, Gain = 0.7f });
            p.Rules.Add(new SoundRule(SoundEventType.Custom, "chip/blip"));
            return p;
        }

        static SoundPack Pack(SoundPackId id, float i0, float i1)
        {
            return new SoundPack { Id = id, I0 = i0, I1 = i1 };
        }
    }
}
