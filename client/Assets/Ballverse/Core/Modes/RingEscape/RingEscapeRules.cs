using System;
using System.Collections.Generic;
using System.Globalization;
using Ballverse.Core.Params;

namespace Ballverse.Core.Modes.RingEscape
{
    /// <summary>
    /// Cross-parameter validation of M01 (spec §2.9 M01 "Валидация геометрии" rules 1–7 and the progression limits): one function
    /// for the panel, presets, Remote Config, project import and the generator. <see cref="Constrain"/> moves the dependent
    /// parameter to the nearest valid grid value, in this order (each step keeps the earlier ones):
    /// <list type="number">
    /// <item>progression ring limit («Отдаление камеры» ≤ 12 m, «Статично» ≤ 4 m) → lower <c>ringCount</c>;</item>
    /// <item>ball size must pass a gap of the widest allowed width at ring 0 and fit between rings of the widest spacing → lower <c>ballSize</c>;</item>
    /// <item>rule 1 → raise <c>ringSpacing</c> (then the progression limit again);</item>
    /// <item>rule 2 → lower <c>gapSize</c>;</item>
    /// <item>rule 3 at ring 0 → raise <c>gapSize</c>; rule 3 on the other rings → lower <c>gapShrink</c>;</item>
    /// <item>rule 7 (start layout) → lower <c>ballCount</c>;</item>
    /// <item>rule 6 (motion) → raise <c>restitution</c> to 0.95 when the launch is ≥ 2 m/s, otherwise raise <c>gravity</c> to 0.2 g;</item>
    /// <item>rule 6b (rotation) → lengthen <c>reversePeriod</c> when even 360 °/s cannot sweep a turn, then raise <c>rotationSpeed</c>;</item>
    /// <item>«Сжатие» → <c>destroyPassed</c> = on; <c>forcedWinner</c> beyond the teams → any.</item>
    /// </list>
    /// Rules 4 (growth) and 5 (speed envelope) are enforced while running (growth clipped to d_grow, ω clamped per tick).
    /// </summary>
    public static class RingEscapeRules
    {
        public static ParamSet Constrain(ParamSet v, DeviceTier tier, List<ParamIssue> issues)
        {
            if (v == null) throw new ArgumentNullException(nameof(v));
            if (issues == null) throw new ArgumentNullException(nameof(issues));
            ParamSchema schema = RingEscapeParams.Schema;
            RingEscapeConfig c = RingEscapeConfig.From(v);

            // Progression limit.
            int maxRings = RingEscapeGeometry.MaxRingCount(c.Progression, c.InnerRadius, c.RingSpacing);
            if (c.RingCount > maxRings) v = SetInt(v, RingEscapeParams.RingCount, maxRings, "progression limit", issues);

            // Ball size bound from rules 1-3 at their widest settings.
            float bound = MaxBallSizeGeometric(c);
            if (c.BallSize > bound + 1e-6f) v = SetFloatDown(schema, v, CommonParams.BallSize, bound, tier, "rules 1-3: ball cannot pass ring 0", issues);

            c = RingEscapeConfig.From(v);
            if (!RingEscapeGeometry.SpacingOk(c.RingSpacing, c.BallSize, c.RingThickness))
            {
                v = SetFloatUp(schema, v, RingEscapeParams.RingSpacing, c.BallSize + c.RingThickness + RingEscapeGeometry.Clearance, tier,
                    "rule 1: ring spacing >= ball + ring + 0.05 m", issues);
                c = RingEscapeConfig.From(v);
                maxRings = RingEscapeGeometry.MaxRingCount(c.Progression, c.InnerRadius, c.RingSpacing);
                if (c.RingCount > maxRings) v = SetInt(v, RingEscapeParams.RingCount, maxRings, "progression limit", issues);
                c = RingEscapeConfig.From(v);
            }

            if (!RingEscapeGeometry.SolidArcOk(c.GapCount, c.GapSizeDeg))
            {
                v = SetFloatDown(schema, v, RingEscapeParams.GapSize, RingEscapeGeometry.MaxGapForCount(c.GapCount), tier,
                    "rule 2: solid arc >= 20 deg between gaps", issues);
                c = RingEscapeConfig.From(v);
            }

            float gapMin0 = RingEscapeGeometry.GapMinDeg(c.InnerRadius, c.BallSize, c.RingThickness);
            if (c.GapSizeDeg + 1e-3f < gapMin0)
            {
                v = SetFloatUp(schema, v, RingEscapeParams.GapSize, gapMin0, tier, "rule 3: gap passable at ring 0", issues);
                c = RingEscapeConfig.From(v);
            }
            if (!RingEscapeGeometry.PassabilityOk(c))
            {
                int maxShrink = (int)MathF.Floor(RingEscapeGeometry.MaxGapShrink(c) * 100f + 1e-3f);
                v = SetInt(v, RingEscapeParams.GapShrink, Math.Max(0, Math.Min(c.GapShrinkPercent(), maxShrink)), "rule 3: every gap passable", issues);
                c = RingEscapeConfig.From(v);
                // Float rounding at the edge: step down until every ring passes (at most a few steps).
                while (!RingEscapeGeometry.PassabilityOk(c) && c.GapShrinkPercent() > 0)
                {
                    v = SetInt(v, RingEscapeParams.GapShrink, c.GapShrinkPercent() - 1, "rule 3: every gap passable", issues);
                    c = RingEscapeConfig.From(v);
                }
            }

            int capacity = RingEscapeGeometry.StartCapacity(c.BallSize, c.InnerRadius, c.RingThickness);
            if (c.BallCount > capacity) v = SetInt(v, CommonParams.BallCount, Math.Max(1, capacity), "rule 7: balls fit inside ring 0", issues);

            c = RingEscapeConfig.From(v);
            if (!RingEscapeGeometry.MotionOk(c.GravityG, c.LaunchSpeed, c.Restitution))
            {
                if (c.LaunchSpeed + 1e-4f >= RingEscapeGeometry.MotionMinLaunch)
                    v = SetFloatUp(schema, v, CommonParams.Restitution, RingEscapeGeometry.MotionMinRestitution, tier, "rule 6: ball never stops", issues);
                else
                    v = SetFloatUp(schema, v, CommonParams.Gravity, RingEscapeGeometry.MotionMinGravity, tier, "rule 6: ball never stops", issues);
                c = RingEscapeConfig.From(v);
            }

            float minRot = RingEscapeGeometry.MinRotationSpeedDeg(c);
            if (minRot > RingEscapeGeometry.MaxOmegaDeg + 1e-3f && c.SpeedLaw == RingSpeedLaw.Reverse)
            {
                // Even 360 °/s cannot sweep a full turn between reversals on the slowest ring: lengthen the period.
                float m = RingEscapeGeometry.MinSpeedMultiplierOf(c.SpeedStep, c.RingCount);
                v = SetFloatUp(schema, v, RingEscapeParams.ReversePeriod, 360f / (m * RingEscapeGeometry.MaxOmegaDeg), tier,
                    "rule 6b: a full turn between reversals", issues);
                c = RingEscapeConfig.From(v);
                minRot = RingEscapeGeometry.MinRotationSpeedDeg(c);
            }
            if (c.RotationSpeedDeg + 1e-3f < minRot)
            {
                v = SetFloatUp(schema, v, RingEscapeParams.RotationSpeed, minRot, tier, "rule 6b: every ring keeps turning", issues);
                c = RingEscapeConfig.From(v);
            }

            if (c.Progression == RingProgression.Compress && !c.DestroyPassed)
            {
                v = v.With(RingEscapeParams.DestroyPassed, ParamValue.Bool(true));
                issues.Add(new ParamIssue(RingEscapeParams.DestroyPassed, ParamIssueCode.OutOfRange, "progression compress: passed rings are always destroyed"));
            }
            int teams = c.TeamCount;
            if (c.ForcedWinner > teams) v = SetInt(v, CommonParams.ForcedWinner, 0, "forced winner beyond the " + teams + " colours", issues);
            return v;
        }

