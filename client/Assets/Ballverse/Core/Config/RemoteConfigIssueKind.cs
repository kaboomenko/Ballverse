namespace Ballverse.Core.Config
{
    /// <summary>What went wrong with one Remote Config value (spec §9.3: every key falls back to its built-in default).</summary>
    public enum RemoteConfigIssueKind
    {
        /// <summary>Key is not in the spec §9.3 table; ignored.</summary>
        UnknownKey = 0,
        /// <summary>Value is null, empty or whitespace; treated as "not set".</summary>
        EmptyValue = 1,
        /// <summary>Value is not well-formed JSON.</summary>
        InvalidJson = 2,
        /// <summary>Value has the wrong type (e.g. text for a number, object for a list, fractional integer).</summary>
        InvalidType = 3,
        /// <summary>Number outside its range; clamped.</summary>
        OutOfRange = 4,
        /// <summary>One element / field of a list or map is invalid; that element is ignored.</summary>
        InvalidEntry = 5,
        /// <summary>Mode code or id not in <see cref="ModeCatalog"/>; ignored.</summary>
        UnknownMode = 6,
        /// <summary>Repeated mode, id, model, platform or language; the first occurrence wins.</summary>
        DuplicateEntry = 7,
        /// <summary>
        /// <c>free_modes</c> did not list every default free mode; they stay free anyway (spec §9.3 extension only, §5.1).
        /// </summary>
        NarrowedFreeModes = 8,
        /// <summary><c>gallery_order</c> did not list every released mode; missing ones appended in catalog order.</summary>
        IncompleteGalleryOrder = 9,
        /// <summary>Unexpected failure while parsing; the key keeps its default.</summary>
        InternalError = 10,
        /// <summary>
        /// A non-empty list (mode list) or map entry (safe-zone platform, text-template language) had no valid element; the
        /// default (or current) value is kept instead of an empty one. Only an explicit empty array clears an entry.
        /// </summary>
        NoValidEntries = 11,
        /// <summary>
        /// More issues of this key were suppressed (Info/Warning and Errors are each capped per key); the summary carries the
        /// highest suppressed severity.
        /// </summary>
        IssuesSuppressed = 12,
        /// <summary>
        /// A catalog mode that is not in this build's release (<see cref="ModeInfo.InRelease"/>) was listed in <c>free_modes</c>
        /// or <c>gallery_order</c>; ignored (spec §9.3: "Режим из списка, которого нет в сборке, игнорируется").
        /// </summary>
        UnreleasedMode = 13,
    }
}
