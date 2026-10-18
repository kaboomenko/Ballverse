using System;
using System.Collections.Generic;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Loads sound packs shipped as content data (spec §7.6 "data instead of code", §7.8) through
    /// <see cref="CoreJson"/>. Never throws on bad input: problems are returned as error strings.
    /// </summary>
    public static class SoundPackJson
    {
        /// <summary>Upper bound for the JSON text, characters.</summary>
        public const int MaxJsonLength = 256 * 1024;

        /// <summary>Parses and validates a pack. On failure <paramref name="pack"/> is null and errors are non-empty.</summary>
        public static bool TryParse(string json, out SoundPack pack, out IReadOnlyList<string> errors)
        {
            pack = null;
            var list = new List<string>();
            errors = list;
            if (string.IsNullOrWhiteSpace(json))
            {
                list.Add("empty input");
                return false;
            }
            if (json.Length > MaxJsonLength)
            {
                list.Add("input exceeds " + MaxJsonLength + " characters");
                return false;
            }

            SoundPack parsed;
            try
            {
                parsed = CoreJson.Deserialize<SoundPack>(json);
            }
            catch (Exception ex)
            {
                // Parse boundary for remote data: any reader/converter failure becomes an error, never an exception.
                list.Add("malformed JSON: " + ex.Message);
                return false;
            }
            if (parsed == null)
            {
                list.Add("JSON is not an object");
                return false;
            }

            list.AddRange(parsed.Validate());
            if (list.Count > 0) return false;
            pack = parsed;
            return true;
        }

        /// <summary>Serializes a pack with <see cref="CoreJson"/>.</summary>
        public static string Serialize(SoundPack pack, bool indented = true)
        {
            return CoreJson.Serialize(pack, indented);
        }
    }
}
