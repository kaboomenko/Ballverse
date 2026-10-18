namespace Ballverse.Core.Projects
{
    /// <summary>How bad a <see cref="ValidationIssue"/> is.</summary>
    public enum IssueSeverity
    {
        /// <summary>The value was replaced or clamped; the sanitized document is usable.</summary>
        Warning = 0,
        /// <summary>The value cannot be repaired without outside context; the document must not be used or saved.</summary>
        Error = 1,
    }
}
