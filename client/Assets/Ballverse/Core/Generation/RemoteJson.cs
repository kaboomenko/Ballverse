using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Generation
{
    /// <summary>
    /// Tolerant parsing of keyed Remote Config objects (spec §9.3 <c>generation_weights</c>, <c>generation_budget_by_tier</c>):
    /// the top level must be a JSON object, and every entry is converted on its own so one broken entry never discards the others.
    /// </summary>
    internal static class RemoteJson
    {
        static readonly JsonSerializer s_serializer = JsonSerializer.Create(CoreJson.Settings);

        /// <summary>
        /// The entries of a JSON object, or null when the payload is empty, <c>null</c>, malformed or not an object
        /// (the last two add a "malformed JSON" warning).
        /// </summary>
        public static Dictionary<string, JToken> ParseObject(string json, string configKey, List<string> warnings)
        {
            JToken root;
            try
            {
                root = CoreJson.Deserialize<JToken>(json);
            }
            catch (JsonException e)
            {
                warnings.Add(configKey + ": malformed JSON (" + e.Message + ")");
                return null;
            }
            if (root == null || root.Type == JTokenType.Null) return null;
            if (!(root is JObject obj))
            {
                warnings.Add(configKey + ": malformed JSON (expected an object, got " + root.Type + ")");
                return null;
            }
            var entries = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (JProperty p in obj.Properties()) entries[p.Name] = p.Value;
            return entries;
        }

        /// <summary>
        /// Converts one entry; on a null, non-object or wrongly typed entry adds a warning for <paramref name="path"/>.
        /// Fields named in <paramref name="integerFields"/> (matched case-insensitively, like the deserializer) must be JSON
        /// integers or null: a fractional number or a quoted number is rejected instead of being silently rounded or parsed.
        /// </summary>
        public static bool TryConvert<T>(JToken token, string path, List<string> warnings, out T value, params string[] integerFields)
            where T : class
        {
            value = null;
            if (token == null || token.Type == JTokenType.Null)
            {
                warnings.Add(path + ": null entry");
                return false;
            }
            if (token.Type != JTokenType.Object)
            {
                warnings.Add(path + ": entry must be an object, got " + token.Type);
                return false;
            }
            foreach (JProperty p in ((JObject)token).Properties())
            {
                if (p.Value.Type == JTokenType.Integer || p.Value.Type == JTokenType.Null) continue;
                foreach (string field in integerFields)
                {
                    if (!string.Equals(p.Name, field, StringComparison.OrdinalIgnoreCase)) continue;
                    warnings.Add(path + ": invalid entry (" + p.Name + " must be an integer, got " + p.Value.Type + ")");
                    return false;
                }
            }
            try
            {
                value = token.ToObject<T>(s_serializer);
            }
            catch (JsonException e)
            {
                warnings.Add(path + ": invalid entry (" + e.Message + ")");
                return false;
            }
            catch (FormatException e)
            {
                warnings.Add(path + ": invalid entry (" + e.Message + ")");
                return false;
            }
            catch (InvalidCastException e)
            {
                warnings.Add(path + ": invalid entry (" + e.Message + ")");
                return false;
            }
            catch (OverflowException e)
            {
                warnings.Add(path + ": invalid entry (" + e.Message + ")");
                return false;
            }
            if (value == null)
            {
                warnings.Add(path + ": null entry");
                return false;
            }
            return true;
        }
    }
}
