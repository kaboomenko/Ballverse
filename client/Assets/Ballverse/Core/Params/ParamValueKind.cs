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
        /// <summary>
        /// Raw JSON that no parameter type can hold (null, a nested object, an array with non-string items). Every
        /// parameter reports it as <see cref="ParamIssueCode.TypeMismatch"/>, so remote presets carrying it are rejected
        /// (spec §2.3). Never produced by sanitizing.
        /// </summary>
        Invalid = 8,
    }
}
