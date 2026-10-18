namespace Ballverse.Core.Projects
{
    /// <summary>Card data for the project list (spec §3.9 "Список проектов": name, duration, modified date, mode icon).</summary>
    public sealed class ProjectSummary
    {
        public string Id { get; }
        public string Name { get; }
        public string CreatedAt { get; }
        public string UpdatedAt { get; }
        public string ModeId { get; }
        /// <summary>Reel duration per <see cref="ProjectTiming.ReelDurationUs"/>.</summary>
        public long DurationUs { get; }
        /// <summary>Whether the card was read from the backup file.</summary>
        public LoadSource Source { get; }

        public ProjectSummary(string id, string name, string createdAt, string updatedAt, string modeId, long durationUs, LoadSource source)
        {
            Id = id;
            Name = name;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
            ModeId = modeId;
            DurationUs = durationUs;
            Source = source;
        }
    }
}
