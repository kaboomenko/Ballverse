using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Config;
using Ballverse.Core.Layout;
using Ballverse.Core.Params;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Ballverse.UI.Localization;
using Ballverse.UI.ViewModels;

namespace Ballverse.App
{
    /// <summary>Tool «Макет» (spec §3.3) over the open project's layout settings.</summary>
    public sealed class StudioLayoutTool : IStudioLayoutTool
    {
        readonly StudioViewModel _vm;
        bool _zones;

        public StudioLayoutTool(StudioViewModel vm)
        {
            _vm = vm;
        }

        LayoutSettings L => _vm.Doc?.Layout ?? new LayoutSettings();
        PipSettings P => L.Pip ?? new PipSettings();

        public LayoutType Layout => L.Type;
        public bool Swapped => L.Swap;
        public float Ratio => (float)L.Ratio;
        public int PipSizePercent => (int)Math.Round(P.SizeFraction * 100.0);
        public PipShape PipShape => P.Shape;
        public FloatVec2 PipCenter => new FloatVec2((float)P.X, (float)P.Y);
        public int PipBorderPx => P.Border?.Px ?? 0;
        public ColorRgba PipBorderColor => StudioViewModel.ParseColor(P.Border?.Color, new ColorRgba(255, 255, 255));
        public bool PipShadow => P.Shadow;
        public int DividerPx => L.Divider?.Px ?? 0;
        public ColorRgba DividerColor => StudioViewModel.ParseColor(L.Divider?.Color, new ColorRgba(255, 255, 255, 51));
        public bool GlassDivider => L.Divider != null && L.Divider.Glass;
        public int CornerRadiusPx => L.CornerRadius;
        public int PaddingPx => L.Padding;
        public bool PlatformZones => _zones;

        public void SetLayout(LayoutType layout)
        {
            _vm.Edit(d =>
            {
                d.Layout.Type = layout;
                if ((layout == LayoutType.L3 || layout == LayoutType.L5) && d.Layout.Pip == null) d.Layout.Pip = new PipSettings();
            }, null);
        }

        public void SetSwapped(bool swapped) => _vm.Edit(d => d.Layout.Swap = swapped, null);

        public void SetRatio(float ratio)
        {
            float r = RatioMagnets.Snap(ratio);
            double v = StudioViewModel.Clamp(r, ProjectLimits.MinRatio, ProjectLimits.MaxRatio);
            _vm.Edit(d => d.Layout.Ratio = v, "ratio");
        }

        public void SetPipSize(int percent)
        {
            double f = StudioViewModel.Clamp(percent / 100.0, ProjectLimits.MinPipSize, ProjectLimits.MaxPipSize);
            EditPip(p => p.SizeFraction = f, "pip_size");
        }

        public void SetPipShape(PipShape shape) => EditPip(p => p.Shape = shape, null);

        public void MovePip(FloatVec2 center, bool final)
        {
            ProjectDocument doc = _vm.Doc;
            if (doc == null || !center.IsFinite) return;
            float x = Math.Max(0f, Math.Min(1f, center.X));
            float y = Math.Max(0f, Math.Min(1f, center.Y));
            if (final)
            {
                try
                {
                    LayoutSpec spec = ProjectComposition.BuildLayoutSpec(doc.Layout, doc.UserVideo != null);
                    if (spec.Pip != null)
                    {
                        RectUnion masks = RectUnion.FromNormalized(_vm.Ctx.Remote.MaskUnion, 1080, 1920);
                        PipSnapResult snap = PipMagnets.Snap(1080, 1920, spec.Pip.WithCenter(new FloatVec2(x, y)), new FloatVec2(x, y), masks);
                        FloatVec2 c = LayoutCalculator.PipCenterOf(snap.Rect, 1080, 1920);
                        x = c.X;
                        y = c.Y;
                    }
                }
                catch (ArgumentException e)
                {
                    _vm.Ctx.Notices.Report("studio.pip_snap", e);
                }
            }
            EditPip(p =>
            {
                p.X = x;
                p.Y = y;
            }, "pip_move");
        }

