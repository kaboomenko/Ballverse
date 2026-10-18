using System.Collections.Generic;

namespace Ballverse.Core.Integration
{
    /// <summary>
    /// A value built from Remote Config data plus what the adapter could not use (one message per rejected entry, in input
    /// order). The Config module has already validated the data; these messages only appear when the consumer module's rules
    /// are stricter or combine fields (e.g. a delivered density bound that conflicts with a built-in one).
    /// </summary>
    public sealed class AdapterResult<T>
    {
        static readonly IReadOnlyList<string> s_none = new string[0];

        public T Value { get; }
        public IReadOnlyList<string> Issues { get; }
        public bool HasIssues => Issues.Count > 0;

        public AdapterResult(T value, IReadOnlyList<string> issues)
        {
            Value = value;
            Issues = issues == null || issues.Count == 0 ? s_none : issues;
        }
    }
}
