using System.Collections.Generic;

namespace Ballverse.Core.Projects
{
    /// <summary>Result of <see cref="ProjectRepository.List"/>.</summary>
    public sealed class ProjectListResult
    {
        /// <summary>Loadable projects, newest <c>updatedAt</c> first, ties by id (ordinal).</summary>
        public IReadOnlyList<ProjectSummary> Projects { get; }
        /// <summary>One issue per skipped folder (path = folder path), in folder-name order.</summary>
        public IReadOnlyList<ValidationIssue> Skipped { get; }

        public ProjectListResult(IReadOnlyList<ProjectSummary> projects, IReadOnlyList<ValidationIssue> skipped)
        {
            Projects = projects;
            Skipped = skipped;
        }
    }
}