        public void SetPipBorder(int px) => EditPip(p =>
        {
            if (p.Border == null) p.Border = new PipBorder();
            p.Border.Px = StudioViewModel.Clamp(px, ProjectLimits.MinPipBorderPx, ProjectLimits.MaxPipBorderPx);
        }, "pip_border");

        public void SetPipBorderColor(ColorRgba color) => EditPip(p =>
        {
            if (p.Border == null) p.Border = new PipBorder();
            p.Border.Color = StudioViewModel.FormatColor(color);
        }, "pip_border_color");

        public void SetPipShadow(bool on) => EditPip(p => p.Shadow = on, null);

        public void SetDivider(int px) => _vm.Edit(d =>
        {
            if (d.Layout.Divider == null) d.Layout.Divider = new DividerSettings();
            d.Layout.Divider.Px = StudioViewModel.Clamp(px, ProjectLimits.MinDividerPx, ProjectLimits.MaxDividerPx);
        }, "divider");

        public void SetDividerColor(ColorRgba color) => _vm.Edit(d =>
        {
            if (d.Layout.Divider == null) d.Layout.Divider = new DividerSettings();
            d.Layout.Divider.Color = StudioViewModel.FormatColor(color);
        }, "divider_color");

        public void SetGlassDivider(bool glass) => _vm.Edit(d =>
        {
            if (d.Layout.Divider == null) d.Layout.Divider = new DividerSettings();
            d.Layout.Divider.Glass = glass;
        }, null);

        public void SetCornerRadius(int px) =>
            _vm.Edit(d => d.Layout.CornerRadius = StudioViewModel.Clamp(px, ProjectLimits.MinCornerRadius, ProjectLimits.MaxCornerRadius), "corner");

        public void SetPadding(int px) =>
            _vm.Edit(d => d.Layout.Padding = StudioViewModel.Clamp(px, ProjectLimits.MinPadding, ProjectLimits.MaxPadding), "padding");

        public void SetPlatformZones(bool visible)
        {
            _zones = visible;
            _vm.SetPlatformZones(visible);
        }

        void EditPip(Action<PipSettings> change, string key)
        {
            _vm.Edit(d =>
            {
                if (d.Layout.Pip == null) d.Layout.Pip = new PipSettings();
                change(d.Layout.Pip);
            }, key);
        }
    }

    /// <summary>Tool «Видео» (spec §3.2, §3.3) over the project's user video.</summary>
    public sealed class StudioVideoTool : IStudioVideoTool
    {
        readonly StudioViewModel _vm;
        double _volumeBeforeMute = 1.0;

        public StudioVideoTool(StudioViewModel vm)
        {
            _vm = vm;
        }

        UserVideoSettings V => _vm.Doc?.UserVideo;

        public bool HasVideo => V != null;
        public bool NoVideo => _vm.Doc != null && _vm.Doc.Layout.Type == LayoutType.L4;
        public SlotFill Fill => V?.Fill ?? SlotFill.Cover;
        public FitBackgroundKind FitBackground => V?.FitBackground?.Kind ?? FitBackgroundKind.Blur;
        public ColorRgba FitBackgroundColor => StudioViewModel.ParseColor(V?.FitBackground?.Color, new ColorRgba(0, 0, 0));
        public int RotationDeg => V?.Rotation ?? 0;
        public bool Mirrored => V != null && V.Mirror;
        public int VolumePercent => (int)Math.Round((V?.Volume ?? 1.0) * 100.0);
        public bool Muted => V != null && V.Volume <= 0.0;
        public VideoAfterEnd AfterEnd => V?.AfterEnd ?? VideoAfterEnd.Freeze;

        public void ImportOrReplace() => _vm.ImportVideo();

