using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Params;
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
        /// <summary>Where the trimmed range starts on the timeline (V can be shifted relative to S).</summary>
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
        public static readonly TimelineTrackData Empty = new TimelineTrackData(false, 0, 0, 0, 0, null);
    }

    /// <summary>A text layer's span on the text track.</summary>
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

    public sealed class TimelineMusicItem
    {
        public LocalizedText Name { get; }
        public long StartUs { get; }
        public long EndUs { get; }

        public TimelineMusicItem(LocalizedText name, long startUs, long endUs)
        {
            Name = name;
            StartUs = startUs;
            EndUs = endUs;
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
        /// <summary>Position of V on the timeline.</summary>
        void SetVideoTimelineStart(long us);
        void NudgeVideo(int frames);
        /// <summary>Leaving the studio (project is auto-saved).</summary>
        void Back();
        /// <summary>The screen became visible / hidden (RW1: preload the rewarded ad on entry).</summary>
        void SetScreenActive(bool active);
    }

    public enum PipShape
    {
        RoundedRect = 0,
        Circle = 1,
    }

    /// <summary>Tool "Макет" (spec §3.3).</summary>
    public interface IStudioLayoutTool
    {
        LayoutType Layout { get; }
        bool Swapped { get; }
        /// <summary>L1/L2 split, 30–70%.</summary>
        int ProportionPercent { get; }
        /// <summary>L3/L5 window width, 20–60%.</summary>
        int PipSizePercent { get; }
        PipShape PipShape { get; }
        /// <summary>0–16 px.</summary>
        int DividerPx { get; }
        bool GlassDivider { get; }
        /// <summary>Corner radius of slots, 0–48 px.</summary>
        int CornerRadiusPx { get; }
        /// <summary>Outer padding, 0–48 px.</summary>
        int PaddingPx { get; }
        /// <summary>"Зоны интерфейса" overlay in the preview.</summary>
        bool PlatformZones { get; }
        void SetLayout(LayoutType layout);
        void SetSwapped(bool swapped);
        void SetProportion(int percent);
        void SetPipSize(int percent);
        void SetPipShape(PipShape shape);
        void SetDivider(int px);
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
        /// <summary>0, 90, 180, 270.</summary>
        int RotationDeg { get; }
        bool Mirrored { get; }
        /// <summary>0–200%.</summary>
        int VolumePercent { get; }
        bool Muted { get; }
        /// <summary>Opens the system picker (import or replace).</summary>
        void ImportOrReplace();
        void SetNoVideo(bool noVideo);
        void SetFill(SlotFill fill);
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
        /// <summary>Slow-motion finale length, 0 (off) – 3 s.</summary>
        float SlowMoSeconds { get; }
        /// <summary>0.25 or 0.5.</summary>
        float SlowMoFactor { get; }
        SimAfterEnd AfterEnd { get; }
        /// <summary>Sound pack ids (labels: <c>params.soundPack.{id}</c>).</summary>
        IReadOnlyList<string> SoundPacks { get; }
        string SoundPack { get; }
        void SetSpeed(float speed);
        void SetSlowMo(float seconds, float factor);
        void SetAfterEnd(SimAfterEnd afterEnd);
        void SetSoundPack(string id);
        /// <summary>"Перегенерировать": back to the simulation screen with the same settings; the UI navigates.</summary>
        void Regenerate();
    }

    public enum TextPlate
    {
        None = 0,
        Solid = 1,
        Glass = 2,
    }

    public enum TextAlign
    {
        Left = 0,
        Center = 1,
        Right = 2,
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
        public TextPlate Plate { get; }
        public TextAlign Align { get; }
        public string AnimationId { get; }

        public TextLayerData(int index, string text, int fontIndex, int sizePx, ColorRgba fill, int outlinePx, TextPlate plate,
            TextAlign align, string animationId)
        {
            Index = index;
            Text = text ?? "";
            FontIndex = fontIndex;
            SizePx = sizePx;
            Fill = fill;
            OutlinePx = outlinePx;
            Plate = plate;
            Align = align;
            AnimationId = animationId;
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
        void SetPlate(int index, TextPlate plate);
        void SetAlign(int index, TextAlign align);
        void SetAnimation(int index, string animationId);
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
        void SetFades(float inSeconds, float outSeconds);
        void SetNormalize(bool on);
        void SetVoicePriority(bool on);
    }
}
