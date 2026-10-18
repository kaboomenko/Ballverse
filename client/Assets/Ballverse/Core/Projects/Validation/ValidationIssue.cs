namespace Ballverse.Core.Projects
{
    /// <summary>One finding of <see cref="ProjectValidator.Sanitize"/> or <see cref="ProjectRepository"/>.</summary>
    public sealed class ValidationIssue
    {
        /// <summary>JSON-style path of the value, e.g. "layout.ratio" or "texts[2].fill"; for repository issues, a store path.</summary>
        public string Path { get; }
        public IssueCode Code { get; }
        public IssueSeverity Severity { get; }
        /// <summary>Developer-facing description (not localized).</summary>
        public string Message { get; }

        public ValidationIssue(string path, IssueCode code, IssueSeverity severity, string message)
        {
            Path = path;
            Code = code;
            Severity = severity;
            Message = message;
        }

        public override string ToString()
        {
            return Severity + " " + Code + " at " + Path + ": " + Message;
        }
    }
}