        public void SetNoVideo(bool noVideo)
        {
            _vm.Edit(d =>
            {
                if (noVideo) d.Layout.Type = LayoutType.L4;
                else if (d.Layout.Type == LayoutType.L4) d.Layout.Type = LayoutType.L1;
            }, null);
        }

        public void SetFill(SlotFill fill) => EditVideo(v => v.Fill = fill, null);

        public void SetFitBackground(FitBackgroundKind kind) => EditVideo(v =>
        {
            if (v.FitBackground == null) v.FitBackground = new Core.Projects.FitBackground();
            v.FitBackground.Kind = kind;
        }, null);

        public void SetFitBackgroundColor(ColorRgba color) => EditVideo(v =>
        {
            if (v.FitBackground == null) v.FitBackground = new Core.Projects.FitBackground();
            v.FitBackground.Color = StudioViewModel.FormatColor(color);
        }, "video_fit_color");

        public void SetAfterEnd(VideoAfterEnd afterEnd) => EditVideo(v => v.AfterEnd = afterEnd, null);

        public void Rotate90() => EditVideo(v => v.Rotation = ProjectComposition.SnapRotation(v.Rotation + 90), null);

        public void SetMirrored(bool mirrored) => EditVideo(v => v.Mirror = mirrored, null);

        public void SetVolume(int percent) =>
            EditVideo(v => v.Volume = StudioViewModel.Clamp(percent / 100.0, ProjectLimits.MinVolume, ProjectLimits.MaxVolume), "video_volume");

        public void SetMuted(bool muted)
        {
            EditVideo(v =>
            {
                if (muted)
                {
                    if (v.Volume > 0) _volumeBeforeMute = v.Volume;
                    v.Volume = 0;
                }
                else
                {
                    v.Volume = _volumeBeforeMute > 0 ? _volumeBeforeMute : 1.0;
                }
            }, null);
        }

        void EditVideo(Action<UserVideoSettings> change, string key)
        {
            _vm.Edit(d =>
            {
                if (d.UserVideo != null) change(d.UserVideo);
            }, key);
        }
    }

    /// <summary>Tool «Симуляция» (spec §3.4) over the project's clip settings.</summary>
    public sealed class StudioSimTool : IStudioSimTool
    {
        readonly StudioViewModel _vm;

        public StudioSimTool(StudioViewModel vm)
        {
            _vm = vm;
        }

        ClipSettings C => _vm.Doc?.Clip ?? new ClipSettings();

        public float Speed => (float)C.Speed;
        public float SlowMoSeconds => (float)(C.FinaleSlowMo?.Seconds ?? 0);
        public float SlowMoFactor => (float)(C.FinaleSlowMo?.Factor ?? 0.5);
        public SimAfterEnd AfterEnd => C.AfterEnd;
        public SlotFill Fill => C.Fill;
        public FitBackgroundKind FitBackground => C.FitBackground?.Kind ?? FitBackgroundKind.Blur;
        public ColorRgba FitBackgroundColor => StudioViewModel.ParseColor(C.FitBackground?.Color, new ColorRgba(0, 0, 0));
        public IReadOnlyList<string> SoundPacks => CommonParams.SoundPacks;
        public string SoundPack => C.SoundPack ?? "Notes";

        public void SetSpeed(float speed) =>
            _vm.Edit(d => d.Clip.Speed = StudioViewModel.Clamp(speed, ProjectLimits.MinSpeed, ProjectLimits.MaxSpeed), "clip_speed");

        public void SetSlowMo(float seconds, float factor)
        {
            double s = seconds <= 0f ? 0.0 : StudioViewModel.Clamp(seconds, ProjectLimits.MinActiveSlowMoSeconds, ProjectLimits.MaxSlowMoSeconds);
            double f = StudioViewModel.Clamp(factor, ProjectLimits.MinSlowMoFactor, ProjectLimits.MaxSlowMoFactor);
            _vm.Edit(d =>
            {
                if (d.Clip.FinaleSlowMo == null) d.Clip.FinaleSlowMo = new FinaleSlowMo();
                d.Clip.FinaleSlowMo.Seconds = s;
                d.Clip.FinaleSlowMo.Factor = f;
            }, "slowmo");
        }

