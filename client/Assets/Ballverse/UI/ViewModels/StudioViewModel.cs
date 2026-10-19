using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Ballverse.Core.Params;
using Ballverse.Core.Projects;
using Ballverse.UI.Localization;
using UnityEngine;

namespace Ballverse.UI.ViewModels
{
    /// <summary>One media track of the studio timeline (S or V), in microseconds (spec §3.4).</summary>
    public sealed class TimelineTrackData
    {
        public bool Present { get; }
        /// <summary>Length of the source clip / video.</summary>
        public long SourceUs { get; }
        public long TrimStartUs { get; }
        public long TrimEndUs { get; }
        /// <summary>
        /// Where the trimmed range starts on the displayed timeline (the output starts at 0). V can start before or after
        /// S: the V offset of Core (<c>UserVideoTiming.OffsetUs</c>) is <c>Video.TimelineStartUs − Sim.TimelineStartUs</c>
        /// and may be negative (with "= V" duration S then starts later than 0).
        /// </summary>
        public long TimelineStartUs { get; }
        /// <summary>Evenly spaced thumbnails of the trimmed range (may be empty while they are extracted).</summary>
        public IReadOnlyList<Texture> Thumbnails { get; }

        public TimelineTrackData(bool present, long sourceUs, long trimStartUs, long trimEndUs, long timelineStartUs,
            IReadOnlyList<Texture> thumbnails)
        {
            Present = present;
            SourceUs = sourceUs;
            TrimStartUs = trimStartUs;
            TrimEndUs = trimEndUs;
            TimelineStartUs = timelineStartUs;
            Thumbnails = thumbnails ?? Array.Empty<Texture>();
        }

        public long LengthUs => TrimEndUs - TrimStartUs;
        public long TimelineEndUs => TimelineStartUs + LengthUs;
        public static readonly TimelineTrackData Empty = new TimelineTrackData(false, 0, 0, 0, 0, null);
    }

    /// <summary>A text layer's span on the text track (appearance / disappearance, spec §3.5).</summary>
    public sealed class TimelineTextItem
    {
        public int LayerIndex { get; }
        public long StartUs { get; }
        public long EndUs { get; }
        /// <summary>User text (literal).</summary>
        public string Text { get; }

        public TimelineTextItem(int layerIndex, long startUs, long endUs, string text)
        {
            LayerIndex = layerIndex;
            StartUs = startUs;
            EndUs = endUs;
            Text = text ?? "";
        }
    }

    /// <summary>The music bar: where the track plays on the reel timeline (it starts with the reel).</summary>
    public sealed class TimelineMusicItem
    {
        public LocalizedText Name { get; }
        /// <summary>Reel time where the music starts / ends.</summary>
        public long StartUs { get; }
        public long EndUs { get; }

        /// <summary>Where the music starts inside the track, µs (dragging the bar changes it).</summary>
        public long TrackStartUs { get; }
        /// <summary>Length of the track, µs (0 = unknown: the bar cannot be dragged).</summary>
        public long TrackLengthUs { get; }

        public TimelineMusicItem(LocalizedText name, long startUs, long endUs, long trackStartUs = 0, long trackLengthUs = 0)
        {
            Name = name;
            StartUs = startUs;
            EndUs = endUs;
            TrackStartUs = trackStartUs;
            TrackLengthUs = trackLengthUs;
        }
    }

    public sealed class StudioTimelineData
    {
        public long DurationUs { get; }
        public TimelineTrackData Sim { get; }
        public TimelineTrackData Video { get; }
        public IReadOnlyList<TimelineTextItem> Texts { get; }
        /// <summary>Null when no music is selected.</summary>
        public TimelineMusicItem Music { get; }
        /// <summary>Frame rate of the project (±1 frame nudges).</summary>
        public int Fps { get; }

        public StudioTimelineData(long durationUs, TimelineTrackData sim, TimelineTrackData video,
            IReadOnlyList<TimelineTextItem> texts, TimelineMusicItem music, int fps)
        {
            DurationUs = durationUs;
            Sim = sim ?? TimelineTrackData.Empty;
            Video = video ?? TimelineTrackData.Empty;
            Texts = texts ?? Array.Empty<TimelineTextItem>();
            Music = music;
            Fps = fps > 0 ? fps : 60;
        }

        /// <summary>V start relative to S start (Core <c>UserVideoTiming.OffsetUs</c>; negative = V starts earlier).</summary>
        public long VideoOffsetUs => Video.TimelineStartUs - Sim.TimelineStartUs;
    }

