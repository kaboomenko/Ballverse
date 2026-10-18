namespace Ballverse.Core.Projects
{
    /// <summary>
    /// What fills the margins of a slot in <see cref="SlotFill.Fit"/> mode (spec §3.3 "Фон полей (Fit): цвет, размытое это же
    /// видео, цвет темы"). Serialized camelCase: "blur", "color", "theme".
    /// </summary>
    public enum FitBackgroundKind
    {
        /// <summary>Blurred copy of the slot's own picture (default).</summary>
        Blur = 0,
        /// <summary>Solid <see cref="FitBackground.Color"/>.</summary>
        Color = 1,
        /// <summary>Background color of the current visual theme.</summary>
        Theme = 2,
    }
}
