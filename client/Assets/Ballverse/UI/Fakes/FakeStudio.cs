using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Ballverse.Core.Params;
using Ballverse.Core.Projects;
using Ballverse.UI.Localization;
using Ballverse.UI.ViewModels;
using UnityEngine;

namespace Ballverse.UI.Fakes
{
    sealed class FakeStudio : FakeViewModelBase, IStudioViewModel, IStudioLayoutTool, IStudioVideoTool, IStudioSimTool,
        IStudioTextTool, IStudioSoundTool, IDisposable
    {
        const long Second = 1000000L;
        readonly ProceduralPreview _preview = new ProceduralPreview(180, 320, 6, 11, new Color32(0x0A, 0x0C, 0x16, 0xFF));
        readonly List<TextLayerData> _layers = new List<TextLayerData>();
        readonly string[] _templates = { "which_wins", "comment_color", "part_two", "dont_look_away", "who_survives", "remaining", "timer" };
        readonly string[] _fonts = { "Inter", "Manrope", "Rubik", "Unbounded", "Montserrat", "Comfortaa", "Oswald", "Pacifico" };
        readonly string[] _animations = { "none", "fade", "slide", "typewriter", "bounce" };
        readonly MusicTrackData[] _music =
        {
            new MusicTrackData("calm_waves", LocalizedText.FromLiteral("Calm Waves")),
            new MusicTrackData("neon_drive", LocalizedText.FromLiteral("Neon Drive")),
        };
        bool _active;
        long _simTrimStart;
        long _simTrimEnd = 20 * Second;
        bool _hasVideo;
        long _videoTrimStart;
        long _videoTrimEnd = 15 * Second;
        long _videoOffset;
        long _musicStart;
        const long MusicLength = 95 * Second;
        int _undoDepth;
        int _redoDepth;

        public FakeStudio()
        {
            RebuildTimeline();
        }

        public Texture Preview => _preview.Texture;
        public bool IsPlaying { get; private set; }
        public long PositionUs { get; private set; }
        public StudioTimelineData Timeline { get; private set; }
        public IReadOnlyList<PreviewSlotData> PreviewSlots { get; private set; } = Array.Empty<PreviewSlotData>();
        public DurationMode DurationMode { get; private set; }
        public long ManualDurationUs { get; private set; } = 15 * Second;
        public bool CanUndo => _undoDepth > 0;
        public bool CanRedo => _redoDepth > 0;
        public bool FullscreenPreview { get; private set; }
        public IStudioLayoutTool Layout => this;
        public IStudioVideoTool Video => this;
        public IStudioSimTool Sim => this;
        public IStudioTextTool Text => this;
        public IStudioSoundTool Sound => this;

        void Edit()
        {
            _undoDepth++;
            _redoDepth = 0;
            Notify();
        }

        void RebuildTimeline()
        {
            bool video = _hasVideo && !NoVideo;
            long simLength = _simTrimEnd - _simTrimStart;
            long videoLength = _videoTrimEnd - _videoTrimStart;
            // Same rule as Core StudioTimeline: the output starts with the track that defines the duration.
            long simStart = 0;
            long videoStart = _videoOffset;
            long duration = simLength;
            if (video && DurationMode == DurationMode.MatchVideo)
            {
                simStart = -_videoOffset;
                videoStart = 0;
                duration = videoLength;
            }
            else if (DurationMode == DurationMode.Manual)
            {
                duration = ManualDurationUs;
            }
            var texts = new List<TimelineTextItem>();
            foreach (var l in _layers) texts.Add(new TimelineTextItem(l.Index, l.StartUs, l.EndUs, l.Text));
            Timeline = new StudioTimelineData(duration,
                new TimelineTrackData(true, 30 * Second, _simTrimStart, _simTrimEnd, simStart, new Texture[] { _preview.Texture, _preview.Texture, _preview.Texture }),
                video ? new TimelineTrackData(true, 25 * Second, _videoTrimStart, _videoTrimEnd, videoStart, null) : null,
                texts,
                MusicTrackId != null ? new TimelineMusicItem(LocalizedText.FromLiteral(MusicTrackId), 0, duration, _musicStart, MusicLength) : null,
                60);
            RebuildSlots(video);
        }

        void RebuildSlots(bool video)
        {
            var slots = new List<PreviewSlotData>();
            var first = Swapped ? SlotKind.Video : SlotKind.Simulation;
            var second = Swapped ? SlotKind.Simulation : SlotKind.Video;
            if (!video || _layout == LayoutType.L4)
            {
                slots.Add(new PreviewSlotData(SlotKind.Simulation, new FloatRect(0, 0, 1, 1), false));
            }
            else if (_layout == LayoutType.L1)
            {
                slots.Add(new PreviewSlotData(first, new FloatRect(0, 0, 1, Ratio), false));
                slots.Add(new PreviewSlotData(second, new FloatRect(0, Ratio, 1, 1 - Ratio), false));
            }
            else if (_layout == LayoutType.L2)
            {
                slots.Add(new PreviewSlotData(first, new FloatRect(0, 0, Ratio, 1), false));
                slots.Add(new PreviewSlotData(second, new FloatRect(Ratio, 0, 1 - Ratio, 1), false));
            }
            else
            {
                // L3 / L5: full-frame slot plus the window (9:16 frame, so the window height in frame units is w·9/16·aspect).
                var back = _layout == LayoutType.L3 ? SlotKind.Simulation : SlotKind.Video;
                var front = back == SlotKind.Simulation ? SlotKind.Video : SlotKind.Simulation;
                float w = PipSizePercent / 100f;
                // Window height in frame units: square for the circle, 16:9 of its width otherwise (approximation for the fake).
                float h = PipShape == PipShape.Circle ? w * 9f / 16f : w * 9f / 16f * 1.5f;
                slots.Add(new PreviewSlotData(back, new FloatRect(0, 0, 1, 1), false));
                slots.Add(new PreviewSlotData(front, new FloatRect(PipCenter.X - w / 2, PipCenter.Y - h / 2, w, h), true));
            }
            PreviewSlots = slots;
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (!_active) return;
            if (IsPlaying)
            {
                _preview.Step(dt);
                PositionUs += (long)(dt * Second);
                if (PositionUs >= Timeline.DurationUs) PositionUs = 0;
            }
        }

        public void TogglePlay()
        {
            IsPlaying = !IsPlaying;
            Notify();
        }

        public void Seek(long us, bool dragging)
        {
            PositionUs = Math.Max(0, Math.Min(Timeline.DurationUs, us));
            if (dragging) IsPlaying = false;
            Notify();
        }

        public void Undo()
        {
            if (_undoDepth == 0) return;
            _undoDepth--;
            _redoDepth++;
            Notify();
        }

        public void Redo()
        {
            if (_redoDepth == 0) return;
            _redoDepth--;
            _undoDepth++;
            Notify();
        }

        public void SetFullscreenPreview(bool fullscreen)
        {
            FullscreenPreview = fullscreen;
            Notify();
        }

        public void SetSimTrim(long startUs, long endUs)
        {
            _simTrimStart = startUs;
            _simTrimEnd = endUs;
            RebuildTimeline();
            Edit();
        }

        public void SetVideoTrim(long startUs, long endUs)
        {
            _videoTrimStart = startUs;
            _videoTrimEnd = endUs;
            RebuildTimeline();
            Edit();
        }

        public void SetVideoOffset(long offsetUs)
        {
            _videoOffset = offsetUs;
            RebuildTimeline();
            Edit();
        }

        public void NudgeVideo(int frames)
        {
            _videoOffset = Core.Timeline.UserVideoTiming.NudgeOffsetUs(_videoOffset, frames, 60);
            RebuildTimeline();
            Edit();
        }

        public void SetDurationMode(DurationMode mode)
        {
            DurationMode = mode;
            RebuildTimeline();
            Edit();
        }

        public void SetManualDuration(long us)
        {
            ManualDurationUs = Math.Max(Second, Math.Min(SimConstants.MaxDurationSeconds * Second, us));
            RebuildTimeline();
            Edit();
        }

        public void PanZoomSlot(SlotKind slot, FloatVec2 panDelta, float zoomFactor, bool final)
        {
            if (final) Edit();
        }

        public void ResetSlotTransform(SlotKind slot)
        {
            Edit();
        }

        public void Back()
        {
            IsPlaying = false;
        }

        public void SetScreenActive(bool active)
        {
            _active = active;
            if (!active) IsPlaying = false;
        }

        // Layout tool
        LayoutType IStudioLayoutTool.Layout => _layout;
        LayoutType _layout = LayoutType.L1;
        public bool Swapped { get; private set; }
        public float Ratio { get; private set; } = 0.5f;
        public int PipSizePercent { get; private set; } = 35;
        public PipShape PipShape { get; private set; }
        public FloatVec2 PipCenter { get; private set; } = new FloatVec2(0.75f, 0.25f);
        public int PipBorderPx { get; private set; }
        public ColorRgba PipBorderColor { get; private set; } = new ColorRgba(255, 255, 255);
        public bool PipShadow { get; private set; } = true;
        public int DividerPx { get; private set; } = 4;
        public ColorRgba DividerColor { get; private set; } = new ColorRgba(255, 255, 255);
        public bool GlassDivider { get; private set; }
        public int CornerRadiusPx { get; private set; }
        public int PaddingPx { get; private set; }
        public bool PlatformZones { get; private set; }
        public void SetLayout(LayoutType layout) { _layout = layout; RebuildTimeline(); Edit(); }
        public void SetSwapped(bool swapped) { Swapped = swapped; RebuildTimeline(); Edit(); }
        public void SetRatio(float ratio) { Ratio = ratio; RebuildTimeline(); Edit(); }
        public void SetPipSize(int percent) { PipSizePercent = percent; RebuildTimeline(); Edit(); }
        public void SetPipShape(PipShape shape) { PipShape = shape; RebuildTimeline(); Edit(); }

        public void MovePip(FloatVec2 center, bool final)
        {
            if (final)
            {
                // Same snapping as the app: Core magnets to the four corners (no platform masks in the fake).
                var pip = new PipSpec(PipShape == PipShape.Circle ? SlotShape.Circle : SlotShape.RoundedRect,
                    Math.Max(PipSpec.MinSizeFraction, Math.Min(PipSpec.MaxSizeFraction, PipSizePercent / 100f)), center);
                center = PipMagnets.Snap(1080, 1920, pip, center, RectUnion.Empty).Pip.Center;
            }
            PipCenter = center;
            RebuildTimeline();
            if (final) Edit();
            else Notify();
        }

        public void SetPipBorder(int px) { PipBorderPx = px; Edit(); }
        public void SetPipBorderColor(ColorRgba color) { PipBorderColor = color; Edit(); }
        public void SetPipShadow(bool on) { PipShadow = on; Edit(); }
        public void SetDivider(int px) { DividerPx = px; Edit(); }
        public void SetDividerColor(ColorRgba color) { DividerColor = color; Edit(); }
        public void SetGlassDivider(bool glass) { GlassDivider = glass; Edit(); }
        public void SetCornerRadius(int px) { CornerRadiusPx = px; Edit(); }
        public void SetPadding(int px) { PaddingPx = px; Edit(); }
        public void SetPlatformZones(bool visible) { PlatformZones = visible; Notify(); }

        // Video tool
        public bool HasVideo => _hasVideo;
        public bool NoVideo { get; private set; }
        public SlotFill Fill { get; private set; }
        public FitBackgroundKind FitBackground { get; private set; }
        public ColorRgba FitBackgroundColor { get; private set; } = new ColorRgba(0x10, 0x10, 0x18);
        VideoAfterEnd IStudioVideoTool.AfterEnd => _videoAfterEnd;
        VideoAfterEnd _videoAfterEnd;
        public int RotationDeg { get; private set; }
        public bool Mirrored { get; private set; }
        public int VolumePercent { get; private set; } = 100;
        public bool Muted { get; private set; }

        public void ImportOrReplace()
        {
            After(0.6f, () =>
            {
                _hasVideo = true;
                NoVideo = false;
                RebuildTimeline();
                Edit();
            });
        }

        public void SetNoVideo(bool noVideo) { NoVideo = noVideo; RebuildTimeline(); Edit(); }
        public void SetFill(SlotFill fill) { Fill = fill; Edit(); }
        public void SetFitBackground(FitBackgroundKind kind) { FitBackground = kind; Edit(); }
        public void SetFitBackgroundColor(ColorRgba color) { FitBackgroundColor = color; Edit(); }
        public void SetAfterEnd(VideoAfterEnd afterEnd) { _videoAfterEnd = afterEnd; Edit(); }
        public void Rotate90() { RotationDeg = (RotationDeg + 90) % 360; Edit(); }
        public void SetMirrored(bool mirrored) { Mirrored = mirrored; Edit(); }
        public void SetVolume(int percent) { VolumePercent = percent; Edit(); }
        public void SetMuted(bool muted) { Muted = muted; Edit(); }

        // Simulation tool
        float IStudioSimTool.Speed => _speed;
        float _speed = 1f;
        public float SlowMoSeconds { get; private set; }
        public float SlowMoFactor { get; private set; } = 0.5f;
        public SimAfterEnd AfterEnd { get; private set; }
        SlotFill IStudioSimTool.Fill => _simFill;
        SlotFill _simFill;
        FitBackgroundKind IStudioSimTool.FitBackground => _simFitBackground;
        FitBackgroundKind _simFitBackground;
        ColorRgba IStudioSimTool.FitBackgroundColor => _simFitColor;
        ColorRgba _simFitColor = new ColorRgba(0x10, 0x10, 0x18);
        void IStudioSimTool.SetFill(SlotFill fill) { _simFill = fill; Edit(); }
        void IStudioSimTool.SetFitBackground(FitBackgroundKind kind) { _simFitBackground = kind; Edit(); }
        void IStudioSimTool.SetFitBackgroundColor(ColorRgba color) { _simFitColor = color; Edit(); }
        public IReadOnlyList<string> SoundPacks => CommonParams.SoundPacks;
        public string SoundPack { get; private set; } = "Notes";
        public void SetSpeed(float speed) { _speed = speed; Edit(); }
        public void SetSlowMo(float seconds, float factor) { SlowMoSeconds = seconds; SlowMoFactor = factor; Edit(); }
        public void SetAfterEnd(SimAfterEnd afterEnd) { AfterEnd = afterEnd; Edit(); }
        public void SetSoundPack(string id) { SoundPack = id; Edit(); }
        public void Regenerate() { }

        // Text tool
        public IReadOnlyList<string> Templates => _templates;
        public IReadOnlyList<TextLayerData> Layers => _layers;
        public int MaxLayers => 5;
        public int SelectedIndex { get; private set; } = -1;
        public IReadOnlyList<string> FontNames => _fonts;
        public IReadOnlyList<string> Animations => _animations;
        public string TemplateLanguage { get; private set; } = "en";

        public void AddFromTemplate(string templateId)
        {
            AddLayer(TemplateText(templateId));
        }

        public void AddEmpty()
        {
            AddLayer(Localize != null ? Localize("studio.text.new_layer") : "");
        }

        /// <summary>Resolver for template texts (the fake uses the interface tables; the app has separate template tables).</summary>
        internal Func<string, string> Localize { get; set; }

        string TemplateText(string id)
        {
            return Localize != null ? Localize("studio.text.template." + id) : id;
        }

        void AddLayer(string text)
        {
            if (_layers.Count >= MaxLayers) return;
            long duration = Timeline != null ? Timeline.DurationUs : 20 * Second;
            _layers.Add(new TextLayerData(_layers.Count, text, 0, 64, new ColorRgba(255, 255, 255), 2, false, TextPlate.None, TextAlign.Center,
                "fade", 0, Math.Min(duration, 4 * Second)));
            SelectedIndex = _layers.Count - 1;
            RebuildTimeline();
            Edit();
        }

        public void Select(int index) { SelectedIndex = index; Notify(); }

        public void Remove(int index)
        {
            if (index < 0 || index >= _layers.Count) return;
            _layers.RemoveAt(index);
            for (int i = 0; i < _layers.Count; i++) _layers[i] = With(_layers[i], index: i);
            SelectedIndex = -1;
            RebuildTimeline();
            Edit();
        }

        public void SetText(int index, string text) { Update(index, l => With(l, text: text)); }
        public void SetFont(int index, int fontIndex) { Update(index, l => With(l, font: fontIndex)); }
        public void SetSize(int index, int sizePx) { Update(index, l => With(l, size: sizePx)); }
        public void SetFill(int index, ColorRgba color) { Update(index, l => With(l, fill: color)); }
        public void SetOutline(int index, int px) { Update(index, l => With(l, outline: px)); }
        public void SetShadow(int index, bool on) { Update(index, l => With(l, shadow: on)); }
        public void SetPlate(int index, TextPlate plate) { Update(index, l => With(l, plate: plate)); }
        public void SetAlign(int index, TextAlign align) { Update(index, l => With(l, align: align)); }
        public void SetAnimation(int index, string animationId) { Update(index, l => With(l, anim: animationId)); }
        public void SetTiming(int index, long startUs, long endUs) { Update(index, l => With(l, start: startUs, end: endUs)); }
        public void SetTemplateLanguage(string language) { TemplateLanguage = language; Notify(); }

        void Update(int index, Func<TextLayerData, TextLayerData> f)
        {
            if (index < 0 || index >= _layers.Count) return;
            _layers[index] = f(_layers[index]);
            RebuildTimeline();
            Edit();
        }

        static TextLayerData With(TextLayerData l, int? index = null, string text = null, int? font = null, int? size = null,
            ColorRgba? fill = null, int? outline = null, bool? shadow = null, TextPlate? plate = null, TextAlign? align = null,
            string anim = null, long? start = null, long? end = null)
        {
            return new TextLayerData(index ?? l.Index, text ?? l.Text, font ?? l.FontIndex, size ?? l.SizePx, fill ?? l.Fill,
                outline ?? l.OutlinePx, shadow ?? l.Shadow, plate ?? l.Plate, align ?? l.Align, anim ?? l.AnimationId,
                start ?? l.StartUs, end ?? l.EndUs);
        }

        // Sound tool
        public int SimVolume { get; private set; } = 100;
        public int VideoVolume { get; private set; } = 100;
        public IReadOnlyList<MusicTrackData> MusicTracks => _music;
        public string MusicTrackId { get; private set; }
        public int MusicVolume { get; private set; } = 80;
        public long MusicStartUs => _musicStart;
        public long MusicTrackLengthUs => MusicTrackId != null ? MusicLength : 0;
        public void SetMusicStart(long us) { _musicStart = Math.Max(0, Math.Min(MusicLength - Second, us)); RebuildTimeline(); Edit(); }
        public float FadeInSeconds { get; private set; }
        public float FadeOutSeconds { get; private set; } = 1f;
        public bool Normalize { get; private set; } = true;
        public bool VoicePriority { get; private set; } = true;
        public void SetSimVolume(int percent) { SimVolume = percent; Edit(); }
        public void SetVideoVolume(int percent) { VideoVolume = percent; Edit(); }
        public void SetMusicTrack(string id) { MusicTrackId = id; RebuildTimeline(); Edit(); }
        public void SetMusicVolume(int percent) { MusicVolume = percent; Edit(); }
        public void SetFades(float inSeconds, float outSeconds) { FadeInSeconds = inSeconds; FadeOutSeconds = outSeconds; Edit(); }
        public void SetNormalize(bool on) { Normalize = on; Edit(); }
        public void SetVoicePriority(bool on) { VoicePriority = on; Edit(); }

        public void Dispose()
        {
            _preview.Dispose();
        }
    }
}
