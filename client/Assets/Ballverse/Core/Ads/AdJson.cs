using System;
using System.Globalization;
using System.IO;
using System.Numerics;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Ads
{
    /// <summary>
    /// Tolerant readers for the Ads' own persisted JSON formats (state files and the <see cref="AdPolicyConfig"/> file; never
    /// Remote Config payloads, which the Config module owns). Once the text is parsed, a field of the wrong type or out of
    /// range never throws: it is skipped or clamped by the caller. The reader is Newtonsoft's lenient JsonTextReader, so
    /// comments, single quotes, unquoted property names, trailing commas, hex integers (<c>0x5</c>) and leading-zero integers
    /// (read as octal: <c>010</c> is 8) are accepted as Newtonsoft reads them; files written by the ToJson methods never
    /// contain these forms. Text the reader rejects is reported, always as <see cref="InvalidDataException"/> (like
    /// Foundation's VarInt): text that is not JSON, trailing content, a root that is not an object, and two document-level
    /// reader limits: an integer literal longer than 380 characters and nesting deeper than 64 levels (in any field, known or
    /// not). Either fails the whole document; these limits are not worked around.
    /// Property names match case-insensitively, as Newtonsoft deserialization does.
    /// </summary>
    internal static class AdJson
    {
        /// <summary>Parses <paramref name="json"/>; returns null for the JSON literal <c>null</c>.</summary>
        /// <exception cref="InvalidDataException">Not JSON, trailing content, or a root that is neither an object nor null.</exception>
        public static JObject ParseObject(string json, string what)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            JToken root;
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.FloatParseHandling = FloatParseHandling.Double;
                    root = JToken.ReadFrom(reader);
                    while (reader.Read())
                    {
                        if (reader.TokenType != JsonToken.Comment)
                            throw new InvalidDataException(what + ": unexpected content after the JSON value.");
                    }
                }
            }
            catch (JsonException e)
            {
                throw new InvalidDataException(what + ": malformed JSON. " + e.Message, e);
            }
            if (root.Type == JTokenType.Null) return null;
            if (root is JObject obj) return obj;
            throw new InvalidDataException(what + ": expected a JSON object, got " + root.Type + ".");
        }

        public static JToken Get(JObject obj, string name)
        {
            var t = obj.GetValue(name, StringComparison.OrdinalIgnoreCase);
            return t == null || t.Type == JTokenType.Null || t.Type == JTokenType.Undefined ? null : t;
        }

        /// <summary>
        /// Integer from a JSON number or numeric string. Values beyond <see cref="long"/> saturate toward their sign (up to
        /// the reader's 380-character literal limit, see the class summary); fractions are truncated toward zero; NaN,
        /// booleans and other types fail.
        /// </summary>
        public static bool TryGetLong(JToken t, out long value)
        {
            value = 0;
            if (t == null) return false;
            switch (t.Type)
            {
                case JTokenType.Integer:
                    object raw = ((JValue)t).Value;
                    if (raw is BigInteger big)
                    {
                        value = big.Sign < 0 ? long.MinValue : long.MaxValue;
                        return true;
                    }
                    value = Convert.ToInt64(raw, CultureInfo.InvariantCulture);
                    return true;
                case JTokenType.Float:
                    return FromDouble(Convert.ToDouble(((JValue)t).Value, CultureInfo.InvariantCulture), out value);
                case JTokenType.String:
                    return TryParseLong((string)t, out value);
                default:
                    return false;
            }
        }

        /// <summary><see cref="TryGetLong"/> saturated to the <see cref="int"/> range.</summary>
        public static bool TryGetInt(JToken t, out int value)
        {
            bool ok = TryGetLong(t, out long v);
            value = ok ? SaturateToInt(v) : 0;
            return ok;
        }

        /// <summary>
        /// Like <see cref="TryGetInt"/>, but only integral values are accepted: a fraction, NaN or infinity (as a JSON
        /// number or a numeric string) fails, matching the Config module's integer rule. Integral values beyond the
        /// <see cref="int"/> range saturate.
        /// </summary>
        public static bool TryGetIntegralInt(JToken t, out int value)
        {
            value = 0;
            if (t == null) return false;
            double d;
            switch (t.Type)
            {
                case JTokenType.Integer:
                    return TryGetInt(t, out value);
                case JTokenType.Float:
                    d = Convert.ToDouble(((JValue)t).Value, CultureInfo.InvariantCulture);
                    break;
                case JTokenType.String:
                    string s = ((string)t).Trim();
                    if (long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long n))
                    {
                        value = SaturateToInt(n);
                        return true;
                    }
                    if (!double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return false;
                    break;
                default:
                    return false;
            }
            if (double.IsNaN(d) || double.IsInfinity(d) || Math.Floor(d) != d) return false;
            value = d >= int.MaxValue ? int.MaxValue : d <= int.MinValue ? int.MinValue : (int)d;
            return true;
        }

        /// <summary>JSON boolean, or the strings true/false/1/0 (case-insensitive).</summary>
        public static bool TryGetBool(JToken t, out bool value)
        {
            value = false;
            if (t == null) return false;
            if (t.Type == JTokenType.Boolean)
            {
                value = (bool)t;
                return true;
            }
            return t.Type == JTokenType.String && TryParseBool((string)t, out value);
        }

        public static bool TryGetString(JToken t, out string value)
        {
            value = t != null && t.Type == JTokenType.String ? (string)t : null;
            return value != null;
        }

        /// <summary>A defined member of <typeparamref name="T"/> by name (case-insensitive) or by integer value.</summary>
        public static bool TryGetEnum<T>(JToken t, out T value) where T : struct, Enum
        {
            value = default;
            if (t == null) return false;
            if (t.Type == JTokenType.String)
            {
                string s = ((string)t).Trim();
                // Enum.TryParse also accepts numbers and comma lists; a name must start with a letter or '_'.
                if (s.Length == 0 || !(char.IsLetter(s[0]) || s[0] == '_') || s.IndexOf(',') >= 0) return false;
                return Enum.TryParse(s, true, out value) && Enum.IsDefined(typeof(T), value);
            }
            if (t.Type == JTokenType.Integer && TryGetInt(t, out int i) && Enum.IsDefined(typeof(T), i))
            {
                value = (T)Enum.ToObject(typeof(T), i);
                return true;
            }
            return false;
        }

        /// <summary>Invariant-culture integer or decimal string; same saturation and truncation as <see cref="TryGetLong"/>.</summary>
        public static bool TryParseLong(string s, out long value)
        {
            value = 0;
            if (s == null) return false;
            s = s.Trim();
            if (long.TryParse(s, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)) return true;
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)) return FromDouble(d, out value);
            return false;
        }

        public static bool TryParseBool(string s, out bool value)
        {
            value = false;
            if (s == null) return false;
            s = s.Trim();
            if (s == "1" || string.Equals(s, "true", StringComparison.OrdinalIgnoreCase))
            {
                value = true;
                return true;
            }
            return s == "0" || string.Equals(s, "false", StringComparison.OrdinalIgnoreCase);
        }

        public static int SaturateToInt(long v)
        {
            return v > int.MaxValue ? int.MaxValue : v < int.MinValue ? int.MinValue : (int)v;
        }

        static bool FromDouble(double d, out long value)
        {
            value = 0;
            if (double.IsNaN(d)) return false;
            if (d >= 9.2233720368547758E18) value = long.MaxValue;
            else if (d <= -9.2233720368547758E18) value = long.MinValue;
            else value = (long)Math.Truncate(d);
            return true;
        }
    }
}
