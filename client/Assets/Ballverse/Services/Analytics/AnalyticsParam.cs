using System;
using System.Globalization;

namespace Ballverse.Services
{
    public enum AnalyticsParamType
    {
        String = 0,
        Long = 1,
        Double = 2,
    }

    /// <summary>One event parameter (Firebase supports string, integer and floating values).</summary>
    public readonly struct AnalyticsParam : IEquatable<AnalyticsParam>
    {
        public string Name { get; }
        public AnalyticsParamType Type { get; }
        public string StringValue { get; }
        public long LongValue { get; }
        public double DoubleValue { get; }

        AnalyticsParam(string name, AnalyticsParamType type, string s, long l, double d)
        {
            Name = name;
            Type = type;
            StringValue = s;
            LongValue = l;
            DoubleValue = d;
        }

        public static AnalyticsParam Str(string name, string value)
        {
            return new AnalyticsParam(name, AnalyticsParamType.String, value ?? string.Empty, 0, 0);
        }

        public static AnalyticsParam Int(string name, long value)
        {
            return new AnalyticsParam(name, AnalyticsParamType.Long, null, value, 0);
        }

        public static AnalyticsParam Num(string name, double value)
        {
            return new AnalyticsParam(name, AnalyticsParamType.Double, null, 0, value);
        }

        /// <summary>Booleans are sent as integers 1 / 0 (Firebase has no boolean parameter type).</summary>
        public static AnalyticsParam Bool(string name, bool value)
        {
            return Int(name, value ? 1 : 0);
        }

        internal AnalyticsParam WithString(string value)
        {
            return new AnalyticsParam(Name, AnalyticsParamType.String, value, 0, 0);
        }

        public bool Equals(AnalyticsParam other)
        {
            return Name == other.Name && Type == other.Type && StringValue == other.StringValue
                   && LongValue == other.LongValue && DoubleValue.Equals(other.DoubleValue);
        }

        public override bool Equals(object obj)
        {
            return obj is AnalyticsParam other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Name ?? string.Empty).GetHashCode() ^ (int)Type;
        }

        public override string ToString()
        {
            switch (Type)
            {
                case AnalyticsParamType.Long: return Name + "=" + LongValue.ToString(CultureInfo.InvariantCulture);
                case AnalyticsParamType.Double: return Name + "=" + DoubleValue.ToString("R", CultureInfo.InvariantCulture);
                default: return Name + "=\"" + StringValue + "\"";
            }
        }
    }
}
