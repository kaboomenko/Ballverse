namespace Ballverse.Core.Params
{
    /// <summary>Parameter definition types (spec §7.6 "Схема параметров").</summary>
    public enum ParamType
    {
        Float = 0,
        Int = 1,
        Bool = 2,
        Enum = 3,
        Color = 4,
        ColorList = 5,
        StringList = 6,
        Seed = 7,
        /// <summary>Choice of an arena / track / construction template (spec §7.6).</summary>
        Template = 8,
    }
}
