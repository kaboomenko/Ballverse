using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Ballverse.Core.Projects
{
    /// <summary>One element of <c>texts[]</c> (spec §7.4, §3.5).</summary>
    public sealed class TextLayer
    {
        /// <summary>Layer id unique within the project, e.g. "t1" (<see cref="ProjectPaths.IsSafeId"/>).</summary>
        public string Id { get; set; }

        /// <summary>Template id ("which_wins", …) or null for free text.</summary>
        public string Template { get; set; }

        /// <summary>Displayed text (may contain emoji), at most <see cref="ProjectLimits.MaxTextLength"/> chars.</summary>
        public string Text { get; set; } = "";

        /// <summary>Font id, e.g. "manrope_black".</summary>
        public string Font { get; set; } = ProjectDefaults.Font;

        /// <summary>Font size, 12-160 px.</summary>
        public int Size { get; set; } = 72;

        /// <summary>Fill color "#RRGGBB" or "#RRGGBBAA".</summary>
        public string Fill { get; set; } = "#FFFFFF";

        /// <summary>Outline or null for none.</summary>
        public TextStroke Stroke { get; set; }

        /// <summary>Background plate.</summary>
        [JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
        public TextPlate Plate { get; set; } = TextPlate.None;

        /// <summary>Anchor position [x, y], normalized 0..1 of the frame.</summary>
        public double[] Pos { get; set; } = { 0.5, 0.5 };

        /// <summary>Appearance time on the reel timeline, microseconds.</summary>
        public long InUs { get; set; }

        /// <summary>Disappearance time (exclusive), microseconds; greater than <see cref="InUs"/>.</summary>
        public long OutUs { get; set; } = 4000000;

        /// <summary>Entry animation id ("pop", "slide", "typewriter", "bounce"…) or null.</summary>
        public string Anim { get; set; }
    }
}
