using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ballverse.UI.ViewModels
{
    public enum ProjectsTab
    {
        Projects = 0,
        Clips = 1,
    }

    /// <summary>Where opening an item leads.</summary>
    public enum OpenTarget
    {
        None = 0,
        Studio = 1,
        Simulation = 2,
    }

    public sealed class ProjectCardData
    {
        public string Id { get; }
        /// <summary>User title (literal).</summary>
        public string Title { get; }
        public long DurationMs { get; }
        public DateTime ModifiedLocal { get; }
        public string ModeId { get; }
        public Texture Cover { get; }

        public ProjectCardData(string id, string title, long durationMs, DateTime modifiedLocal, string modeId, Texture cover)
        {
            Id = id;
            Title = title ?? "";
            DurationMs = durationMs;
            ModifiedLocal = modifiedLocal;
            ModeId = modeId;
            Cover = cover;
        }
    }

    /// <summary>Tab "Проекты" (spec §3.9, §4.3).</summary>
    public interface IProjectsViewModel : IObservableViewModel
    {
        IReadOnlyList<ProjectCardData> Projects { get; }
        IReadOnlyList<ProjectCardData> Clips { get; }
        int MaxTitleLength { get; }
        OpenTarget Open(ProjectsTab tab, string id);
        bool Rename(ProjectsTab tab, string id, string title);
        void Duplicate(ProjectsTab tab, string id);
        void Delete(ProjectsTab tab, string id);
        /// <summary>"Экспортировать снова": opens the project in the studio with the export sheet.</summary>
        OpenTarget ExportAgain(string projectId);
    }
}
