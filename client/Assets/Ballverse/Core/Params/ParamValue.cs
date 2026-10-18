using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// Immutable tagged value of one parameter (spec §7.6). Equality is structural; floats compare by bit pattern
    /// (sanitized values never hold -0 or NaN). Lists are copied on construction and exposed read-only.
    /// </summary>
    public sealed class ParamValue : IEquatable<ParamValue>
    {
        static readonly ColorRgba[] s_noColors = new ColorRgba[0];
        static readonly string[] s_noStrings = new string[0];

        readonly double _float;
        readonly long _int;
        readonly ulong _seed;
        readonly bool _bool;
        readonly string _string;
        readonly ColorRgba _color;
        readonly ColorRgba[] _colors;
        readonly string[] _strings;

        public ParamValueKind Kind { get; }

        ParamValue(ParamValueKind kind, double f = 0, long i = 0, ulong seed = 0, bool b = false, string s = null,
            ColorRgba color = default, ColorRgba[] colors = null, string[] strings = null)
        {
            Kind = kind;
            _float = f;
            _int = i;
            _seed = seed;
            _bool = b;
            _string = s;
            _color = color;
            _colors = colors;
            _strings = strings;
        }

        /// <summary>
        /// Value of an unlimited <see cref="IntParam"/> (spec §2.2 targetDuration "Без ограничения").
        /// Serialized as the JSON string <c>"unlimited"</c>.
        /// </summary>
        public static readonly ParamValue Unlimited = Int(IntParam.UnlimitedValue);

        public static ParamValue Float(double value)
        {
            return new ParamValue(ParamValueKind.Float, f: value);
        }

        public static ParamValue Int(long value)
        {
            return new ParamValue(ParamValueKind.Int, i: value);
        }

        public static ParamValue Bool(bool value)
        {
            return new ParamValue(ParamValueKind.Bool, b: value);
        }

        public static ParamValue String(string value)
        {
            return new ParamValue(ParamValueKind.String, s: value ?? "");
        }

        public static ParamValue Color(ColorRgba value)
        {
            return new ParamValue(ParamValueKind.Color, color: value);
        }

        public static ParamValue Colors(IEnumerable<ColorRgba> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var list = new List<ColorRgba>(values);
            return new ParamValue(ParamValueKind.ColorList, colors: list.Count == 0 ? s_noColors : list.ToArray());
        }

        /// <summary>String list; null items become empty strings.</summary>
        public static ParamValue Strings(IEnumerable<string> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var list = new List<string>();
            foreach (string s in values) list.Add(s ?? "");
            return new ParamValue(ParamValueKind.StringList, strings: list.Count == 0 ? s_noStrings : list.ToArray());
        }

        public static ParamValue Seed(ulong value)
        {
            return new ParamValue(ParamValueKind.Seed, seed: value);
        }

        public double AsFloat
        {
            get
            {
                if (Kind == ParamValueKind.Int) return _int;
                Expect(ParamValueKind.Float);
                return _float;
            }
        }

        public long AsInt
        {
            get
            {
                Expect(ParamValueKind.Int);
                return _int;
            }
        }

        public bool AsBool
        {
            get
            {
                Expect(ParamValueKind.Bool);
                return _bool;
            }
        }

        public string AsString
        {
            get
            {
                Expect(ParamValueKind.String);
                return _string;
            }
        }

        public ColorRgba AsColor
        {
            get
            {
                Expect(ParamValueKind.Color);
                return _color;
            }
        }

        public IReadOnlyList<ColorRgba> AsColors
        {
            get
            {
                Expect(ParamValueKind.ColorList);
                return _colors;
            }
        }

        public IReadOnlyList<string> AsStrings
        {
            get
            {
                Expect(ParamValueKind.StringList);
                return _strings;
            }
        }

        public ulong AsSeed
        {
            get
            {
                Expect(ParamValueKind.Seed);
                return _seed;
            }
        }

        public bool IsUnlimited => Kind == ParamValueKind.Int && _int == IntParam.UnlimitedValue;

        public bool Equals(ParamValue other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null || other.Kind != Kind) return false;
            switch (Kind)
            {
                case ParamValueKind.Float:
                    return BitConverter.DoubleToInt64Bits(_float) == BitConverter.DoubleToInt64Bits(other._float);
                case ParamValueKind.Int:
                    return _int == other._int;
                case ParamValueKind.Bool:
                    return _bool == other._bool;
                case ParamValueKind.String:
                    return string.Equals(_string, other._string, StringComparison.Ordinal);
                case ParamValueKind.Color:
                    return _color == other._color;
                case ParamValueKind.ColorList:
                    if (_colors.Length != other._colors.Length) return false;
                    for (int i = 0; i < _colors.Length; i++)
                        if (_colors[i] != other._colors[i]) return false;
                    return true;
                case ParamValueKind.StringList:
                    if (_strings.Length != other._strings.Length) return false;
                    for (int i = 0; i < _strings.Length; i++)
                        if (!string.Equals(_strings[i], other._strings[i], StringComparison.Ordinal)) return false;
                    return true;
                case ParamValueKind.Seed:
                    return _seed == other._seed;
                default:
                    return false;
            }
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ParamValue);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int h = (int)Kind * 397;
                switch (Kind)
                {
                    case ParamValueKind.Float: return h ^ BitConverter.DoubleToInt64Bits(_float).GetHashCode();
                    case ParamValueKind.Int: return h ^ _int.GetHashCode();
                    case ParamValueKind.Bool: return h ^ (_bool ? 1 : 0);
                    case ParamValueKind.String: return h ^ StringComparer.Ordinal.GetHashCode(_string);
                    case ParamValueKind.Color: return h ^ _color.GetHashCode();
                    case ParamValueKind.ColorList:
                        foreach (var c in _colors) h = h * 31 + c.GetHashCode();
                        return h;
                    case ParamValueKind.StringList:
                        foreach (var s in _strings) h = h * 31 + StringComparer.Ordinal.GetHashCode(s);
                        return h;
                    case ParamValueKind.Seed: return h ^ _seed.GetHashCode();
                    default: return h;
                }
            }
        }

        /// <summary>Debug text; not a serialization format (use <see cref="ParamJson"/>).</summary>
        public override string ToString()
        {
            switch (Kind)
            {
                case ParamValueKind.Float: return _float.ToString("R", CultureInfo.InvariantCulture);
                case ParamValueKind.Int: return IsUnlimited ? IntParam.UnlimitedToken : _int.ToString(CultureInfo.InvariantCulture);
                case ParamValueKind.Bool: return _bool ? "true" : "false";
                case ParamValueKind.String: return "\"" + _string + "\"";
                case ParamValueKind.Color: return _color.ToString();
                case ParamValueKind.ColorList: return "[" + string.Join(",", Array.ConvertAll(_colors, c => c.ToString())) + "]";
                case ParamValueKind.StringList:
                {
                    var sb = new StringBuilder("[");
                    for (int i = 0; i < _strings.Length; i++)
                    {
                        if (i > 0) sb.Append(',');
                        sb.Append('"').Append(_strings[i]).Append('"');
                    }
                    return sb.Append(']').ToString();
                }
                case ParamValueKind.Seed: return "seed:" + _seed.ToString(CultureInfo.InvariantCulture);
                default: return Kind.ToString();
            }
        }

        void Expect(ParamValueKind kind)
        {
            if (Kind != kind) throw new InvalidOperationException("Parameter value is " + Kind + ", not " + kind);
        }
    }
}
