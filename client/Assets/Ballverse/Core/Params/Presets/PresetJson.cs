using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Params.Presets
{
    /// <summary>
    /// Preset JSON (spec §2.3 "Пресеты описаны в JSON", §7.6) via <see cref="CoreJson"/>:
    /// <c>{"id","name"?,"nameKey"?,"modeId","isNew"?,"values":{...}}</c>, values encoded as in <see cref="ParamJson"/>.
    /// The source is not stored; it is given by the loading channel. <c>isNew</c> defaults to true for remote presets.
    /// </summary>
    public static class PresetJson
    {
        static readonly JsonSerializer s_serializer = JsonSerializer.Create(CoreJson.Settings);

        /// <summary>Preset as JSON; values in <paramref name="schema"/> order when given, else ordinal id order.</summary>
        public static string Serialize(Preset preset, ParamSchema schema = null, bool indented = true)
        {
            if (preset == null) throw new ArgumentNullException(nameof(preset));
            return CoreJson.Serialize(ToDto(preset, schema), indented);
        }

        /// <summary>JSON array of presets.</summary>
        public static string SerializeList(IEnumerable<Preset> presets, ParamSchema schema = null, bool indented = true)
        {
            if (presets == null) throw new ArgumentNullException(nameof(presets));
            var list = new List<PresetDto>();
            foreach (var p in presets) list.Add(ToDto(p ?? throw new ArgumentException("Null preset", nameof(presets)), schema));
            return CoreJson.Serialize(list, indented);
        }

        /// <summary>Parses one preset; throws <see cref="PresetException"/> on malformed or invalid data.</summary>
        public static Preset Parse(string json, PresetSource source)
        {
            if (!TryParse(json, source, out Preset p, out string error)) throw new PresetException(error);
            return p;
        }

        /// <summary>Parses one preset; never throws.</summary>
        public static bool TryParse(string json, PresetSource source, out Preset preset, out string error)
        {
            preset = null;
            if (!TryParseToken(json, out JToken token, out error)) return false;
            return TryFromToken(token, source, out preset, out error);
        }

        /// <summary>Parses a JSON array of presets (e.g. a mode's built-in presets); throws on the first invalid one.</summary>
        public static IReadOnlyList<Preset> ParseList(string json, PresetSource source)
        {
            if (!TryParseToken(json, out JToken token, out string error)) throw new PresetException(error);
            if (!(token is JArray array)) throw new PresetException("Expected a JSON array of presets");
            var result = new List<Preset>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                if (!TryFromToken(array[i], source, out Preset p, out error)) throw new PresetException("Preset #" + i + ": " + error);
                if (!ids.Add(p.Id)) throw new PresetException("Preset #" + i + ": duplicate id '" + p.Id + "'");
                result.Add(p);
            }
            return result;
        }

        internal static bool TryParseToken(string json, out JToken token, out string error)
        {
            token = null;
            error = null;
            if (json == null)
            {
                error = "JSON is null";
                return false;
            }
            try
            {
                token = JsonConvert.DeserializeObject<JToken>(json, CoreJson.Settings);
                if (token == null)
                {
                    error = "JSON is empty";
                    return false;
                }
                return true;
            }
            catch (JsonException e)
            {
                error = "Malformed JSON: " + e.Message;
                return false;
            }
        }

        internal static bool TryFromToken(JToken token, PresetSource source, out Preset preset, out string error)
        {
            preset = null;
            if (!(token is JObject obj))
            {
                error = "Expected a JSON object";
                return false;
            }
            PresetDto dto;
            try
            {
                dto = obj.ToObject<PresetDto>(s_serializer);
            }
            catch (Exception e) when (e is JsonException || e is FormatException || e is InvalidCastException || e is ArgumentException)
            {
                error = "Invalid preset fields: " + e.Message;
                return false;
            }
            ParamSet values = ParamSet.Empty;
            if (dto.Values != null && dto.Values.Type != JTokenType.Null)
            {
                if (!(dto.Values is JObject valuesObj))
                {
                    error = "Preset " + TextRules.Quote(dto.Id) + ": 'values' must be an object";
                    return false;
                }
                values = ParamJson.FromJObject(valuesObj);
            }
            bool isNew = dto.IsNew ?? source == PresetSource.Remote;
            return Preset.TryCreate(dto.Id, dto.ModeId, values, source, dto.Name, dto.NameKey, isNew, out preset, out error);
        }

        internal static PresetDto ToDto(Preset p, ParamSchema schema)
        {
            return new PresetDto
            {
                Id = p.Id,
                Name = p.Name,
                NameKey = p.NameKey,
                ModeId = p.ModeId,
                IsNew = p.IsNew ? true : p.Source == PresetSource.Remote ? false : (bool?)null,
                Values = ParamJson.ToJObject(p.Values, schema != null && !schema.IsLayer ? schema : null),
            };
        }
    }
}
