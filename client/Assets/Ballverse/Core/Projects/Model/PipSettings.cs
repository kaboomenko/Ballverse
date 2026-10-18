using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Ballverse.Core.Projects
{
    /// <summary><c>layout.pip</c>: floating window for L3/L5 (spec §3.3).</summary>
    public sealed class PipSettings
    {
        /// <summary>Window shape.</summary>
        [JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
        public PipShape Shape { get; set; } = PipShape.RoundedRect;

        /// <summary>Window width as a fraction of the frame width, 0.20-0.60.</summary>
        public double SizeFraction { get; set; } = 0.35;

        /// <summary>Window center X, normalized 0..1.</summary>
        public double X { get; set; } = 0.75;

        /// <summary>Window center Y, normalized 0..1.</summary>
        public double Y { get; set; } = 0.25;
    }
}
