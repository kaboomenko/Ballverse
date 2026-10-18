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
    /// seeds as decimal strings. Reading is schema-free: <see cref="ParamSet.Sanitize"/> converts raw kinds.
    /// </summary>
    public static class ParamJson
    {
        static readonly UTF8Encoding s_utf8 = new UTF8Encoding(false);

        /// <summary>
        /// Canonical JSON: exactly the schema's parameters, keys in schema order (not dictionary order), compact unless
        /// <paramref name="indented"/> (LF newlines, 2-space indent). Values missing from <paramref name="set"/> are
        /// written as defaults; unknown ids are ignored. Pass a sanitized set.
        /// </summary>
        public static string ToCanonicalJson(ParamSet set, ParamSchema schema, bool indented = false)
        {
            if (set == null) throw new ArgumentNullException(nameof(set));
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            schema.RequireComplete();
            return Write(indented, w =>
            {
                w.WriteStartObject();
                foreach (var def in schema.Params)
                {
                    w.WritePropertyName(def.Id);
                    WriteValue(w, set.TryGet(def.Id, out ParamValue v) ? v : def.Default);
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

        /// <summary>Reads a JSON object of raw values. Throws <see cref="FormatException"/> on malformed JSON.</summary>
        public static ParamSet FromJson(string json)
        {
            if (!TryFromJson(json, out ParamSet set, out string error)) throw new FormatException(error);
            return set;
        }

        /// <summary>
        /// Reads a JSON object of raw values; never throws. Nulls, nested objects and arrays with non-string items are
        /// skipped (a later <see cref="ParamSet.Sanitize"/> reports them as missing). Duplicate keys: the last wins.
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
            foreach (var prop in obj.Properties())
            {
                var v = FromToken(prop.Value);
                if (v != null) pairs[prop.Name] = v;
            }
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
                        if (item.Type != JTokenType.String) return null;
                        items.Add((string)((JValue)item).Value);
                    }
                    return ParamValue.Strings(items);
                }
                default:
                    return null;
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
