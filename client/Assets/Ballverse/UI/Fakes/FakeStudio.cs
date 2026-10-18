using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Params;
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
        long _videoStart;
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
            long duration = _simTrimEnd - _simTrimStart;
            var texts = new List<TimelineTextItem>();
            foreach (var l in _layers) texts.Add(new TimelineTextItem(l.Index, 0, Math.Min(duration, 4 * Second), l.Text));
            Timeline = new StudioTimelineData(duration,
                new TimelineTrackData(true, 30 * Second, _simTrimStart, _simTrimEnd, 0, new Texture[] { _preview.Texture, _preview.Texture, _preview.Texture }),
                _hasVideo && !NoVideo ? new TimelineTrackData(true, 25 * Second, _videoTrimStart, _videoTrimEnd, _videoStart, null) : null,
                texts,
                MusicTrackId != null ? new TimelineMusicItem(LocalizedText.FromLiteral(MusicTrackId), 0, duration) : null,
                60);
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

        public void SetVideoTimelineStart(long us)
        {
            _videoStart = us;
            RebuildTimeline();
            Edit();
        }

        public void NudgeVideo(int frames)
        {
            _videoStart = Math.Max(0, _videoStart + frames * (Second / 60));
            RebuildTimeline();
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
        public int ProportionPercent { get; private set; } = 50;
        public int PipSizePercent { get; private set; } = 35;
        public PipShape PipShape { get; private set; }
        public int DividerPx { get; private set; } = 4;
        public bool GlassDivider { get; private set; }
        public int CornerRadiusPx { get; private set; }
        public int PaddingPx { get; private set; }
        public bool PlatformZones { get; private set; }
        public void SetLayout(LayoutType layout) { _layout = layout; Edit(); }
        public void SetSwapped(bool swapped) { Swapped = swapped; Edit(); }
        public void SetProportion(int percent) { ProportionPercent = percent; Edit(); }
        public void SetPipSize(int percent) { PipSizePercent = percent; Edit(); }
        public void SetPipShape(PipShape shape) { PipShape = shape; Edit(); }
        public void SetDivider(int px) { DividerPx = px; Edit(); }
        public void SetGlassDivider(bool glass) { GlassDivider = glass; Edit(); }
        public void SetCornerRadius(int px) { CornerRadiusPx = px; Edit(); }
        public void SetPadding(int px) { PaddingPx = px; Edit(); }
        public void SetPlatformZones(bool visible) { PlatformZones = visible; Notify(); }

        // Video tool
        public bool HasVideo => _hasVideo;
        public bool NoVideo { get; private set; }
        public SlotFill Fill { get; private set; }
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
            _layers.Add(new TextLayerData(_layers.Count, text, 0, 64, new ColorRgba(255, 255, 255), 2, TextPlate.None, TextAlign.Center, "fade"));
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
        public void SetPlate(int index, TextPlate plate) { Update(index, l => With(l, plate: plate)); }
        public void SetAlign(int index, TextAlign align) { Update(index, l => With(l, align: align)); }
        public void SetAnimation(int index, string animationId) { Update(index, l => With(l, anim: animationId)); }
        public void SetTemplateLanguage(string language) { TemplateLanguage = language; Notify(); }

        void Update(int index, Func<TextLayerData, TextLayerData> f)
        {
            if (index < 0 || index >= _layers.Count) return;
            _layers[index] = f(_layers[index]);
            RebuildTimeline();
            Edit();
        }

        static TextLayerData With(TextLayerData l, int? index = null, string text = null, int? font = null, int? size = null,
            ColorRgba? fill = null, int? outline = null, TextPlate? plate = null, TextAlign? align = null, string anim = null)
        {
            return new TextLayerData(index ?? l.Index, text ?? l.Text, font ?? l.FontIndex, size ?? l.SizePx, fill ?? l.Fill,
                outline ?? l.OutlinePx, plate ?? l.Plate, align ?? l.Align, anim ?? l.AnimationId);
        }

        // Sound tool
        public int SimVolume { get; private set; } = 100;
        public int VideoVolume { get; private set; } = 100;
        public IReadOnlyList<MusicTrackData> MusicTracks => _music;
        public string MusicTrackId { get; private set; }
        public int MusicVolume { get; private set; } = 80;
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
