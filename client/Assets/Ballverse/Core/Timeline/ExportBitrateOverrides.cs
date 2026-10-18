namespace Ballverse.Core.Timeline
{
    /// <summary>
    /// Partial bitrate overrides delivered by Remote Config (spec §3.8: "values live in Remote Config").
    /// Null fields keep the current value. JSON via <see cref="CoreJson"/>, e.g.
    /// <c>{"video1080p60Bps":14000000,"audioBps":160000}</c>.
    /// </summary>
    public sealed class ExportBitrateOverrides
    {
        public long? Video1080p60Bps { get; set; }
        public long? Video1080p30Bps { get; set; }
        public long? Video720p60Bps { get; set; }
        public long? Video720p30Bps { get; set; }
        public long? AudioBps { get; set; }

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
