using System.Collections.Generic;

namespace Ballverse.Core.Projects
{
    /// <summary>Result of <see cref="ProjectValidator.Sanitize"/>: a repaired copy plus what was changed.</summary>
    public sealed class SanitizeResult
    {
        /// <summary>Sanitized deep copy; the input document is never modified.</summary>
        public ProjectDocument Document { get; }
        /// <summary>Issues in document order.</summary>
        public IReadOnlyList<ValidationIssue> Issues { get; }

        public SanitizeResult(ProjectDocument document, IReadOnlyList<ValidationIssue> issues)
        {
            Document = document;
            Issues = issues;
        }

        /// <summary>True when nothing had to be changed.</summary>
        public bool IsClean => Issues.Count == 0;

        /// <summary>True when an unrepairable problem was found (see <see cref="IssueSeverity.Error"/>).</summary>
        public bool HasErrors
        {
            get
            {
                for (int i = 0; i < Issues.Count; i++)
                {
                    if (Issues[i].Severity == IssueSeverity.Error) return true;
                }
                return false;
            }
        }
    }
}
