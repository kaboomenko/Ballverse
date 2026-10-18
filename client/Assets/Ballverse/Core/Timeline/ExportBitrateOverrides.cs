namespace Ballverse.Core.Timeline
{
    /// <summary>
    /// Partial bitrate overrides (spec §3.8: "values live in Remote Config"), applied with
    /// <see cref="ExportBitrateTable.WithOverrides"/>. Null fields keep the current value. Typed only: the Remote Config key
    /// <c>export_bitrates</c> is parsed by the Config module (kbps per resolution and fps), and
    /// <c>Ballverse.Core.Integration.RemoteConfigAdapters</c> converts it into this type.
    /// </summary>
    public sealed class ExportBitrateOverrides
    {
        public long? Video1080p60Bps { get; set; }
        public long? Video1080p30Bps { get; set; }
        public long? Video720p60Bps { get; set; }
        public long? Video720p30Bps { get; set; }
        public long? AudioBps { get; set; }
    }
}