    /// <summary>
    /// A slot of the composited preview for gestures (spec §4.6: drag / pinch / double tap on studio slots, PiP drag).
    /// <see cref="Rect"/> is normalized to the 9:16 preview, origin top-left (Core <c>FrameLayout</c> rects divided by the
    /// canvas size); the list is ordered bottom to top (the PiP window, when present, comes last).
    /// </summary>
    public sealed class PreviewSlotData
    {
        public SlotKind Slot { get; }
        public FloatRect Rect { get; }
        public bool IsPip { get; }

        public PreviewSlotData(SlotKind slot, FloatRect rect, bool isPip)
        {
            Slot = slot;
            Rect = rect;
            IsPip = isPip;
        }
    }

    /// <summary>Studio tool tabs (spec §4.3).</summary>
    public enum StudioTool
    {
        Layout = 0,
        Video = 1,
        Simulation = 2,
        Text = 3,
        Sound = 4,
    }

    /// <summary>Studio screen (spec §3.3–§3.7, §4.3): preview, timeline, tools, export.</summary>
    public interface IStudioViewModel : IObservableViewModel
    {
        /// <summary>Composited 9:16 preview frame (same compositor as export, spec §3.7).</summary>
        Texture Preview { get; }
        bool IsPlaying { get; }
        long PositionUs { get; }
        StudioTimelineData Timeline { get; }
        /// <summary>Slots of the current layout in the preview (for pan / zoom / PiP gestures).</summary>
        IReadOnlyList<PreviewSlotData> PreviewSlots { get; }
        /// <summary>Reel duration mode (spec §3.4 "Длительность ролика: Авто / = V / вручную").</summary>
        DurationMode DurationMode { get; }
        /// <summary>Content duration for <see cref="DurationMode.Manual"/>, 1–180 s in µs.</summary>
        long ManualDurationUs { get; }
        bool CanUndo { get; }
        bool CanRedo { get; }
        bool FullscreenPreview { get; }

        IStudioLayoutTool Layout { get; }
        IStudioVideoTool Video { get; }
        IStudioSimTool Sim { get; }
        IStudioTextTool Text { get; }
        IStudioSoundTool Sound { get; }

        void TogglePlay();
        /// <summary>Scrub to a time; <paramref name="dragging"/> while the finger is down (frame ≤ 100 ms, spec §3.7).</summary>
        void Seek(long us, bool dragging);
        void Undo();
        void Redo();
        void SetFullscreenPreview(bool fullscreen);
        void SetSimTrim(long startUs, long endUs);
        void SetVideoTrim(long startUs, long endUs);
        /// <summary>
        /// Shift of V against S, as Core <c>UserVideoTiming.OffsetUs</c>: V start minus S start on the timeline, may be
        /// negative (V before S). The VM clamps it to Core's range.
        /// </summary>
        void SetVideoOffset(long offsetUs);
        void NudgeVideo(int frames);
        void SetDurationMode(DurationMode mode);
        /// <summary>Manual reel duration, µs (the VM keeps it in 1–180 s).</summary>
        void SetManualDuration(long us);
        /// <summary>
        /// Pan / zoom of a slot's content by gesture in the preview (spec §3.3 "Панорама и масштаб"): <paramref name="panDelta"/>
        /// in fractions of the slot size since the last call (+x right, +y down), <paramref name="zoomFactor"/> multiplies the
        /// zoom. The VM clamps pan to −1..1 and zoom to 1..<c>ProjectLimits.MaxZoom</c>; <paramref name="final"/> on release
        /// (one undo step).
        /// </summary>
        void PanZoomSlot(SlotKind slot, FloatVec2 panDelta, float zoomFactor, bool final);
        /// <summary>Double tap on a slot: pan and zoom back to the default.</summary>
        void ResetSlotTransform(SlotKind slot);
        /// <summary>Leaving the studio (project is auto-saved).</summary>
        void Back();
        /// <summary>The screen became visible / hidden (RW1: preload the rewarded ad on entry).</summary>
        void SetScreenActive(bool active);
    }

