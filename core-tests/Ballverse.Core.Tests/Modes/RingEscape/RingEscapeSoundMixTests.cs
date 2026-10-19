using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Ballverse.Core.Integration;
using Ballverse.Core.Modes;
using Ballverse.Core.Modes.RingEscape;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;
using Xunit;
using Xunit.Abstractions;

namespace Ballverse.Core.Tests.Modes.RingEscape
{
    /// <summary>
    /// QA round 2: M01 sound through the real mixer decisions (spec §2.7 per-body 50 ms / 40 per s limits) and the export path
    /// (EVNT → render-stream source → mode wrapper) equal to the live list field for field (spec §7.8).
    /// </summary>
    public class RingEscapeSoundMixTests
    {
        static readonly IModeDefinition Mode = RingEscapeMode.Instance;
        readonly ITestOutputHelper _out;

        public RingEscapeSoundMixTests(ITestOutputHelper output)
        {
            _out = output;
        }

        static List<SoundEvent> LiveSound(ParamSet values, ulong seed)
        {
            var list = new List<SoundEvent>();
            var session = new ModeSession(Mode, values, seed, new ModeSessionOptions { SoundEvents = list });
            session.Start();
            session.RunHeadless(SimConstants.MaxDurationTicks);
            session.Complete();
            return list;
        }

        public static IEnumerable<object[]> Presets()
        {
            foreach (Preset p in RingEscapePresets.All) yield return new object[] { p.Id };
        }

        static ParamSet PresetValues(string id)
        {
            foreach (Preset p in Mode.BuiltInPresets)
                if (p.Id == id) return ModeParams.ApplyPreset(Mode, p, DeviceTier.A).Values;
            throw new ArgumentException(id);
        }

        [Theory]
        [MemberData(nameof(Presets))]
        public void BreaksAndTheEscapeChordAreNeverDroppedByThePerBodyLimit(string presetId)
        {
            ParamSet values = PresetValues(presetId);
            int finishes = 0, finishPlayed = 0, breaks = 0, breakPlayed = 0, breakGlobal = 0;
            foreach (SoundPackId pack in new[] { SoundPackId.Notes, SoundPackId.Glass })
            {
                for (ulong seed = 1; seed <= 5; seed++)
                {
                    List<SoundEvent> live = LiveSound(values, seed);
                    var mixer = new SoundEventProcessor(SoundPackCatalog.Get(pack));
                    foreach (SoundEvent e in live)
                    {
                        SoundDecision d = mixer.Process(in e, out _);
                        if (e.Type == SoundEventType.Break)
                        {
                            breaks++;
                            Assert.True(e.BodyId >= RingEscapeScene.RingIdBase, "break emitted by the ring, not the ball");
                            Assert.NotEqual(SoundDecision.BodyRateLimited, d);
                            if (d == SoundDecision.Played) breakPlayed++;
                            if (d == SoundDecision.GlobalRateLimited) breakGlobal++;
                        }
                        else if (e.Type == SoundEventType.Finish)
                        {
                            finishes++;
                            Assert.Equal(-1, e.BodyId);
                            if (d == SoundDecision.Played) finishPlayed++;
                            else _out.WriteLine(presetId + " " + pack + " seed " + seed + " finish " + d + " t" + e.Tick);
                        }
                    }
                }
            }
            _out.WriteLine(presetId + ": finishes " + finishPlayed + "/" + finishes + ", breaks " + breakPlayed + "/" + breaks + " global " + breakGlobal);
            Assert.True(breaks > 0);
            Assert.True(finishes > 0);
            Assert.Equal(finishes, finishPlayed);
            // Breaks have priority over hits within the 40/s (RingEscapeSound.BreakSoundCap): at 1× none is refused.
            Assert.Equal(breaks, breakPlayed);
        }