        public void SetAfterEnd(SimAfterEnd afterEnd) => _vm.Edit(d => d.Clip.AfterEnd = afterEnd, null);

        public void SetFill(SlotFill fill) => _vm.Edit(d => d.Clip.Fill = fill, null);

        public void SetFitBackground(FitBackgroundKind kind) => _vm.Edit(d =>
        {
            if (d.Clip.FitBackground == null) d.Clip.FitBackground = new Core.Projects.FitBackground();
            d.Clip.FitBackground.Kind = kind;
        }, null);

        public void SetFitBackgroundColor(ColorRgba color) => _vm.Edit(d =>
        {
            if (d.Clip.FitBackground == null) d.Clip.FitBackground = new Core.Projects.FitBackground();
            d.Clip.FitBackground.Color = StudioViewModel.FormatColor(color);
        }, "clip_fit_color");

        public void SetSoundPack(string id)
        {
            string pack = null;
            foreach (string p in CommonParams.SoundPacks)
            {
                if (string.Equals(p, id, StringComparison.Ordinal)) pack = p;
            }
            if (pack == null) return;
            _vm.Edit(d => d.Clip.SoundPack = pack, null);
        }

        public void Regenerate() => _vm.Regenerate();
    }

    /// <summary>Tool «Текст» (spec §3.5): up to 5 layers from templates (Remote Config or built-in, in the template language).</summary>
    public sealed class StudioTextTool : IStudioTextTool
    {
        static readonly string[] s_templates = { "which_wins", "comment_color", "part_two", "dont_look_away", "who_survives", "remaining", "timer" };
        static readonly string[] s_animations = { "none", "fade", "slide", "typewriter", "bounce" };

        /// <summary>Font ids of the bundled OFL fonts (spec §3.5) and their display names.</summary>
        public static readonly string[] FontIds =
            { ProjectDefaults.Font, "inter_bold", "rubik_bold", "unbounded_bold", "montserrat_bold", "comfortaa_bold", "oswald_bold", "pacifico_regular" };

        static readonly string[] s_fontNames = { "Manrope", "Inter", "Rubik", "Unbounded", "Montserrat", "Comfortaa", "Oswald", "Pacifico" };

        static readonly Dictionary<string, string> s_en = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["which_wins"] = "Which ball wins?",
            ["comment_color"] = "Write your colour in the comments",
            ["part_two"] = "Part 2",
            ["dont_look_away"] = "Try not to look away",
            ["who_survives"] = "Who survives?",
            ["remaining"] = "Remaining: " + TextLayerTiming.CounterPlaceholder,
            ["timer"] = "Timer",
            ["new"] = "Your text",
        };

