using System.Collections.Generic;

namespace Ballverse.Core.Params
{
    /// <summary><c>Color</c> parameter (spec §7.6); raw strings are parsed as <c>#RRGGBB</c> / <c>#RRGGBBAA</c>.</summary>
    public sealed class ColorParam : ParamDef
    {
        public override ParamType Type => ParamType.Color;

        public ColorParam(string id, ColorRgba defaultValue)
            : base(id, ParamValue.Color(defaultValue))
        {
        }

        public ColorRgba DefaultValue => Default.AsColor;

        internal override void Validate()
        {
            ValidateCommon();
            if (Default.Kind != ParamValueKind.Color) throw Error("default must be a colour");
        }

        internal override ParamValue Sanitize(ParamValue raw, DeviceTier tier, bool applyTierCap, List<ParamIssue> issues)
        {
            if (raw.Kind == ParamValueKind.Color) return raw;
            if (raw.Kind == ParamValueKind.String)
            {
                if (ColorRgba.TryParse(raw.AsString, out ColorRgba c)) return ParamValue.Color(c);
                return Fallback(ParamIssueCode.InvalidValue, "invalid colour " + TextRules.Quote(raw.AsString), tier, applyTierCap, issues);
            }
            return Fallback(ParamIssueCode.TypeMismatch, "expected a colour, got " + Describe(raw), tier, applyTierCap, issues);
        }
    }
}
