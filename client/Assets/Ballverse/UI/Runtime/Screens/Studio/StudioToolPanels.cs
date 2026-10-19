using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Params;
using Ballverse.Core.Projects;
using Ballverse.UI.Components;
using Ballverse.UI.Format;
using Ballverse.UI.Layout;
using Ballverse.UI.Params;
using Ballverse.UI.Studio;
using Ballverse.UI.ViewModels;
using UnityEngine.UIElements;

namespace Ballverse.UI.Screens
{
    /// <summary>
    /// Contents of the studio tool sheets (spec §4.3 Макет · Видео · Симуляция · Текст · Звук). Each panel builds its
    /// rows once and refreshes values in place when the view model changes, so sliders keep working while dragged.
    /// </summary>
    internal abstract class StudioToolPanel
    {
        protected readonly UiContext Ctx;
        protected readonly IStudioViewModel Vm;
        protected FormBuilder Form;

        protected StudioToolPanel(UiContext ctx, IStudioViewModel vm)
        {
            Ctx = ctx;
            Vm = vm;
        }

        public abstract string TitleKey { get; }

        public void Build(VisualElement container)
        {
            container.Clear();
            Form = new FormBuilder(Ctx, container);
            OnBuild();
            Refresh();
        }

        protected abstract void OnBuild();
        public abstract void Refresh();

        /// <summary>Panels whose row set depends on values (e.g. text layers) rebuild instead of refreshing.</summary>
        public virtual bool NeedsRebuild => false;

        protected string Percent(double v)
        {
            return ParamValueFormatter.FormatWithUnit(v, 1, "%", Ctx.Localizer);
        }

        protected string Px(double v)
        {
            return ParamValueFormatter.FormatWithUnit(v, 1, "px", Ctx.Localizer);
        }

        protected string Seconds(double v)
        {
            return ParamValueFormatter.FormatWithUnit(v, 0.1, "s", Ctx.Localizer);
        }

        protected static void Show(VisualElement element, bool visible)
        {
            if (element != null) element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>"Фон полей (Fit)" row: blurred copy, colour, theme colour (spec §3.3).</summary>
        protected SegmentedControl FitBackgroundRow(FormBuilder form, Action<int> changed)
        {
            return form.Segmented("studio.fit_bg",
                new[] { Ctx.L("studio.fit_bg.blur"), Ctx.L("studio.fit_bg.color"), Ctx.L("studio.fit_bg.theme") }, 0, changed);
        }

        protected string Clock(long us)
        {
            return DisplayFormat.Clock(us, true);
        }

        protected static int IndexOf<T>(IReadOnlyList<T> list, T value)
        {
            for (int i = 0; i < list.Count; i++)
                if (EqualityComparer<T>.Default.Equals(list[i], value)) return i;
            return -1;
        }
    }

    internal sealed class LayoutToolPanel : StudioToolPanel
    {
        static readonly LayoutType[] s_layouts = { LayoutType.L1, LayoutType.L2, LayoutType.L3, LayoutType.L4, LayoutType.L5 };
        SegmentedControl _layout;
        BvSwitch _swap;
        SliderRow _proportion;
        SliderRow _pipSize;
        SegmentedControl _pipShape;
        SliderRow _pipBorder;
        ColorSwatches _pipBorderColor;
        BvSwitch _pipShadow;
        Label _pipNote;
        SliderRow _divider;
        ColorSwatches _dividerColor;
        BvSwitch _glassDivider;
        SliderRow _radius;
        SliderRow _padding;
        BvSwitch _zones;

        public LayoutToolPanel(UiContext ctx, IStudioViewModel vm) : base(ctx, vm) { }

        public override string TitleKey => "studio.tool.layout";

