using System;
using System.Collections.Generic;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// One parameter definition with its §7.6 attributes: id, group, order, <c>live</c>, <c>visibleIf</c>, label key
    /// and default. Definitions are immutable; the <c>With…</c> / <c>In…</c> methods return modified copies.
    /// Range and option checks run when a <see cref="ParamSchemaBuilder"/> builds the schema.
    /// </summary>
    public abstract class ParamDef
    {
        public string Id { get; private set; }
        public abstract ParamType Type { get; }
        public ParamGroup Group { get; private set; }
        /// <summary>Order inside <see cref="Group"/> (second sort key of a schema; ties keep insertion order).</summary>
        public int Order { get; private set; }
        /// <summary>
        /// Editable while <c>Running</c>: applied immediately and recorded in the replay PARM chunk (spec §2.2 panel rules, §7.4).
        /// Structural parameters (false) offer a restart instead.
        /// </summary>
        public bool Live { get; private set; }
        /// <summary>Panel visibility condition, or null when always visible (spec §7.6).</summary>
        public VisibleIf VisibleIf { get; private set; }
        /// <summary>Localization key of the label; defaults to <c>params.{id}</c>.</summary>
        public string LabelKey { get; private set; }
        /// <summary>Default value; always a sanitized value of this parameter once the schema is built.</summary>
        public ParamValue Default { get; protected set; }

        protected ParamDef(string id, ParamValue defaultValue)
        {
            Id = id;
            Default = defaultValue;
            Group = ParamGroup.Mode;
            LabelKey = "params." + id;
        }

        /// <summary>Copy placed in <paramref name="group"/> at <paramref name="order"/>.</summary>
        public ParamDef InGroup(ParamGroup group, int order)
        {
            var c = Clone();
            c.Group = group;
            c.Order = order;
            return c;
        }

        /// <summary>Copy marked live (true) or structural (false), spec §2.2.</summary>
        public ParamDef AsLive(bool live = true)
        {
            var c = Clone();
            c.Live = live;
            return c;
        }

        public ParamDef WithVisibleIf(VisibleIf condition)
        {
            var c = Clone();
            c.VisibleIf = condition;
            return c;
        }

        public ParamDef WithLabelKey(string labelKey)
        {
            if (string.IsNullOrEmpty(labelKey)) throw new ArgumentException("Label key is required", nameof(labelKey));
            var c = Clone();
            c.LabelKey = labelKey;
            return c;
        }

        /// <summary>Copy with a different default (checked when the schema is built).</summary>
        public ParamDef WithDefault(ParamValue defaultValue)
        {
            var c = Clone();
            c.Default = defaultValue;
            return c;
        }

        public override string ToString()
        {
            return Type + " " + Id;
        }

        /// <summary>Throws <see cref="ParamSchemaException"/> when the definition is inconsistent; may normalize <see cref="Default"/>.</summary>
        internal abstract void Validate();

        /// <summary>
        /// Converts a raw value into a valid value of this parameter, appending what changed to <paramref name="issues"/>.
        /// Never throws for any input value.
        /// </summary>
        internal abstract ParamValue Sanitize(ParamValue raw, DeviceTier tier, bool applyTierCap, List<ParamIssue> issues);

        /// <summary>
        /// Copy of this (common) definition taking the overridable attributes (range, step, default, tier caps, options)
        /// from <paramref name="modeDef"/>, which has the same id and type (spec §7.6 "параметры режима поверх общих").
        /// </summary>
        internal virtual ParamDef ApplyOverride(ParamDef modeDef)
        {
            return modeDef.CopyAttributesFrom(this);
        }

        /// <summary>
        /// Whether a <see cref="VisibleIf"/> value can ever match this parameter: sanitizing it reports nothing (plain
        /// double-to-float rounding and kind conversions such as a colour string are not reported).
        /// <paramref name="normalized"/> is the sanitized form the condition must compare against.
        /// </summary>
        internal bool TryNormalizeConditionValue(ParamValue value, out ParamValue normalized)
        {
            var scratch = new List<ParamIssue>();
            normalized = Sanitize(value, DeviceTier.A, false, scratch);
            return scratch.Count == 0;
        }

        internal void SetVisibleIf(VisibleIf condition)
        {
            VisibleIf = condition;
        }

        internal ParamDef Clone()
        {
            return (ParamDef)MemberwiseClone();
        }

        internal ParamDef CopyAttributesFrom(ParamDef source)
        {
            var c = Clone();
            c.Group = source.Group;
            c.Order = source.Order;
            c.Live = source.Live;
            c.VisibleIf = source.VisibleIf;
            c.LabelKey = source.LabelKey;
            return c;
        }

        internal void ValidateCommon()
        {
            if (!TextRules.IsValidParamId(Id))
                throw new ParamSchemaException(Id, "id must start with an ASCII letter and contain only letters, digits or '_' (max 64)");
            if (Default == null) throw new ParamSchemaException(Id, "default value is required");
        }

        internal ParamSchemaException Error(string message)
        {
            return new ParamSchemaException(Id, message);
        }

        internal ParamValue Fallback(ParamIssueCode code, string message, DeviceTier tier, bool applyTierCap, List<ParamIssue> issues)
        {
            issues.Add(new ParamIssue(Id, code, message + "; using default"));
            return DefaultFor(tier, applyTierCap);
        }

        /// <summary>Kind of a raw value for issue messages ("Invalid (null)" for unusable JSON).</summary>
        internal static string Describe(ParamValue raw)
        {
            return raw.Kind == ParamValueKind.Invalid ? "invalid JSON " + raw.InvalidDescription : raw.Kind.ToString();
        }

        /// <summary>The default clamped to the tier cap when one applies.</summary>
        internal virtual ParamValue DefaultFor(DeviceTier tier, bool applyTierCap)
        {
            return Default;
        }
    }
}
