namespace Ballverse.Core.Config
{
    /// <summary>How much a <see cref="RemoteConfigIssue"/> affected the parsed values.</summary>
    public enum RemoteConfigIssueSeverity
    {
        /// <summary>No effect on values (unknown key, empty value).</summary>
        Info = 0,
        /// <summary>The value was accepted but adjusted: clamped, an entry dropped, a list completed, or it is suspicious.</summary>
        Warning = 1,
        /// <summary>The whole value (or one whole map entry) was rejected; the key (or that entry) keeps its default.</summary>
        Error = 2,
    }
}
