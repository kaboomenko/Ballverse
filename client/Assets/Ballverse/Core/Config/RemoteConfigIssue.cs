namespace Ballverse.Core.Config
{
    /// <summary>One diagnostic produced by <see cref="RemoteConfigParser"/>. Meant for logs and analytics, never shown to users.</summary>
    public sealed class RemoteConfigIssue
    {
        /// <summary>Remote Config key the issue belongs to.</summary>
        public string Key { get; }
        public RemoteConfigIssueSeverity Severity { get; }
        public RemoteConfigIssueKind Kind { get; }
        /// <summary>Human-readable detail in English.</summary>
        public string Message { get; }

        public RemoteConfigIssue(string key, RemoteConfigIssueSeverity severity, RemoteConfigIssueKind kind, string message)
        {
            Key = key ?? string.Empty;
            Severity = severity;
            Kind = kind;
            Message = message ?? string.Empty;
        }

        public override string ToString()
        {
            return Severity + " " + Kind + " [" + Key + "]: " + Message;
        }
    }
}