        protected override void OnBuild()
        {
            var t = Vm.Layout;
            var labels = new string[s_layouts.Length];
            for (int i = 0; i < labels.Length; i++) labels[i] = Ctx.L("studio.layout." + s_layouts[i].ToString().ToLowerInvariant());
            _layout = Form.Segmented("studio.layout.type", labels, 0, i => t.SetLayout(s_layouts[i]));
            _swap = Form.Toggle("studio.layout.swap", false, v => t.SetSwapped(v));
            // 1% steps; Core RatioMagnets turns 33 / 50 / 67 into exactly 1/3, 1/2, 2/3 and keeps every step reachable.
            _proportion = Form.Slider("studio.layout.proportion", StudioValues.RatioMinPercent, StudioValues.RatioMaxPercent, 1, 50,
                Percent, (v, final) => t.SetRatio(StudioValues.RatioFromPercent(v)));
            _pipSize = Form.Slider("studio.layout.pip_size", 20, 60, 1, 35, Percent, (v, final) => t.SetPipSize((int)Math.Round(v)));
            _pipShape = Form.Segmented("studio.layout.pip_shape",
                new[] { Ctx.L("studio.layout.pip_rounded"), Ctx.L("studio.layout.pip_circle") }, 0,
                i => t.SetPipShape(StudioValues.At(StudioValues.PipShapeOrder, i)));
            _pipBorder = Form.Slider("studio.layout.pip_border", 0, ProjectLimits.MaxPipBorderPx, 1, 0, Px,
                (v, final) => t.SetPipBorder((int)Math.Round(v)));
            _pipBorderColor = Form.Colors("studio.layout.pip_border_color", t.PipBorderColor, c => t.SetPipBorderColor(c));
            _pipShadow = Form.Toggle("studio.layout.pip_shadow", true, v => t.SetPipShadow(v));
            _pipNote = Form.NoteKey("studio.layout.pip_drag_note");
            _divider = Form.Slider("studio.layout.divider", 0, 16, 1, 0, Px, (v, final) => t.SetDivider((int)Math.Round(v)));
            _dividerColor = Form.Colors("studio.layout.divider_color", t.DividerColor, c => t.SetDividerColor(c));
            _glassDivider = Form.Toggle("studio.layout.glass_divider", false, v => t.SetGlassDivider(v));
            _radius = Form.Slider("studio.layout.corner_radius", 0, 48, 1, 0, Px, (v, final) => t.SetCornerRadius((int)Math.Round(v)));
            _padding = Form.Slider("studio.layout.padding", 0, 48, 1, 0, Px, (v, final) => t.SetPadding((int)Math.Round(v)));
            _zones = Form.Toggle("studio.layout.zones", false, v => t.SetPlatformZones(v), "studio.layout.zones_note");
        }

        public override void Refresh()
        {
            var t = Vm.Layout;
            _layout.SetSelectedWithoutNotify(Array.IndexOf(s_layouts, t.Layout));
            bool split = t.Layout == LayoutType.L1 || t.Layout == LayoutType.L2;
            bool pip = t.Layout == LayoutType.L3 || t.Layout == LayoutType.L5;
            _swap.SetValueWithoutNotify(t.Swapped);
            Show(_swap.parent, split || pip);
            _proportion.SetValueWithoutNotify(StudioValues.PercentFromRatio(t.Ratio));
            Show(_proportion.Root, split);
            _pipSize.SetValueWithoutNotify(t.PipSizePercent);
            Show(_pipSize.Root, pip);
            _pipShape.SetSelectedWithoutNotify(StudioValues.IndexIn(StudioValues.PipShapeOrder, t.PipShape));
            Show(_pipShape.parent, pip);
            _pipBorder.SetValueWithoutNotify(t.PipBorderPx);
            Show(_pipBorder.Root, pip);
            _pipBorderColor.SetSelectedWithoutNotify(t.PipBorderColor);
            Show(_pipBorderColor.parent, pip && t.PipBorderPx > 0);
            _pipShadow.SetValueWithoutNotify(t.PipShadow);
            Show(_pipShadow.parent, pip);
            Show(_pipNote, pip);
            _divider.SetValueWithoutNotify(t.DividerPx);
            Show(_divider.Root, split);
            _dividerColor.SetSelectedWithoutNotify(t.DividerColor);
            Show(_dividerColor.parent, split && t.DividerPx > 0 && !t.GlassDivider);
            _glassDivider.SetValueWithoutNotify(t.GlassDivider);
            Show(_glassDivider.parent, split);
            _radius.SetValueWithoutNotify(t.CornerRadiusPx);
            _padding.SetValueWithoutNotify(t.PaddingPx);
            _zones.SetValueWithoutNotify(t.PlatformZones);
        }
    }

