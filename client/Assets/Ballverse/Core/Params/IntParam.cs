using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// <c>Int(min, max, step)</c> parameter (spec §7.6). Values lie on <c>min + k·step</c>; rounding to the grid is
    /// half-up. Optionally accepts the special value "unlimited" (spec §2.2 <c>targetDuration</c> "Без ограничения"),
    /// stored as <see cref="UnlimitedValue"/> and serialized as the string <see cref="UnlimitedToken"/>.
    /// </summary>
    public sealed class IntParam : ParamDef
    {
        /// <summary>Stored value meaning "unlimited"; never a regular value (max must be below it).</summary>
        public const int UnlimitedValue = int.MaxValue;
        /// <summary>JSON form of <see cref="UnlimitedValue"/>.</summary>
        public const string UnlimitedToken = "unlimited";

        public int Min { get; private set; }
        public int Max { get; private set; }
        public int Step { get; private set; }
        public string Unit { get; private set; }
        public TierCap TierCap { get; private set; }
        /// <summary>Whether "unlimited" is a valid value.</summary>
        public bool AllowUnlimited { get; private set; }

        public override ParamType Type => ParamType.Int;

        public IntParam(string id, int min, int max, int step, int defaultValue, string unit = null)
            : base(id, ParamValue.Int(defaultValue))
        {
            Min = min;
            Max = max;
            Step = step;
            Unit = unit;
        }

        public IntParam WithTierCap(TierCap cap)
        {
            var c = (IntParam)Clone();
            c.TierCap = cap;
            return c;
        }

        /// <summary>Copy that accepts "unlimited"; with <paramref name="isDefault"/> it is also the default.</summary>
        public IntParam WithUnlimited(bool isDefault = false)
        {
            var c = (IntParam)Clone();
            c.AllowUnlimited = true;
            if (isDefault) c.Default = ParamValue.Unlimited;
            return c;
        }

        public IntParam WithRange(int min, int max, int step, int defaultValue)
        {
            var c = (IntParam)Clone();
            c.Min = min;
            c.Max = max;
            c.Step = step;
            c.Default = ParamValue.Int(defaultValue);
            return c;
        }

        /// <summary>Largest valid value on <paramref name="tier"/>: the last grid point not above min(max, tierCap).</summary>
        public int EffectiveMax(DeviceTier tier)
        {
            long effMax = Max;
            double? cap = TierCap?.For(tier);
            if (cap.HasValue && cap.Value < effMax) effMax = (long)cap.Value;
            long kMax = (effMax - Min) / Step;
            return (int)(Min + kMax * Step);
        }

        internal override void Validate()
        {
            ValidateCommon();
            if (Min > Max) throw Error("min " + Min + " is greater than max " + Max);
            if (Step <= 0) throw Error("step must be > 0, got " + Step);
            if (Max >= UnlimitedValue) throw Error("max must be below " + UnlimitedValue + " (reserved for 'unlimited')");
            if (Default.Kind != ParamValueKind.Int) throw Error("default must be an integer");
            long d = Default.AsInt;
            bool unlimitedDefault = AllowUnlimited && d == UnlimitedValue;
            if (!unlimitedDefault)
            {
                if (d < Min || d > Max) throw Error("default " + d + " is outside [" + Min + ", " + Max + "]");
                if ((d - Min) % Step != 0) throw Error("default " + d + " is not on the step grid");
            }
            if (TierCap != null)
            {
                foreach (DeviceTier tier in new[] { DeviceTier.A, DeviceTier.B, DeviceTier.C })
                {
                    double? cap = TierCap.For(tier);
                    if (!cap.HasValue) continue;
                    if (cap.Value != Math.Floor(cap.Value) || cap.Value < Min || cap.Value > Max)
                        throw Error("tier " + tier + " cap " + cap.Value.ToString(CultureInfo.InvariantCulture)
                            + " must be an integer in [" + Min + ", " + Max + "]");
                }
            }
        }

        internal override ParamValue Sanitize(ParamValue raw, DeviceTier tier, bool applyTierCap, List<ParamIssue> issues)
        {
            long effMax = applyTierCap ? EffectiveMaxRaw(tier) : Max;
            long kMax = (effMax - Min) / Step;
            bool below, above, aboveMax;
            long k;
            string original;

            switch (raw.Kind)
            {
                case ParamValueKind.String:
                    if (AllowUnlimited && raw.AsString == UnlimitedToken) return ParamValue.Unlimited;
                    return Fallback(ParamIssueCode.TypeMismatch, "expected an integer, got a string", tier, applyTierCap, issues);
                case ParamValueKind.Int:
                {
                    long v = raw.AsInt;
                    if (AllowUnlimited && v == UnlimitedValue) return ParamValue.Unlimited;
                    original = v.ToString(CultureInfo.InvariantCulture);
                    below = v < Min;
                    above = v > effMax;
                    aboveMax = v > Max;
                    k = below || above ? 0 : (2 * (v - Min) + Step) / (2L * Step);
                    if (!below && !above && k <= kMax && Min + k * Step == v) return raw;
                    break;
                }
                case ParamValueKind.Float:
                {
                    double x = raw.AsFloat;
                    if (double.IsNaN(x) || double.IsInfinity(x))
                        return Fallback(ParamIssueCode.NonFinite, "value is not finite", tier, applyTierCap, issues);
                    original = x.ToString("R", CultureInfo.InvariantCulture);
                    below = x < Min;
                    above = x > effMax;
                    aboveMax = x > Max;
                    k = below || above ? 0 : (long)Math.Floor((x - Min) / Step + 0.5);
                    if (!below && !above && k <= kMax && Min + k * Step == x) return ParamValue.Int(Min + k * Step);
                    break;
                }
                default:
                    return Fallback(ParamIssueCode.TypeMismatch, "expected an integer, got " + raw.Kind, tier, applyTierCap, issues);
            }

            if (below)
            {
                issues.Add(new ParamIssue(Id, ParamIssueCode.OutOfRange, original + " is below min " + Min));
                k = 0;
            }
            else if (above)
            {
                issues.Add(aboveMax
                    ? new ParamIssue(Id, ParamIssueCode.OutOfRange, original + " is above max " + Max)
                    : new ParamIssue(Id, ParamIssueCode.TierCapped, original + " is above the tier " + tier + " cap " + effMax));
                k = kMax;
            }
            else
            {
                if (k > kMax) k = kMax;
                issues.Add(new ParamIssue(Id, ParamIssueCode.Snapped,
                    original + " snapped to " + (Min + k * Step).ToString(CultureInfo.InvariantCulture)));
            }
            return ParamValue.Int(Min + k * Step);
        }

        internal override ParamValue DefaultFor(DeviceTier tier, bool applyTierCap)
        {
            if (!applyTierCap || TierCap == null || Default.IsUnlimited) return Default;
            long effMax = EffectiveMaxRaw(tier);
            long kMax = (effMax - Min) / Step;
            long d = Default.AsInt;
            return d <= Min + kMax * Step ? Default : ParamValue.Int(Min + kMax * Step);
        }

        internal override ParamDef ApplyOverride(ParamDef modeDef)
        {
            var merged = (IntParam)base.ApplyOverride(modeDef);
            if (merged.Unit == null) merged.Unit = Unit;
            return merged;
        }

        long EffectiveMaxRaw(DeviceTier tier)
        {
            double? cap = TierCap?.For(tier);
            return cap.HasValue && cap.Value < Max ? (long)cap.Value : Max;
        }
    }
}
