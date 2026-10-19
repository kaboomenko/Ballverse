using System;
using System.Collections.Generic;
using Ballverse.Core.Audio;
using Ballverse.Core.Audio.Mastering;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.Timeline;

namespace Ballverse.Core.Studio
{
    /// <summary>
    /// Turns a (sanitized) <see cref="ProjectDocument"/> into the inputs of the other Core modules, so that the studio preview and
    /// the export build them identically (spec §7.5 "one compositor"): the <see cref="TimelineSpec"/> (Timeline), the
    /// <see cref="LayoutSpec"/>, slot contents and <see cref="FrameRequest"/> (Layout), the <see cref="TrackMixSettings"/> (Audio)
    /// and the <see cref="ExportSettings"/> (Timeline). Pure and total: every method accepts any document the
    /// <see cref="ProjectValidator"/> lets through and clamps values the target constructors would reject (for example a trim past
    /// the end of a render stream that is shorter than the recorded clip), so opening a project never throws here.
    /// </summary>
    public static class ProjectComposition
    {
        /// <summary>
        /// Timeline input of <paramref name="doc"/> at <paramref name="fps"/> for a render stream of <paramref name="availableTicks"/>
        /// ticks (spec §3.4). Speed and slow-mo factor go through the same rounding as <see cref="ProjectTiming"/>.
        /// </summary>
        /// <param name="availableTicks">Tick count of the clip's render stream (<c>RenderStreamReader.TickCount</c>), or the replay's duration.</param>
        public static TimelineSpec BuildTimelineSpec(ProjectDocument doc, int fps, int availableTicks)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            OutputFps.Validate(fps, nameof(fps));
            ClipTiming clip = BuildClipTiming(doc.Clip ?? new ClipSettings(), availableTicks);
            UserVideoTiming video = BuildVideoTiming(doc.UserVideo);
            OutputSettings output = doc.Output ?? new OutputSettings();
            DurationMode mode = Enum.IsDefined(typeof(DurationMode), output.DurationMode) ? output.DurationMode : DurationMode.Auto;
            long manual = output.ManualDurationUs ?? 0;
            if (mode == DurationMode.Manual)
            {
                if (manual <= 0) manual = Math.Max(ProjectLimits.MinDurationUs, ProjectTiming.ClipOutputUs(doc.Clip ?? new ClipSettings()));
                manual = Clamp(manual, ProjectLimits.MinDurationUs, ProjectLimits.MaxDurationUs);
            }
            else
            {
                manual = Clamp(manual, 0, TimelineSpec.MaxManualDurationUs);
            }
            long? cover = null;
            CoverSettings c = output.Cover;
            if (c != null && c.PutFirst) cover = Clamp(c.AtUs, 0, StudioTimeline.MaxSampleTimeUs);
            return new TimelineSpec(clip, video, mode, fps, manual, cover);
        }

        /// <summary>Compiles <see cref="BuildTimelineSpec"/>.</summary>
        public static StudioTimeline BuildTimeline(ProjectDocument doc, int fps, int availableTicks)
        {
            return new StudioTimeline(BuildTimelineSpec(doc, fps, availableTicks));
        }