        /// <summary>Every passed ring of a combo-heavy run gets its break sound in the list (the budget never drops one there).</summary>
        [Fact]
        public void EveryPassedRingOfHundredRingsSoundsAndCombosRiseAudibly()
        {
            ParamSet values = PresetValues(RingEscapePresets.HundredRings);
            ModeConformance.Recording rec = ModeConformance.Record(Mode, values, 3, SimConstants.MaxDurationTicks);
            var mixer = new SoundEventProcessor(SoundPackCatalog.Get(SoundPackId.Notes));
            int breaks = 0, comboRises = 0, lastMidi = -1, lastTick = -100;
            foreach (SoundEvent e in rec.SoundEvents)
            {
                SoundDecision d = mixer.Process(in e, out VoiceRequest v);
                if (e.Type != SoundEventType.Break) continue;
                breaks++;
                Assert.Equal(SoundDecision.Played, d);
                if (e.Tick - lastTick <= 3 && v.Semitones > lastMidi) comboRises++;
                lastMidi = v.Semitones;
                lastTick = e.Tick;
            }
            Assert.Equal(rec.Outcome.Result.Score, breaks);
            Assert.True(comboRises > 0, "no audible combo rise");
        }

        public static IEnumerable<object[]> ExportCases()
        {
            yield return new object[] { "defaults", 2026UL };
            yield return new object[] { RingEscapePresets.ColorRace, 2UL };
            yield return new object[] { RingEscapePresets.HundredRings, 5UL };
            yield return new object[] { RingEscapePresets.NeonChaos, 7UL };
            yield return new object[] { "rings60_15s", 11UL };
            yield return new object[] { "keptRings", 4UL };
        }

        static ParamSet CaseValues(string name)
        {
            ParamSet d = ModeParams.Defaults(Mode, DeviceTier.A);
            switch (name)
            {
                case "defaults": return ModeParams.Prepare(Mode, d, DeviceTier.A).Values;
                case "rings60_15s":
                    return ModeParams.Prepare(Mode, d.With(RingEscapeParams.RingCount, ParamValue.Int(60))
                        .With(CommonParams.TargetDuration, ParamValue.Int(15)), DeviceTier.A).Values;
                case "keptRings":
                    return ModeParams.Prepare(Mode, d.With(RingEscapeParams.DestroyPassed, ParamValue.Bool(false)), DeviceTier.A).Values;
                default: return PresetValues(name);
            }
        }

        /// <summary>
        /// Export path (spec §7.8): EVNT records + the render stream through the Studio's mode-agnostic source, wrapped by the
        /// mode's <see cref="IModeSoundMapping"/> resolved from the header's modeId, give exactly the live list (tick, type, body
        /// id, impulse, size, pan). Without the wrapper the mixer still takes the same play / drop decisions.
        /// </summary>
        [Theory]
        [MemberData(nameof(ExportCases))]
        public void ExportedSoundEqualsTheLiveListFieldForField(string name, ulong seed)
        {
            ParamSet values = CaseValues(name);
            ModeConformance.Recording rec = ModeConformance.Record(Mode, values, seed, SimConstants.MaxDurationTicks);
            ReplayFile replay = ReplayReader.Read(rec.Replay);
            SoundEvent[] exported, generic;
            using (var reader = new RenderStreamReader(new MemoryStream(rec.RenderStream)))
            {
                ISoundBodySource source = ModeSoundMapping.ForReplay(ReleaseModes.CreateRegistry(), replay.Header.ModeId,
                    new StreamSoundBodySourceMirror(reader));
                Assert.IsType<RingEscapeSound.BodySource>(source);
                exported = ReplaySoundEvents.Convert(replay, source);
            }
            using (var reader = new RenderStreamReader(new MemoryStream(rec.RenderStream)))
            {
                generic = ReplaySoundEvents.Convert(replay, new StreamSoundBodySourceMirror(reader));
            }
            Assert.Equal(rec.SoundEvents.Count, exported.Length);
            int breaks = 0, finishes = 0;
            for (int i = 0; i < exported.Length; i++)
            {
                SoundEvent live = rec.SoundEvents[i];
                Assert.True(live.Equals(exported[i]), i + ": live " + live + " vs export " + exported[i]);
                if (live.Type == SoundEventType.Break) breaks++;
                if (live.Type == SoundEventType.Finish) finishes++;
            }
            Assert.True(breaks > 0);
            Assert.True(finishes > 0);

            foreach (SoundPackId pack in new[] { SoundPackId.Notes, SoundPackId.Glass, SoundPackId.Drums })
            {
                var a = new SoundEventProcessor(SoundPackCatalog.Get(pack));
                var b = new SoundEventProcessor(SoundPackCatalog.Get(pack));
                for (int i = 0; i < generic.Length; i++)
                {
                    SoundEvent l = rec.SoundEvents[i];
                    Assert.Equal(a.Process(in l, out _), b.Process(in generic[i], out _));
                }
            }
        }

