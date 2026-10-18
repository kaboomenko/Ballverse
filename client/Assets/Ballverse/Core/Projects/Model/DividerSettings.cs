namespace Ballverse.Core.Projects
{
    /// <summary><c>layout.divider</c> (spec §3.3 "Разделитель").</summary>
    public sealed class DividerSettings
    {
        /// <summary>Thickness, 0-16 px.</summary>
        public int Px { get; set; } = 6;

        /// <summary>Color "#RRGGBB" or "#RRGGBBAA".</summary>
        public string Color { get; set; } = ProjectDefaults.DividerColor;

        /// <summary>"Glass" divider (spec §4.5).</summary>
        public bool Glass { get; set; } = true;
    }
}
