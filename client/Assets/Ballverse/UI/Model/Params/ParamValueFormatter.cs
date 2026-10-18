using System;
using System.Globalization;
using Ballverse.Core.Params;
using Ballverse.UI.Localization;

namespace Ballverse.UI.Params
{
    /// <summary>Display text of parameter values: numbers on the step grid, localized units, "unlimited", seeds.</summary>
    public static class ParamValueFormatter
    {
        /// <summary>Number of decimals that shows every value of a step grid (0–3).</summary>
        public static int DecimalsForStep(double step)
        {
            if (!(step > 0) || double.IsInfinity(step)) return 0;
            for (int d = 0; d <= 3; d++)
            {
                double scaled = step * Math.Pow(10, d);
                if (Math.Abs(scaled - Math.Round(scaled)) < 1e-6) return d;
            }
            return 3;
        }

        public static string FormatNumber(double value, double step, CultureInfo culture)
        {
            int d = DecimalsForStep(step);
            return value.ToString("F" + d.ToString(CultureInfo.InvariantCulture), culture ?? CultureInfo.InvariantCulture);
        }

        /// <summary>Localization key of a unit pattern such as <c>unit.percent</c> = "{0}%", or null for no unit.</summary>
        public static string UnitKey(string unit)
        {
            if (string.IsNullOrEmpty(unit)) return null;
            switch (unit)
            {
                case "x": return "unit.times";
                case "%": return "unit.percent";
                case "°": return "unit.deg";
                case "s": return "unit.s";
                case "ms": return "unit.ms";
                case "m": return "unit.m";
                case "g": return "unit.g";
                case "m/s": return "unit.mps";
                case "px": return "unit.px";
                default: return "unit." + unit;
            }
        }

        /// <summary>Number with its unit, e.g. "1.5×", "80 %" → per language table.</summary>
        public static string FormatWithUnit(double value, double step, string unit, Localizer localizer)
        {
            if (localizer == null) throw new ArgumentNullException(nameof(localizer));
            string number = FormatNumber(value, step, localizer.Culture);
            string key = UnitKey(unit);
            if (key == null) return number;
            string pattern = localizer.GetOrDefault(key, null);
            if (pattern == null) return number + " " + unit;
            try
            {
                return string.Format(localizer.Culture, pattern, number);
            }
            catch (FormatException)
            {
                return number + " " + unit;
            }
        }

        /// <summary>Short text of a row's current value for the row's trailing label.</summary>
        public static string Format(ParamRowModel row, Localizer localizer)
        {
            if (row == null) throw new ArgumentNullException(nameof(row));
            if (localizer == null) throw new ArgumentNullException(nameof(localizer));
            var v = row.Value;
            switch (row.Kind)
            {
                case ParamEditorKind.Slider:
                    return FormatWithUnit(v.AsFloat, row.Step, row.Unit, localizer);
                case ParamEditorKind.IntSlider:
                    if (v.IsUnlimited) return localizer.Get("params.unlimited");
                    return FormatWithUnit(v.AsInt, row.Step, row.Unit, localizer);
                case ParamEditorKind.Toggle:
                    return localizer.Get(v.AsBool ? "common.on" : "common.off");
                case ParamEditorKind.Segmented:
                case ParamEditorKind.Chips:
                    foreach (var o in row.Options)
                        if (o.Id == v.AsString) return localizer.Get(o.LabelKey);
                    return v.AsString;
                case ParamEditorKind.Color:
                    return v.AsColor.ToString();
                case ParamEditorKind.ColorList:
                    return v.AsColors.Count == 0
                        ? localizer.Get("params.colors.theme")
                        : localizer.Plural("params.colors.count", v.AsColors.Count);
                case ParamEditorKind.StringList:
                    return localizer.Plural("params.names.count", v.AsStrings.Count);
                case ParamEditorKind.Seed:
                    return v.AsSeed.ToString(CultureInfo.InvariantCulture);
                default:
                    return v.ToString();
            }
        }

        /// <summary>Label key of a settings group, e.g. <c>params.group.physics</c>.</summary>
        public static string GroupLabelKey(ParamGroup group)
        {
            switch (group)
            {
                case ParamGroup.Basic: return "params.group.basic";
                case ParamGroup.Physics: return "params.group.physics";
                case ParamGroup.Look: return "params.group.look";
                case ParamGroup.Sound: return "params.group.sound";
                case ParamGroup.Director: return "params.group.director";
                default: return "params.group.mode";
            }
        }

        /// <summary>
        /// Value of a slider position for a row: snapped to the step grid inside [Min, Max]. Pure helper shared by the
        /// slider control and tests; the schema sanitizes again on edit.
        /// </summary>
        public static double SnapToStep(double value, double min, double max, double step)
        {
            if (double.IsNaN(value)) return min;
            if (max < min) max = min;
            double v = Math.Max(min, Math.Min(max, value));
            if (step > 0)
            {
                double k = Math.Floor((v - min) / step + 0.5);
                v = min + k * step;
                if (v > max + step * 1e-9) v -= step;
                v = Math.Max(min, Math.Min(max, v));
            }
            return v;
        }
    }
}
