using System;
using System.Collections.Generic;
using System.Globalization;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// <c>Int(min, max, step)</c> parameter (spec §7.6). Values lie on <c>min + k·step</c>; rounding to the grid is
    /// half-up. Optionally accepts the special value "unlimited" (spec §2.2 <c>targetDuration</c> "Без ограничения"),
    /// stored as <see cref="UnlimitedValue"/> and serialized as the string <see cref="UnlimitedToken"/>.
    /// Tier caps above max are allowed and have no effect (effective max = min(max, cap), spec §2.9 body budgets).
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
            if (Min > Max) throw Error("min " + I(Min) + " is greater than max " + I(Max));
            if (Step <= 0) throw Error("step must be > 0, got " + I(Step));
            if (Max >= UnlimitedValue) throw Error("max must be below " + I(UnlimitedValue) + " (reserved for 'unlimited')");
            if (Default.Kind != ParamValueKind.Int) throw Error("default must be an integer");
            long d = Default.AsInt;
            bool unlimitedDefault = AllowUnlimited && d == UnlimitedValue;
            if (!unlimitedDefault)
            {
                if (d < Min || d > Max) throw Error("default " + I(d) + " is outside [" + I(Min) + ", " + I(Max) + "]");
                if ((d - Min) % Step != 0) throw Error("default " + I(d) + " is not on the step grid");
            }
            if (TierCap != null)
            {
                foreach (DeviceTier tier in new[] { DeviceTier.A, DeviceTier.B, DeviceTier.C })
                {
                    double? cap = TierCap.For(tier);
                    if (!cap.HasValue) continue;
                    if (double.IsNaN(cap.Value) || cap.Value != Math.Floor(cap.Value) || cap.Value < Min)
                        throw Error("tier " + tier + " cap " + cap.Value.ToString(CultureInfo.InvariantCulture)
                            + " must be an integer >= min " + I(Min));
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
                case ParamValueKind.Seed when raw.AsSeed > long.MaxValue:
                    // JSON integers above long.MaxValue are read as seeds (see ParamJson): far above any max.
                    original = raw.AsSeed.ToString(CultureInfo.InvariantCulture);
                    below = false;
                    above = true;
                    aboveMax = true;
                    k = 0;
                    break;
                case ParamValueKind.Seed:
                    return Sanitize(ParamValue.Int((long)raw.AsSeed), tier, applyTierCap, issues);
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
                    return Fallback(ParamIssueCode.TypeMismatch, "expected an integer, got " + Describe(raw), tier, applyTierCap, issues);
            }

            if (below)
            {
                issues.Add(new ParamIssue(Id, ParamIssueCode.OutOfRange, original + " is below min " + I(Min)));
                k = 0;
            }
            else if (above)
            {
                issues.Add(aboveMax
                    ? new ParamIssue(Id, ParamIssueCode.OutOfRange, original + " is above max " + I(Max))
                    : new ParamIssue(Id, ParamIssueCode.TierCapped, original + " is above the tier " + tier + " cap " + I(effMax)));
                k = kMax;
            }
            else
            {
                if (k > kMax) k = kMax;
                issues.Add(new ParamIssue(Id, ParamIssueCode.Snapped,
                    original + " snapped to " + I(Min + k * Step)));
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

        /// <summary>
        /// Range, step, default and tier caps come from the mode; the unit is inherited when the mode gives none, and
        /// "unlimited" stays allowed when the common parameter allows it (a mode cannot drop a §2.2 range value such as
        /// targetDuration "Без ограничения" by overriding only the default).
        /// </summary>
        internal override ParamDef ApplyOverride(ParamDef modeDef, VisibleIf visibleIf)
        {
            var merged = (IntParam)base.ApplyOverride(modeDef, visibleIf);
            if (merged.Unit == null) merged.Unit = Unit;
            merged.AllowUnlimited = merged.AllowUnlimited || AllowUnlimited;
            return merged;
        }

        long EffectiveMaxRaw(DeviceTier tier)
        {
            double? cap = TierCap?.For(tier);
            return cap.HasValue && cap.Value < Max ? (long)cap.Value : Max;
        }

        /// <summary>Culture-invariant integer text for messages (no locale minus sign such as U+2212).</summary>
        static string I(long v)
        {
            return v.ToString(CultureInfo.InvariantCulture);
        }
    }
}
