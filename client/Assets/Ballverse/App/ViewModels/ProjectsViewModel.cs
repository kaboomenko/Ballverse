using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.UI.ViewModels;
using UnityEngine;

namespace Ballverse.App
{
    /// <summary>
    /// Tab «Проекты» (spec §3.9, §4.3): projects (cover from <c>thumbs/cover.png</c>, rendered lazily one per frame) and saved
    /// clips. A project opens in the studio, a clip on the simulation screen with its mode, parameters and seed. Rename /
    /// duplicate / delete go through the studio runtime; deleting a project also drops its rewarded-export credit. Main thread.
    /// </summary>
    public sealed class ProjectsViewModel : ViewModelBase, IProjectsViewModel
    {
        readonly AppContext _ctx;
        readonly StudioViewModel _studio;
        readonly SimulationViewModel _sim;
        readonly List<ProjectCardData> _projects = new List<ProjectCardData>();
        readonly List<ProjectCardData> _clips = new List<ProjectCardData>();
        readonly Dictionary<string, Texture> _covers = new Dictionary<string, Texture>(StringComparer.Ordinal);
        readonly Queue<string> _coverQueue = new Queue<string>();
        readonly HashSet<string> _coverTried = new HashSet<string>(StringComparer.Ordinal);
        bool _dirty = true;

        public ProjectsViewModel(AppContext ctx, StudioViewModel studio, SimulationViewModel sim)
            : base(ctx.Notices.Report)
        {
            _ctx = ctx;
            _studio = studio;
            _sim = sim;
        }

        public IReadOnlyList<ProjectCardData> Projects
        {
            get
            {
                if (_dirty) Refresh();
                return _projects;
            }
        }

        public IReadOnlyList<ProjectCardData> Clips
        {
            get
            {
                if (_dirty) Refresh();
                return _clips;
            }
        }

        public int MaxTitleLength => ProjectLimits.MaxNameLength;

        /// <summary>The library changed (new project / clip, export, rename elsewhere): reload on next read.</summary>
        public void Invalidate()
        {
            _dirty = true;
            Notify();
        }

        /// <summary>Renders one missing cover per frame (main thread).</summary>
        public void Tick(float deltaSeconds)
        {
            if (_coverQueue.Count == 0 || _ctx.AppPaused) return;
            string id = _coverQueue.Dequeue();
            try
            {
                string path = _ctx.Studio.RenderCover(id);
                if (path == null) return;
                Texture tex = _ctx.Textures.Load(path);
                if (tex == null) return;
                ReleaseCover(id);
                _covers[id] = tex;
                _dirty = true;
                Notify();
            }
            catch (Exception e) when (e is IOException || e is InvalidOperationException || e is UnauthorizedAccessException || e is InvalidDataException)
            {
                _ctx.Notices.Report("projects.cover", e);
            }
        }

