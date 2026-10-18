using Newtonsoft.Json;

namespace Ballverse.Core.Projects
{
    /// <summary>One element of <c>exports[]</c>: export history (spec §7.4, §3.8, §3.9).</summary>
    public sealed class ExportRecord
    {
        /// <summary>ISO-8601 time the export finished.</summary>
        public string At { get; set; }

        /// <summary>Output height: 720 or 1080 (<see cref="ExportResolution"/>).</summary>
        public int Res { get; set; } = (int)ExportResolution.P1080;

        /// <summary>Frame rate: 30 or 60.</summary>
        public int Fps { get; set; } = 60;

        /// <summary>Wall time the export took, milliseconds.</summary>
        public long Ms { get; set; }

        /// <summary>Exported file relative to the project folder, or null (saved to the gallery only).</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public string File { get; set; }
    }
}
