using Ballverse.UI.Components;
using Ballverse.UI.Format;
using Ballverse.UI.Layout;
using Ballverse.UI.Navigation;
using Ballverse.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Screens
{
    /// <summary>
    /// Studio (spec §4.3, §3.3–§3.7): 9:16 preview (~60% height) with play / time / fullscreen / undo / redo, the
    /// timeline (S, V, text, music), the glass tool tab bar whose tools open bottom sheets, and "Экспорт" as the main
    /// action. Without a user video the preview shows "+ Добавить своё видео" and the "Без видео" switch.
    /// </summary>
    public sealed class StudioScreen : ScreenController
    {
        IStudioViewModel _vm;
        VisualElement _previewArea;
        VisualElement _preview;
        VisualElement _addVideo;
        VisualElement _playIcon;
        Label _time;
        VisualElement _noVideo;
        VisualElement _undo;
        VisualElement _redo;
        VisualElement _fullscreen;
        VisualElement _fullscreenPreview;
        VisualElement _fullscreenPlayIcon;
        VisualElement _fullscreenZones;
        TimelineView _timeline;
        BottomSheet _toolSheet;
        StudioToolPanel _toolPanel;
        StudioTool _activeTool;
        StudioTimelineData _shownTimeline;

        public override ScreenId Id => ScreenId.Studio;
        protected override string Layout => "Studio";

        protected override void OnCreate()
        {
            _vm = Ctx.ViewModels.Studio;
            _previewArea = Q<VisualElement>("preview-area");
            _preview = Q<VisualElement>("preview");
            _addVideo = Q<VisualElement>("add-video");
            _playIcon = Q<VisualElement>("play-icon");
            _time = Q<Label>("time");
            _noVideo = Q<VisualElement>("no-video");
            _undo = Q<VisualElement>("undo");
            _redo = Q<VisualElement>("redo");
            _fullscreen = Q<VisualElement>("fullscreen-overlay");
            _fullscreenPreview = Q<VisualElement>("fullscreen-preview");
            _fullscreenPlayIcon = Q<VisualElement>("fullscreen-play-icon");
            _fullscreenZones = Q<VisualElement>("fullscreen-zones");

            _timeline = new TimelineView(Ctx);
            Q<VisualElement>("timeline-host").Add(_timeline);
            _timeline.Seek += (us, dragging) => _vm.Seek(us, dragging);
            _timeline.SimTrimCommitted += (a, b) => _vm.SetSimTrim(a, b);
            _timeline.VideoTrimCommitted += (a, b) => _vm.SetVideoTrim(a, b);
            _timeline.VideoStartCommitted += us => _vm.SetVideoTimelineStart(us);
            _timeline.TextSelected += i =>
            {
                _vm.Text.Select(i);
                OpenTool(StudioTool.Text);
            };
            _timeline.AddVideoRequested += () => _vm.Video.ImportOrReplace();

            Tap.On(Root, "back", () =>
            {
                if (!HandleBack()) Ctx.Navigator.Pop();
            });
            Tap.On(_undo, () => _vm.Undo());
            Tap.On(_redo, () => _vm.Redo());
            Tap.On(Root, "export", () => Ctx.ViewModels.Export.Begin());
            Tap.On(Root, "play", () => _vm.TogglePlay());
            Tap.On(Root, "fullscreen", () => _vm.SetFullscreenPreview(true));
            Tap.On(_addVideo, () => _vm.Video.ImportOrReplace());
            Tap.On(_noVideo, () => _vm.Video.SetNoVideo(!_vm.Video.NoVideo));
            Tap.On(Root, "fullscreen-close", () => _vm.SetFullscreenPreview(false));
            Tap.On(Root, "fullscreen-play", () => _vm.TogglePlay());
            Tap.On(_fullscreenZones, () => _vm.Layout.SetPlatformZones(!_vm.Layout.PlatformZones));
            Tap.On(Root, "tool-layout", () => OpenTool(StudioTool.Layout));
            Tap.On(Root, "tool-video", () => OpenTool(StudioTool.Video));
            Tap.On(Root, "tool-sim", () => OpenTool(StudioTool.Simulation));
            Tap.On(Root, "tool-text", () => OpenTool(StudioTool.Text));
            Tap.On(Root, "tool-sound", () => OpenTool(StudioTool.Sound));
            _previewArea.RegisterCallback<GeometryChangedEvent>(e => FitPreview());
        }

        protected override void OnShow()
        {
            _vm.Changed += Refresh;
            _vm.SetScreenActive(true);
            _shownTimeline = null;
            Refresh();
        }

        protected override void OnHide()
        {
            _vm.Changed -= Refresh;
            _vm.SetScreenActive(false);
            Ctx.Glass.SetSource(null, _preview);
            _toolSheet?.Close();
        }

        public override bool HandleBack()
        {
            if (_vm.FullscreenPreview)
            {
                _vm.SetFullscreenPreview(false);
                return true;
            }
            _vm.Back();
            return false;
        }

        protected override void OnLanguageChanged()
        {
            _shownTimeline = null;
            Refresh();
            if (_toolPanel != null && _toolSheet != null && _toolSheet.IsOpen) _toolPanel.Build(_toolSheet.Content);
        }

        void FitPreview()
        {
            float w = _previewArea.layout.width;
            float h = _previewArea.layout.height;
            AspectFit.Fit(w, h, 9f / 16f, out float pw, out float ph);
            _preview.style.width = pw;
            _preview.style.height = ph;
        }

        void Refresh()
        {
            var video = _vm.Video;
            TextureSlot.Set(_preview, _vm.Preview);
            TextureSlot.Set(_fullscreenPreview, _vm.Preview);
            Ctx.Glass.SetSource(_vm.Preview, _preview);
            Icons.Set(_playIcon, _vm.IsPlaying ? IconKind.Pause : IconKind.Play);
            Icons.Set(_fullscreenPlayIcon, _vm.IsPlaying ? IconKind.Pause : IconKind.Play);
            long duration = _vm.Timeline != null ? _vm.Timeline.DurationUs : 0;
            _time.text = Ctx.Format("studio.time", DisplayFormat.Clock(_vm.PositionUs, true), DisplayFormat.Clock(duration, true));
            _undo.SetEnabled(_vm.CanUndo);
            _redo.SetEnabled(_vm.CanRedo);
            bool showAdd = !video.HasVideo && !video.NoVideo;
            _addVideo.style.display = showAdd ? DisplayStyle.Flex : DisplayStyle.None;
            _noVideo.style.display = video.HasVideo ? DisplayStyle.None : DisplayStyle.Flex;
            _noVideo.EnableInClassList("bv-chip--selected", video.NoVideo);
            _fullscreen.style.display = _vm.FullscreenPreview ? DisplayStyle.Flex : DisplayStyle.None;
            _fullscreenZones.EnableInClassList("bv-chip--selected", _vm.Layout.PlatformZones);
            if (_vm.Timeline != null)
            {
                bool changed = !ReferenceEquals(_vm.Timeline, _shownTimeline);
                _shownTimeline = _vm.Timeline;
                _timeline.SetData(_vm.Timeline, _vm.PositionUs);
                if (changed && _toolPanel != null && _toolPanel.NeedsRebuild && _toolSheet != null && _toolSheet.IsOpen)
                    _toolPanel.Build(_toolSheet.Content);
            }
            if (_toolPanel != null && _toolSheet != null && _toolSheet.IsOpen)
            {
                if (_toolPanel.NeedsRebuild) _toolPanel.Build(_toolSheet.Content);
                else _toolPanel.Refresh();
            }
        }

        public override void Tick(float deltaTime)
        {
            if (_vm.IsPlaying && _vm.Timeline != null) _timeline.SetData(_vm.Timeline, _vm.PositionUs);
        }

        void OpenTool(StudioTool tool)
        {
            if (_toolSheet != null && _toolSheet.IsOpen)
            {
                if (_activeTool == tool) return;
                _toolSheet.Close();
            }
            _activeTool = tool;
            _toolPanel = CreatePanel(tool);
            HighlightTool(tool);
            _toolSheet = new BottomSheet(Ctx, new SheetOptions
            {
                TitleKey = _toolPanel.TitleKey,
                Detents = SheetDetents.HalfAndFull,
                ScrimOpacity = 0f,
                Closed = () =>
                {
                    HighlightTool(null);
                    _toolSheet = null;
                    _toolPanel = null;
                },
            });
            _toolPanel.Build(_toolSheet.Content);
            _toolSheet.Show();
        }

        StudioToolPanel CreatePanel(StudioTool tool)
        {
            switch (tool)
            {
                case StudioTool.Layout: return new LayoutToolPanel(Ctx, _vm);
                case StudioTool.Video: return new VideoToolPanel(Ctx, _vm);
                case StudioTool.Simulation:
                    return new SimToolPanel(Ctx, _vm, () =>
                    {
                        _toolSheet?.Close();
                        Ctx.Navigator.Push(ScreenId.Simulation);
                    });
                case StudioTool.Text: return new TextToolPanel(Ctx, _vm);
                default: return new SoundToolPanel(Ctx, _vm);
            }
        }

        void HighlightTool(StudioTool? tool)
        {
            SetTab("tool-layout", tool == StudioTool.Layout);
            SetTab("tool-video", tool == StudioTool.Video);
            SetTab("tool-sim", tool == StudioTool.Simulation);
            SetTab("tool-text", tool == StudioTool.Text);
            SetTab("tool-sound", tool == StudioTool.Sound);
        }

        void SetTab(string name, bool selected)
        {
            Q<VisualElement>(name).EnableInClassList("bv-tool-tab--selected", selected);
        }
    }
}