    internal sealed class VideoToolPanel : StudioToolPanel
    {
        VisualElement _import;
        Label _importLabel;
        BvSwitch _noVideo;
        SegmentedControl _fill;
        SegmentedControl _fitBackground;
        ColorSwatches _fitColor;
        BvSwitch _mirror;
        SliderRow _volume;
        BvSwitch _muted;
        ChipRow _afterEnd;
        VisualElement _editRows;

        public VideoToolPanel(UiContext ctx, IStudioViewModel vm) : base(ctx, vm) { }

        public override string TitleKey => "studio.tool.video";

        protected override void OnBuild()
        {
            var t = Vm.Video;
            _import = Form.Button("studio.video.import", IconKind.Video, () => t.ImportOrReplace(), "primary");
            _importLabel = _import.Q<Label>();
            _noVideo = Form.Toggle("studio.no_video", false, v => t.SetNoVideo(v), "studio.video.no_video_note");
            _editRows = new VisualElement();
            Form.Container.Add(_editRows);
            var edit = new FormBuilder(Ctx, _editRows);
            _fill = edit.Segmented("studio.video.fill", new[] { Ctx.L("studio.video.fill_cover"), Ctx.L("studio.video.fill_fit") }, 0,
                i => t.SetFill(i == 0 ? SlotFill.Cover : SlotFill.Fit));
            _fitBackground = FitBackgroundRow(edit, i => t.SetFitBackground(StudioValues.At(StudioValues.FitBackgroundOrder, i)));
            _fitColor = edit.Colors("studio.fit_bg.color_pick", t.FitBackgroundColor, c => t.SetFitBackgroundColor(c));
            edit.NoteKey("studio.video.gesture_note");
            edit.Button("studio.video.rotate", IconKind.Rotate, () => t.Rotate90());
            _mirror = edit.Toggle("studio.video.mirror", false, v => t.SetMirrored(v));
            _volume = edit.Slider("studio.video.volume", 0, 200, 1, 100, Percent, (v, final) => t.SetVolume((int)Math.Round(v)));
            _muted = edit.Toggle("studio.video.mute", false, v => t.SetMuted(v));
            var afterLabels = new string[StudioValues.VideoAfterEndOrder.Length];
            for (int i = 0; i < afterLabels.Length; i++)
                afterLabels[i] = Ctx.L("studio.video.after." + StudioValues.VideoAfterEndOrder[i].ToString().ToLowerInvariant());
            _afterEnd = edit.Chips("studio.video.after_end", afterLabels, 0,
                i => t.SetAfterEnd(StudioValues.At(StudioValues.VideoAfterEndOrder, i)));
            edit.Section("studio.video.nudge");
            edit.Button("studio.video.nudge_back", IconKind.Minus, () => Vm.NudgeVideo(-1));
            edit.Button("studio.video.nudge_forward", IconKind.Plus, () => Vm.NudgeVideo(1));
        }

        public override void Refresh()
        {
            var t = Vm.Video;
            Ctx.Binder.SetKey(_importLabel, t.HasVideo ? "studio.video.replace" : "studio.video.import");
            _noVideo.SetValueWithoutNotify(t.NoVideo);
            Show(_editRows, t.HasVideo && !t.NoVideo);
            _fill.SetSelectedWithoutNotify(t.Fill == SlotFill.Fit ? 1 : 0);
            _fitBackground.SetSelectedWithoutNotify(StudioValues.IndexIn(StudioValues.FitBackgroundOrder, t.FitBackground));
            Show(_fitBackground.parent, t.Fill == SlotFill.Fit);
            _fitColor.SetSelectedWithoutNotify(t.FitBackgroundColor);
            Show(_fitColor.parent, t.Fill == SlotFill.Fit && t.FitBackground == FitBackgroundKind.Color);
            _mirror.SetValueWithoutNotify(t.Mirrored);
            _volume.SetValueWithoutNotify(t.VolumePercent);
            _muted.SetValueWithoutNotify(t.Muted);
            _afterEnd.SetSelectedWithoutNotify(StudioValues.IndexIn(StudioValues.VideoAfterEndOrder, t.AfterEnd));
        }
    }