        /// <summary>
        /// Slider ranges that keep the rules for the other values fixed (spec §2.9 "зависимый ползунок сужает диапазон"):
        /// ringCount, ballCount, ballSize, ringSpacing, gapSize, gapShrink, rotationSpeed, gravity.
        /// </summary>
        public static bool TryGetDynamicRange(string id, ParamSet values, DeviceTier tier, out double min, out double max)
        {
            min = 0;
            max = 0;
            if (id == null || values == null) return false;
            ParamSchema schema = RingEscapeParams.Schema;
            if (!schema.TryGet(id, out ParamDef def)) return false;
            RingEscapeConfig c = RingEscapeConfig.From(values);
            switch (id)
            {
                case RingEscapeParams.RingCount:
                    return IntRange(def, tier, 1, RingEscapeGeometry.MaxRingCount(c.Progression, c.InnerRadius, c.RingSpacing), out min, out max);
                case CommonParams.BallCount:
                    return IntRange(def, tier, 1, RingEscapeGeometry.StartCapacity(c.BallSize, c.InnerRadius, c.RingThickness), out min, out max);
                case CommonParams.BallSize:
                {
                    float hi = c.RingSpacing - c.RingThickness - RingEscapeGeometry.Clearance;
                    for (int i = 0; i < c.RingCount; i++)
                    {
                        float w = RingEscapeGeometry.FreeWidth(RingEscapeGeometry.PassRadius(c, i), RingEscapeGeometry.GapDeg(c, i), c.RingThickness)
                            - RingEscapeGeometry.Clearance;
                        if (w < hi) hi = w;
                    }
                    hi = Math.Min(hi, MaxDiameterForCount(c));
                    return FloatRange((FloatParam)def, tier, double.NegativeInfinity, hi, out min, out max);
                }
                case RingEscapeParams.RingSpacing:
                    return FloatRange((FloatParam)def, tier, c.BallSize + c.RingThickness + RingEscapeGeometry.Clearance, double.PositiveInfinity, out min, out max);
                case RingEscapeParams.GapSize:
                {
                    float lo = RingEscapeGeometry.GapMinDeg(c.InnerRadius, c.BallSize, c.RingThickness);
                    if (c.RingCount > 1 && c.GapShrink > 0f)
                    {
                        // The narrowest ring must stay passable: gapSize ≥ gapMin(ř_i) / (1 − shrink · i / (n − 1)).
                        for (int i = 1; i < c.RingCount; i++)
                        {
                            float f = 1f - c.GapShrink * i / (c.RingCount - 1);
                            float need = RingEscapeGeometry.GapMinDeg(RingEscapeGeometry.PassRadius(c, i), c.BallSize, c.RingThickness) / f;
                            if (need > lo) lo = need;
                        }
                    }
                    return FloatRange((FloatParam)def, tier, lo, RingEscapeGeometry.MaxGapForCount(c.GapCount), out min, out max);
                }
                case RingEscapeParams.GapShrink:
                    return IntRange(def, tier, 0, (int)MathF.Floor(RingEscapeGeometry.MaxGapShrink(c) * 100f + 1e-3f), out min, out max);
                case RingEscapeParams.RotationSpeed:
                    return FloatRange((FloatParam)def, tier, RingEscapeGeometry.MinRotationSpeedDeg(c), double.PositiveInfinity, out min, out max);
                case CommonParams.Gravity:
                    if (c.LaunchSpeed + 1e-4f >= RingEscapeGeometry.MotionMinLaunch && c.Restitution + 1e-4f >= RingEscapeGeometry.MotionMinRestitution)
                        return false;
                    return FloatRange((FloatParam)def, tier, RingEscapeGeometry.MotionMinGravity, double.PositiveInfinity, out min, out max);
                default:
                    return false;
            }
        }

