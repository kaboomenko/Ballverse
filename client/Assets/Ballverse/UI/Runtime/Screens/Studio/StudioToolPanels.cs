using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Params;
using Ballverse.UI.Components;
using Ballverse.UI.Format;
using Ballverse.UI.Layout;
using Ballverse.UI.Params;
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
        SliderRow _divider;
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
            _proportion = Form.Slider("studio.layout.proportion", MagnetSnap.ProportionMin, MagnetSnap.ProportionMax, 1, 50, Percent,
                (v, final) => t.SetProportion((int)Math.Round(v)));
            _proportion.Slider.Snap = v => MagnetSnap.SnapProportion((int)Math.Round(v));
            _pipSize = Form.Slider("studio.layout.pip_size", 20, 60, 1, 35, Percent, (v, final) => t.SetPipSize((int)Math.Round(v)));
            _pipShape = Form.Segmented("studio.layout.pip_shape",
                new[] { Ctx.L("studio.layout.pip_rounded"), Ctx.L("studio.layout.pip_circle") }, 0,
                i => t.SetPipShape(i == 0 ? PipShape.RoundedRect : PipShape.Circle));
            _divider = Form.Slider("studio.layout.divider", 0, 16, 1, 0, Px, (v, final) => t.SetDivider((int)Math.Round(v)));
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
            _swap.parent.style.display = split || pip ? DisplayStyle.Flex : DisplayStyle.None;
            _proportion.SetValueWithoutNotify(t.ProportionPercent);
            _proportion.Root.style.display = split ? DisplayStyle.Flex : DisplayStyle.None;
            _pipSize.SetValueWithoutNotify(t.PipSizePercent);
            _pipSize.Root.style.display = pip ? DisplayStyle.Flex : DisplayStyle.None;
            _pipShape.SetSelectedWithoutNotify(t.PipShape == PipShape.Circle ? 1 : 0);
            _pipShape.parent.style.display = pip ? DisplayStyle.Flex : DisplayStyle.None;
            _divider.SetValueWithoutNotify(t.DividerPx);
            _divider.Root.style.display = split ? DisplayStyle.Flex : DisplayStyle.None;
            _glassDivider.SetValueWithoutNotify(t.GlassDivider);
            _glassDivider.parent.style.display = split ? DisplayStyle.Flex : DisplayStyle.None;
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
        BvSwitch _mirror;
        SliderRow _volume;
        BvSwitch _muted;
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
            edit.Button("studio.video.rotate", IconKind.Rotate, () => t.Rotate90());
            _mirror = edit.Toggle("studio.video.mirror", false, v => t.SetMirrored(v));
            _volume = edit.Slider("studio.video.volume", 0, 200, 1, 100, Percent, (v, final) => t.SetVolume((int)Math.Round(v)));
            _muted = edit.Toggle("studio.video.mute", false, v => t.SetMuted(v));
            edit.Section("studio.video.nudge");
            edit.Button("studio.video.nudge_back", IconKind.Minus, () => Vm.NudgeVideo(-1));
            edit.Button("studio.video.nudge_forward", IconKind.Plus, () => Vm.NudgeVideo(1));
        }

        public override void Refresh()
        {
            var t = Vm.Video;
            Ctx.Binder.SetKey(_importLabel, t.HasVideo ? "studio.video.replace" : "studio.video.import");
            _noVideo.SetValueWithoutNotify(t.NoVideo);
            _editRows.style.display = t.HasVideo && !t.NoVideo ? DisplayStyle.Flex : DisplayStyle.None;
            _fill.SetSelectedWithoutNotify(t.Fill == SlotFill.Fit ? 1 : 0);
            _mirror.SetValueWithoutNotify(t.Mirrored);
            _volume.SetValueWithoutNotify(t.VolumePercent);
            _muted.SetValueWithoutNotify(t.Muted);
        }
    }

    internal sealed class SimToolPanel : StudioToolPanel
    {
        static readonly SimAfterEnd[] s_after = { SimAfterEnd.Freeze, SimAfterEnd.SlowMoReplay, SimAfterEnd.Loop, SimAfterEnd.ContinueSim };
        SliderRow _speed;
        SliderRow _slowMo;
        SegmentedControl _slowFactor;
        ChipRow _after;
        ChipRow _pack;
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
            _slowMo = Form.Slider("studio.sim.slowmo", 0, 3, 0.5, 0, v => v <= 0 ? Ctx.L("common.off") : Seconds(v),
                (v, final) =>
                {
                    if (final) t.SetSlowMo((float)v, t.SlowMoFactor);
                });
            _slowFactor = Form.Segmented("studio.sim.slowmo_factor",
                new[] { DisplayFormat.Multiplier(0.25, Ctx.Localizer), DisplayFormat.Multiplier(0.5, Ctx.Localizer) }, 1,
                i => t.SetSlowMo(t.SlowMoSeconds, i == 0 ? 0.25f : 0.5f));
            var afterLabels = new string[s_after.Length];
            for (int i = 0; i < s_after.Length; i++) afterLabels[i] = Ctx.L("studio.sim.after." + s_after[i].ToString().ToLowerInvariant());
            _after = Form.Chips("studio.sim.after_end", afterLabels, 0, i => t.SetAfterEnd(s_after[i]));
            var packs = new string[t.SoundPacks.Count];
            for (int i = 0; i < packs.Length; i++) packs[i] = Ctx.L("params.soundPack." + t.SoundPacks[i]);
            _pack = Form.Chips("studio.sim.sound_pack", packs, 0, i => t.SetSoundPack(Vm.Sim.SoundPacks[i]));
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
            _slowMo.SetValueWithoutNotify(t.SlowMoSeconds);
            _slowFactor.SetSelectedWithoutNotify(t.SlowMoFactor <= 0.3f ? 0 : 1);
            _slowFactor.parent.style.display = t.SlowMoSeconds > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _after.SetSelectedWithoutNotify(Array.IndexOf(s_after, t.AfterEnd));
            _pack.SetSelectedWithoutNotify(IndexOf(t.SoundPacks, t.SoundPack));
        }
    }

    internal sealed class TextToolPanel : StudioToolPanel
    {
        int _builtLayers = -1;
        int _builtSelected = -2;

        public TextToolPanel(UiContext ctx, IStudioViewModel vm) : base(ctx, vm) { }

        public override string TitleKey => "studio.tool.text";

        public override bool NeedsRebuild => Vm.Text.Layers.Count != _builtLayers || Vm.Text.SelectedIndex != _builtSelected;

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

            if (t.SelectedIndex < 0 || t.SelectedIndex >= t.Layers.Count) return;
            var sel = t.Layers[t.SelectedIndex];
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
            Form.Segmented("studio.text.plate",
                new[] { Ctx.L("studio.text.plate_none"), Ctx.L("studio.text.plate_solid"), Ctx.L("studio.text.plate_glass") },
                (int)sel.Plate, i => t.SetPlate(s, (TextPlate)i));
            Form.Segmented("studio.text.align", new string[3], (int)sel.Align, i => t.SetAlign(s, (TextAlign)i),
                new[] { IconKind.AlignLeft, IconKind.AlignCenter, IconKind.AlignRight });
            if (t.Animations.Count > 0)
            {
                var anims = new string[t.Animations.Count];
                for (int i = 0; i < anims.Length; i++) anims[i] = Ctx.L("studio.text.anim." + t.Animations[i]);
                Form.Chips("studio.text.animation", anims, IndexOf(t.Animations, sel.AnimationId), i => t.SetAnimation(s, t.Animations[i]));
            }
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
            _fadeIn.SetValueWithoutNotify(t.FadeInSeconds);
            _fadeOut.SetValueWithoutNotify(t.FadeOutSeconds);
            _voice.SetValueWithoutNotify(t.VoicePriority);
            _normalize.SetValueWithoutNotify(t.Normalize);
        }
    }
}