    internal sealed class SimToolPanel : StudioToolPanel
    {
        static readonly SimAfterEnd[] s_after = { SimAfterEnd.Freeze, SimAfterEnd.SlowMoReplay, SimAfterEnd.Loop, SimAfterEnd.ContinueSim };
        SliderRow _speed;
        BvSwitch _slowMoOn;
        SliderRow _slowMo;
        SegmentedControl _slowFactor;
        ChipRow _after;
        SegmentedControl _fill;
        SegmentedControl _fitBackground;
        ColorSwatches _fitColor;
        ChipRow _pack;
        SegmentedControl _duration;
        SliderRow _manualDuration;
        readonly Action _regenerate;

        public SimToolPanel(UiContext ctx, IStudioViewModel vm, Action regenerate) : base(ctx, vm)
        {
            _regenerate = regenerate;
        }

        public override string TitleKey => "studio.tool.simulation";

        protected override void OnBuild()
        {
            var t = Vm.Sim;
            _speed = Form.Slider("studio.sim.speed", 0.5, 2.0, 0.05, 1.0, v => DisplayFormat.Multiplier(v, Ctx.Localizer),
                (v, final) =>
                {
                    if (final) t.SetSpeed((float)v);
                });
            // Spec §3.4: the finale slow-mo is off or 1–3 s (Core ClipTiming rejects anything in between).
            _slowMoOn = Form.Toggle("studio.sim.slowmo", false,
                on => t.SetSlowMo(StudioValues.SlowMoSeconds(on, _slowMo.Slider.Value), t.SlowMoFactor));
            _slowMo = Form.Slider("studio.sim.slowmo_length", StudioValues.SlowMoMinSeconds, StudioValues.SlowMoMaxSeconds,
                StudioValues.SlowMoStepSeconds, StudioValues.SlowMoMinSeconds, Seconds,
                (v, final) =>
                {
                    if (final) t.SetSlowMo(StudioValues.SlowMoSeconds(true, v), t.SlowMoFactor);
                });
            _slowFactor = Form.Segmented("studio.sim.slowmo_factor",
                new[] { DisplayFormat.Multiplier(0.25, Ctx.Localizer), DisplayFormat.Multiplier(0.5, Ctx.Localizer) }, 1,
                i => t.SetSlowMo(t.SlowMoSeconds, i == 0 ? 0.25f : 0.5f));
            var afterLabels = new string[s_after.Length];
            for (int i = 0; i < s_after.Length; i++) afterLabels[i] = Ctx.L("studio.sim.after." + s_after[i].ToString().ToLowerInvariant());
            _after = Form.Chips("studio.sim.after_end", afterLabels, 0, i => t.SetAfterEnd(s_after[i]));
            _fill = Form.Segmented("studio.sim.fill", new[] { Ctx.L("studio.video.fill_cover"), Ctx.L("studio.video.fill_fit") }, 0,
                i => t.SetFill(i == 0 ? SlotFill.Cover : SlotFill.Fit));
            _fitBackground = FitBackgroundRow(Form, i => t.SetFitBackground(StudioValues.At(StudioValues.FitBackgroundOrder, i)));
            _fitColor = Form.Colors("studio.fit_bg.color_pick", t.FitBackgroundColor, c => t.SetFitBackgroundColor(c));
            var packs = new string[t.SoundPacks.Count];
            for (int i = 0; i < packs.Length; i++) packs[i] = Ctx.L("params.soundPack." + t.SoundPacks[i]);
            _pack = Form.Chips("studio.sim.sound_pack", packs, 0, i => t.SetSoundPack(Vm.Sim.SoundPacks[i]));

            Form.Section("studio.reel.title");
            _duration = Form.Segmented("studio.reel.duration",
                new[] { Ctx.L("studio.reel.duration_auto"), Ctx.L("studio.reel.duration_video"), Ctx.L("studio.reel.duration_manual") }, 0,
                i => Vm.SetDurationMode(StudioValues.At(StudioValues.DurationModeOrder, i)));
            _manualDuration = Form.Slider("studio.reel.manual_length", 1, SimConstants.MaxDurationSeconds, 1, 15, Seconds,
                (v, final) =>
                {
                    if (final) Vm.SetManualDuration((long)Math.Round(v) * TimelineGeometry.MicrosPerSecond);
                });
            Form.Button("studio.sim.regenerate", IconKind.Dice, () =>
            {
                t.Regenerate();
                _regenerate?.Invoke();
            });
        }

