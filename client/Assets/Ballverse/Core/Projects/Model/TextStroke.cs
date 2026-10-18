namespace Ballverse.Core.Projects
{
    /// <summary><c>texts[].stroke</c>: outline 0-8 px (spec §3.5).</summary>
    public sealed class TextStroke
    {
        /// <summary>Outline width, 0-8 px.</summary>
        public int Px { get; set; }

        /// <summary>Outline color "#RRGGBB" or "#RRGGBBAA".</summary>
        public string Color { get; set; } = "#000000";
    }
}
