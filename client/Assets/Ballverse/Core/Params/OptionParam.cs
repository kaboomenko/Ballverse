using System;
using System.Collections.Generic;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// Base of parameters whose value is one id from a fixed option list (<see cref="EnumParam"/>, <see cref="TemplateParam"/>).
    /// Option ids match case-sensitively (ordinal); an unknown id falls back to the default.
    /// </summary>
    public abstract class OptionParam : ParamDef
    {
        string[] _options;

        /// <summary>Option ids in panel order (read-only view).</summary>
        public IReadOnlyList<string> Options => Array.AsReadOnly(_options);

        protected OptionParam(string id, IEnumerable<string> options, string defaultOption)
            : base(id, ParamValue.String(defaultOption))
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            _options = new List<string>(options).ToArray();
        }

        public string DefaultValue => Default.AsString;

        public bool HasOption(string option)
        {
            return Array.IndexOf(_options, option) >= 0;
        }

        /// <summary>Localization key of an option label: <c>{labelKey}.{option}</c>.</summary>
        public string OptionLabelKey(string option)
        {
            return LabelKey + "." + option;
        }

        /// <summary>Copy with a different option list and default (checked when the schema is built).</summary>
        public OptionParam WithOptions(IEnumerable<string> options, string defaultOption)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            var c = (OptionParam)Clone();
            c._options = new List<string>(options).ToArray();
            c.Default = ParamValue.String(defaultOption);
            return c;
        }

        internal override void Validate()
        {
            ValidateCommon();
            if (_options.Length == 0) throw Error("at least one option is required");
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string o in _options)
            {
                if (string.IsNullOrEmpty(o)) throw Error("option ids must be non-empty");
                if (!seen.Add(o)) throw Error("duplicate option '" + o + "'");
            }
            if (Default.Kind != ParamValueKind.String || !HasOption(Default.AsString))
                throw Error("default " + Default + " is not one of the options");
        }

        internal override ParamValue Sanitize(ParamValue raw, DeviceTier tier, bool applyTierCap, List<ParamIssue> issues)
        {
            if (raw.Kind != ParamValueKind.String)
                return Fallback(ParamIssueCode.TypeMismatch, "expected an option id, got " + Describe(raw), tier, applyTierCap, issues);
            if (HasOption(raw.AsString)) return raw;
            return Fallback(ParamIssueCode.InvalidOption, "unknown option " + TextRules.Quote(raw.AsString), tier, applyTierCap, issues);
        }
    }
}
