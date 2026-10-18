using System.Collections.Generic;
using Ballverse.Core.Layout;
using Ballverse.UI.Components;
using Ballverse.UI.Format;
using Ballverse.UI.Layout;
using Ballverse.UI.Navigation;
using Ballverse.UI.Studio;
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
        PreviewGestures _gestures;
        readonly List<GestureSlot> _gestureSlots = new List<GestureSlot>();
        /// <summary>The open tool sheet; a replaced sheet's late Closed callback cannot clear its successor.</summary>
        readonly CurrentRef<ToolSession> _tool = new CurrentRef<ToolSession>();

        sealed class ToolSession
        {
            public StudioTool Tool;
            public BottomSheet Sheet;
            public StudioToolPanel Panel;
        }

        /// <summary>Tool whose sheet is open (null when none).</summary>
        public StudioTool? ActiveTool => _tool.HasValue ? _tool.Value.Tool : (StudioTool?)null;
        internal BottomSheet ActiveToolSheet => _tool.Value?.Sheet;

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
            _timeline.VideoOffsetCommitted += offset => _vm.SetVideoOffset(offset);
            _timeline.TextSelected += i =>
            {
                _vm.Text.Select(i);
                OpenTool(StudioTool.Text);
            };
            _timeline.TextTimingCommitted += (i, start, end) => _vm.Text.SetTiming(i, start, end);
            _timeline.MusicStartCommitted += us => _vm.Sound.SetMusicStart(us);
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

            // Spec §3.3 / §4.6: drag the PiP window, drag / pinch a slot's content, double tap resets it.
            _gestures = new PreviewGestures(new GestureSink(this));
            _preview.RegisterCallback<PointerDownEvent>(OnPreviewDown);
            _preview.RegisterCallback<PointerMoveEvent>(OnPreviewMove);
            _preview.RegisterCallback<PointerUpEvent>(OnPreviewUp);
            _preview.RegisterCallback<PointerCancelEvent>(e => OnPreviewLost(e.pointerId));
            _preview.RegisterCallback<PointerCaptureOutEvent>(e => OnPreviewLost(e.pointerId));
        }

        sealed class GestureSink : IPreviewGestureSink
        {
            readonly StudioScreen _screen;

            public GestureSink(StudioScreen screen)
            {
                _screen = screen;
            }

            public void MovePip(FloatVec2 center, bool final)
            {
                _screen._vm.Layout.MovePip(center, final);
            }

            public void PanZoom(SlotKind slot, FloatVec2 panDelta, float zoomFactor, bool final)
            {
                _screen._vm.PanZoomSlot(slot, panDelta, zoomFactor, final);
            }

            public void ResetSlot(SlotKind slot)
            {
                _screen._vm.ResetSlotTransform(slot);
            }
        }

        FloatVec2 PreviewPoint(Vector2 panelPosition)
        {
            var local = _preview.WorldToLocal(panelPosition);
            return new FloatVec2(local.x, local.y);
        }

        void OnPreviewDown(PointerDownEvent e)
        {
            _gestureSlots.Clear();
            var slots = _vm.PreviewSlots;
            if (slots != null)
                foreach (var slot in slots) _gestureSlots.Add(new GestureSlot(slot.Slot, slot.Rect, slot.IsPip));
            if (!_gestures.Down(e.pointerId, PreviewPoint(e.position), _preview.layout.width, _preview.layout.height, _gestureSlots,
                    Time.unscaledTime)) return;
            _preview.CapturePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnPreviewMove(PointerMoveEvent e)
        {
            if (!_preview.HasPointerCapture(e.pointerId)) return;
            _gestures.Move(e.pointerId, PreviewPoint(e.position));
            e.StopPropagation();
        }

        void OnPreviewUp(PointerUpEvent e)
        {
            if (!_preview.HasPointerCapture(e.pointerId)) return;
            _gestures.Up(e.pointerId, PreviewPoint(e.position), Time.unscaledTime);
            _preview.ReleasePointer(e.pointerId);
            e.StopPropagation();
        }

        void OnPreviewLost(int pointerId)
        {
            _gestures.Cancel(pointerId);
        }

        protected override void OnShow()
        {
            _vm.Changed += Refresh;
            _vm.SetScreenActive(true);
            Refresh();
        }

        protected override void OnHide()
        {
            _vm.Changed -= Refresh;
            _vm.SetScreenActive(false);
            Ctx.Glass.SetSource(null, _preview);
            CloseTool();
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
            Refresh();
            var session = _tool.Value;
            if (session != null) session.Panel.Build(session.Sheet.Content);
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
                _timeline.SetData(_vm.Timeline, _vm.PositionUs);
            }
            var session = _tool.Value;
            if (session != null)
            {
                if (session.Panel.NeedsRebuild) session.Panel.Build(session.Sheet.Content);
                else session.Panel.Refresh();
            }
        }

        public override void Tick(float deltaTime)
        {
            if (_vm.IsPlaying && _vm.Timeline != null) _timeline.SetData(_vm.Timeline, _vm.PositionUs);
        }

        internal void OpenTool(StudioTool tool)
        {
            var current = _tool.Value;
            if (current != null && !current.Sheet.IsActive)
            {
                // Dismissed by a swipe or back and still animating out: treat it as gone.
                _tool.Release(current);
                current = null;
            }
            if (current != null)
            {
                if (current.Tool == tool)
                {
                    // Same tool again (e.g. another text pill): the panel follows the selection.
                    if (current.Panel.NeedsRebuild) current.Panel.Build(current.Sheet.Content);
                    return;
                }
                CloseTool();
            }
            var session = new ToolSession { Tool = tool, Panel = CreatePanel(tool) };
            session.Sheet = new BottomSheet(Ctx, new SheetOptions
            {
                TitleKey = session.Panel.TitleKey,
                Detents = SheetDetents.HalfAndFull,
                ScrimOpacity = 0f,
                // Runs after the close animation, possibly when another tool's sheet is already open: only the
                // session that is still current may clear the state.
                Closed = () =>
                {
                    if (_tool.Release(session)) HighlightTool(null);
                },
            });
            _tool.Set(session);
            HighlightTool(tool);
            session.Panel.Build(session.Sheet.Content);
            session.Sheet.Show();
        }

        /// <summary>Closes the open tool sheet; the screen forgets it at once (its close animation may still run).</summary>
        internal void CloseTool()
        {
            var session = _tool.Value;
            if (session == null) return;
            _tool.Release(session);
            HighlightTool(null);
            session.Sheet.Close();
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
                        CloseTool();
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
