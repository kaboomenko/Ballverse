using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Ballverse.Core.Projects
{
    /// <summary><c>output</c> section (spec §7.4, §3.4, §3.8, §3.10).</summary>
    public sealed class OutputSettings
    {
        /// <summary>Frame aspect; 1.0 supports only "9:16" (spec §3.8).</summary>
        public string Aspect { get; set; } = ProjectLimits.Aspect916;

        /// <summary>How the reel duration is chosen (spec §3.4).</summary>
        [JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
        public DurationMode DurationMode { get; set; } = DurationMode.Auto;

        /// <summary>Reel duration for <see cref="Ballverse.Core.DurationMode.Manual"/>, 1-180 s in microseconds; omitted otherwise.</summary>
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
    }
}
