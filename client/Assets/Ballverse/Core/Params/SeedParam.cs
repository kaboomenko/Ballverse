using System.Collections.Generic;
using System.Globalization;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// <c>Seed</c> parameter (spec §7.6, §2.2 <c>seed</c>): an unsigned 64-bit value serialized as a decimal JSON string
    /// so it survives JSON readers that use doubles. Raw decimal strings and non-negative integers are accepted.
    /// The schema default is a fixed number (Core is pure); the app supplies a fresh random seed itself.
    /// </summary>
    public sealed class SeedParam : ParamDef
    {
        public override ParamType Type => ParamType.Seed;

        public SeedParam(string id, ulong defaultValue = 0UL)
            : base(id, ParamValue.Seed(defaultValue))
        {
        }

        public ulong DefaultValue => Default.AsSeed;

        /// <summary>Parses a decimal seed (digits only, invariant culture, 0..2^64-1).</summary>
        public static bool TryParse(string text, out ulong seed)
        {
            return ulong.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out seed);
        }

        internal override void Validate()
        {
            ValidateCommon();
            if (Default.Kind != ParamValueKind.Seed) throw Error("default must be a seed");
        }

        internal override ParamValue Sanitize(ParamValue raw, DeviceTier tier, bool applyTierCap, List<ParamIssue> issues)
        {
            switch (raw.Kind)
            {
                case ParamValueKind.Seed:
                    return raw;
                case ParamValueKind.Int:
                    if (raw.AsInt >= 0) return ParamValue.Seed((ulong)raw.AsInt);
                    return Fallback(ParamIssueCode.InvalidValue, "negative seed", tier, applyTierCap, issues);
                case ParamValueKind.String:
                    if (TryParse(raw.AsString, out ulong seed)) return ParamValue.Seed(seed);
                    return Fallback(ParamIssueCode.InvalidValue, "invalid seed " + TextRules.Quote(raw.AsString), tier, applyTierCap, issues);
                default:
                    return Fallback(ParamIssueCode.TypeMismatch, "expected a seed, got " + raw.Kind, tier, applyTierCap, issues);
            }
        }
    }
}
