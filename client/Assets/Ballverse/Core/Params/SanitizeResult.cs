using System.Collections.Generic;

namespace Ballverse.Core.Params
{
    /// <summary>Output of <see cref="ParamSet.Sanitize"/>: the valid set and what had to change.</summary>
    public sealed class SanitizeResult
    {
        public ParamSet Values { get; }
        /// <summary>Changes in schema order, then dropped unknown ids in ordinal order.</summary>
        public IReadOnlyList<ParamIssue> Issues { get; }
        public bool HasIssues => Issues.Count > 0;

        public SanitizeResult(ParamSet values, IReadOnlyList<ParamIssue> issues)
        {
            Values = values;
            Issues = issues;
        }
    }
}