        public override void Refresh()
        {
            var t = Vm.Sim;
            _speed.SetValueWithoutNotify(t.Speed);
            bool slowMo = t.SlowMoSeconds > 0f;
            _slowMoOn.SetValueWithoutNotify(slowMo);
            if (slowMo) _slowMo.SetValueWithoutNotify(t.SlowMoSeconds);
            Show(_slowMo.Root, slowMo);
            _slowFactor.SetSelectedWithoutNotify(t.SlowMoFactor <= 0.3f ? 0 : 1);
            Show(_slowFactor.parent, slowMo);
            _after.SetSelectedWithoutNotify(Array.IndexOf(s_after, t.AfterEnd));
            _fill.SetSelectedWithoutNotify(t.Fill == SlotFill.Fit ? 1 : 0);
            _fitBackground.SetSelectedWithoutNotify(StudioValues.IndexIn(StudioValues.FitBackgroundOrder, t.FitBackground));
            Show(_fitBackground.parent, t.Fill == SlotFill.Fit);
            _fitColor.SetSelectedWithoutNotify(t.FitBackgroundColor);
            Show(_fitColor.parent, t.Fill == SlotFill.Fit && t.FitBackground == FitBackgroundKind.Color);
            _pack.SetSelectedWithoutNotify(IndexOf(t.SoundPacks, t.SoundPack));
            _duration.SetSelectedWithoutNotify(StudioValues.IndexIn(StudioValues.DurationModeOrder, Vm.DurationMode));
            // "= V" needs a user video.
            _duration.SetSegmentEnabled(1, Vm.Video.HasVideo && !Vm.Video.NoVideo);
            _manualDuration.SetValueWithoutNotify(Math.Max(1, Vm.ManualDurationUs / TimelineGeometry.MicrosPerSecond));
            Show(_manualDuration.Root, Vm.DurationMode == DurationMode.Manual);
        }
    }

    internal sealed class TextToolPanel : StudioToolPanel
    {
        int _builtLayers = -1;
        int _builtSelected = -2;
        long _builtStart = -1;
        long _builtEnd = -1;

        public TextToolPanel(UiContext ctx, IStudioViewModel vm) : base(ctx, vm) { }

        public override string TitleKey => "studio.tool.text";

        public override bool NeedsRebuild
        {
            get
            {
                var t = Vm.Text;
                if (t.Layers.Count != _builtLayers || t.SelectedIndex != _builtSelected) return true;
                // Timing changes come from the timeline drag; the timing line follows them.
                var sel = Selected(t);
                return sel != null && (sel.StartUs != _builtStart || sel.EndUs != _builtEnd);
            }
        }

        static TextLayerData Selected(IStudioTextTool t)
        {
            return t.SelectedIndex >= 0 && t.SelectedIndex < t.Layers.Count ? t.Layers[t.SelectedIndex] : null;
        }

