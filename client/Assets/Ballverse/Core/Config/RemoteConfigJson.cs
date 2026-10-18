using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Config
{
    /// <summary>
    /// JSON helpers for Remote Config payloads. Reader settings follow <see cref="CoreJson"/>:
    /// invariant culture, no date parsing (strings stay strings), doubles for floats, depth limited.
    /// </summary>
    internal static class RemoteConfigJson
    {
        const int MaxDepth = 64;

        /// <summary>Parses one complete JSON document; fractional numbers become doubles. Never throws.</summary>
        public static bool TryParse(string text, out JToken token, out string error)
        {
            return TryParse(text, FloatParseHandling.Double, out token, out error);
        }

        /// <summary>
        /// Parses raw JSON that is stored for another module (remote presets, spec §2.3): fractional numbers are read as decimals so
        /// up to 28 significant digits survive re-serialization as delivered. Falls back to doubles when a number exceeds the
        /// decimal range (such values are then exact only to double precision; non-finite ones are rejected by the caller).
        /// Never throws.
        /// </summary>
        public static bool TryParsePreservingNumbers(string text, out JToken token, out string error)
        {
            if (TryParse(text, FloatParseHandling.Decimal, out token, out error)) return true;
            return TryParse(text, FloatParseHandling.Double, out token, out error);
        }

        static bool TryParse(string text, FloatParseHandling floats, out JToken token, out string error)
        {
            token = null;
            error = null;
            if (text == null)
            {
                error = "null text";
                return false;
            }
            try
            {
                using (var reader = new JsonTextReader(new StringReader(text)))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.FloatParseHandling = floats;
                    reader.Culture = CultureInfo.InvariantCulture;
                    reader.MaxDepth = MaxDepth;
                    var settings = new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Ignore,
                        LineInfoHandling = LineInfoHandling.Ignore,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    };
                    var t = JToken.ReadFrom(reader, settings);
                    while (reader.Read())
                    {
                        if (reader.TokenType != JsonToken.Comment)
                        {
                            error = "unexpected content after the JSON value";
                            return false;
                        }
                    }
                    token = t;
                    return true;
                }
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>
        /// Integer value of a JSON number. Integral floats (60.0) are accepted; huge integers saturate to the long range.
        /// False for fractions, non-numbers, NaN/Infinity.
        /// </summary>
        public static bool TryGetInteger(JToken token, out long value)
        {
            value = 0;
            if (token == null) return false;
            if (token.Type == JTokenType.Integer)
            {
                object v = ((JValue)token).Value;
                if (v is BigInteger big)
                {
                    value = big.Sign < 0 ? long.MinValue : long.MaxValue;
                    return true;
                }
                value = Convert.ToInt64(v, CultureInfo.InvariantCulture);
                return true;
            }
            if (token.Type == JTokenType.Float)
            {
                double d = Convert.ToDouble(((JValue)token).Value, CultureInfo.InvariantCulture);
                return TryIntegral(d, out value);
            }
            return false;
        }

        /// <summary>Finite double value of a JSON number. False for non-numbers and NaN/Infinity.</summary>
        public static bool TryGetNumber(JToken token, out double value)
        {
            value = 0;
            if (token == null) return false;
            if (token.Type == JTokenType.Integer)
            {
                object v = ((JValue)token).Value;
                value = v is BigInteger big ? (double)big : Convert.ToDouble(v, CultureInfo.InvariantCulture);
                return !double.IsInfinity(value);
            }
            if (token.Type == JTokenType.Float)
            {
                value = Convert.ToDouble(((JValue)token).Value, CultureInfo.InvariantCulture);
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            return false;
        }

        /// <summary>String value of a JSON string token, else null.</summary>
        public static string GetString(JToken token)
        {
            return token != null && token.Type == JTokenType.String ? (string)((JValue)token).Value : null;
        }

        /// <summary>Integral double to long, saturating; false for NaN/Infinity/fractions.</summary>
        public static bool TryIntegral(double d, out long value)
        {
            value = 0;
            if (double.IsNaN(d) || double.IsInfinity(d) || Math.Floor(d) != d) return false;
            if (d >= 9.2233720368547758E18) value = long.MaxValue;
            else if (d <= -9.2233720368547758E18) value = long.MinValue;
            else value = (long)d;
            return true;
        }

        /// <summary>
        /// True when any number in <paramref name="token"/> is NaN or ±Infinity (e.g. a literal beyond the double range such as
        /// 1e400). Such a value would be re-serialized as the string "Infinity", silently changing its JSON type.
        /// </summary>
        public static bool ContainsNonFiniteNumber(JToken token)
        {
            var stack = new System.Collections.Generic.Stack<JToken>();
            stack.Push(token);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t.Type == JTokenType.Float)
                {
                    object v = ((JValue)t).Value;
                    if (v is double d && (double.IsNaN(d) || double.IsInfinity(d))) return true;
                    if (v is float f && (float.IsNaN(f) || float.IsInfinity(f))) return true;
                }
                else if (t is JContainer c)
                {
                    foreach (var child in c.Children()) stack.Push(child);
                }
            }
            return false;
        }

        public static string Compact(JToken token)
        {
            return token.ToString(Formatting.None);
        }

        /// <summary>Short excerpt of a raw value for issue messages.</summary>
        public static string Excerpt(string s)
        {
            if (s == null) return "null";
            const int max = 40;
            return s.Length <= max ? "'" + s + "'" : "'" + s.Substring(0, max) + "…'";
        }
    }
}
