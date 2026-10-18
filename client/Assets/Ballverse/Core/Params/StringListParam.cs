using System;
using System.Collections.Generic;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// <c>StringList(maxItems, maxLen)</c> parameter (spec §7.6), e.g. <c>labelNames</c>: up to 32 names of 16 characters
    /// (spec §2.2). Lengths count Unicode code points; sanitizing cuts the list to <see cref="MaxItems"/> and each item
    /// to <see cref="MaxLength"/> without splitting surrogate pairs, and replaces unpaired surrogates with U+FFFD (they
    /// would not survive UTF-8). Items are otherwise kept as given (empty allowed).
    /// </summary>
    public sealed class StringListParam : ParamDef
    {
        static readonly char[] s_pasteSeparators = { ',', '\n', '\r' };

        public int MaxItems { get; private set; }
        public int MaxLength { get; private set; }

        public override ParamType Type => ParamType.StringList;

        public StringListParam(string id, int maxItems, int maxLength, IEnumerable<string> defaultValue)
            : base(id, ParamValue.Strings(defaultValue ?? throw new ArgumentNullException(nameof(defaultValue))))
        {
            MaxItems = maxItems;
            MaxLength = maxLength;
        }

        public IReadOnlyList<string> DefaultValue => Default.AsStrings;

        /// <summary>
        /// Splits pasted text into items "через запятую или построчно" (spec §2.9 M07): separators are commas and line
        /// breaks, items are trimmed, empty items dropped. The result is not truncated (sanitizing does that).
        /// </summary>
        public static IReadOnlyList<string> SplitPastedText(string text)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(text)) return result;
            foreach (string part in text.Split(s_pasteSeparators))
            {
                string t = part.Trim();
                if (t.Length > 0) result.Add(t);
            }
            return result;
        }

        internal override void Validate()
        {
            ValidateCommon();
            if (MaxItems < 1) throw Error("maxItems must be >= 1");
            if (MaxLength < 1) throw Error("maxLen must be >= 1");
            if (Default.Kind != ParamValueKind.StringList) throw Error("default must be a string list");
            var d = Default.AsStrings;
            if (d.Count > MaxItems) throw Error("default has " + d.Count + " items, max " + MaxItems);
            foreach (string s in d)
            {
                if (TextRules.CodePointCount(s) > MaxLength) throw Error("default item " + TextRules.Quote(s) + " is longer than " + MaxLength);
                if (TextRules.HasLoneSurrogate(s)) throw Error("default item has an unpaired surrogate");
            }
        }

        internal override ParamValue Sanitize(ParamValue raw, DeviceTier tier, bool applyTierCap, List<ParamIssue> issues)
        {
            if (raw.Kind != ParamValueKind.StringList)
                return Fallback(ParamIssueCode.TypeMismatch, "expected a string list, got " + Describe(raw), tier, applyTierCap, issues);
            var items = raw.AsStrings;
            bool changed = false;
            int count = items.Count;
            if (count > MaxItems)
            {
                issues.Add(new ParamIssue(Id, ParamIssueCode.ListTruncated, count + " items cut to " + MaxItems));
                count = MaxItems;
                changed = true;
            }
            var result = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                string s = items[i];
                string repaired = TextRules.ReplaceLoneSurrogates(s);
                if (!ReferenceEquals(repaired, s))
                {
                    issues.Add(new ParamIssue(Id, ParamIssueCode.InvalidText, "item " + i + ": unpaired surrogates replaced with U+FFFD"));
                    s = repaired;
                    changed = true;
                }
                if (TextRules.CodePointCount(s) > MaxLength)
                {
                    issues.Add(new ParamIssue(Id, ParamIssueCode.StringTruncated, "item " + i + " cut to " + MaxLength + " characters"));
                    s = TextRules.Truncate(s, MaxLength);
                    changed = true;
                }
                result.Add(s);
            }
            return changed ? ParamValue.Strings(result) : raw;
        }
    }
}
