using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Ballverse.Core.Projects
{
    /// <summary><c>output</c> section (spec §7.4, §3.4, §3.8, §3.10).</summary>
    public sealed class OutputSettings
    {
        /// <summary>Frame aspect; 1.0 supports only "9:16" (spec §3.8).</summary>
        public string Aspect { get; set; } = ProjectLimits.Aspect916;

        /// <summary>How the reel duration is chosen (spec §3.4).</summary>
        [JsonConverter(typeof(TolerantEnumConverter), typeof(CamelCaseNamingStrategy))]
        public DurationMode DurationMode { get; set; } = DurationMode.Auto;

        /// <summary>
        /// Content duration for <see cref="Ballverse.Core.DurationMode.Manual"/>, microseconds, without the 0.1 s cover lead-in:
        /// the reel is this plus <see cref="ProjectTiming.CoverLeadInUs"/> when <c>cover.putFirst</c> is on (spec §3.4). The
        /// validator keeps it in 1-180 s, or 1-179.9 s in Manual mode with the lead-in, so the reel stays within 180 s.
        /// Null when never set; kept (1-180 s) in the other modes so switching back to Manual restores it.
        /// </summary>
        public long? ManualDurationUs { get; set; }

        /// <summary>
        /// User's opt-in "keep the watermark" toggle (spec §3.10). Honoured only with the NO_WATERMARK entitlement and
        /// resolved at export time by <see cref="WatermarkPolicy.Resolve"/>; never "baked" into the project. Default false,
        /// so entitled users get no watermark by default, also when re-exporting projects made before the purchase.
        /// </summary>
        public bool Watermark { get; set; }

        /// <summary>Last chosen export height (720 or 1080), or null for the default (spec §3.8).</summary>
        public int? Resolution { get; set; }

        /// <summary>Last chosen export frame rate (30 or 60), or null for the tier default (spec §3.8).</summary>
        public int? Fps { get; set; }

        /// <summary>Cover frame (spec §3.4 "Обложка"); added at schema 1.</summary>
        public CoverSettings Cover { get; set; } = new CoverSettings();
    }
}