        void Refresh()
        {
            _dirty = false;
            _projects.Clear();
            _clips.Clear();
            try
            {
                ProjectListResult list = _ctx.Studio.ListProjects();
                foreach (ProjectSummary p in list.Projects)
                {
                    _projects.Add(new ProjectCardData(p.Id, p.Name, p.DurationUs / 1000, ParseLocal(p.UpdatedAt ?? p.CreatedAt), p.ModeId, Cover(p.Id)));
                }
                foreach (ClipEntry c in _ctx.Studio.Clips.ListClips())
                {
                    ClipRecord r = c.Record;
                    _clips.Add(new ProjectCardData(c.Id, r.Name ?? DefaultClipName(r.ModeId), r.DurationTicks * 1000L / SimConstants.TickRate,
                        ParseLocal(r.CreatedAt), r.ModeId, null));
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                _ctx.Notices.Report("projects.list", e);
            }
        }

        Texture Cover(string projectId)
        {
            if (_covers.TryGetValue(projectId, out Texture t)) return t;
            string path = Path.Combine(_ctx.Studio.ProjectDirectory(projectId), ProjectPaths.ThumbsDir, "cover.png");
            if (File.Exists(path))
            {
                Texture tex = _ctx.Textures.Load(path);
                if (tex != null)
                {
                    _covers[projectId] = tex;
                    return tex;
                }
            }
            if (_coverTried.Add(projectId)) _coverQueue.Enqueue(projectId);
            return null;
        }

        void ReleaseCover(string id)
        {
            if (_covers.TryGetValue(id, out Texture old))
            {
                _ctx.Textures.Release(old);
                _covers.Remove(id);
            }
        }

        /// <summary>Low memory: drop the cover textures (reloaded from disk on next read).</summary>
        public void ReleaseCovers()
        {
            foreach (Texture t in _covers.Values) _ctx.Textures.Release(t);
            _covers.Clear();
            _dirty = true;
            Notify();
        }

        static string DefaultClipName(string modeId)
        {
            return ModeCatalog.TryGetById(modeId, out ModeInfo info) ? info.NameEn : modeId ?? "";
        }

        static DateTime ParseLocal(string iso)
        {
            if (iso != null && DateTime.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime utc))
                return utc.ToLocalTime();
            return DateTime.MinValue;
        }

        public OpenTarget Open(ProjectsTab tab, string id)
        {
            if (tab == ProjectsTab.Projects) return _studio.OpenProject(id) ? OpenTarget.Studio : OpenTarget.None;
            ClipEntry clip;
            try
            {
                clip = _ctx.Studio.Clips.GetClip(id);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _ctx.Notices.Report("projects.open_clip", e, AppStrings.ProjectActionFailed);
                return OpenTarget.None;
            }
            if (clip == null || !ClipParams.TryRead(_ctx.Studio.Clips.FullDirectory(clip), _ctx.Modes, out IModeDefinition mode, out ParamSet values, out _))
            {
                _ctx.Notices.Toast(AppStrings.ProjectActionFailed);
                return OpenTarget.None;
            }
            _sim.OpenWith(mode, values);
            return OpenTarget.Simulation;
        }

        public bool Rename(ProjectsTab tab, string id, string title)
        {
            string name = title?.Trim();
            if (string.IsNullOrEmpty(name) || name.Length > MaxTitleLength) return false;
            try
            {
                bool ok = tab == ProjectsTab.Projects ? _ctx.Studio.Rename(id, name) != null : _ctx.Studio.Clips.RenameClip(id, name) != null;
                Invalidate();
                return ok;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is ArgumentException)
            {
                _ctx.Notices.Report("projects.rename", e, AppStrings.ProjectActionFailed);
                return false;
            }
        }

        public void Duplicate(ProjectsTab tab, string id)
        {
            try
            {
                if (tab == ProjectsTab.Projects) _ctx.Studio.Duplicate(id);
                else _ctx.Studio.CreateFromClip(id); // a clip "duplicates" into a new project to edit
                Invalidate();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is ArgumentException)
            {
                _ctx.Notices.Report("projects.duplicate", e, AppStrings.ProjectActionFailed);
            }
        }

        public void Delete(ProjectsTab tab, string id)
        {
            try
            {
                if (tab == ProjectsTab.Projects)
                {
                    if (_studio.CurrentProjectId == id) _studio.Close();
                    if (_ctx.Studio.DeleteProject(id)) _ctx.Services.RewardedExport.RemoveProject(id);
                    ReleaseCover(id);
                }
                else
                {
                    _ctx.Studio.Clips.DeleteClip(id);
                }
                Invalidate();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is ArgumentException)
            {
                _ctx.Notices.Report("projects.delete", e, AppStrings.ProjectActionFailed);
            }
        }

        public OpenTarget ExportAgain(string projectId)
        {
            return _studio.OpenProject(projectId) ? OpenTarget.Studio : OpenTarget.None;
        }
    }
}
