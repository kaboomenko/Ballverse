using Ballverse.UI.Components;
using Ballverse.UI.Format;
using Ballverse.UI.Layout;
using Ballverse.UI.Localization;
using Ballverse.UI.Navigation;
using Ballverse.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Screens
{
    /// <summary>
    /// Simulation screen (spec §4.3): edge-to-edge render target, glass top bar (back, mode + preset → presets sheet,
    /// gear → settings sheet), floating glass bottom bar (⟲ retry · ⏯ · 🎲 generate with long press for style /
    /// duration · ⚡ speed · «В видео»), result card with the last 5 runs, first-run hints and quest.
    /// </summary>
    public sealed class SimulationScreen : ScreenController
    {
        ISimulationViewModel _vm;
        VisualElement _render;
        VisualElement _chromeTop;
        VisualElement _chromeBottom;
        Label _modeName;
        Label _presetName;
        Label _presetModified;
        VisualElement _playIcon;
        VisualElement _generateButton;
        Label _speedLabel;
        VisualElement _generating;
        VisualElement _generatingTrack;
        VisualElement _generatingFill;
        VisualElement _resultHost;
        VisualElement _resultDot;
        Label _resultHeadline;
        Label _resultTime;
        VisualElement _resultStats;
        ScrollView _resultRuns;
        VisualElement _quest;
        VisualElement _questSteps;
        VisualElement _hint;
        Label _hintText;
        SimSettingsSheet _settingsSheet;
        PresetsSheet _presetsSheet;
        GenerateSheet _generateSheet;
        float _sinceInteraction;
        bool _chromeShown = true;
        float _chromeOpacity = 1f;
        Tween _chromeTween;
        int _arenaPointer = -1;
        SimResultData _shownResult;
        string _shownModeId;

        public override ScreenId Id => ScreenId.Simulation;
        protected override string Layout => "Simulation";

        protected override void OnCreate()
        {
            _vm = Ctx.ViewModels.Simulation;
            _render = Q<VisualElement>("render");
            _chromeTop = Q<VisualElement>("chrome-top");
            _chromeBottom = Q<VisualElement>("chrome-bottom");
            _modeName = Q<Label>("mode-name");
            _presetName = Q<Label>("preset-name");
            _presetModified = Q<Label>("preset-modified");
            _playIcon = Q<VisualElement>("play-icon");
            _generateButton = Q<VisualElement>("generate");
            _speedLabel = Q<Label>("speed-label");
            _generating = Q<VisualElement>("generating");
            _generatingTrack = Q<VisualElement>("generating-track");
            _generatingFill = Q<VisualElement>("generating-fill");
            _resultHost = Q<VisualElement>("result");
            _resultDot = Q<VisualElement>("result-dot");
            _resultHeadline = Q<Label>("result-headline");
            _resultTime = Q<Label>("result-time");
            _resultStats = Q<VisualElement>("result-stats");
            _resultRuns = Q<ScrollView>("result-runs");
            _resultRuns.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            _resultRuns.verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _quest = Q<VisualElement>("quest");
            _questSteps = Q<VisualElement>("quest-steps");
            _hint = Q<VisualElement>("hint");
            _hintText = Q<Label>("hint-text");

            _settingsSheet = new SimSettingsSheet(Ctx, () => _vm.Params);
            _presetsSheet = new PresetsSheet(Ctx, () => _vm.Presets);
            _generateSheet = new GenerateSheet(Ctx, () => _vm);

            Tap.On(Root, "back", () =>
            {
                if (!HandleBack()) Ctx.Navigator.Pop();
            });
            Tap.On(Root, "title-area", () => _presetsSheet.Open());
            Tap.On(Root, "settings", () => _settingsSheet.Open());
            Tap.On(Root, "retry", () => _vm.Retry());
            Tap.On(Root, "play", () => _vm.TogglePlay());
            _generateButton.AddManipulator(new TapOrLongPress(() => _vm.Generate(), () => _generateSheet.Open()));
            Tap.On(Root, "speed", () => _vm.CycleSpeed());
            Tap.On(Root, "to-video", ToVideo);
            Tap.On(Root, "result-retry", () => _vm.Retry());
            Tap.On(Root, "result-generate", () => _vm.Generate());
            Tap.On(Root, "result-to-video", ToVideo);
            Tap.On(Root, "result-save-clip", () =>
            {
                _vm.SaveClip();
                Ctx.Toasts.Show(Ctx.L("sim.clip_saved"));
            });
            Tap.On(Root, "quest-header", () =>
            {
                var q = _vm.Quest;
                if (q != null) _vm.SetQuestCollapsed(!q.Collapsed);
            });

            Root.RegisterCallback<PointerDownEvent>(OnAnyPointerDown, TrickleDown.TrickleDown);
            _render.RegisterCallback<PointerDownEvent>(OnArenaDown);
            _render.RegisterCallback<PointerMoveEvent>(OnArenaMove);
            _render.RegisterCallback<PointerUpEvent>(OnArenaUp);
            _render.RegisterCallback<PointerCancelEvent>(OnArenaCancel);
        }

        protected override void OnShow()
        {
            _vm.Changed += Refresh;
            _vm.Notice += OnNotice;
            _vm.SetScreenActive(true);
            _sinceInteraction = 0f;
            Refresh();
        }

        protected override void OnHide()
        {
            _vm.Changed -= Refresh;
            _vm.Notice -= OnNotice;
            _vm.SetScreenActive(false);
            Ctx.Glass.SetSource(null, _render);
            _settingsSheet.Close();
            _presetsSheet.Close();
            _generateSheet.Close();
        }

        protected override void OnLanguageChanged()
        {
            _shownResult = null;
            Refresh();
        }

        public override bool HandleBack()
        {
            if (_vm.IsInteractive && _vm.Phase == SimPhaseView.Running && _vm.IsPlaying)
            {
                _vm.Pause();
                Ctx.Dialogs.Confirm("sim.leave_title", "sim.leave_body", "sim.leave_confirm", false, () =>
                {
                    _vm.Back();
                    Ctx.Navigator.Pop();
                });
                return true;
            }
            _vm.Back();
            return false;
        }

        void ToVideo()
        {
            _vm.ToVideo();
            Ctx.Navigator.Push(ScreenId.Studio);
        }

        void OnNotice(LocalizedText text)
        {
            Ctx.Toasts.Show(Ctx.L(text));
        }

        void Refresh()
        {
            if (_shownModeId != _vm.ModeId)
            {
                _shownModeId = _vm.ModeId;
                if (_vm.ModeNameKey != null) Ctx.Binder.SetKey(_modeName, _vm.ModeNameKey);
            }
            var presets = _vm.Presets;
            _presetName.text = presets != null ? Ctx.L(presets.CurrentName) : "";
            _presetModified.style.display = presets != null && presets.IsModified ? DisplayStyle.Flex : DisplayStyle.None;
            Icons.Set(_playIcon, _vm.IsPlaying ? IconKind.Pause : IconKind.Play);
            _speedLabel.text = DisplayFormat.Multiplier(_vm.Speed, Ctx.Localizer);
            _generateButton.EnableInClassList("bv-dice-btn--busy", _vm.IsGenerating);
            bool showProgress = _vm.IsGenerating && _vm.GenerationProgress >= 0f;
            _generating.style.display = _vm.IsGenerating ? DisplayStyle.Flex : DisplayStyle.None;
            _generatingTrack.style.display = showProgress ? DisplayStyle.Flex : DisplayStyle.None;
            if (showProgress) _generatingFill.style.width = Length.Percent(Mathf.Clamp01(_vm.GenerationProgress) * 100f);
            RefreshResult();
            RefreshQuest();
            RefreshHint();
            Ctx.Glass.SetSource(_vm.RenderTarget, _render);
            TextureSlot.Set(_render, _vm.RenderTarget);
        }

        void RefreshResult()
        {
            var result = _vm.Result;
            _resultHost.style.display = result != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (result == null)
            {
                _shownResult = null;
                return;
            }
            if (result != _shownResult)
            {
                _shownResult = result;
                _resultHeadline.text = Ctx.L(result.Headline);
                _resultDot.style.display = result.WinnerColor.HasValue ? DisplayStyle.Flex : DisplayStyle.None;
                if (result.WinnerColor.HasValue) _resultDot.style.backgroundColor = ColorSwatches.ToColor(result.WinnerColor.Value);
                _resultTime.text = Ctx.Format("result.time", DisplayFormat.DurationMs(result.DurationMs));
                _resultStats.Clear();
                foreach (var stat in result.Stats)
                {
                    var row = new VisualElement();
                    row.AddToClassList("bv-result__stat");
                    row.Add(Ctx.KeyLabel(stat.LabelKey, "bv-result__stat-label"));
                    row.Add(UiContext.TextLabel(Ctx.L(stat.Value), "bv-result__stat-value"));
                    _resultStats.Add(row);
                }
            }
            _resultRuns.Clear();
            foreach (var run in _vm.PreviousRuns)
            {
                var thumb = new VisualElement();
                thumb.AddToClassList("bv-run-thumb");
                thumb.EnableInClassList("bv-run-thumb--current", run.IsCurrent);
                TextureSlot.Set(thumb, run.Thumbnail);
                thumb.Add(UiContext.TextLabel(DisplayFormat.DurationMs(run.DurationMs), "bv-run-thumb__label"));
                int index = run.Index;
                Tap.On(thumb, () => _vm.SelectPreviousRun(index));
                _resultRuns.Add(thumb);
            }
        }

        void RefreshQuest()
        {
            var q = _vm.Quest;
            _quest.style.display = q != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (q == null) return;
            _questSteps.style.display = q.Collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            _quest.EnableInClassList("bv-quest--collapsed", q.Collapsed);
            Q<VisualElement>("quest-run").EnableInClassList("bv-quest__step--done", q.RanSimulation);
            Q<VisualElement>("quest-generate").EnableInClassList("bv-quest__step--done", q.Generated);
            Q<VisualElement>("quest-export").EnableInClassList("bv-quest__step--done", q.Exported);
        }

        void RefreshHint()
        {
            string key;
            switch (_vm.ActiveHint)
            {
                case HintStep.Generate: key = "hint.generate"; break;
                case HintStep.Settings: key = "hint.settings"; break;
                case HintStep.ToVideo: key = "hint.to_video"; break;
                default: key = null; break;
            }
            _hint.style.display = key != null ? DisplayStyle.Flex : DisplayStyle.None;
            if (key != null) Ctx.Binder.SetKey(_hintText, key);
        }

        VisualElement HintTarget()
        {
            switch (_vm.ActiveHint)
            {
                case HintStep.Generate: return _generateButton;
                case HintStep.Settings: return Q<VisualElement>("settings");
                case HintStep.ToVideo: return Q<VisualElement>("to-video");
                default: return null;
            }
        }

        public override void Tick(float deltaTime)
        {
            _sinceInteraction += deltaTime;
            bool overlay = Ctx.Sheets.AnyOpen || Ctx.Dialogs.AnyOpen || _vm.Result != null;
            var state = ChromeVisibilityPolicy.Evaluate(_vm.Phase, _vm.IsPlaying, _vm.IsInteractive, _sinceInteraction, overlay);
            ApplyChrome(state);
            PositionHint();
            Ctx.Glass.SetSource(_vm.RenderTarget, _render);
            TextureSlot.Set(_render, _vm.RenderTarget);
        }

        void ApplyChrome(ChromeState state)
        {
            if (state.Interactive == _chromeShown && Mathf.Approximately(state.Opacity, _chromeOpacity)) return;
            _chromeShown = state.Interactive;
            float from = _chromeTop.resolvedStyle.opacity;
            float to = state.Opacity;
            _chromeOpacity = to;
            if (state.Interactive)
            {
                _chromeTop.style.visibility = Visibility.Visible;
                _chromeBottom.style.visibility = Visibility.Visible;
            }
            _chromeTween?.Cancel();
            _chromeTween = Tween.Run(_chromeTop, 0.3f, Ease.OutCubic, k =>
            {
                float o = Mathf.Lerp(from, to, k);
                _chromeTop.style.opacity = o;
                _chromeBottom.style.opacity = o;
            }, () =>
            {
                if (!_chromeShown)
                {
                    _chromeTop.style.visibility = Visibility.Hidden;
                    _chromeBottom.style.visibility = Visibility.Hidden;
                }
            });
        }

        void PositionHint()
        {
            var target = HintTarget();
            if (target == null || _hint.resolvedStyle.display == DisplayStyle.None) return;
            var tb = target.worldBound;
            var rb = Root.worldBound;
            float w = _hint.layout.width;
            float h = _hint.layout.height;
            if (float.IsNaN(w) || float.IsNaN(h)) return;
            float x = Mathf.Clamp(tb.center.x - rb.x - w * 0.5f, 12f, rb.width - w - 12f);
            bool above = tb.center.y > rb.center.y;
            float y = above ? tb.y - rb.y - h - 12f : tb.yMax - rb.y + 12f;
            _hint.style.left = x;
            _hint.style.top = y;
        }

        void OnAnyPointerDown(PointerDownEvent e)
        {
            if (_vm.ActiveHint != HintStep.None) _vm.DismissHint();
            if (_vm.IsInteractive && _vm.Phase == SimPhaseView.Running && _vm.IsPlaying)
            {
                // Interactive game: only touches outside the arena (bar strips) bring the bars back (spec §4.3).
                float y = e.position.y;
                bool inTopStrip = y <= _chromeTop.worldBound.yMax;
                bool inBottomStrip = y >= _chromeBottom.worldBound.y;
                if (!inTopStrip && !inBottomStrip) return;
            }
            _sinceInteraction = 0f;
        }

        Vector2 ArenaUv(Vector3 panelPosition)
        {
            var wb = _render.worldBound;
            float lx = Mathf.Clamp01((panelPosition.x - wb.x) / Mathf.Max(1f, wb.width));
            float ly = Mathf.Clamp01((panelPosition.y - wb.y) / Mathf.Max(1f, wb.height));
            var tex = _vm.RenderTarget;
            float texAspect = tex != null && tex.height > 0 ? tex.width / (float)tex.height : wb.width / Mathf.Max(1f, wb.height);
            AspectFit.CoverUv(texAspect, wb.width / Mathf.Max(1f, wb.height), out float sx, out float sy, out float ox, out float oy);
            return new Vector2(lx * sx + ox, (1f - ly) * sy + oy);
        }

        void OnArenaDown(PointerDownEvent e)
        {
            if (!_vm.IsInteractive || _arenaPointer >= 0) return;
            _arenaPointer = e.pointerId;
            _render.CapturePointer(_arenaPointer);
            _vm.OnArenaPointer(ArenaPointerPhase.Down, ArenaUv(e.position), e.pointerId);
        }

        void OnArenaMove(PointerMoveEvent e)
        {
            if (e.pointerId != _arenaPointer) return;
            _vm.OnArenaPointer(ArenaPointerPhase.Move, ArenaUv(e.position), e.pointerId);
        }

        void OnArenaUp(PointerUpEvent e)
        {
            if (e.pointerId != _arenaPointer) return;
            _vm.OnArenaPointer(ArenaPointerPhase.Up, ArenaUv(e.position), e.pointerId);
            ReleaseArena();
        }

        void OnArenaCancel(PointerCancelEvent e)
        {
            if (e.pointerId != _arenaPointer) return;
            _vm.OnArenaPointer(ArenaPointerPhase.Cancel, ArenaUv(e.position), e.pointerId);
            ReleaseArena();
        }

        void ReleaseArena()
        {
            int id = _arenaPointer;
            _arenaPointer = -1;
            if (id >= 0 && _render.HasPointerCapture(id)) _render.ReleasePointer(id);
        }
    }
}
