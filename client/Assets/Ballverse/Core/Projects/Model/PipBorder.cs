namespace Ballverse.Core.Projects
{
    /// <summary><c>layout.pip.border</c>: frame of the PiP window (spec §3.3 L3 "рамка"). Not in the §7.4 example; added at schema 1.</summary>
    public sealed class PipBorder
    {
        /// <summary>Frame width, 0-<see cref="ProjectLimits.MaxPipBorderPx"/> px (0 = no frame; the range is this module's choice).</summary>
        public int Px { get; set; }

        /// <summary>Frame color "#RRGGBB" or "#RRGGBBAA".</summary>
        public string Color { get; set; } = ProjectDefaults.PipBorderColor;
    }
}
