using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// <c>Float(min, max, step, unit)</c> parameter (spec §7.6). Values are <see cref="float"/>s that lie exactly on the
    /// grid <c>(float)(min + k·step)</c>, computed in double and then cast, so sanitizing is idempotent and free of drift.
    /// Rounding to the grid is half-up (ties go to the larger k).
    /// </summary>
    public sealed class FloatParam : ParamDef
    {
        /// <summary>Tolerance, in steps, for treating a value as on the range boundary instead of outside it.</summary>
        const double GridTolerance = 0.01;
        /// <summary>Smallest allowed step relative to max(|min|, |max|): keeps float rounding below 1% of a step.</summary>
        const double MinRelativeStep = 1e-5;

        public double Min { get; private set; }
        public double Max { get; private set; }
        public double Step { get; private set; }
        /// <summary>Display unit, e.g. "m", "x", "°" (may be null).</summary>
        public string Unit { get; private set; }
        /// <summary>Per-tier maximum, or null (spec §7.6 <c>tierCap</c>).</summary>
        public TierCap TierCap { get; private set; }

        public override ParamType Type => ParamType.Float;

        public FloatParam(string id, double min, double max, double step, double defaultValue, string unit = null)
            : base(id, ParamValue.Float(defaultValue))
        {
            Min = min;
            Max = max;
            Step = step;
            Unit = unit;
        }

        public float DefaultValue => (float)Default.AsFloat;

        public FloatParam WithTierCap(TierCap cap)
        {
            var c = (FloatParam)Clone();
            c.TierCap = cap;
            return c;
        }

        public FloatParam WithRange(double min, double max, double step, double defaultValue)
        {
            var c = (FloatParam)Clone();
            c.Min = min;
            c.Max = max;
            c.Step = step;
            c.Default = ParamValue.Float(defaultValue);
            return c;
        }

        /// <summary>Upper bound on <paramref name="tier"/>: min(max, tierCap).</summary>
        public double EffectiveMax(DeviceTier tier)
        {
            double? cap = TierCap?.For(tier);
            return cap.HasValue ? Math.Min(Max, cap.Value) : Max;
        }

        /// <summary>The grid value nearest to <paramref name="value"/> within [min, effective max]. Value must be finite.</summary>
        public float Snap(double value, DeviceTier tier)
        {
            return SnapCore(value, EffectiveMax(tier));
        }

        internal override void Validate()
        {
            ValidateCommon();
            if (!IsFinite(Min) || !IsFinite(Max)) throw Error("min and max must be finite");
            if (Min > Max) throw Error("min " + F(Min) + " is greater than max " + F(Max));
            if (!IsFinite(Step) || Step <= 0) throw Error("step must be a finite number > 0, got " + F(Step));
            if (Step < Math.Max(Math.Abs(Min), Math.Abs(Max)) * MinRelativeStep)
                throw Error("step " + F(Step) + " is too small for the range (float precision)");
            if (Math.Abs(Min) > float.MaxValue || Math.Abs(Max) > float.MaxValue) throw Error("range exceeds float");
            if (Default.Kind != ParamValueKind.Float && Default.Kind != ParamValueKind.Int) throw Error("default must be a number");
            double d = Default.AsFloat;
            if (!IsFinite(d) || d < Min - GridTolerance * Step || d > Max + GridTolerance * Step)
                throw Error("default " + F(d) + " is outside [" + F(Min) + ", " + F(Max) + "]");
            double t = (d - Min) / Step;
            if (Math.Abs(t - Math.Round(t)) > GridTolerance) throw Error("default " + F(d) + " is not on the step grid");
            if (TierCap != null)
            {
                foreach (DeviceTier tier in new[] { DeviceTier.A, DeviceTier.B, DeviceTier.C })
                {
                    double? cap = TierCap.For(tier);
                    if (cap.HasValue && (!IsFinite(cap.Value) || cap.Value < Min || cap.Value > Max))
                        throw Error("tier " + tier + " cap " + F(cap.Value) + " is outside [" + F(Min) + ", " + F(Max) + "]");
                }
            }
            Default = ParamValue.Float(SnapCore(d, Max));
        }

        internal override ParamValue Sanitize(ParamValue raw, DeviceTier tier, bool applyTierCap, List<ParamIssue> issues)
        {
            double x;
            if (raw.Kind == ParamValueKind.Float)
            {
                x = raw.AsFloat;
                if (!IsFinite(x)) return Fallback(ParamIssueCode.NonFinite, "value is not finite", tier, applyTierCap, issues);
            }
            else if (raw.Kind == ParamValueKind.Int)
            {
                x = raw.AsInt;
            }
            else
            {
                return Fallback(ParamIssueCode.TypeMismatch, "expected a number, got " + raw.Kind, tier, applyTierCap, issues);
            }

            double effMax = applyTierCap ? EffectiveMax(tier) : Max;
            double t = (x - Min) / Step;
            bool clamped = false;
            if (t < -GridTolerance)
            {
                issues.Add(new ParamIssue(Id, ParamIssueCode.OutOfRange, F(x) + " is below min " + F(Min)));
                clamped = true;
            }
            else if (t > (effMax - Min) / Step + GridTolerance)
            {
                bool aboveMax = t > (Max - Min) / Step + GridTolerance;
                issues.Add(aboveMax
                    ? new ParamIssue(Id, ParamIssueCode.OutOfRange, F(x) + " is above max " + F(Max))
                    : new ParamIssue(Id, ParamIssueCode.TierCapped, F(x) + " is above the tier " + tier + " cap " + F(effMax)));
                clamped = true;
            }

            float result = SnapCore(x, effMax);
            // Plain double-to-float rounding of an on-grid value (1.35 -> 1.35f) is not reported.
            if (!clamped && BitConverter.SingleToInt32Bits(result) != BitConverter.SingleToInt32Bits((float)x))
            {
                issues.Add(new ParamIssue(Id, ParamIssueCode.Snapped, F(x) + " snapped to " + F(result)));
            }
            return ParamValue.Float(result);
        }

        internal override ParamValue DefaultFor(DeviceTier tier, bool applyTierCap)
        {
            if (!applyTierCap || TierCap == null) return Default;
            return ParamValue.Float(SnapCore(Default.AsFloat, EffectiveMax(tier)));
        }

        internal override ParamDef ApplyOverride(ParamDef modeDef)
        {
            var merged = (FloatParam)base.ApplyOverride(modeDef);
            if (merged.Unit == null) merged.Unit = Unit;
            return merged;
        }

        float SnapCore(double x, double effMax)
        {
            long kMax = (long)Math.Floor((effMax - Min) / Step + 1e-9);
            if (kMax < 0) kMax = 0;
            double t = (x - Min) / Step;
            long k;
            if (t <= 0) k = 0;
            else if (t >= kMax) k = kMax;
            else k = (long)Math.Floor(t + 0.5);
            if (k > kMax) k = kMax;
            float r = (float)(Min + k * Step);
            return r == 0f ? 0f : r;
        }

        static bool IsFinite(double d)
        {
            return !double.IsNaN(d) && !double.IsInfinity(d);
        }

        static string F(double d)
        {
            return d.ToString("R", CultureInfo.InvariantCulture);
        }
    }
}
