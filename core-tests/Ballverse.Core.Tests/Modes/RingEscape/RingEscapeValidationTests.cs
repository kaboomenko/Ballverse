using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Modes;
using Ballverse.Core.Modes.RingEscape;
using Ballverse.Core.Params;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Modes.RingEscape
{
    /// <summary>Spec §2.9 M01 "Валидация геометрии" rules 1–7, the card's worked numbers, presets and §8.5 test (а).</summary>
    public class RingEscapeValidationTests
    {
        static readonly IModeDefinition Mode = RingEscapeMode.Instance;

        static ParamSet Defaults => ModeParams.Defaults(Mode);

        static ParamSet With(params (string id, ParamValue v)[] values)
        {
            ParamSet s = Mode.Schema.CreateDefaults();
            foreach (var (id, v) in values) s = s.With(id, v);
            return s;
        }

        [Fact]
        public void SchemaFollowsTheCard()
        {
            ParamSchema s = Mode.Schema;
            Assert.Equal(12, ((IntParam)s.Get(RingEscapeParams.RingCount)).DefaultValue());
            Assert.Equal(20, ((IntParam)s.Get(CommonParams.BallCount)).Max);
            Assert.Equal(12, ((IntParam)s.Get(CommonParams.BallCount)).EffectiveMax(DeviceTier.B));
            Assert.Equal(8, ((IntParam)s.Get(CommonParams.BallCount)).EffectiveMax(DeviceTier.C));
            Assert.Equal(0.35f, ((FloatParam)s.Get(CommonParams.BallSize)).DefaultValue);
            Assert.Equal(1.0f, ((FloatParam)s.Get(CommonParams.Restitution)).DefaultValue);
            Assert.Equal(0f, ((FloatParam)s.Get(CommonParams.Friction)).DefaultValue);
            Assert.Equal(20f, ((FloatParam)s.Get(CommonParams.MaxSpeed)).DefaultValue);
            Assert.True(s.Get(RingEscapeParams.RotationSpeed).Live);
            Assert.True(s.Get(RingEscapeParams.BreakFx).Live);
            Assert.False(s.Get(RingEscapeParams.RingCount).Live);
            Assert.True(s.Contains(CommonParams.ForcedWinner));
            ParamSet d = s.CreateDefaults();
            Assert.False(s.IsVisible(RingEscapeParams.DestroyPassed, d)); // hidden in «Сжатие»
            Assert.False(s.IsVisible(RingEscapeParams.Competition, d));   // one ball
            Assert.True(s.IsVisible(RingEscapeParams.Competition, d.With(CommonParams.BallCount, ParamValue.Int(2))));
            Assert.False(s.IsVisible(CommonParams.ForcedWinner, d));
            Assert.False(s.IsVisible(RingEscapeParams.LaunchAngle, d));
            Assert.True(s.IsVisible(RingEscapeParams.LaunchAngle, d.With(RingEscapeParams.LaunchAngleRandom, ParamValue.Bool(false))));
        }

        [Fact]
        public void DefaultsAreValidAndMatchTheCardNumbers()
        {
            var issues = new List<ParamIssue>();
            ParamSet d = Mode.Schema.CreateDefaults();
            Assert.Equal(d, Mode.Constrain(d, DeviceTier.A, issues));
            Assert.Empty(issues);
            RingEscapeConfig c = RingEscapeConfig.From(d);
            // Rule 3 at the defaults: gapMin = 25.0°, so a 35° gap allows gapShrink ≤ 28 %.
            Assert.Equal(25.0f, RingEscapeGeometry.GapMinDeg(1.2f, 0.35f, 0.12f), 1);
            Assert.Equal(28, (int)MathF.Floor(RingEscapeGeometry.MaxGapShrink(c) * 100f));
            // Rule 7: 22 balls fit in ring 0 at the defaults (≥ the 20-ball maximum).
            Assert.Equal(22, RingEscapeGeometry.StartCapacity(0.35f, 1.2f, 0.12f));
            // Speeds: the outermost ring in the solver (12 m) allows 119 °/s; the default rings never hit the envelope.
            Assert.Equal(119f, RingEscapeGeometry.OmegaLimitRad(12f) / SimMath.DegToRad, 0);
        }

        [Fact]
        public void ImpossibleEscapeNumbersMatchTheCard()
        {
            // 4 rings, innerRadius 1.6 m, ring 0.06 m, ball 0.2 m, gap 30° shrinking 50 % → last gap 15°, free width 0.36 m, gapMin 11.1°.
            Assert.Equal(15f, RingEscapeGeometry.GapDeg(30f, 0.5f, 3, 4), 3);
            Assert.Equal(0.358f, RingEscapeGeometry.FreeWidth(1.6f, 15f, 0.06f), 2);
            Assert.Equal(11.1f, RingEscapeGeometry.GapMinDeg(1.6f, 0.2f, 0.06f), 1);
        }

        [Fact]
        public void Rule1RaisesTheSpacing()
        {
            var issues = new List<ParamIssue>();
            ParamSet v = Mode.Constrain(With((CommonParams.BallSize, ParamValue.Float(0.5)), (RingEscapeParams.RingSpacing, ParamValue.Float(0.3)),
                (RingEscapeParams.GapSize, ParamValue.Float(60))), DeviceTier.A, issues);
            Assert.Equal(0.7f, v.GetFloat(RingEscapeParams.RingSpacing), 4); // 0.5 + 0.12 + 0.05 = 0.67 → 0.70 on the grid
            Assert.Contains(issues, i => i.ParamId == RingEscapeParams.RingSpacing);
        }

        [Fact]
        public void Rule2LowersTheGapForManyGaps()
        {
            var issues = new List<ParamIssue>();
            ParamSet v = Mode.Constrain(With((RingEscapeParams.GapCount, ParamValue.Int(4)), (RingEscapeParams.GapSize, ParamValue.Float(100))), DeviceTier.A, issues);
            Assert.Equal(70f, v.GetFloat(RingEscapeParams.GapSize));
            Assert.True(RingEscapeGeometry.SolidArcOk(4, 70f));
        }

        [Fact]
        public void Rule3RaisesTheGapAndLowersTheShrink()
        {
            var issues = new List<ParamIssue>();
            ParamSet v = Mode.Constrain(With((RingEscapeParams.GapSize, ParamValue.Float(10)), (RingEscapeParams.GapShrink, ParamValue.Int(50))), DeviceTier.A, issues);
            Assert.Equal(26f, v.GetFloat(RingEscapeParams.GapSize)); // ⌈25.03°⌉
            Assert.Equal(3, v.GetInt(RingEscapeParams.GapShrink));   // 1 − 25.03 / 26 = 3.7 %
            RingEscapeConfig c = RingEscapeConfig.From(v);
            Assert.True(RingEscapeGeometry.PassabilityOk(c));
            ParamSet w = Mode.Constrain(Defaults.With(RingEscapeParams.GapShrink, ParamValue.Int(50)), DeviceTier.A, new List<ParamIssue>());
            Assert.Equal(28, w.GetInt(RingEscapeParams.GapShrink));
        }

        [Fact]
        public void Rule3InOtherProgressionsUsesEachRingsOwnRadius()
        {
            ParamSet v = Mode.Constrain(With((RingEscapeParams.Progression, ParamValue.String(RingEscapeParams.ProgressionStatic)),
                (RingEscapeParams.GapShrink, ParamValue.Int(50)), (RingEscapeParams.RingCount, ParamValue.Int(100))), DeviceTier.A, new List<ParamIssue>());
            RingEscapeConfig c = RingEscapeConfig.From(v);
            Assert.Equal(5, c.RingCount); // «Статично»: ⌊(4 − 1.2) / 0.6⌋ + 1
            Assert.True(RingEscapeGeometry.PassabilityOk(c));
            Assert.True(c.GapShrink > 0.28f); // larger outer radii allow more shrink than «Сжатие»
        }

        [Fact]
        public void ProgressionLimitsTheRingCount()
        {
            ParamSet zoom = Mode.Constrain(With((RingEscapeParams.Progression, ParamValue.String(RingEscapeParams.ProgressionCameraZoom)),
                (RingEscapeParams.RingCount, ParamValue.Int(100))), DeviceTier.A, new List<ParamIssue>());
            Assert.Equal(19, zoom.GetInt(RingEscapeParams.RingCount)); // ⌊(12 − 1.2) / 0.6⌋ + 1
            ParamSet compress = Mode.Constrain(With((RingEscapeParams.RingCount, ParamValue.Int(100))), DeviceTier.A, new List<ParamIssue>());
            Assert.Equal(100, compress.GetInt(RingEscapeParams.RingCount));
        }

        [Fact]
        public void Rule7BallsMustFitInsideRingZero()
        {
            // Docs QA residual: innerRadius 1.2, spacing 1.2, ball 1.0, gap 120° passed rules 1–6 with 20 balls.
            var issues = new List<ParamIssue>();
            ParamSet v = Mode.Constrain(With((RingEscapeParams.RingSpacing, ParamValue.Float(1.2)), (CommonParams.BallSize, ParamValue.Float(1.0)),
                (RingEscapeParams.GapSize, ParamValue.Float(120)), (CommonParams.BallCount, ParamValue.Int(20))), DeviceTier.A, issues);
            RingEscapeConfig c = RingEscapeConfig.From(v);
            int capacity = RingEscapeGeometry.StartCapacity(c.BallSize, c.InnerRadius, c.RingThickness);
            Assert.True(capacity < 20);
            Assert.Equal(capacity, c.BallCount);
            Assert.Contains(issues, i => i.ParamId == CommonParams.BallCount);
            // The layout keeps 0.05 m between balls and to the ring's inner face.
            var pos = new Vec2[capacity];
            Assert.Equal(capacity, RingEscapeGeometry.Layout(c.BallSize, c.InnerRadius, c.RingThickness, capacity, pos));
            AssertLayoutClear(pos, capacity, c);
        }

        [Theory]
        [InlineData(0.35f, 1.2f, 0.12f)]
        [InlineData(0.6f, 1.2f, 0.12f)]
        [InlineData(0.1f, 2.5f, 0.04f)]
        [InlineData(0.2f, 0.6f, 0.04f)]
        [InlineData(0.9f, 2.5f, 0.3f)]
        public void LayoutPlacesEveryCountUpToTheCapacityWithClearance(float d, float inner, float t)
        {
            var c = new RingEscapeConfig { BallSize = d, InnerRadius = inner, RingThickness = t };
            int capacity = RingEscapeGeometry.StartCapacity(d, inner, t);
            Assert.True(capacity >= 1);
            var pos = new Vec2[Math.Max(capacity, 1)];
            for (int n = 1; n <= Math.Min(capacity, 20); n++)
            {
                Assert.Equal(n, RingEscapeGeometry.Layout(d, inner, t, n, pos));
                AssertLayoutClear(pos, n, c);
            }
            Assert.Equal(1, RingEscapeGeometry.Layout(d, inner, t, 1, pos));
            Assert.Equal(Vec2.Zero, pos[0]); // a single ball starts at the centre
        }

        static void AssertLayoutClear(Vec2[] pos, int n, RingEscapeConfig c)
        {
            float r = c.BallSize * 0.5f;
            float free = c.InnerRadius - c.RingThickness * 0.5f - RingEscapeGeometry.Clearance;
            for (int i = 0; i < n; i++)
            {
                Assert.True(pos[i].Length + r <= free + 1e-4f, "ball " + i + " touches ring 0");
                for (int j = i + 1; j < n; j++)
                    Assert.True(Vec2.Distance(pos[i], pos[j]) >= c.BallSize + RingEscapeGeometry.Clearance - 1e-4f, "balls " + i + "," + j + " too close");
            }
        }

        [Fact]
        public void Rule6MotionAndRotationKeepTheBallFromStoppingForGood()
        {
            var issues = new List<ParamIssue>();
            ParamSet still = Mode.Constrain(With((CommonParams.Gravity, ParamValue.Float(0)), (RingEscapeParams.LaunchSpeed, ParamValue.Float(0))), DeviceTier.A, issues);
            Assert.Equal(0.2f, still.GetFloat(CommonParams.Gravity), 4);
            ParamSet zeroG = Mode.Constrain(With((CommonParams.Gravity, ParamValue.Float(0)), (CommonParams.Restitution, ParamValue.Float(0.5)),
                (RingEscapeParams.LaunchSpeed, ParamValue.Float(5))), DeviceTier.A, new List<ParamIssue>());
            Assert.Equal(0f, zeroG.GetFloat(CommonParams.Gravity));
            Assert.Equal(0.95f, zeroG.GetFloat(CommonParams.Restitution), 4);

            // Docs QA residual: rotationSpeed 0 never turns the gap under a resting ball → at least 10 °/s on every ring.
            ParamSet frozen = Mode.Constrain(With((RingEscapeParams.RotationSpeed, ParamValue.Float(0))), DeviceTier.A, new List<ParamIssue>());
            Assert.Equal(10f, frozen.GetFloat(RingEscapeParams.RotationSpeed));
            ParamSet slowOuter = Mode.Constrain(With((RingEscapeParams.RotationSpeed, ParamValue.Float(0)), (RingEscapeParams.SpeedStep, ParamValue.Int(-20))),
                DeviceTier.A, new List<ParamIssue>());
            Assert.Equal(50f, slowOuter.GetFloat(RingEscapeParams.RotationSpeed)); // multiplier floor 0.2 on ring 11
            ParamSet reverse = Mode.Constrain(With((RingEscapeParams.RotationSpeed, ParamValue.Float(0)), (RingEscapeParams.SpeedLaw, ParamValue.String(RingEscapeParams.LawReverse)),
                (RingEscapeParams.ReversePeriod, ParamValue.Float(15))), DeviceTier.A, new List<ParamIssue>());
            Assert.Equal(24f, reverse.GetFloat(RingEscapeParams.RotationSpeed)); // a full turn between reversals: 360° / 15 s
            // Slowest ring at the 0.2 floor with a 4 s period would need 450 °/s: the period grows to 5 s, then 360 °/s.
            ParamSet tight = Mode.Constrain(With((RingEscapeParams.SpeedStep, ParamValue.Int(-20)), (RingEscapeParams.SpeedLaw, ParamValue.String(RingEscapeParams.LawReverse)),
                (RingEscapeParams.ReversePeriod, ParamValue.Float(4))), DeviceTier.A, new List<ParamIssue>());
            Assert.Equal(5f, tight.GetFloat(RingEscapeParams.ReversePeriod));
            Assert.Equal(360f, tight.GetFloat(RingEscapeParams.RotationSpeed));
        }

        [Fact]
        public void CompressAlwaysDestroysPassedRingsAndForcedWinnerStaysAmongTheColours()
        {
            var issues = new List<ParamIssue>();
            ParamSet v = Mode.Constrain(With((RingEscapeParams.DestroyPassed, ParamValue.Bool(false)), (CommonParams.ForcedWinner, ParamValue.Int(9)),
                (CommonParams.BallCount, ParamValue.Int(3)), (RingEscapeParams.Competition, ParamValue.Bool(true))), DeviceTier.A, issues);
            Assert.True(v.GetBool(RingEscapeParams.DestroyPassed));
            Assert.Equal(0, v.GetInt(CommonParams.ForcedWinner));
            Assert.Equal(3, Mode.GetParticipantCount(v));
            Assert.Equal(0, Mode.GetParticipantCount(Defaults));
        }

        [Fact]
        public void DynamicRangesNarrowTheDependentSliders()
        {
            ParamSet d = Defaults;
            Assert.True(Mode.TryGetDynamicRange(RingEscapeParams.GapSize, d, DeviceTier.A, out double min, out double max));
            Assert.Equal(26, min);
            Assert.Equal(120, max);
            Assert.True(Mode.TryGetDynamicRange(RingEscapeParams.GapShrink, d, DeviceTier.A, out min, out max));
            Assert.Equal(28, max);
            Assert.True(Mode.TryGetDynamicRange(RingEscapeParams.RotationSpeed, d, DeviceTier.A, out min, out _));
            Assert.Equal(10, min);
            Assert.True(Mode.TryGetDynamicRange(CommonParams.BallCount, d, DeviceTier.B, out _, out max));
            Assert.Equal(12, max);
            Assert.True(Mode.TryGetDynamicRange(RingEscapeParams.RingSpacing, d, DeviceTier.A, out min, out _));
            Assert.Equal(0.55, min, 4);
            Assert.True(Mode.TryGetDynamicRange(CommonParams.BallSize, d, DeviceTier.A, out _, out max));
            Assert.Equal(0.43, max, 4); // rule 1 at the default spacing: 0.6 − 0.12 − 0.05
            Assert.False(Mode.TryGetDynamicRange(RingEscapeParams.BreakFx, d, DeviceTier.A, out _, out _));
            // Every value inside a dynamic range is valid.
            foreach (string id in new[] { RingEscapeParams.GapSize, RingEscapeParams.GapShrink, CommonParams.BallSize, CommonParams.BallCount, RingEscapeParams.RotationSpeed })
            {
                Mode.TryGetDynamicRange(id, d, DeviceTier.A, out min, out max);
                foreach (double x in new[] { min, max })
                {
                    ParamValue pv = Mode.Schema.Get(id).Type == ParamType.Int ? ParamValue.Int((long)x) : ParamValue.Float(x);
                    var issues = new List<ParamIssue>();
                    Mode.Constrain(d.With(id, Mode.Schema.SanitizeValue(id, pv, DeviceTier.A)), DeviceTier.A, issues);
                    Assert.True(issues.Count == 0, id + "=" + x + ": " + (issues.Count > 0 ? issues[0].ToString() : ""));
                }
            }
        }

        [Fact]
        public void LiveRotationEditIsClampedToTheDynamicRange()
        {
            var s = new ModeSession(Mode, Defaults, 1);
            Assert.Equal(10.0, s.SetLiveParam(RingEscapeParams.RotationSpeed, ParamValue.Float(0)).AsFloat);
            Assert.Equal(200.0, s.SetLiveParam(RingEscapeParams.RotationSpeed, ParamValue.Float(200)).AsFloat);
        }

        [Fact]
        public void PresetsAreValidAsAuthored()
        {
            ModeConformance.AssertPresetsValid(Mode);
            Assert.Equal(8, Mode.BuiltInPresets.Count);
        }

        [Fact]
        public void TenThousandRandomSetsSatisfyEveryRuleAfterValidation()
        {
            // §8.5 test (а): random sets after validation satisfy rules 1–7 and the progression limits; validation is idempotent.
            List<ParamSet> sets = ModeConformance.AssertRandomSetsPrepare(Mode, 10000, 20261018);
            foreach (ParamSet v in sets) AssertAllRules(v);
        }

        internal static void AssertAllRules(ParamSet v)
        {
            RingEscapeConfig c = RingEscapeConfig.From(v);
            Assert.True(c.RingCount <= RingEscapeGeometry.MaxRingCount(c.Progression, c.InnerRadius, c.RingSpacing), "progression limit " + v);
            Assert.True(RingEscapeGeometry.SpacingOk(c.RingSpacing, c.BallSize, c.RingThickness), "rule 1 " + v);
            Assert.True(RingEscapeGeometry.SolidArcOk(c.GapCount, c.GapSizeDeg), "rule 2 " + v);
            Assert.True(RingEscapeGeometry.PassabilityOk(c), "rule 3 " + v);
            Assert.True(RingEscapeGeometry.GrowLimit(c, 0) >= c.BallSize - 1e-4f, "rule 4 start " + v);
            Assert.True(RingEscapeGeometry.MotionOk(c.GravityG, c.LaunchSpeed, c.Restitution), "rule 6 " + v);
            Assert.True(c.RotationSpeedDeg + 1e-3f >= RingEscapeGeometry.MinRotationSpeedDeg(c), "rule 6b " + v);
            Assert.True(c.BallCount <= RingEscapeGeometry.StartCapacity(c.BallSize, c.InnerRadius, c.RingThickness), "rule 7 " + v);
            Assert.True(c.Progression != RingProgression.Compress || c.DestroyPassed, "compress destroys " + v);
        }
    }

    static class IntParamExtensions
    {
        public static int DefaultValue(this IntParam p) => (int)p.Default.AsInt;
    }
}