        protected override void OnBuild()
        {
            var t = Vm.Text;
            _builtLayers = t.Layers.Count;
            _builtSelected = t.SelectedIndex;
            Form.Section("studio.text.templates");
            var templates = new string[t.Templates.Count];
            for (int i = 0; i < templates.Length; i++) templates[i] = Ctx.L("studio.text.template." + t.Templates[i]);
            bool canAdd = t.Layers.Count < t.MaxLayers;
            var chips = Form.Chips(null, templates, -1, i => t.AddFromTemplate(t.Templates[i]));
            chips.SetEnabled(canAdd);
            Form.Segmented("studio.text.template_language", new[] { Ctx.L("language.en_short"), Ctx.L("language.ru_short") }, t.TemplateLanguage == "ru" ? 1 : 0,
                i => t.SetTemplateLanguage(i == 0 ? "en" : "ru"));
            var add = Form.Button("studio.text.add", IconKind.Plus, () => t.AddEmpty());
            add.SetEnabled(canAdd);
            Form.Note(Ctx.Format("studio.text.count", t.Layers.Count, t.MaxLayers));

            if (t.Layers.Count > 0)
            {
                Form.Section("studio.text.layers");
                foreach (var layer in t.Layers)
                {
                    int index = layer.Index;
                    var row = Form.NavRow(IconKind.Text, "studio.text.layer", layer.Text, () => t.Select(index), false, false);
                    row.EnableInClassList("bv-row--selected", index == t.SelectedIndex);
                }
            }

            var sel = Selected(t);
            _builtStart = sel?.StartUs ?? -1;
            _builtEnd = sel?.EndUs ?? -1;
            if (sel == null) return;
            int s = sel.Index;
            Form.Section("studio.text.style");
            Form.Text("studio.text.content", sel.Text, 200, true, v => t.SetText(s, v));
            if (t.FontNames.Count > 0) Form.Chips("studio.text.font", ToArray(t.FontNames), sel.FontIndex, i => t.SetFont(s, i));
            Form.Slider("studio.text.size", 12, 160, 1, sel.SizePx, Px, (v, final) =>
            {
                if (final) t.SetSize(s, (int)Math.Round(v));
            });
            Form.Colors("studio.text.fill", sel.Fill, c => t.SetFill(s, c));
            Form.Slider("studio.text.outline", 0, 8, 1, sel.OutlinePx, Px, (v, final) =>
            {
                if (final) t.SetOutline(s, (int)Math.Round(v));
            });
            Form.Toggle("studio.text.shadow", sel.Shadow, v => t.SetShadow(s, v));
            // Segments map to Core enums by name (StudioValues), never by casting the index.
            Form.Segmented("studio.text.plate",
                new[] { Ctx.L("studio.text.plate_none"), Ctx.L("studio.text.plate_solid"), Ctx.L("studio.text.plate_glass") },
                StudioValues.IndexIn(StudioValues.PlateOrder, sel.Plate), i => t.SetPlate(s, StudioValues.At(StudioValues.PlateOrder, i)));
            Form.Segmented("studio.text.align", new string[3], StudioValues.IndexIn(StudioValues.AlignOrder, sel.Align),
                i => t.SetAlign(s, StudioValues.At(StudioValues.AlignOrder, i)),
                new[] { IconKind.AlignLeft, IconKind.AlignCenter, IconKind.AlignRight });
            if (t.Animations.Count > 0)
            {
                var anims = new string[t.Animations.Count];
                for (int i = 0; i < anims.Length; i++) anims[i] = Ctx.L("studio.text.anim." + t.Animations[i]);
                Form.Chips("studio.text.animation", anims, IndexOf(t.Animations, sel.AnimationId), i => t.SetAnimation(s, t.Animations[i]));
            }
            Form.Note(Ctx.Format("studio.text.timing", Clock(sel.StartUs), Clock(sel.EndUs)));
            Form.NoteKey("studio.text.timing_note");
            Form.Button("studio.text.remove", IconKind.Trash, () => t.Remove(s), "destructive");
        }

        public override void Refresh()
        {
        }

        static string[] ToArray(IReadOnlyList<string> list)
        {
            var a = new string[list.Count];
            for (int i = 0; i < a.Length; i++) a[i] = list[i];
            return a;
        }
    }

    internal sealed class SoundToolPanel : StudioToolPanel
    {
        SliderRow _sim;
        SliderRow _video;
        ChipRow _music;
        SliderRow _musicVolume;
        SliderRow _musicStart;
        long _musicStartRange = -1;
        SliderRow _fadeIn;
        SliderRow _fadeOut;
        BvSwitch _normalize;
        BvSwitch _voice;
        VisualElement _musicRows;
        readonly List<string> _trackIds = new List<string>();