        [Fact]
        public void SoundMappingResolvesOnlyForModesThatImplementIt()
        {
            var inner = new UniformSoundBodySource(0.5f);
            ModeRegistry registry = ReleaseModes.CreateRegistry();
            Assert.IsType<RingEscapeSound.BodySource>(ModeSoundMapping.ForReplay(registry, "ring_escape", inner));
            Assert.Same(inner, ModeSoundMapping.ForReplay(registry, "unknown_mode", inner));
            Assert.Same(inner, ModeSoundMapping.ForReplay(registry, null, inner));
            Assert.Same(inner, ModeSoundMapping.ForMode(FakeBoxMode.Instance, inner));
        }

        [Fact]
        public void LiveImpulsesEqualTheDequantizedEvntImpulse()
        {
            float raw = 0.8164913f;
            Assert.Equal(ReplayQuantization.DequantizeImpulse(ReplayQuantization.QuantizeImpulse(raw)), ModeSound.Impulse(raw));
            SoundEvent e = ModeSound.ForBall(SoundEventType.Hit, 3, raw, RenderQuantization.Default, 0.175f, 1f);
            Assert.Equal(ModeSound.Impulse(raw), e.Impulse);
        }

        [Fact]
        public void SoundBudgetKeepsTheMixerWindowAndPriorities()
        {
            var budget = new ModeSoundBudget();
            // 32 hits in tick 10 fill the hit cap; a 33rd hit waits, a break still fits up to 39, the chord up to 40.
            for (int i = 0; i < 32; i++) Assert.True(budget.TryTake(10, 32));
            Assert.False(budget.TryTake(10, 32));
            for (int i = 0; i < 7; i++) Assert.True(budget.TryTake(20, 39));
            Assert.False(budget.TryTake(20, 39));
            Assert.True(budget.TryTake(30, 40));
            Assert.False(budget.TryTake(30, 40));
            Assert.False(budget.TryTake(69, 40)); // tick 10 is still inside 69 − 59 … 69
            Assert.True(budget.TryTake(70, 32));  // the 32 sounds of tick 10 left the window
            Assert.Equal(9, budget.Count);
            Assert.True(budget.TryTake(1000, 1));
            Assert.Equal(1, budget.Count);

            // Against the real mixer window: whatever the budget lets through at cap 40 is never globally refused.
            var mixer = new SoundEventProcessor(SoundPackCatalog.Get(SoundPackId.Notes));
            var b2 = new ModeSoundBudget();
            uint x = 5;
            for (int tick = 0; tick < 3000; tick++)
            {
                x = x * 1664525u + 1013904223u;
                int n = (int)(x >> 30); // 0…3 sounds per tick: up to 180 per second offered
                for (int k = 0; k < n; k++)
                {
                    if (!b2.TryTake(tick, 40)) continue;
                    var e = new SoundEvent(tick, SoundEventType.Finish, -1, 1f, 0.5f, 0f);
                    Assert.Equal(SoundDecision.Played, mixer.Process(in e, out _));
                }
            }
        }
    }
}
