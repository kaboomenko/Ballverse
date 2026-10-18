namespace Ballverse.Core.Params
{
    /// <summary>
    /// Settings panel group (spec §2.2). Declaration order is the panel order and the first sort key of a schema.
    /// <see cref="Basic"/> is shown expanded; the others are collapsible.
    /// </summary>
    public enum ParamGroup
    {
        Basic = 0,
        Physics = 1,
        Look = 2,
        Sound = 3,
        Director = 4,
        /// <summary>Mode-specific parameters (spec §2.9 "Параметры режима").</summary>
        Mode = 5,
    }
}
