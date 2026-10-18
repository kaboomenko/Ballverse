using Newtonsoft.Json.Linq;

namespace Ballverse.Core.Params.Presets
{
    /// <summary>JSON shape of a preset (serialized with <see cref="CoreJson"/>; property order is the file order).</summary>
    internal sealed class PresetDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string NameKey { get; set; }
        public string ModeId { get; set; }
        public bool? IsNew { get; set; }
        /// <summary>Object of raw values (see <see cref="ParamJson"/>); a JToken so a wrong shape is a validation error.</summary>
        public JToken Values { get; set; }
    }
}