    /// <summary>Tool "Макет" (spec §3.3).</summary>
    public interface IStudioLayoutTool
    {
        LayoutType Layout { get; }
        bool Swapped { get; }
        /// <summary>
        /// L1/L2 split as the Core ratio (project.json <c>layout.ratio</c>), 0.30–0.70; the magnet values are exactly 1/3,
        /// 1/2 and 2/3 (Core <see cref="RatioMagnets"/>).
        /// </summary>
        float Ratio { get; }
        /// <summary>L3/L5 window width, 20–60%.</summary>
        int PipSizePercent { get; }
        PipShape PipShape { get; }
        /// <summary>PiP window center, normalized to the frame (0..1, origin top-left; project.json <c>layout.pip.x/y</c>).</summary>
        FloatVec2 PipCenter { get; }
        /// <summary>PiP frame width, 0–<c>ProjectLimits.MaxPipBorderPx</c> px (0 = no frame).</summary>
        int PipBorderPx { get; }
        ColorRgba PipBorderColor { get; }
        bool PipShadow { get; }
        /// <summary>0–16 px.</summary>
        int DividerPx { get; }
        ColorRgba DividerColor { get; }
        bool GlassDivider { get; }
        /// <summary>Corner radius of slots, 0–48 px.</summary>
        int CornerRadiusPx { get; }
        /// <summary>Outer padding, 0–48 px.</summary>
        int PaddingPx { get; }
        /// <summary>"Зоны интерфейса" overlay in the preview.</summary>
        bool PlatformZones { get; }
        void SetLayout(LayoutType layout);
        void SetSwapped(bool swapped);
        /// <summary>Committed split, already snapped by <see cref="RatioMagnets.Snap(float)"/> (every 1% step stays reachable).</summary>
        void SetRatio(float ratio);
        void SetPipSize(int percent);
        void SetPipShape(PipShape shape);
        /// <summary>
        /// PiP window dragged in the preview (spec §3.3, §4.6): while dragging (<paramref name="final"/> false) the window
        /// follows <paramref name="center"/>; on release the VM snaps it to a corner anchor with Core
        /// <see cref="PipMagnets.Snap"/> (outside the platform UI zones) and commits one undo step.
        /// </summary>
        void MovePip(FloatVec2 center, bool final);
        void SetPipBorder(int px);
        void SetPipBorderColor(ColorRgba color);
        void SetPipShadow(bool on);
        void SetDivider(int px);
        void SetDividerColor(ColorRgba color);
        void SetGlassDivider(bool glass);
        void SetCornerRadius(int px);
        void SetPadding(int px);
        void SetPlatformZones(bool visible);
    }

    /// <summary>Tool "Видео" (spec §3.2, §3.3).</summary>
    public interface IStudioVideoTool
    {
        bool HasVideo { get; }
        /// <summary>"Без видео" (layout L4).</summary>
        bool NoVideo { get; }
        SlotFill Fill { get; }
        /// <summary>Margin fill of the V slot in Fit mode (spec §3.3 "Фон полей").</summary>
        FitBackgroundKind FitBackground { get; }
        ColorRgba FitBackgroundColor { get; }
        /// <summary>0, 90, 180, 270.</summary>
        int RotationDeg { get; }
        bool Mirrored { get; }
        /// <summary>0–200%.</summary>
        int VolumePercent { get; }
        bool Muted { get; }
        /// <summary>What the V slot shows when the video is shorter than the reel (spec §3.4 "Если V короче ролика").</summary>
        VideoAfterEnd AfterEnd { get; }
        /// <summary>Opens the system picker (import or replace).</summary>
        void ImportOrReplace();
        void SetNoVideo(bool noVideo);
        void SetFill(SlotFill fill);
        void SetFitBackground(FitBackgroundKind kind);
        void SetFitBackgroundColor(ColorRgba color);
        void SetAfterEnd(VideoAfterEnd afterEnd);
        void Rotate90();
        void SetMirrored(bool mirrored);
        void SetVolume(int percent);
        void SetMuted(bool muted);
    }

    /// <summary>Tool "Симуляция" (spec §3.4).</summary>
    public interface IStudioSimTool
    {
        /// <summary>0.5–2×.</summary>
        float Speed { get; }
        /// <summary>
        /// Slow-motion finale length: 0 (off) or 1–3 s (spec §3.4; Core <c>ClipTiming.MinSlowMoTicks</c> ..
        /// <c>MaxSlowMoTicks</c>). The UI never sends a value between 0 and 1 s.
        /// </summary>
        float SlowMoSeconds { get; }
        /// <summary>0.25 or 0.5.</summary>
        float SlowMoFactor { get; }
        SimAfterEnd AfterEnd { get; }
        /// <summary>Fill of the S slot (spec §3.3 "Регулировки внутри слота").</summary>
        SlotFill Fill { get; }
        /// <summary>Margin fill of the S slot in Fit mode.</summary>
        FitBackgroundKind FitBackground { get; }
        ColorRgba FitBackgroundColor { get; }
        /// <summary>Sound pack ids (labels: <c>params.soundPack.{id}</c>).</summary>
        IReadOnlyList<string> SoundPacks { get; }
        string SoundPack { get; }
        void SetSpeed(float speed);
        /// <param name="seconds">0 (off) or 1–3 s.</param>
        void SetSlowMo(float seconds, float factor);
        void SetAfterEnd(SimAfterEnd afterEnd);
        void SetFill(SlotFill fill);
        void SetFitBackground(FitBackgroundKind kind);
        void SetFitBackgroundColor(ColorRgba color);
        void SetSoundPack(string id);
        /// <summary>"Перегенерировать": back to the simulation screen with the same settings; the UI navigates.</summary>
        void Regenerate();
    }

