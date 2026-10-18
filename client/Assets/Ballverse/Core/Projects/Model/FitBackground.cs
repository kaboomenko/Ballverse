using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Ballverse.Core.Projects
{
    /// <summary>
    /// <c>clip.fitBackground</c> / <c>userVideo.fitBackground</c>: margin fill of a slot in <see cref="SlotFill.Fit"/> mode
    /// (spec §3.3 "Фон полей"). Not in the §7.4 example; added at schema 1 so re-export reproduces the reel (spec §3.9).
    /// Ignored in <see cref="SlotFill.Cover"/> mode.
    /// </summary>
    public sealed class FitBackground
    {
        /// <summary>Margin fill kind.</summary>
        [JsonConverter(typeof(TolerantEnumConverter), typeof(CamelCaseNamingStrategy))]
        public FitBackgroundKind Kind { get; set; } = FitBackgroundKind.Blur;

        /// <summary>Color "#RRGGBB" or "#RRGGBBAA" used by <see cref="FitBackgroundKind.Color"/> (kept for the other kinds).</summary>
        public string Color { get; set; } = ProjectDefaults.FitBackgroundColor;
    }
}
