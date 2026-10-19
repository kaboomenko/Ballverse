using System;
using System.Collections.Generic;
using Ballverse.Core.Integration;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.Timeline;

namespace Ballverse.Core.Studio
{
    /// <summary>Everything decided when the user taps "Export" (spec §3.8 step 1, §7.5 "Снимок проекта"), see <see cref="ExportJob.CreateState"/>.</summary>
    public sealed class ExportJobRequest
    {
        /// <summary>Folder name of the job in the export store (<see cref="ProjectPaths.IsSafeId"/>).</summary>
        public string JobId { get; set; }
        /// <summary>The project as it is now; it is cloned (later edits do not touch the export).</summary>
        public ProjectDocument Project { get; set; }
        /// <summary>Resolution, fps and bitrates (<see cref="ProjectComposition.ResolveExportSettings"/>).</summary>
        public ExportSettings Settings { get; set; }
        /// <summary><see cref="WatermarkPolicy.Resolve"/> at the moment of the export.</summary>
        public bool Watermark { get; set; } = true;
        /// <summary>Normalized platform masks (Config <c>platform_safe_zones</c>); null = the built-in defaults.</summary>
        public IReadOnlyList<FloatRect> MaskRects { get; set; }
        /// <summary>Normalized boxes of all text layers measured at the 1080×1920 reference (see <see cref="FrameRequest.TextBounds"/>).</summary>
        public IReadOnlyList<FloatRect> TextBounds { get; set; }
        /// <summary>Decoded user-video frame size (reader output); 0×0 without a video.</summary>
        public IntVec2 VideoSize { get; set; }
        /// <summary>Box the reader was opened with (<see cref="VideoDecodeSizing.Compute"/>).</summary>
        public IntVec2 VideoBox { get; set; }
        /// <summary>Tick count of the clip's render stream.</summary>
        public int StreamTicks { get; set; }
        public float LogoAspect { get; set; } = WatermarkLayout.DefaultLogoAspect;
        public bool SaveToGallery { get; set; } = true;
        public bool KeepInProject { get; set; }
        /// <summary>ISO-8601 time of the export (file name, history).</summary>
        public string NowIso { get; set; }
    }

    /// <summary>
    /// A planned export (spec §7.5): the immutable <see cref="ExportResumeState"/> snapshot plus everything derived from it —
    /// the compiled timeline, the export settings, the frame planner and the segment table. A resumed export rebuilds exactly
    /// the same job from the saved state, so the frames of the remaining segments match the finished ones (no seam). Pure.
    /// </summary>
    public sealed class ExportJob
    {
        public ExportResumeState State { get; }
        public StudioTimeline Timeline { get; }
        public ExportSettings Settings { get; }
        /// <summary>Composition request at the export canvas (the compositor draws every frame from <see cref="ExportFramePlanner.GetFrame"/>).</summary>
        public FrameRequest Request { get; }
        public ExportFramePlanner Planner { get; }
        public ExportPlan Plan => Planner.Plan;
        public int FrameCount => Planner.FrameCount;
        public int SegmentCount => Plan.SegmentCount;

        ExportJob(ExportResumeState state, StudioTimeline timeline, ExportSettings settings, FrameRequest request, ExportFramePlanner planner)
        {
            State = state;
            Timeline = timeline;
            Settings = settings;
            Request = request;
            Planner = planner;
        }

        public ExportSegment GetSegment(int index)
        {
            return Plan.GetSegment(index);
        }

        /// <summary>Video settings of the user video slot at the export canvas, or null when no video slot is drawn.</summary>
        public SlotGeometry? VideoSlot => Planner.LayoutPlan.Layout.HasVideo ? Planner.LayoutPlan.Layout.Video : (SlotGeometry?)null;

        /// <summary>
        /// Ticks of a render stream that an export uses: the stream's tick count limited to the 180-second maximum (a run ending at
        /// the limit writes one frame more than <see cref="SimConstants.MaxDurationTicks"/>). The same value is stored in the resume
        /// state and compared against the clip when rendering, so a long stream is not mistaken for a changed one.
        /// </summary>
        public static int EffectiveStreamTicks(int streamTicks)
        {
            return Math.Max(1, Math.Min(streamTicks, SimConstants.MaxDurationTicks));
        }

