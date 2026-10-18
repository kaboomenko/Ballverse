using System;
using System.Collections.Generic;
using Ballverse.Core.Params;

namespace Ballverse.UI.Params
{
    /// <summary>Editor control chosen for a parameter type (spec §7.6: the panel is generated from the schema).</summary>
    public enum ParamEditorKind
    {
        /// <summary>Float: slider snapped to the step grid.</summary>
        Slider = 0,
        /// <summary>Int: slider; with "unlimited" an extra ∞ chip.</summary>
        IntSlider = 1,
        Toggle = 2,
        /// <summary>Enum / template with few short options: segmented control.</summary>
        Segmented = 3,
        /// <summary>Enum / template with many options: scrolling chip row.</summary>
        Chips = 4,
        Color = 5,
        ColorList = 6,
        StringList = 7,
        Seed = 8,
    }

    /// <summary>One option of an enum / template parameter.</summary>
    public sealed class ParamOption
    {
        public string Id { get; }
        public string LabelKey { get; }

        public ParamOption(string id, string labelKey)
        {
            Id = id;
            LabelKey = labelKey;
        }
    }

    /// <summary>Everything one settings row needs to draw and edit a parameter. Immutable snapshot.</summary>
    public sealed class ParamRowModel
    {
        static readonly ParamOption[] s_noOptions = new ParamOption[0];

        public ParamDef Def { get; }
        public string Id => Def.Id;
        public ParamEditorKind Kind { get; }
        public string LabelKey => Def.LabelKey;
        /// <summary>Value shown: a pending structural edit when there is one, otherwise the applied value.</summary>
        public ParamValue Value { get; }
        public bool Live => Def.Live;
        /// <summary>Edited while running but not applied yet (structural): the panel offers a restart.</summary>
        public bool Pending { get; }
        /// <summary>Numeric range on the current tier (max is min(max, tierCap)).</summary>
        public double Min { get; }
        public double Max { get; }
        public double Step { get; }
        /// <summary>The device tier lowers the maximum (spec §2.9): show a hint.</summary>
        public bool TierCapped { get; }
        public string Unit { get; }
        public bool AllowUnlimited { get; }
        public IReadOnlyList<ParamOption> Options { get; }
        /// <summary>Colour list bounds / string list bounds.</summary>
        public int MinCount { get; }
        public int MaxCount { get; }
        public int MaxLength { get; }

        internal ParamRowModel(ParamDef def, ParamEditorKind kind, ParamValue value, bool pending, double min, double max,
            double step, bool tierCapped, string unit, bool allowUnlimited, IReadOnlyList<ParamOption> options,
            int minCount, int maxCount, int maxLength)
        {
            Def = def;
            Kind = kind;
            Value = value;
            Pending = pending;
            Min = min;
            Max = max;
            Step = step;
            TierCapped = tierCapped;
            Unit = unit;
            AllowUnlimited = allowUnlimited;
            Options = options ?? s_noOptions;
            MinCount = minCount;
            MaxCount = maxCount;
            MaxLength = maxLength;
        }

        /// <summary>Segmented control when there are at most this many options; chips otherwise.</summary>
        public const int MaxSegmentedOptions = 4;

        internal static ParamRowModel Create(ParamDef def, ParamValue value, bool pending, Core.DeviceTier tier)
        {
            switch (def)
            {
                case FloatParam f:
                {
                    double max = f.EffectiveMax(tier);
                    return new ParamRowModel(def, ParamEditorKind.Slider, value, pending, f.Min, max, f.Step, max < f.Max,
                        f.Unit, false, null, 0, 0, 0);
                }
                case IntParam i:
                {
                    int max = i.EffectiveMax(tier);
                    return new ParamRowModel(def, ParamEditorKind.IntSlider, value, pending, i.Min, max, i.Step, max < i.Max,
                        i.Unit, i.AllowUnlimited, null, 0, 0, 0);
                }
                case BoolParam _:
                    return new ParamRowModel(def, ParamEditorKind.Toggle, value, pending, 0, 1, 1, false, null, false, null, 0, 0, 0);
                case OptionParam o:
                {
                    var options = new List<ParamOption>(o.Options.Count);
                    foreach (string id in o.Options) options.Add(new ParamOption(id, o.OptionLabelKey(id)));
                    var kind = options.Count <= MaxSegmentedOptions ? ParamEditorKind.Segmented : ParamEditorKind.Chips;
                    return new ParamRowModel(def, kind, value, pending, 0, 0, 0, false, null, false, options, 0, 0, 0);
                }
                case ColorParam _:
                    return new ParamRowModel(def, ParamEditorKind.Color, value, pending, 0, 0, 0, false, null, false, null, 0, 0, 0);
                case ColorListParam cl:
                    return new ParamRowModel(def, ParamEditorKind.ColorList, value, pending, 0, 0, 0, false, null, false, null,
                        cl.MinCount, cl.MaxCount, 0);
                case StringListParam sl:
                    return new ParamRowModel(def, ParamEditorKind.StringList, value, pending, 0, 0, 0, false, null, false, null,
                        0, sl.MaxItems, sl.MaxLength);
                case SeedParam _:
                    return new ParamRowModel(def, ParamEditorKind.Seed, value, pending, 0, 0, 0, false, null, false, null, 0, 0, 0);
                default:
                    throw new ArgumentException("Unsupported parameter type " + def.Type, nameof(def));
            }
        }
    }
}