        /// <summary>Clip timing with trims clamped into the render stream (at least one tick), speed 0.5–2×, slow-mo 0 or 1–3 s at 0.25–0.5×.</summary>
        public static ClipTiming BuildClipTiming(ClipSettings clip, int availableTicks)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            int available = Clamp(availableTicks, 1, SimConstants.MaxDurationTicks);
            int trimIn = Clamp(clip.TrimInTick, 0, available - 1);
            int trimOut = Clamp(clip.TrimOutTick, trimIn + 1, available);
            int speed = ProjectTiming.PercentFromRatio(clip.Speed);
            speed = speed <= 0 ? ClipTiming.NormalSpeedPercent : Clamp(speed, ClipTiming.MinSpeedPercent, ClipTiming.MaxSpeedPercent);
            FinaleSlowMo slow = clip.FinaleSlowMo ?? new FinaleSlowMo();
            int slowTicks = ProjectTiming.TicksFromSeconds(slow.Seconds);
            slowTicks = slowTicks <= 0 ? 0 : Clamp(slowTicks, ClipTiming.MinSlowMoTicks, ClipTiming.MaxSlowMoTicks);
            int factor = ProjectTiming.PercentFromRatio(slow.Factor);
            factor = factor <= 0 ? ClipTiming.MaxSlowMoFactorPercent : Clamp(factor, ClipTiming.MinSlowMoFactorPercent, ClipTiming.MaxSlowMoFactorPercent);
            SimAfterEnd afterEnd = Enum.IsDefined(typeof(SimAfterEnd), clip.AfterEnd) ? clip.AfterEnd : SimAfterEnd.Freeze;
            return new ClipTiming(trimIn, trimOut, available, speed, slowTicks, factor, afterEnd);
        }

        /// <summary>User video timing, or null when the project has no video (or an empty trim).</summary>
        public static UserVideoTiming BuildVideoTiming(UserVideoSettings video)
        {
            if (video == null) return null;
            long trimIn = Clamp(video.TrimInUs, 0, UserVideoTiming.MaxSourceDurationUs - 1);
            long trimOut = Clamp(video.TrimOutUs, 0, UserVideoTiming.MaxSourceDurationUs);
            if (trimOut <= trimIn) return null;
            long offset = Clamp(video.OffsetUs, -UserVideoTiming.MaxAbsOffsetUs, UserVideoTiming.MaxAbsOffsetUs);
            VideoAfterEnd afterEnd = Enum.IsDefined(typeof(VideoAfterEnd), video.AfterEnd) ? video.AfterEnd : VideoAfterEnd.Freeze;
            return new UserVideoTiming(trimIn, trimOut, offset, afterEnd);
        }

        /// <summary>
        /// Layout settings → <see cref="LayoutSpec"/> (spec §3.3). <b>Decision:</b> a project without a user video is composed in
        /// the layout's L4 form (simulation only) instead of leaving an empty video slot; the stored layout is unchanged, so it
        /// applies again as soon as a video is imported.
        /// </summary>
        public static LayoutSpec BuildLayoutSpec(LayoutSettings layout, bool hasUserVideo)
        {
            LayoutSettings l = layout ?? new LayoutSettings();
            PipSettings p = l.Pip ?? ProjectDefaults.Pip();
            SlotShape shape = p.Shape == PipShape.Circle ? SlotShape.Circle : SlotShape.RoundedRect;
            var pip = new PipSpec(shape, Finite(p.SizeFraction, PipSpec.DefaultSizeFraction),
                new FloatVec2(Finite(p.X, PipSpec.Default.Center.X), Finite(p.Y, PipSpec.Default.Center.Y)));
            LayoutType type = l.Type >= LayoutType.L1 && l.Type <= LayoutType.L5 ? l.Type : LayoutType.L1;
            if (!hasUserVideo) type = LayoutType.L4;
            DividerSettings divider = l.Divider ?? new DividerSettings();
            return new LayoutSpec(type, l.Swap, Finite(l.Ratio, LayoutSpec.DefaultRatio), divider.Px, l.CornerRadius, l.Padding, pip);
        }

        /// <summary>How the user video sits in its slot: rotation (snapped to 90°), mirror, fill, pan and zoom (spec §3.3).</summary>
        public static SlotContent BuildVideoContent(UserVideoSettings video)
        {
            if (video == null) return SlotContent.Default;
            int rotation = SnapRotation(video.Rotation);
            SlotFill fill = video.Fill == SlotFill.Fit ? SlotFill.Fit : SlotFill.Cover;
            return new SlotContent(rotation, video.Mirror, fill, Pan(video.Pan), Finite(video.Zoom, 1f));
        }

        /// <summary>How the simulation picture sits in its slot: fill, pan and zoom (spec §3.3, project schema 1); never rotated or mirrored.</summary>
        public static SlotContent BuildSimContent(ClipSettings clip)
        {
            if (clip == null) return SlotContent.Default;
            SlotFill fill = clip.Fill == SlotFill.Fit ? SlotFill.Fit : SlotFill.Cover;
            return new SlotContent(0, false, fill, Pan(clip.Pan), Finite(clip.Zoom, 1f));
        }

        /// <summary>
        /// Composition request of <paramref name="doc"/> on a canvas (spec §7.5): preview and export call this with the same
        /// arguments except the canvas size.
        /// </summary>
        /// <param name="videoSourceSize">Decoded user-video frame size (reader output, rotation metadata applied); 0×0 when not loaded.</param>
        /// <param name="showWatermark">Result of <see cref="WatermarkPolicy.Resolve"/> for this export/preview.</param>
        /// <param name="masks">Normalized platform UI masks (Config <c>platform_safe_zones</c>), or null for the defaults.</param>
        /// <param name="textBounds">Normalized boxes of every text layer, measured once at the 1080×1920 reference.</param>
        /// <param name="logoAspect">Width / height of the watermark artwork.</param>
        public static FrameRequest BuildFrameRequest(ProjectDocument doc, int canvasWidth, int canvasHeight, IntVec2 videoSourceSize,
            bool showWatermark, IReadOnlyList<FloatRect> masks, IReadOnlyList<FloatRect> textBounds,
            float logoAspect = WatermarkLayout.DefaultLogoAspect)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            bool hasVideo = doc.UserVideo != null;
            return new FrameRequest(canvasWidth, canvasHeight, BuildLayoutSpec(doc.Layout, hasVideo), hasVideo ? videoSourceSize : default(IntVec2),
                BuildVideoContent(doc.UserVideo), showWatermark, logoAspect, masks, textBounds);
        }

        /// <summary>
        /// Identity of everything the studio mix of <paramref name="doc"/> on <paramref name="timeline"/> depends on: the audio block,
        /// the clip's sounds and timing, the user video's file, trim, offset, volume and after-end behaviour (with the placement
        /// the timeline resolved from them), the output duration and the timeline's fps and length. Geometry edits (pan, zoom, fill, rotation, layout, texts) leave it unchanged, so the preview does not
        /// remix while the user pinches or drags.
        /// </summary>
        public static string AudioMixKey(ProjectDocument doc, StudioTimeline timeline)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (timeline == null) throw new ArgumentNullException(nameof(timeline));
            ClipSettings c = doc.Clip ?? new ClipSettings();
            OutputSettings o = doc.Output ?? new OutputSettings();
            UserVideoSettings v = doc.UserVideo;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            string video = v == null ? "-" : v.File + "|" + v.TrimInUs.ToString(inv) + "|" + v.TrimOutUs.ToString(inv) + "|" + v.OffsetUs.ToString(inv)
                + "|" + v.Volume.ToString("R", inv) + "|" + v.AfterEnd;
            // Every input of UserAudioPlacement.ComputeRuns as the timeline resolved it (QA round 2: "if V is shorter than the
            // reel" Loop places the audio again, Freeze/Black/ExpandSim are silent after the end).
            UserVideoTiming vt = timeline.Spec.Video;
            string placement = vt == null ? "-" : vt.TrimInUs.ToString(inv) + "|" + vt.TrimOutUs.ToString(inv) + "|" + vt.OffsetUs.ToString(inv)
                + "|" + vt.AfterEnd + "|" + timeline.VideoStartUs.ToString(inv) + "|" + timeline.ContentStartUs.ToString(inv);
            return CoreJson.Serialize(doc.Audio, false) + "|" + c.Replay + "|" + c.SoundPack + "|" + c.Melody + "|" + c.TrimInTick.ToString(inv)
                + "|" + c.TrimOutTick.ToString(inv) + "|" + c.Speed.ToString("R", inv) + "|" + CoreJson.Serialize(c.FinaleSlowMo, false) + "|" + c.AfterEnd
                + "|" + video + "|" + placement + "|" + o.DurationMode + "|" + (o.ManualDurationUs?.ToString(inv) ?? "-") + "|" + CoreJson.Serialize(o.Cover, false)
                + "|" + timeline.Fps.ToString(inv) + "|" + timeline.FrameCount.ToString(inv);
        }

        /// <summary>
        /// Export mix settings from the project's <c>audio</c> block (spec §3.6): gains 0–200 %, user track off without a video or at
        /// 0 %, music start inside the track (<see cref="MusicSettings.StartUs"/> → negative start frame), fades, ducking and
        /// normalization.
        /// </summary>
        public static TrackMixSettings BuildMixSettings(AudioSettings audio, bool hasUserVideo)
        {
            AudioSettings a = audio ?? new AudioSettings();
            var s = new TrackMixSettings
            {
                SimGain = (float)Finite(a.Sim, 1.0),
                UserGain = (float)Finite(a.User, 1.0),
                UserEnabled = hasUserVideo && a.User > 0,
                DuckMusicUnderUser = a.DuckMusicUnderVoice,
                Normalize = a.Normalize,
            };
            MusicSettings m = a.Music;
            if (m != null)
            {
                s.MusicGain = (float)Finite(m.Volume, 1.0);
                s.MusicStartFrame = -AudioConstants.MicrosToFrames(Math.Max(0, m.StartUs));
                s.MusicFadeInSeconds = (float)(Clamp(m.FadeInUs, 0, ProjectLimits.MaxMusicFadeUs) / 1e6);
                s.MusicFadeOutSeconds = (float)(Clamp(m.FadeOutUs, 0, ProjectLimits.MaxMusicFadeUs) / 1e6);
            }
            else
            {
                s.MusicGain = 0f;
            }
            return s;
        }

        /// <summary>
        /// Export file settings (spec §3.8): explicit choice of the export sheet, else the project's last choice
        /// (<see cref="OutputSettings.Resolution"/> / <see cref="OutputSettings.Fps"/>), else 1080p at the tier's default fps.
        /// </summary>
        public static ExportSettings ResolveExportSettings(OutputSettings output, DeviceTier tier, ExportBitrateTable bitrates = null,
            ExportResolution? resolution = null, int? fps = null)
        {
            ExportResolution res = resolution ?? ResolutionOf(output?.Resolution);
            int f = fps ?? (output?.Fps is int stored && OutputFps.IsValid(stored) ? stored : ExportSettings.DefaultFps(tier));
            return new ExportSettings(res, f, bitrates);
        }

        /// <summary>720 → P720, anything else (1080, null, garbage) → P1080 (the default, spec §3.8).</summary>
        public static ExportResolution ResolutionOf(int? height)
        {
            return height == (int)ExportResolution.P720 ? ExportResolution.P720 : ExportResolution.P1080;
        }

        /// <summary>Snaps any angle to 0/90/180/270 (clockwise), as the slot content requires a multiple of 90.</summary>
        public static int SnapRotation(int degrees)
        {
            int d = ((degrees % 360) + 360) % 360;
            int snapped = (d + 45) / 90 * 90;
            return snapped == 360 ? 0 : snapped;
        }

        static FloatVec2 Pan(double[] pan)
        {
            if (pan == null || pan.Length < 2) return FloatVec2.Zero;
            return new FloatVec2(Finite(pan[0], 0f), Finite(pan[1], 0f));
        }

        static float Finite(double v, float fallback)
        {
            return double.IsNaN(v) || double.IsInfinity(v) ? fallback : (float)v;
        }

        static double Finite(double v, double fallback)
        {
            return double.IsNaN(v) || double.IsInfinity(v) ? fallback : v;
        }

        static int Clamp(int v, int min, int max)
        {
            return v < min ? min : v > max ? max : v;
        }

        static long Clamp(long v, long min, long max)
        {
            return v < min ? min : v > max ? max : v;
        }
    }
}