        /// <summary>
        /// Builds the persisted snapshot of a new export. Throws <see cref="ArgumentException"/> when the request is incomplete or the
        /// project cannot be sanitized.
        /// </summary>
        public static ExportResumeState CreateState(ExportJobRequest r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            ProjectPaths.RequireSafeId(r.JobId, nameof(r));
            if (r.Project == null) throw new ArgumentException("project required", nameof(r));
            if (r.Settings == null) throw new ArgumentException("settings required", nameof(r));
            SanitizeResult sanitized = ProjectValidator.Sanitize(ProjectJson.Clone(r.Project));
            if (sanitized.HasErrors) throw new ArgumentException("project cannot be exported: invalid document", nameof(r));
            ProjectDocument doc = sanitized.Document;
            int ticks = EffectiveStreamTicks(r.StreamTicks);
            StudioTimeline timeline = ProjectComposition.BuildTimeline(doc, r.Settings.Fps, ticks);
            IReadOnlyList<FloatRect> masks = r.MaskRects ?? PlatformUiMasks.Default.Union();
            var state = new ExportResumeState
            {
                JobId = r.JobId,
                ProjectId = doc.Id,
                CreatedAt = r.NowIso,
                Project = doc,
                Resolution = (int)r.Settings.Resolution,
                Fps = r.Settings.Fps,
                VideoBitrateBps = r.Settings.VideoBitrateBps,
                AudioBitrateBps = r.Settings.AudioBitrateBps,
                Watermark = r.Watermark,
                MaskRects = ExportResumeState.FromRects(masks),
                TextBounds = ExportResumeState.FromRects(r.TextBounds),
                VideoWidth = doc.UserVideo != null ? Math.Max(0, r.VideoSize.X) : 0,
                VideoHeight = doc.UserVideo != null ? Math.Max(0, r.VideoSize.Y) : 0,
                VideoBoxWidth = doc.UserVideo != null ? Math.Max(0, r.VideoBox.X) : 0,
                VideoBoxHeight = doc.UserVideo != null ? Math.Max(0, r.VideoBox.Y) : 0,
                LogoAspect = r.LogoAspect > 0f && !float.IsInfinity(r.LogoAspect) ? r.LogoAspect : WatermarkLayout.DefaultLogoAspect,
                StreamTicks = ticks,
                FrameCount = timeline.FrameCount,
                SegmentFrames = r.Settings.SegmentFrames,
                SaveToGallery = r.SaveToGallery,
                KeepInProject = r.KeepInProject,
                OutputName = ExportFileNaming.FileName(doc.Name, doc.Clip?.ModeId, r.NowIso),
            };
            string error = ExportResumeStore.Validate(state);
            if (error != null) throw new ArgumentException("export state invalid: " + error, nameof(r));
            return state;
        }

        /// <summary>Rebuilds the job of a (new or resumed) state. Throws <see cref="ArgumentException"/> when the state is invalid or inconsistent.</summary>
        public static ExportJob FromState(ExportResumeState state)
        {
            string error = ExportResumeStore.Validate(state);
            if (error != null) throw new ArgumentException("export state invalid: " + error, nameof(state));
            ExportSettings settings = state.ToSettings();
            StudioTimeline timeline = ProjectComposition.BuildTimeline(state.Project, state.Fps, state.StreamTicks);
            if (timeline.FrameCount != state.FrameCount)
                throw new ArgumentException("export state invalid: frame count " + state.FrameCount + " but the project gives " + timeline.FrameCount, nameof(state));
            FrameRequest request = ProjectComposition.BuildFrameRequest(state.Project, settings.Width, settings.Height,
                new IntVec2(state.VideoWidth, state.VideoHeight), state.Watermark, state.GetMaskRects(), state.GetTextBounds(), state.LogoAspect);
            var planner = new ExportFramePlanner(timeline, settings, request);
            return new ExportJob(state, timeline, settings, request, planner);
        }
    }
}
