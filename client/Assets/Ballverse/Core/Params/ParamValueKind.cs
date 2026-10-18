namespace Ballverse.Core.Params
{
    /// <summary>
    /// Storage kind of a <see cref="ParamValue"/>. Raw values read from JSON may have a kind that does not match the
    /// parameter type (for example a colour arrives as <see cref="String"/>); <see cref="ParamSet.Sanitize"/> converts them.
    /// </summary>
    public enum ParamValueKind
    {
        Float = 0,
        Int = 1,
        Bool = 2,
        /// <summary>Enum / template option id, or any raw JSON string.</summary>
        String = 3,
        Color = 4,
        ColorList = 5,
        StringList = 6,
        Seed = 7,
    }
}
