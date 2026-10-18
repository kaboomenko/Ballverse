using System.Collections.Generic;

namespace Ballverse.Core.Params
{
    /// <summary><c>Bool</c> parameter (spec §7.6).</summary>
    public sealed class BoolParam : ParamDef
    {
        public override ParamType Type => ParamType.Bool;

        public BoolParam(string id, bool defaultValue)
            : base(id, ParamValue.Bool(defaultValue))
        {
        }

        public bool DefaultValue => Default.AsBool;

        internal override void Validate()
        {
            ValidateCommon();
            if (Default.Kind != ParamValueKind.Bool) throw Error("default must be a bool");
        }

        internal override ParamValue Sanitize(ParamValue raw, DeviceTier tier, bool applyTierCap, List<ParamIssue> issues)
        {
            if (raw.Kind == ParamValueKind.Bool) return raw;
            return Fallback(ParamIssueCode.TypeMismatch, "expected a bool, got " + Describe(raw), tier, applyTierCap, issues);
        }
    }
}
