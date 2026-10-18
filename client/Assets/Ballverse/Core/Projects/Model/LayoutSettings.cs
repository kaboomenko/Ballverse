using Newtonsoft.Json;

namespace Ballverse.Core.Projects
{
    /// <summary><c>layout</c> section of project.json (spec §7.4, §3.3).</summary>
    public sealed class LayoutSettings
    {
        /// <summary>Layout code L1-L5, serialized as "L1".."L5"; an unknown name is reset by the validator.</summary>
        [JsonConverter(typeof(TolerantEnumConverter))]
        public LayoutType Type { get; set; } = LayoutType.L1;

        /// <summary>Swap slots (L1' etc.).</summary>
        public bool Swap { get; set; }

        /// <summary>Share of the first slot for L1/L2, 0.30-0.70.</summary>
        public double Ratio { get; set; } = 0.5;

        /// <summary>Divider between slots (L1/L2).</summary>
        public DividerSettings Divider { get; set; } = new DividerSettings();

        /// <summary>Slot corner radius, 0-48 px.</summary>
        public int CornerRadius { get; set; }

        /// <summary>Outer padding, 0-48 px.</summary>
        public int Padding { get; set; }

        /// <summary>Picture-in-picture window for L3/L5, otherwise usually null.</summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Include)]
        public PipSettings Pip { get; set; }
    }
}
