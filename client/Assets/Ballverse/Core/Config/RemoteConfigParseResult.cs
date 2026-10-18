using System.Collections.Generic;

namespace Ballverse.Core.Config
{
    /// <summary>Output of <see cref="RemoteConfigParser.Parse"/>: the typed snapshot plus diagnostics. Deconstructs as <c>var (values, issues) = …</c>.</summary>
    public sealed class RemoteConfigParseResult
    {
        /// <summary>Never null; every key that failed holds its default.</summary>
        public RemoteConfigValues Values { get; }
        /// <summary>Diagnostics in spec §9.3 key order, then unknown keys in ordinal order. Deterministic for the same input.</summary>
        public IReadOnlyList<RemoteConfigIssue> Issues { get; }

        public RemoteConfigParseResult(RemoteConfigValues values, IReadOnlyList<RemoteConfigIssue> issues)
        {
            Values = values;
            Issues = issues;
        }

        public void Deconstruct(out RemoteConfigValues values, out IReadOnlyList<RemoteConfigIssue> issues)
        {
            values = Values;
            issues = Issues;
        }
    }
}
