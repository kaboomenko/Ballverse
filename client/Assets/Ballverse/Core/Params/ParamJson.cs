using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// JSON form of parameter values (spec §7.4 replay <c>paramsJson</c> / <c>paramsHash</c>, §7.6 preset serialization).
    /// Value encoding: floats as numbers via <see cref="CanonicalFloat"/>, ints as integers, unlimited as
    /// <c>"unlimited"</c>, bools, option ids as strings, colours as canonical <c>#RRGGBB[AA]</c> strings, lists as arrays,
    /// seeds as decimal strings. Reading is schema-free: <see cref="ParamSet.Sanitize"/> converts raw kinds. JSON integers
    /// above <see cref="long.MaxValue"/> (up to <see cref="ulong.MaxValue"/>) are read as seeds, which numeric
    /// parameters accept as numbers (and clamp).
    /// </summary>
    public static class ParamJson
    {
        static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false);

        /// <summary>
        /// Canonical JSON: exactly the schema's parameters, keys in schema order (not dictionary order), compact unless
        /// <paramref name="indented"/> (LF newlines, 2-space indent). Values missing from <paramref name="set"/> are
        /// written as defaults; unknown ids are ignored. Every value is first normalized through its definition (tier caps
        /// not applied), so equivalent raw kinds give the same bytes (an int 30 for a float parameter is written 30.0, a
        /// seed string as the seed). Throws <see cref="ArgumentException"/> when a value is not already valid (it would
        /// be clamped, snapped, truncated or replaced by sanitizing): pass a sanitized set (spec §7.4 paramsJson).
        /// </summary>
        public static string ToCanonicalJson(ParamSet set, ParamSchema schema, bool indented = false)
        {
            if (set == null) throw new ArgumentNullException(nameof(set));
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            schema.RequireComplete();
            var values = new ParamValue[schema.Count];
            var issues = new List<ParamIssue>();
            for (int i = 0; i < values.Length; i++)
            {
                var def = schema.Params[i];
                values[i] = set.TryGet(def.Id, out ParamValue v) ? def.Sanitize(v, DeviceTier.A, false, issues) : def.Default;
                if (issues.Count > 0)
                    throw new ArgumentException("Parameter set is not sanitized: " + issues[0], nameof(set));
            }
            return Write(indented, w =>
            {
                w.WriteStartObject();
                for (int i = 0; i < values.Length; i++)
                {
                    w.WritePropertyName(schema.Params[i].Id);
                    WriteValue(w, values[i]);
                }
                w.WriteEndObject();
            });
        }

        /// <summary>UTF-8 bytes of the compact canonical JSON (the replay header <c>paramsJson</c>, spec §7.4).</summary>
        public static byte[] ToCanonicalUtf8(ParamSet set, ParamSchema schema)
        {
            return s_utf8.GetBytes(ToCanonicalJson(set, schema));
        }

        /// <summary>
        /// <c>paramsHash</c> of the replay header (spec §7.4): XXH64 (seed 0) of the compact canonical UTF-8 JSON.
        /// Stable across runs and platforms.
        /// </summary>
        public static ulong ParamsHash(ParamSet set, ParamSchema schema)
        {
            return XxHash64.Hash(ToCanonicalUtf8(set, schema));
        }

        /// <summary>
        /// JSON of every value in <paramref name="set"/> (partial sets allowed). Keys of <paramref name="schema"/> come
        /// first in schema order, then any other ids in ordinal order.
        /// </summary>
        public static string ToJson(ParamSet set, ParamSchema schema = null, bool indented = true)
        {
            if (set == null) throw new ArgumentNullException(nameof(set));
            return Write(indented, w =>
            {
                w.WriteStartObject();
                foreach (string id in OrderedIds(set, schema))
                {
                    w.WritePropertyName(id);
                    WriteValue(w, set.Get(id));
                }
                w.WriteEndObject();
            });
        }

        /// <summary>
        /// Compact canonical JSON of one value, in the encoding of <see cref="ToCanonicalJson"/> (for the replay PARM
        /// chunk "paramId · value", spec §7.4). Pass a sanitized value (see <see cref="ParamSchema.SanitizeValue"/>).
        /// </summary>
        public static string WriteValue(ParamValue value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (value.Kind == ParamValueKind.Invalid) throw new ArgumentException("An invalid raw value cannot be written", nameof(value));
            return Write(false, w => WriteValue(w, value));
        }

        /// <summary>
        /// Reads one raw value written by <see cref="WriteValue"/> (or any JSON value: unusable shapes become
        /// <see cref="ParamValueKind.Invalid"/>). Throws <see cref="FormatException"/> on malformed JSON. Sanitize the
        /// result with <see cref="ParamSchema.SanitizeValue"/>.
        /// </summary>
        public static ParamValue ReadValue(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.FloatParseHandling = FloatParseHandling.Double;
                    var token = JToken.ReadFrom(reader);
                    if (reader.Read()) throw new FormatException("Unexpected content after the JSON value");
                    return FromToken(token);
                }
            }
            catch (JsonException e)
            {
                throw new FormatException("Malformed JSON: " + e.Message, e);
            }
        }

        /// <summary>Reads a JSON object of raw values. Throws <see cref="FormatException"/> on malformed JSON.</summary>
        public static ParamSet FromJson(string json)
        {
            if (!TryFromJson(json, out ParamSet set, out string error)) throw new FormatException(error);
            return set;
        }

        /// <summary>
        /// Reads a JSON object of raw values; never throws. Nulls, nested objects and arrays with non-string items become
        /// <see cref="ParamValueKind.Invalid"/> values, which sanitizing reports as type mismatches (or unknown ids), so
        /// they are never dropped silently (spec §2.3). Duplicate keys: the last wins.
        /// </summary>
        public static bool TryFromJson(string json, out ParamSet set, out string error)
        {
            set = null;
            error = null;
            if (json == null)
            {
                error = "JSON is null";
                return false;
            }
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    reader.DateParseHandling = DateParseHandling.None;
                    reader.FloatParseHandling = FloatParseHandling.Double;
                    var token = JToken.ReadFrom(reader, new JsonLoadSettings
                    {
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Replace,
                        CommentHandling = CommentHandling.Ignore,
                    });
                    while (reader.Read())
                    {
                        if (reader.TokenType != JsonToken.Comment)
                        {
                            error = "Unexpected content after the JSON object";
                            return false;
                        }
                    }
                    if (!(token is JObject obj))
                    {
                        error = "Expected a JSON object of parameter values";
                        return false;
                    }
                    set = FromJObject(obj);
                    return true;
                }
            }
            catch (JsonException e)
            {
                error = "Malformed JSON: " + e.Message;
                return false;
            }
        }

        internal static ParamSet FromJObject(JObject obj)
        {
            var pairs = new Dictionary<string, ParamValue>(StringComparer.Ordinal);
            foreach (var prop in obj.Properties()) pairs[prop.Name] = FromToken(prop.Value);
            return new ParamSet(pairs);
        }

        internal static JObject ToJObject(ParamSet set, ParamSchema schema)
        {
            var obj = new JObject();
            foreach (string id in OrderedIds(set, schema)) obj.Add(id, ToToken(set.Get(id)));
            return obj;
        }

        internal static ParamValue FromToken(JToken token)
        {
            switch (token.Type)
            {
                case JTokenType.Integer:
                {
                    object raw = ((JValue)token).Value;
                    if (raw is BigInteger big)
                    {
                        if (big.Sign >= 0 && big <= ulong.MaxValue) return ParamValue.Seed((ulong)big);
                        return ParamValue.Float((double)big);
                    }
                    return ParamValue.Int(Convert.ToInt64(raw, CultureInfo.InvariantCulture));
                }
                case JTokenType.Float:
                    return ParamValue.Float(Convert.ToDouble(((JValue)token).Value, CultureInfo.InvariantCulture));
                case JTokenType.Boolean:
                    return ParamValue.Bool((bool)((JValue)token).Value);
                case JTokenType.String:
                    return ParamValue.String((string)((JValue)token).Value);
                case JTokenType.Array:
                {
                    var items = new List<string>();
                    foreach (var item in (JArray)token)
                    {
                        if (item.Type != JTokenType.String) return ParamValue.Invalid("array with a non-string item");
                        items.Add((string)((JValue)item).Value);
                    }
                    return ParamValue.Strings(items);
                }
                case JTokenType.Null:
                    return ParamValue.Invalid("null");
                case JTokenType.Object:
                    return ParamValue.Invalid("object");
                default:
                    return ParamValue.Invalid(token.Type.ToString().ToLowerInvariant());
            }
        }

        internal static JToken ToToken(ParamValue v)
        {
            switch (v.Kind)
            {
                case ParamValueKind.Float:
                    return new JRaw(FormatFloat(v.AsFloat));
                case ParamValueKind.Int:
                    return v.IsUnlimited ? new JValue(IntParam.UnlimitedToken) : new JValue(v.AsInt);
                case ParamValueKind.Bool:
                    return new JValue(v.AsBool);
                case ParamValueKind.String:
                    return new JValue(v.AsString);
                case ParamValueKind.Color:
                    return new JValue(v.AsColor.ToString());
                case ParamValueKind.ColorList:
                {
                    var a = new JArray();
                    foreach (var c in v.AsColors) a.Add(c.ToString());
                    return a;
                }
                case ParamValueKind.StringList:
                {
                    var a = new JArray();
                    foreach (var s in v.AsStrings) a.Add(s);
                    return a;
                }
                case ParamValueKind.Seed:
                    return new JValue(v.AsSeed.ToString(CultureInfo.InvariantCulture));
                case ParamValueKind.Invalid:
                    return JValue.CreateNull();
                default:
                    throw new ArgumentOutOfRangeException(nameof(v));
            }
        }

        static void WriteValue(JsonWriter w, ParamValue v)
        {
            switch (v.Kind)
            {
                case ParamValueKind.Float:
                    w.WriteRawValue(FormatFloat(v.AsFloat));
                    break;
                case ParamValueKind.Int:
                    if (v.IsUnlimited) w.WriteValue(IntParam.UnlimitedToken);
                    else w.WriteValue(v.AsInt);
                    break;
                case ParamValueKind.Bool:
                    w.WriteValue(v.AsBool);
                    break;
                case ParamValueKind.String:
                    w.WriteValue(v.AsString);
                    break;
                case ParamValueKind.Color:
                    w.WriteValue(v.AsColor.ToString());
                    break;
                case ParamValueKind.ColorList:
                    w.WriteStartArray();
                    foreach (var c in v.AsColors) w.WriteValue(c.ToString());
                    w.WriteEndArray();
                    break;
                case ParamValueKind.StringList:
                    w.WriteStartArray();
                    foreach (var s in v.AsStrings) w.WriteValue(s);
                    w.WriteEndArray();
                    break;
                case ParamValueKind.Seed:
                    w.WriteValue(v.AsSeed.ToString(CultureInfo.InvariantCulture));
                    break;
                case ParamValueKind.Invalid:
                    // Only reachable from ToJson of an unsanitized set; read back as Invalid again.
                    w.WriteNull();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(v));
            }
        }

        /// <summary>Sanitized floats are float-exact; unsanitized doubles outside float range are written as-is via "R".</summary>
        static string FormatFloat(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d))
                throw new ArgumentException("Non-finite value cannot be serialized; sanitize first");
            float f = (float)d;
            if (!float.IsInfinity(f) && (double)f == d) return CanonicalFloat.Format(f);
            string r = d.ToString("R", CultureInfo.InvariantCulture);
            return r.IndexOf('.') >= 0 || r.IndexOf('E') >= 0 ? r : r + ".0";
        }

        static string Write(bool indented, Action<JsonWriter> body)
        {
            var sw = new StringWriter(CultureInfo.InvariantCulture) { NewLine = "\n" };
            using (var w = new JsonTextWriter(sw))
            {
                w.Formatting = indented ? Formatting.Indented : Formatting.None;
                w.Indentation = 2;
                w.Culture = CultureInfo.InvariantCulture;
                body(w);
            }
            return sw.ToString();
        }

        static IEnumerable<string> OrderedIds(ParamSet set, ParamSchema schema)
        {
            var result = new List<string>(set.Count);
            if (schema != null)
            {
                foreach (var def in schema.Params)
                    if (set.Contains(def.Id)) result.Add(def.Id);
            }
            foreach (string id in set.Ids)
                if (schema == null || !schema.Contains(id)) result.Add(id);
            return result;
        }
    }
}