        /// <summary>Largest ball diameter rules 1–3 allow at all (widest spacing, widest gap at ring 0).</summary>
        static float MaxBallSizeGeometric(RingEscapeConfig c)
        {
            float bySpacing = 1.2f - c.RingThickness - RingEscapeGeometry.Clearance;
            float gapMax = Math.Min(120f, RingEscapeGeometry.MaxGapForCount(c.GapCount));
            float byGap = RingEscapeGeometry.FreeWidth(c.InnerRadius, gapMax, c.RingThickness) - RingEscapeGeometry.Clearance;
            return Math.Min(bySpacing, byGap);
        }

        /// <summary>Largest diameter whose start layout still holds ballCount balls (rule 7), by bisection on the layout.</summary>
        static float MaxDiameterForCount(RingEscapeConfig c)
        {
            float lo = 0.1f, hi = 1.5f;
            if (RingEscapeGeometry.StartCapacity(hi, c.InnerRadius, c.RingThickness) >= c.BallCount) return hi;
            if (RingEscapeGeometry.StartCapacity(lo, c.InnerRadius, c.RingThickness) < c.BallCount) return lo;
            for (int i = 0; i < 24; i++)
            {
                float mid = 0.5f * (lo + hi);
                if (RingEscapeGeometry.StartCapacity(mid, c.InnerRadius, c.RingThickness) >= c.BallCount) lo = mid;
                else hi = mid;
            }
            return lo;
        }

