using Newtonsoft.Json;

namespace Ballverse.Core.Timeline
{
    /// <summary>
    /// Partial bitrate overrides delivered by Remote Config (spec §3.8: "values live in Remote Config").
    /// Null fields keep the current value. JSON via <see cref="CoreJson"/>, e.g.
    /// <c>{"video1080p60Bps":14000000,"audioBps":160000}</c>. Values must be JSON integers or null; anything else makes
    /// <see cref="FromJson"/> throw <see cref="JsonSerializationException"/>.
    /// </summary>
    public sealed class ExportBitrateOverrides
    {
        [JsonConverter(typeof(StrictInt64Converter))]
        public long? Video1080p60Bps { get; set; }
        [JsonConverter(typeof(StrictInt64Converter))]
        public long? Video1080p30Bps { get; set; }
        [JsonConverter(typeof(StrictInt64Converter))]
        public long? Video720p60Bps { get; set; }
        [JsonConverter(typeof(StrictInt64Converter))]
        public long? Video720p30Bps { get; set; }
        [JsonConverter(typeof(StrictInt64Converter))]
        public long? AudioBps { get; set; }

        /// <summary>Parses a payload; returns null for the JSON literal <c>null</c>.</summary>
        public static ExportBitrateOverrides FromJson(string json)
        {
            return CoreJson.Deserialize<ExportBitrateOverrides>(json);
        }

        public string ToJson()
        {
            return CoreJson.Serialize(this, false);
        }
    }
}
