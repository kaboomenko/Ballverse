using System;
using System.Collections.Generic;

namespace Ballverse.Core.Params
{
    /// <summary>Kind of a <see cref="VisibleIf"/> condition.</summary>
    public enum VisibleIfKind
    {
        /// <summary>The target equals one value.</summary>
        Is = 0,
        /// <summary>The target equals any value of a set.</summary>
        In = 1,
        /// <summary>The target is a <see cref="BoolParam"/> set to true.</summary>
        IsTrue = 2,
    }

    /// <summary>
    /// Visibility condition on another parameter (spec §7.6 <c>visibleIf</c>), e.g. <c>melody</c> is shown only when
    /// <c>soundPack == Melody</c> (spec §2.2). A parameter is visible when its condition holds and its target is itself
    /// visible; conditions are validated (known target, no cycles) when the schema is built.
    /// </summary>
    public sealed class VisibleIf
    {
        readonly ParamValue[] _values;

        public VisibleIfKind Kind { get; }
        public string TargetId { get; }
        /// <summary>Accepted values (one for <see cref="VisibleIfKind.Is"/>, empty for <see cref="VisibleIfKind.IsTrue"/>).</summary>
        public IReadOnlyList<ParamValue> Values => Array.AsReadOnly(_values);

        VisibleIf(VisibleIfKind kind, string targetId, ParamValue[] values)
        {
            if (string.IsNullOrEmpty(targetId)) throw new ArgumentException("Target id is required", nameof(targetId));
            Kind = kind;
            TargetId = targetId;
            _values = values;
        }

        /// <summary>Visible when the enum / template target has this option id.</summary>
        public static VisibleIf Is(string targetId, string option)
        {
            return new VisibleIf(VisibleIfKind.Is, targetId, new[] { ParamValue.String(option) });
        }

        /// <summary>Visible when the target equals <paramref name="value"/>.</summary>
        public static VisibleIf Is(string targetId, ParamValue value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            return new VisibleIf(VisibleIfKind.Is, targetId, new[] { value });
        }

        /// <summary>Visible when the enum / template target has any of these option ids.</summary>
        public static VisibleIf In(string targetId, params string[] options)
        {
            if (options == null || options.Length == 0) throw new ArgumentException("At least one option is required", nameof(options));
            return new VisibleIf(VisibleIfKind.In, targetId, Array.ConvertAll(options, ParamValue.String));
        }

        /// <summary>Visible when the target equals any of <paramref name="values"/>.</summary>
        public static VisibleIf In(string targetId, IEnumerable<ParamValue> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var list = new List<ParamValue>();
            foreach (var v in values) list.Add(v ?? throw new ArgumentException("Null value", nameof(values)));
            if (list.Count == 0) throw new ArgumentException("At least one value is required", nameof(values));
            return new VisibleIf(VisibleIfKind.In, targetId, list.ToArray());
        }

        /// <summary>Visible when the bool target is true.</summary>
        public static VisibleIf IsTrue(string targetId)
        {
            return new VisibleIf(VisibleIfKind.IsTrue, targetId, new ParamValue[0]);
        }

        /// <summary>Same condition with other values (used to store sanitized forms when a schema is built).</summary>
        internal VisibleIf WithValues(ParamValue[] values)
        {
            return new VisibleIf(Kind, TargetId, values);
        }

        /// <summary>Evaluates the condition against the target's current value (null counts as not matching).</summary>
        public bool Matches(ParamValue targetValue)
        {
            if (targetValue == null) return false;
            if (Kind == VisibleIfKind.IsTrue) return targetValue.Kind == ParamValueKind.Bool && targetValue.AsBool;
            foreach (var v in _values)
                if (v.Equals(targetValue)) return true;
            return false;
        }

        public override string ToString()
        {
            switch (Kind)
            {
                case VisibleIfKind.IsTrue: return TargetId + " is true";
                case VisibleIfKind.Is: return TargetId + " == " + _values[0];
                default: return TargetId + " in [" + string.Join(", ", Array.ConvertAll(_values, v => v.ToString())) + "]";
            }
        }
    }
}
