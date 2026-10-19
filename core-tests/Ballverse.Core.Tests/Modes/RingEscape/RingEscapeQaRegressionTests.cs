using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Ballverse.Core.Generation;
using Ballverse.Core.Integration;
using Ballverse.Core.Modes;
using Ballverse.Core.Modes.RingEscape;
using Ballverse.Core.Params;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Modes.RingEscape
{
    /// <summary>Regression tests for the QA round 1 findings on M01 (labels, kept-ring growth, «Новый шар» budget, sound,
    /// «Минимализм», duration hint) and the §8.5 «20 сидов × 2 прогона» determinism requirement.</summary>
    public class RingEscapeQaRegressionTests
    {
        static readonly IModeDefinition Mode = RingEscapeMode.Instance;

        static ParamSet Prepared(DeviceTier tier, params (string id, ParamValue v)[] values)
        {
            ParamSet s = ModeParams.Defaults(Mode, tier);
            foreach (var (id, v) in values) s = s.With(id, v);
            return ModeParams.Prepare(Mode, s, tier).Values;
        }

        static List<string> Names(int edit)
        {
            var l = new List<string>();
            for (int i = 0; i < 32; i++) l.Add("n" + edit + "_" + i);
            return l;
        }

        // ------------------------------------------------------------------ labels (QA major 1)

        [Fact]
        public void LiveLabelNameEditsNeverOverflowTheLabelTable()
        {
            // 80 number labels + 60 edits × 32 new names = 2 000 strings > RenderStreamLimits.MaxLabels (1 024).
            ParamSet values = Prepared(DeviceTier.A, (CommonParams.Labels, ParamValue.String("names")), (CommonParams.BallCount, ParamValue.Int(3)));
            ModeConformance.Recording rec = ModeConformance.Record(Mode, values, 1, 150, s =>
            {
                if (s.Tick < 60) s.SetLiveParam(CommonParams.LabelNames, ParamValue.Strings(Names(s.Tick)));
            });
            Assert.Equal(RenderStreamLimits.MaxLabels, rec.Session.Visuals.LabelCount);
            Assert.Equal(0, rec.Session.Visuals.FreeLabelSlots);
            Assert.Equal(150, rec.Session.Tick);
            ModeConformance.AssertRenderStreamReadable(Mode, rec);
            ReplayFile file = ReplayReader.Read(rec.Replay);
            Assert.Equal(60, file.ParamChanges.Count);
            ModeConformance.AssertReplayRoundTrip(Mode, rec);

            // A name that no longer fits is drawn without a label; the earlier names keep theirs.
            RenderStreamReader reader = new RenderStreamReader(new MemoryStream(rec.RenderStream));
            RenderFrame last = reader.ReadFrame(reader.TickCount - 1);
            foreach (BodyState b in last.Bodies)
                if (b.Id < RingEscapeScene.RingIdBase) Assert.Equal(BodyState.NoLabel, b.LabelId);
            RenderFrame early = reader.ReadFrame(2);
            foreach (BodyState b in early.Bodies)
                if (b.Id < RingEscapeScene.RingIdBase) Assert.StartsWith("n1_", reader.GetLabel(b.LabelId));
        }

        [Fact]
        public void HandMadeReplayWithManyLabelChangesResimulatesWithoutThrowing()
        {
            ParamSet values = Prepared(DeviceTier.A, (CommonParams.Labels, ParamValue.String("names")));
            ReplayHeader h = ReplayParams.CreateHeader(Mode.Info.Id, Mode.SimVersion, "probe", PhysicsBackend.CoreSim, 1, values, Mode.Schema);
            var ms = new MemoryStream();
            var w = new ReplayWriter(ms, h);
            for (int i = 0; i < 40; i++) w.AddParamChange(ReplayParams.ParamChange((uint)(i + 1), CommonParams.LabelNames, ParamValue.Strings(Names(i)), Mode.Schema));
            w.Finish(120);
            var bvs = new MemoryStream();
            ModeReplayResult r = ModeReplay.Resimulate(Mode, ms.ToArray(), new ModeReplayOptions { RenderStream = bvs });
            Assert.Equal(ModeReplayStatus.Verified, r.Status);
            Assert.Equal(120, r.Ticks);
            Assert.Equal(121, new RenderStreamReader(new MemoryStream(bvs.ToArray())).TickCount);
        }

        // ------------------------------------------------------------------ growth with kept rings (QA major 3)

        [Fact]
        public void KeptRingsBoundGrowthSoABallThatFallsBackCanLeaveAgain()
        {
            // QA probe P5: «Статично», destroyPassed off, ring 0 passes only 0.29 m but ring 1 allowed 1.03 m; the seeds that
            // trapped a grown ball inside the passed ring 0 must now escape, and every run keeps the probe's invariants.
            ParamSet values = Prepared(DeviceTier.A,
                (RingEscapeParams.Progression, ParamValue.String(RingEscapeParams.ProgressionStatic)),
                (RingEscapeParams.DestroyPassed, ParamValue.Bool(false)),
                (RingEscapeParams.InnerRadius, ParamValue.Float(0.6)),
                (RingEscapeParams.RingSpacing, ParamValue.Float(1.2)),
                (RingEscapeParams.RingCount, ParamValue.Int(3)),
                (CommonParams.BallSize, ParamValue.Float(0.2)),
                (RingEscapeParams.GapSize, ParamValue.Float(40)),
                (RingEscapeParams.GrowOnHit, ParamValue.Int(10)));
            RingEscapeConfig c = RingEscapeConfig.From(values);
            Assert.False(c.EffectiveDestroyPassed);
            Assert.Equal(40f, c.GapSizeDeg);
            Assert.True(RingEscapeGeometry.GrowLimit(c, 1) > 3f * RingEscapeGeometry.GrowLimit(c, 0));
            foreach (ulong seed in new ulong[] { 101, 102, 114, 123, 131, 189, 1, 2, 3, 4 })
            {
                var s = new ModeSession(Mode, values, seed, new ModeSessionOptions { BuildFrames = false });
                var scene = (RingEscapeScene)s.Scene;
                float maxDiameter = 0f;
                s.Start();
                while (s.Step())
                {
                    ref readonly BallState b = ref s.World.GetBall(0);
                    if (b.Alive) maxDiameter = MathF.Max(maxDiameter, 2f * b.Radius);
                    if (scene.CurrentRing > 0) Assert.Equal(scene.GrowLimit(0), scene.EffectiveGrowLimit);
                }
                Assert.True(scene.EscapeTick >= 0, "seed " + seed + " did not escape");
                Assert.True(maxDiameter <= RingEscapeGeometry.GrowLimit(c, 0) + 1e-4f, "seed " + seed + " grew to " + maxDiameter);
                RingEscapeProbe.RunChecked(values, seed);
            }
        }

        [Fact]
        public void DestroyedPassedRingsStillBoundGrowthFromTheCurrentRing()
        {
            ParamSet values = Prepared(DeviceTier.A, (RingEscapeParams.GrowOnHit, ParamValue.Int(10)), (RingEscapeParams.GapShrink, ParamValue.Int(20)));
            var s = new ModeSession(Mode, values, 3, new ModeSessionOptions { BuildFrames = false });
            var scene = (RingEscapeScene)s.Scene;
            s.Start();
            while (scene.CurrentRing < 3 && s.Step()) { }
            Assert.Equal(scene.GrowLimit(scene.CurrentRing), scene.EffectiveGrowLimit);
        }

        // ------------------------------------------------------------------ «Новый шар» budget (QA major 4)

        [Theory]
        [InlineData(DeviceTier.A, 20)]
        [InlineData(DeviceTier.B, 12)]
        [InlineData(DeviceTier.C, 8)]
        public void NewBallStaysWithinTheTierBallBudget(DeviceTier tier, int budget)
        {
            Assert.Equal(budget, (int)BodyBudgets.BallCap(ModeCode.M01).For(tier).Value);
            ParamSet values = Prepared(tier, (CommonParams.BallCount, ParamValue.Int(budget)), (RingEscapeParams.RingCount, ParamValue.Int(3)),
                (RingEscapeParams.GapSize, ParamValue.Float(60)), (RingEscapeParams.AfterEscape, ParamValue.String(RingEscapeParams.AfterNewBall)));
            Assert.Equal(budget, values.GetInt(CommonParams.BallCount));
            Assert.Equal(budget, values.GetInt(RingEscapeParams.MaxRoundBalls));
            // A set prepared for this tier keeps its cap wherever it is played back (the value travels in the replay).
            Assert.True(ModeParams.IsRunnable(Mode, values, out string problem), problem);

            var s = new ModeSession(Mode, values, 11, new ModeSessionOptions { BuildFrames = false, Tier = DeviceTier.A });
            var scene = (RingEscapeScene)s.Scene;
            int maxAlive = 0;
            s.Start();
            while (s.Step())
            {
                int alive = 0;
                for (int i = 0; i < s.World.BallCount; i++) if (s.World.GetBall(i).Alive) alive++;
                maxAlive = Math.Max(maxAlive, alive);
            }
            Assert.True(scene.Round >= 2, "rounds " + scene.Round);
            Assert.True(maxAlive <= budget, "alive " + maxAlive + " > " + budget);
        }

        [Fact]
        public void NewBallGrowsOneBallPerRoundUpToMaxRoundBalls()
        {
            ParamSet values = Prepared(DeviceTier.A, (RingEscapeParams.RingCount, ParamValue.Int(3)), (RingEscapeParams.GapSize, ParamValue.Float(60)),
                (RingEscapeParams.AfterEscape, ParamValue.String(RingEscapeParams.AfterNewBall)), (RingEscapeParams.MaxRoundBalls, ParamValue.Int(4)));
            var s = new ModeSession(Mode, values, 21, new ModeSessionOptions { BuildFrames = false });
            var scene = (RingEscapeScene)s.Scene;
            s.RunToEnd();
            Assert.True(scene.Round >= 4, "rounds " + scene.Round);
            Assert.Equal(4, scene.RoundBallCount);
        }

        // ------------------------------------------------------------------ sound progression (QA major 5)

        [Fact]
        public void RingHitsAndBreaksRiseInPitchWithTheRingAndTheCombo()
        {
            ParamSet values = Prepared(DeviceTier.A);
            ModeConformance.Recording rec = ModeConformance.Record(Mode, values, 2026, SimConstants.MaxDurationTicks);
            ReplayFile file = ReplayReader.Read(rec.Replay);

            // Live sound and EVNT come from the same facts in the same order (Hit / Break).
            var live = new List<SoundEvent>();
            foreach (SoundEvent e in rec.SoundEvents) if (e.Type == SoundEventType.Hit || e.Type == SoundEventType.Break) live.Add(e);
            var evnt = new List<ReplayEvent>();
            foreach (ReplayEvent e in file.Events) if (e.Type == ReplayEventType.Hit || e.Type == ReplayEventType.Break) evnt.Add(e);
            Assert.Equal(evnt.Count, live.Count);

            var exportSource = new RingEscapeSound.BodySource(new UniformSoundBodySource(0.35f));
            int lastBreakMidi = -1, lastBreakRing = -1, breaks = 0, ringHits = 0;
            for (int i = 0; i < live.Count; i++)
            {
                ReplayEvent r = evnt[i];
                SoundEvent s = live[i];
                Assert.Equal((int)r.Tick, s.Tick);
                Assert.Equal((int)r.Type, (int)s.Type);
                SoundEvent exported = ReplaySoundEvents.ToSoundEvent(in r, exportSource);
                if (r.Type == ReplayEventType.Break)
                {
                    breaks++;
                    int ring = RingEscapeSound.RingOfBreakExtra(r.Extra);
                    int combo = RingEscapeSound.ComboOfBreakExtra(r.Extra);
                    Assert.Equal(lastBreakRing + 1, ring);
                    Assert.True(combo >= 1);
                    Assert.Equal(s.BodySize, exported.BodySize); // the export hears what the app played
                    int midi = SoundPitch.PentatonicMidi(s.BodySize, RingEscapeSound.PitchSizeMin, RingEscapeSound.PitchSizeMax, 60, RingEscapeSound.BreakSteps);
                    int step = RingEscapeSound.BreakStep(ring, combo);
                    Assert.Equal(PentatonicMidi(60, step), midi);
                    // Within one combo, and for first rings of a flight below the wrap, the pitch rises ring by ring.
                    if (lastBreakMidi >= 0 && ring + combo - 1 <= RingEscapeSound.BreakSteps && combo > 1) Assert.True(midi > lastBreakMidi);
                    lastBreakMidi = midi;
                    lastBreakRing = ring;
                }
                else if (r.Extra >= 0)
                {
                    ringHits++;
                    int ring = (int)r.Extra;
                    Assert.Equal(s.BodySize, exported.BodySize);
                    int midi = SoundPitch.PentatonicMidi(s.BodySize, RingEscapeSound.PitchSizeMin, RingEscapeSound.PitchSizeMax, 48, RingEscapeSound.HitSteps);
                    Assert.Equal(PentatonicMidi(48, RingEscapeSound.HitStep(ring)), midi);
                }
                else
                {
                    // Ball–ball hits keep the ball size.
                    Assert.Equal(ModeSound.BodySize(rec.Session.Scene.Quantization, 0.175f), s.BodySize);
                }
            }
            Assert.Equal(12, breaks);
            Assert.True(ringHits > 12);
        }

        [Fact]
        public void PitchStepsFollowTheCardsProgression()
        {
            float previous = float.MaxValue;
            for (int ring = 0; ring <= RingEscapeSound.HitSteps; ring++)
            {
                float size = RingEscapeSound.HitPitchSize(ring);
                Assert.True(size < previous); // smaller pitch size = higher note
                previous = size;
                Assert.Equal(PentatonicMidi(48, ring), SoundPitch.PentatonicMidi(size, RingEscapeSound.PitchSizeMin, RingEscapeSound.PitchSizeMax, 48, 15));
            }
            Assert.Equal(RingEscapeSound.HitPitchSize(0), RingEscapeSound.HitPitchSize(RingEscapeSound.HitSteps + 1)); // wraps after 3 octaves
            // A combo climbs faster than one ring per break.
            Assert.True(RingEscapeSound.BreakStep(3, 3) > RingEscapeSound.BreakStep(4, 1));
            Assert.Equal(RingEscapeSound.BreakStep(2, 1) + 1, RingEscapeSound.BreakStep(3, 1));
            long extra = RingEscapeSound.BreakExtra(37, 5);
            Assert.Equal(37, RingEscapeSound.RingOfBreakExtra(extra));
            Assert.Equal(5, RingEscapeSound.ComboOfBreakExtra(extra));
            Assert.Equal(99, RingEscapeSound.BreakExtra(99, 1));
        }

        static int PentatonicMidi(int low, int step)
        {
            return low + 12 * (step / 5) + SoundPitch.PentatonicIntervals[step % 5];
        }

        // ------------------------------------------------------------------ «Минимализм» (QA minor 7)

        [Theory]
        [InlineData(15)]
        [InlineData(60)]
        [InlineData(120)]
        [InlineData(180)]
        [InlineData(0)]
        public void MinimalismKeepsSixToTenRingsAtEveryTarget(int target)
        {
            ParamSet b = ModeParams.Defaults(Mode, DeviceTier.B);
            if (target > 0) b = b.With(CommonParams.TargetDuration, ParamValue.Int(target));
            b = ModeParams.Prepare(Mode, b, DeviceTier.B).Values;
            for (ulong seed = 0; seed < 40; seed++)
            {
                ParamSet c = Mode.GenerateCandidate(new ModeCandidateRequest { BaseValues = b, Seed = seed, Style = GenerationStyle.Minimalism, Tier = DeviceTier.B });
                Assert.InRange(c.GetInt(RingEscapeParams.RingCount), RingEscapeGenerator.MinimalismMinRings, RingEscapeGenerator.MinimalismMaxRings);
                Assert.Equal(1, c.GetInt(CommonParams.BallCount));
            }
        }

        // ------------------------------------------------------------------ duration hint (QA minor 8)

        [Theory]
        [InlineData(0)]
        [InlineData(60)]
        [InlineData(180)]
        public void DurationHintStaysHiddenWhenTheBudgetIsTooShortForAVerdict(int budget)
        {
            RingEscapeDurationEstimate e = RingEscapeDurationHint.Estimate(Prepared(DeviceTier.B), 1, budget);
            Assert.False(e.ShowHint);
            Assert.Equal(0, e.SeedsEscaped);
        }

        [Fact]
        public void DurationHintIgnoresTheCountdown()
        {
            ParamSet values = Prepared(DeviceTier.B, (CommonParams.Countdown, ParamValue.Bool(true)));
            for (ulong seed = 1; seed <= 5; seed++)
            {
                RingEscapeDurationEstimate e = RingEscapeDurationHint.Estimate(values, seed, 900);
                Assert.False(e.ShowHint, "seed " + seed);
                Assert.True(e.ProjectedSeconds < 180f, "seed " + seed + ": " + e.ProjectedSeconds);
            }
        }
    }
}