        public SoundToolPanel(UiContext ctx, IStudioViewModel vm) : base(ctx, vm) { }

        public override string TitleKey => "studio.tool.sound";

        protected override void OnBuild()
        {
            var t = Vm.Sound;
            _sim = Form.Slider("studio.sound.sim_volume", 0, 200, 1, 100, Percent, (v, final) => t.SetSimVolume((int)Math.Round(v)));
            _video = Form.Slider("studio.sound.video_volume", 0, 200, 1, 100, Percent, (v, final) => t.SetVideoVolume((int)Math.Round(v)));
            _trackIds.Clear();
            var labels = new List<string> { Ctx.L("studio.sound.no_music") };
            _trackIds.Add(null);
            foreach (var m in t.MusicTracks)
            {
                labels.Add(Ctx.L(m.Name));
                _trackIds.Add(m.Id);
            }
            _music = Form.Chips("studio.sound.music", labels, 0, i => t.SetMusicTrack(_trackIds[i]));
            _musicRows = new VisualElement();
            Form.Container.Add(_musicRows);
            var mf = new FormBuilder(Ctx, _musicRows);
            _musicVolume = mf.Slider("studio.sound.music_volume", 0, 200, 1, 100, Percent, (v, final) => t.SetMusicVolume((int)Math.Round(v)));
            _musicStart = mf.Slider("studio.sound.music_start", 0, 1, 0.5, 0, Seconds, (v, final) =>
            {
                if (final) t.SetMusicStart((long)Math.Round(v * TimelineGeometry.MicrosPerSecond));
            });
            _fadeIn = mf.Slider("studio.sound.fade_in", 0, 2, 0.1, 0, Seconds, (v, final) =>
            {
                if (final) t.SetFades((float)v, t.FadeOutSeconds);
            });
            _fadeOut = mf.Slider("studio.sound.fade_out", 0, 2, 0.1, 0, Seconds, (v, final) =>
            {
                if (final) t.SetFades(t.FadeInSeconds, (float)v);
            });
            _voice = mf.Toggle("studio.sound.voice_priority", true, v => t.SetVoicePriority(v), "studio.sound.voice_priority_note");
            _normalize = Form.Toggle("studio.sound.normalize", true, v => t.SetNormalize(v), "studio.sound.normalize_note");
        }

        public override void Refresh()
        {
            var t = Vm.Sound;
            _sim.SetValueWithoutNotify(t.SimVolume);
            _video.SetValueWithoutNotify(t.VideoVolume);
            _video.Root.style.display = Vm.Video.HasVideo ? DisplayStyle.Flex : DisplayStyle.None;
            int index = _trackIds.IndexOf(t.MusicTrackId);
            _music.SetSelectedWithoutNotify(index < 0 ? 0 : index);
            _musicRows.style.display = t.MusicTrackId != null ? DisplayStyle.Flex : DisplayStyle.None;
            _musicVolume.SetValueWithoutNotify(t.MusicVolume);
            if (t.MusicTrackLengthUs != _musicStartRange)
            {
                _musicStartRange = t.MusicTrackLengthUs;
                double max = Math.Max(0, t.MusicTrackLengthUs - TimelineGeometry.MicrosPerSecond) / (double)TimelineGeometry.MicrosPerSecond;
                _musicStart.Slider.Configure(0, Math.Max(0.5, Math.Floor(max * 2) / 2), 0.5, 0);
            }
            _musicStart.SetValueWithoutNotify(t.MusicStartUs / (double)TimelineGeometry.MicrosPerSecond);
            Show(_musicStart.Root, t.MusicTrackLengthUs > TimelineGeometry.MicrosPerSecond);
            _fadeIn.SetValueWithoutNotify(t.FadeInSeconds);
            _fadeOut.SetValueWithoutNotify(t.FadeOutSeconds);
            _voice.SetValueWithoutNotify(t.VoicePriority);
            _normalize.SetValueWithoutNotify(t.Normalize);
        }
    }
}
