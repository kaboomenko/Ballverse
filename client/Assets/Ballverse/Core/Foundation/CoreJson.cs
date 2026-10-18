using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Ballverse.Core
{
    /// <summary>
    /// One JSON configuration for every Core file format (projects, presets, settings, remote config payloads).
    /// camelCase keys, enums as strings, invariant culture, no type-name handling (no polymorphic deserialization).
    /// </summary>
    public static class CoreJson
    {
        public static readonly JsonSerializerSettings Settings = Create(Formatting.Indented);
        public static readonly JsonSerializerSettings CompactSettings = Create(Formatting.None);

        public static string Serialize<T>(T value, bool indented = true)
        {
            return JsonConvert.SerializeObject(value, indented ? Settings : CompactSettings);
        }

        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, Settings);
        }

        static JsonSerializerSettings Create(Formatting formatting)
        {
            var settings = new JsonSerializerSettings
            {
                Formatting = formatting,
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double,
                NullValueHandling = NullValueHandling.Ignore,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                TypeNameHandling = TypeNameHandling.None,
                ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            };
            settings.Converters.Add(new StringEnumConverter());
            return settings;
        }
    }
}
