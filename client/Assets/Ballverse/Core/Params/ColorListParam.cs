using System;
using System.Collections.Generic;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// <c>ColorList(minCount, maxCount)</c> parameter (spec §7.6), e.g. <c>ballColors</c> (spec §2.2: a palette or one
    /// colour per ball / team). Sanitizing drops unparseable entries, cuts the list to <see cref="MaxCount"/> and pads
    /// a short list up to <see cref="MinCount"/> with the default list's entries at the same positions.
    /// </summary>
    public sealed class ColorListParam : ParamDef
    {
        public int MinCount { get; private set; }
        public int MaxCount { get; private set; }

        public override ParamType Type => ParamType.ColorList;

        public ColorListParam(string id, int minCount, int maxCount, IEnumerable<ColorRgba> defaultValue)
            : base(id, ParamValue.Colors(defaultValue ?? throw new ArgumentNullException(nameof(defaultValue))))
        {
            MinCount = minCount;
            MaxCount = maxCount;
        }

        public IReadOnlyList<ColorRgba> DefaultValue => Default.AsColors;

        internal override void Validate()
        {
            ValidateCommon();
            if (MinCount < 0) throw Error("minCount must be >= 0");
            if (MaxCount < 1 || MaxCount < MinCount) throw Error("maxCount must be >= max(1, minCount)");
            if (Default.Kind != ParamValueKind.ColorList) throw Error("default must be a colour list");
            int n = Default.AsColors.Count;
            if (n < MinCount || n > MaxCount)
                throw Error("default has " + n + " colours, expected " + MinCount + ".." + MaxCount);
        }

        internal override ParamValue Sanitize(ParamValue raw, DeviceTier tier, bool applyTierCap, List<ParamIssue> issues)
        {
            List<ColorRgba> colors;
            bool changed = false;
            if (raw.Kind == ParamValueKind.ColorList)
            {
                colors = new List<ColorRgba>(raw.AsColors);
            }
            else if (raw.Kind == ParamValueKind.StringList)
            {
                colors = new List<ColorRgba>();
                var items = raw.AsStrings;
                for (int i = 0; i < items.Count; i++)
                {
                    if (ColorRgba.TryParse(items[i], out ColorRgba c))
                    {
                        colors.Add(c);
                    }
                    else
                    {
                        issues.Add(new ParamIssue(Id, ParamIssueCode.InvalidValue,
                            "invalid colour " + TextRules.Quote(items[i]) + " at index " + i + " dropped"));
                    }
                }
                changed = true;
            }
            else
            {
                return Fallback(ParamIssueCode.TypeMismatch, "expected a colour list, got " + raw.Kind, tier, applyTierCap, issues);
            }

            if (colors.Count > MaxCount)
            {
                issues.Add(new ParamIssue(Id, ParamIssueCode.ListTruncated, colors.Count + " colours cut to " + MaxCount));
                colors.RemoveRange(MaxCount, colors.Count - MaxCount);
                changed = true;
            }
            if (colors.Count < MinCount)
            {
                issues.Add(new ParamIssue(Id, ParamIssueCode.ListPadded, colors.Count + " colours padded to " + MinCount));
                var defaults = Default.AsColors;
                for (int i = colors.Count; i < MinCount; i++) colors.Add(defaults[i]);
                changed = true;
            }
            return changed ? ParamValue.Colors(colors) : raw;
        }
    }
}