        static readonly Dictionary<string, string> s_ru = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["which_wins"] = "Какой шар победит?",
            ["comment_color"] = "Пиши свой цвет в комментариях",
            ["part_two"] = "Часть 2",
            ["dont_look_away"] = "Попробуй не отвести взгляд",
            ["who_survives"] = "Кто выживет?",
            ["remaining"] = "Осталось: " + TextLayerTiming.CounterPlaceholder,
            ["timer"] = "Таймер",
            ["new"] = "Твой текст",
        };

        readonly StudioViewModel _vm;
        readonly List<TextLayerData> _layers = new List<TextLayerData>();
        int _selected = -1;
        int _layersRevision = -1;
        ProjectDocument _layersDoc;

        public StudioTextTool(StudioViewModel vm)
        {
            _vm = vm;
        }

        public IReadOnlyList<string> Templates => s_templates;
        public int MaxLayers => ProjectLimits.MaxTextLayers;
        public int SelectedIndex => _selected < Layers.Count ? _selected : -1;
        public IReadOnlyList<string> FontNames => s_fontNames;
        public IReadOnlyList<string> Animations => s_animations;

        public string TemplateLanguage
        {
            get
            {
                string lang = _vm.Ctx.Settings.Data.TemplateLanguage;
                if (lang == "en" || lang == "ru") return lang;
                string device = _vm.Ctx.Device.LanguageTag ?? "";
                return device.StartsWith("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";
            }
        }

        public IReadOnlyList<TextLayerData> Layers
        {
            get
            {
                ProjectDocument doc = _vm.Doc;
                if (doc == null)
                {
                    _layers.Clear();
                    return _layers;
                }
                if (!ReferenceEquals(doc, _layersDoc) || doc.Texts.Count != _layersRevision || Stale(doc)) RebuildLayers(doc);
                return _layers;
            }
        }

        internal void ResetSelection()
        {
            _selected = -1;
            _layersDoc = null;
        }

        bool Stale(ProjectDocument doc)
        {
            for (int i = 0; i < doc.Texts.Count && i < _layers.Count; i++)
            {
                TextLayer t = doc.Texts[i];
                TextLayerData l = _layers[i];
                if (l.Text != (t.Text ?? "") || l.StartUs != t.InUs || l.EndUs != t.OutUs || l.SizePx != t.Size || l.FontIndex != FontIndex(t.Font)
                    || l.Plate != t.Plate || l.Align != t.Align || l.Shadow != t.Shadow || (l.AnimationId ?? "none") != (t.Anim ?? "none")
                    || l.OutlinePx != (t.Stroke?.Px ?? 0) || l.Fill != StudioViewModel.ParseColor(t.Fill, new ColorRgba(255, 255, 255)))
                    return true;
            }
            return false;
        }

        void RebuildLayers(ProjectDocument doc)
        {
            _layers.Clear();
            for (int i = 0; i < doc.Texts.Count; i++)
            {
                TextLayer t = doc.Texts[i];
                _layers.Add(new TextLayerData(i, t.Text, FontIndex(t.Font), t.Size, StudioViewModel.ParseColor(t.Fill, new ColorRgba(255, 255, 255)),
                    t.Stroke?.Px ?? 0, t.Shadow, t.Plate, t.Align, t.Anim ?? "none", t.InUs, t.OutUs));
            }
            _layersDoc = doc;
            _layersRevision = doc.Texts.Count;
        }

        static int FontIndex(string id)
        {
            int i = Array.IndexOf(FontIds, id);
            return i < 0 ? 0 : i;
        }

        public void AddFromTemplate(string templateId)
        {
            string text = TemplateText(templateId);
            AddLayer(templateId, text);
        }

        public void AddEmpty()
        {
            AddLayer(null, TemplateText("new"));
        }

        void AddLayer(string templateId, string text)
        {
            ProjectDocument doc = _vm.Doc;
            if (doc == null || doc.Texts.Count >= ProjectLimits.MaxTextLayers) return;
            long duration = Math.Max(ProjectLimits.MinDurationUs, _vm.Timeline.DurationUs);
            int index = doc.Texts.Count;
            var layer = new TextLayer
            {
                Id = "t" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Template = templateId,
                Text = text ?? "",
                InUs = 0,
                OutUs = Math.Min(4 * ProjectLimits.MicrosPerSecond, duration),
                Anim = "fade",
            };
            _vm.Edit(d =>
            {
                if (d.Texts.Count < ProjectLimits.MaxTextLayers) d.Texts.Add(layer);
            }, null);
            _selected = index;
            _vm.NotifyChanged();
        }

        string TemplateText(string id)
        {
            string lang = TemplateLanguage;
            RemoteConfigValues remote = _vm.Ctx.Remote.Values;
            if (id != null && remote != null && remote.TryGetTextTemplates(lang, out IReadOnlyList<TextTemplate> templates))
            {
                foreach (TextTemplate t in templates)
                {
                    if (t.Id == id) return t.Text;
                }
            }
            Dictionary<string, string> table = lang == "ru" ? s_ru : s_en;
            return id != null && table.TryGetValue(id, out string text) ? text : table["new"];
        }

        public void Select(int index)
        {
            _selected = index;
            _vm.NotifyChanged();
        }

        public void Remove(int index)
        {
            EditLayer(index, null, d => d.Texts.RemoveAt(index));
            if (_selected >= index) _selected--;
            _vm.NotifyChanged();
        }

        public void SetText(int index, string text)
        {
            string t = text ?? "";
            if (t.Length > ProjectLimits.MaxTextLength) t = t.Substring(0, ProjectLimits.MaxTextLength);
            EditLayer(index, "text_" + index, d => d.Texts[index].Text = t);
        }

        public void SetFont(int index, int fontIndex)
        {
            string id = FontIds[StudioViewModel.Clamp(fontIndex, 0, FontIds.Length - 1)];
            EditLayer(index, null, d => d.Texts[index].Font = id);
        }

        public void SetSize(int index, int sizePx) =>
            EditLayer(index, "text_size_" + index, d => d.Texts[index].Size = StudioViewModel.Clamp(sizePx, ProjectLimits.MinTextSize, ProjectLimits.MaxTextSize));

        public void SetFill(int index, ColorRgba color) =>
            EditLayer(index, "text_fill_" + index, d => d.Texts[index].Fill = StudioViewModel.FormatColor(color));

        public void SetOutline(int index, int px)
        {
            int v = StudioViewModel.Clamp(px, ProjectLimits.MinStrokePx, ProjectLimits.MaxStrokePx);
            EditLayer(index, "text_outline_" + index, d =>
            {
                TextLayer t = d.Texts[index];
                if (v == 0) t.Stroke = null;
                else
                {
                    if (t.Stroke == null) t.Stroke = new TextStroke();
                    t.Stroke.Px = v;
                }
            });
        }

        public void SetShadow(int index, bool on) => EditLayer(index, null, d => d.Texts[index].Shadow = on);

        public void SetPlate(int index, TextPlate plate) => EditLayer(index, null, d => d.Texts[index].Plate = plate);

        public void SetAlign(int index, TextAlign align) => EditLayer(index, null, d => d.Texts[index].Align = align);

        public void SetAnimation(int index, string animationId)
        {
            string id = Array.IndexOf(s_animations, animationId) >= 0 ? animationId : "none";
            EditLayer(index, null, d => d.Texts[index].Anim = id == "none" ? null : id);
        }

        public void SetTiming(int index, long startUs, long endUs)
        {
            long a = Math.Max(0, startUs);
            long b = Math.Max(a + 100000, endUs);
            EditLayer(index, "text_timing_" + index, d =>
            {
                d.Texts[index].InUs = a;
                d.Texts[index].OutUs = b;
            });
        }

        public void SetTemplateLanguage(string language)
        {
            if (language != "en" && language != "ru") return;
            _vm.Ctx.Settings.Update(d => d.TemplateLanguage = language);
            _vm.NotifyChanged();
        }

        void EditLayer(int index, string key, Action<ProjectDocument> change)
        {
            ProjectDocument doc = _vm.Doc;
            if (doc == null || index < 0 || index >= doc.Texts.Count) return;
            _vm.Edit(d =>
            {
                if (index < d.Texts.Count) change(d);
            }, key);
        }
    }

    /// <summary>Tool «Звук» (spec §3.6): track volumes, built-in music, fades, normalization, ducking.</summary>
    public sealed class StudioSoundTool : IStudioSoundTool
    {
        readonly StudioViewModel _vm;
        readonly List<MusicTrackData> _tracks = new List<MusicTrackData>();

        public StudioSoundTool(StudioViewModel vm)
        {
            _vm = vm;
            foreach (string id in Ballverse.Sim.ProceduralMusic.Ids)
                _tracks.Add(new MusicTrackData(id, LocalizedText.FromLiteral(Ballverse.Sim.ProceduralMusic.NameOf(id))));
        }

        AudioSettings A => _vm.Doc?.Audio ?? new AudioSettings();

        public int SimVolume => (int)Math.Round(A.Sim * 100.0);
        public int VideoVolume => (int)Math.Round(A.User * 100.0);
        public IReadOnlyList<MusicTrackData> MusicTracks => _tracks;
        public string MusicTrackId => A.Music?.Track;
        public int MusicVolume => (int)Math.Round((A.Music?.Volume ?? 1.0) * 100.0);
        public long MusicStartUs => A.Music?.StartUs ?? 0;
        public long MusicTrackLengthUs => TrackLengthUs(MusicTrackId);
        public float FadeInSeconds => (A.Music?.FadeInUs ?? 0) / 1000000f;
        public float FadeOutSeconds => (A.Music?.FadeOutUs ?? 0) / 1000000f;
        public bool Normalize => A.Normalize;
        public bool VoicePriority => A.DuckMusicUnderVoice;

        public static long TrackLengthUs(string id)
        {
            if (id == null) return 0;
            foreach (string known in Ballverse.Sim.ProceduralMusic.Ids)
            {
                if (known == id) return Ballverse.Sim.ProceduralMusic.LengthUs;
            }
            return 0;
        }

        public void SetSimVolume(int percent) =>
            _vm.Edit(d => d.Audio.Sim = StudioViewModel.Clamp(percent / 100.0, ProjectLimits.MinVolume, ProjectLimits.MaxVolume), "sim_volume");

        public void SetVideoVolume(int percent) =>
            _vm.Edit(d => d.Audio.User = StudioViewModel.Clamp(percent / 100.0, ProjectLimits.MinVolume, ProjectLimits.MaxVolume), "user_volume");

        public void SetMusicTrack(string id)
        {
            if (id != null && TrackLengthUs(id) == 0) return;
            _vm.Edit(d =>
            {
                if (id == null)
                {
                    d.Audio.Music = null;
                    return;
                }
                double volume = d.Audio.Music?.Volume ?? 1.0;
                d.Audio.Music = new MusicSettings { Track = id, Volume = volume };
            }, null);
        }

        public void SetMusicVolume(int percent) => EditMusic(m => m.Volume = StudioViewModel.Clamp(percent / 100.0, ProjectLimits.MinVolume, ProjectLimits.MaxVolume), "music_volume");

        public void SetMusicStart(long us)
        {
            long max = Math.Max(0, MusicTrackLengthUs - ProjectLimits.MinDurationUs);
            long v = Math.Max(0, Math.Min(max, us));
            EditMusic(m => m.StartUs = v, "music_start");
        }

        public void SetFades(float inSeconds, float outSeconds)
        {
            long fin = (long)Math.Round(StudioViewModel.Clamp(inSeconds, 0, 2) * 1000000.0);
            long fout = (long)Math.Round(StudioViewModel.Clamp(outSeconds, 0, 2) * 1000000.0);
            EditMusic(m =>
            {
                m.FadeInUs = Math.Min(fin, ProjectLimits.MaxMusicFadeUs);
                m.FadeOutUs = Math.Min(fout, ProjectLimits.MaxMusicFadeUs);
            }, "music_fades");
        }

        public void SetNormalize(bool on) => _vm.Edit(d => d.Audio.Normalize = on, null);

        public void SetVoicePriority(bool on) => _vm.Edit(d => d.Audio.DuckMusicUnderVoice = on, null);

        void EditMusic(Action<MusicSettings> change, string key)
        {
            _vm.Edit(d =>
            {
                if (d.Audio.Music != null) change(d.Audio.Music);
            }, key);
        }
    }
}