        static int GapShrinkPercent(this RingEscapeConfig c)
        {
            return (int)MathF.Round(c.GapShrink * 100f);
        }

        // ------------------------------------------------------------------ grid helpers

        static ParamSet SetInt(ParamSet v, string id, int value, string rule, List<ParamIssue> issues)
        {
            ParamValue old = v.Get(id);
            if (old.AsInt == value) return v;
            issues.Add(new ParamIssue(id, ParamIssueCode.OutOfRange, rule + ": " + old.AsInt.ToString(CultureInfo.InvariantCulture)
                + " -> " + value.ToString(CultureInfo.InvariantCulture)));
            return v.With(id, ParamValue.Int(value));
        }

        /// <summary>Lowest grid value ≥ <paramref name="target"/> (capped at the effective max).</summary>
        static ParamSet SetFloatUp(ParamSchema schema, ParamSet v, string id, double target, DeviceTier tier, string rule, List<ParamIssue> issues)
        {
            var def = (FloatParam)schema.Get(id);
            double k = Math.Ceiling((target - def.Min) / def.Step - 1e-6);
            double x = Math.Min(def.Min + Math.Max(0, k) * def.Step, def.EffectiveMax(tier));
            return SetFloat(def, v, id, x, tier, rule, issues);
        }

        /// <summary>Highest grid value ≤ <paramref name="target"/> (not below min).</summary>
        static ParamSet SetFloatDown(ParamSchema schema, ParamSet v, string id, double target, DeviceTier tier, string rule, List<ParamIssue> issues)
        {
            var def = (FloatParam)schema.Get(id);
            double k = Math.Floor((target - def.Min) / def.Step + 1e-6);
            double x = def.Min + Math.Max(0, k) * def.Step;
            return SetFloat(def, v, id, x, tier, rule, issues);
        }

        static ParamSet SetFloat(FloatParam def, ParamSet v, string id, double x, DeviceTier tier, string rule, List<ParamIssue> issues)
        {
            float value = def.Snap(x, tier);
            float old = v.GetFloat(id);
            if (old == value) return v;
            issues.Add(new ParamIssue(id, ParamIssueCode.OutOfRange, rule + ": " + old.ToString("R", CultureInfo.InvariantCulture)
                + " -> " + value.ToString("R", CultureInfo.InvariantCulture)));
            return v.With(id, ParamValue.Float(value));
        }

        static bool IntRange(ParamDef def, DeviceTier tier, int lo, int hi, out double min, out double max)
        {
            var p = (IntParam)def;
            min = Math.Max(p.Min, lo);
            max = Math.Min(p.EffectiveMax(tier), hi);
            if (max < min) max = min;
            return true;
        }

        static bool FloatRange(FloatParam def, DeviceTier tier, double lo, double hi, out double min, out double max)
        {
            double effMax = def.EffectiveMax(tier);
            double a = double.IsNegativeInfinity(lo) ? def.Min : def.Min + Math.Max(0, Math.Ceiling((lo - def.Min) / def.Step - 1e-6)) * def.Step;
            double b = double.IsPositiveInfinity(hi) ? effMax : def.Min + Math.Floor((Math.Min(hi, effMax) - def.Min) / def.Step + 1e-6) * def.Step;
            a = Math.Max(def.Min, Math.Min(a, effMax));
            b = Math.Max(def.Min, Math.Min(b, effMax));
            if (b < a) b = a;
            min = def.Snap(a, tier);
            max = def.Snap(b, tier);
            return true;
        }
    }
}