    public sealed class TextLayerData
    {
        public int Index { get; }
        public string Text { get; }
        public int FontIndex { get; }
        /// <summary>12–160 px.</summary>
        public int SizePx { get; }
        public ColorRgba Fill { get; }
        /// <summary>0–8 px.</summary>
        public int OutlinePx { get; }
        public bool Shadow { get; }
        public TextPlate Plate { get; }
        public TextAlign Align { get; }
        public string AnimationId { get; }
        /// <summary>Appearance / disappearance on the reel timeline, µs (end exclusive).</summary>
        public long StartUs { get; }
        public long EndUs { get; }

        public TextLayerData(int index, string text, int fontIndex, int sizePx, ColorRgba fill, int outlinePx, bool shadow,
            TextPlate plate, TextAlign align, string animationId, long startUs, long endUs)
        {
            Index = index;
            Text = text ?? "";
            FontIndex = fontIndex;
            SizePx = sizePx;
            Fill = fill;
            OutlinePx = outlinePx;
            Shadow = shadow;
            Plate = plate;
            Align = align;
            AnimationId = animationId;
            StartUs = startUs;
            EndUs = endUs;
        }
    }

    /// <summary>Tool "Текст" (spec §3.5).</summary>
    public interface IStudioTextTool
    {
        /// <summary>Template ids (labels: <c>studio.text.template.{id}</c>).</summary>
        IReadOnlyList<string> Templates { get; }
        IReadOnlyList<TextLayerData> Layers { get; }
        int MaxLayers { get; }
        /// <summary>-1 when nothing is selected.</summary>
        int SelectedIndex { get; }
        /// <summary>Font family names (OFL fonts bundled with the compositor; shown as is).</summary>
        IReadOnlyList<string> FontNames { get; }
        /// <summary>Entrance animation ids (labels: <c>studio.text.anim.{id}</c>).</summary>
        IReadOnlyList<string> Animations { get; }
        /// <summary>Language of template texts, independent of the interface (spec §4.7): "en" / "ru".</summary>
        string TemplateLanguage { get; }
        void AddFromTemplate(string templateId);
        void AddEmpty();
        void Select(int index);
        void Remove(int index);
        void SetText(int index, string text);
        void SetFont(int index, int fontIndex);
        void SetSize(int index, int sizePx);
        void SetFill(int index, ColorRgba color);
        void SetOutline(int index, int px);
        void SetShadow(int index, bool on);
        void SetPlate(int index, TextPlate plate);
        void SetAlign(int index, TextAlign align);
        void SetAnimation(int index, string animationId);
        /// <summary>Appearance / disappearance time on the reel timeline (dragged on the text track), µs; end &gt; start.</summary>
        void SetTiming(int index, long startUs, long endUs);
        void SetTemplateLanguage(string language);
    }

    public sealed class MusicTrackData
    {
        public string Id { get; }
        public LocalizedText Name { get; }

        public MusicTrackData(string id, LocalizedText name)
        {
            Id = id;
            Name = name;
        }
    }

    /// <summary>Tool "Звук" (spec §3.6).</summary>
    public interface IStudioSoundTool
    {
        /// <summary>0–200%.</summary>
        int SimVolume { get; }
        /// <summary>0–200%.</summary>
        int VideoVolume { get; }
        IReadOnlyList<MusicTrackData> MusicTracks { get; }
        /// <summary>Null = no music.</summary>
        string MusicTrackId { get; }
        int MusicVolume { get; }
        /// <summary>Where the music starts inside the track, µs (spec §3.6 "начало"; Core <c>MusicSettings.StartUs</c>).</summary>
        long MusicStartUs { get; }
        /// <summary>Length of the selected track, µs (0 when unknown or none).</summary>
        long MusicTrackLengthUs { get; }
        /// <summary>0–2 s.</summary>
        float FadeInSeconds { get; }
        float FadeOutSeconds { get; }
        /// <summary>Loudness normalization to −14 LUFS.</summary>
        bool Normalize { get; }
        /// <summary>"Голос важнее" ducking.</summary>
        bool VoicePriority { get; }
        void SetSimVolume(int percent);
        void SetVideoVolume(int percent);
        void SetMusicTrack(string id);
        void SetMusicVolume(int percent);
        /// <summary>Start inside the track, µs (the VM clamps it to the track).</summary>
        void SetMusicStart(long us);
        void SetFades(float inSeconds, float outSeconds);
        void SetNormalize(bool on);
        void SetVoicePriority(bool on);
    }
}
