using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.UI.Layout;
using Ballverse.UI.Localization;
using Ballverse.UI.Theming;
using Ballverse.UI.ViewModels;
using UnityEngine;

namespace Ballverse.UI.Fakes
{
    /// <summary>
    /// Editor stand-ins for every screen view model: plausible data and timed state changes (simulation phases,
    /// generation, export progress, purchases) so the whole shell can be browsed and tested without App / Services /
    /// Studio. Call <see cref="Tick"/> every frame (see <see cref="UiPreviewBootstrap"/>).
    /// </summary>
    public sealed class FakeViewModels : IBallverseViewModels, IDisposable
    {
        readonly FakeGallery _gallery;
        readonly FakeSimulation _simulation;
        readonly FakeStudio _studio;
        readonly FakeExport _export;
        readonly FakeProjects _projects;
        readonly FakeShop _shop;
        readonly FakeSettings _settings;
        readonly FakeConsent _consent = new FakeConsent();
        readonly FakeSystemAppearance _system = new FakeSystemAppearance();

        public FakeViewModels()
        {
            _shop = new FakeShop();
            _settings = new FakeSettings(_shop);
            _gallery = new FakeGallery();
            _simulation = new FakeSimulation();
            _studio = new FakeStudio();
            _export = new FakeExport(_shop);
            _projects = new FakeProjects();
            _gallery.ModeOpened += id => _simulation.Open(id);
        }

        public IGalleryViewModel Gallery => _gallery;
        public ISimulationViewModel Simulation => _simulation;
        public IStudioViewModel Studio => _studio;
        public IExportViewModel Export => _export;
        public IProjectsViewModel Projects => _projects;
        public IShopViewModel Shop => _shop;
        public IAppSettingsViewModel Settings => _settings;
        public ITrackingConsentViewModel TrackingConsent => _consent;
        public ISystemAppearance SystemAppearance => _system;

        /// <summary>Lets fakes resolve display texts they generate (template texts) through the shell's tables.</summary>
        public void SetLocalizer(Func<string, string> localize)
        {
            _studio.Localize = localize;
        }

        public void Tick(float dt)
        {
            _gallery.Tick(dt);
            _simulation.Tick(dt);
            _studio.Tick(dt);
            _export.Tick(dt);
            _shop.Tick(dt);
        }

        public void Dispose()
        {
            _gallery.Dispose();
            _simulation.Dispose();
            _studio.Dispose();
        }
    }

    /// <summary>Base with a Changed event and a simple timer queue.</summary>
    public abstract class FakeViewModelBase : IObservableViewModel
    {
        readonly List<KeyValuePair<float, Action>> _timers = new List<KeyValuePair<float, Action>>();

        public event Action Changed;

        protected void Notify()
        {
            Changed?.Invoke();
        }

        protected void After(float seconds, Action action)
        {
            _timers.Add(new KeyValuePair<float, Action>(seconds, action));
        }

        protected void CancelTimers()
        {
            _timers.Clear();
        }

        public virtual void Tick(float dt)
        {
            for (int i = 0; i < _timers.Count; i++)
            {
                var t = _timers[i];
                float left = t.Key - dt;
                if (left <= 0f)
                {
                    _timers.RemoveAt(i);
                    i--;
                    t.Value();
                }
                else
                {
                    _timers[i] = new KeyValuePair<float, Action>(left, t.Value);
                }
            }
        }
    }

    sealed class FakeGallery : FakeViewModelBase, IGalleryViewModel, IDisposable
    {
        readonly ModeCardData[] _modes =
        {
            ModeCardData.ForMode("ring_escape", false, false, false),
            ModeCardData.ForMode("geometry_bounce", false, false, true),
            ModeCardData.ForMode("helix_drop", true, false, true),
        };
        readonly Dictionary<string, ProceduralPreview> _previews = new Dictionary<string, ProceduralPreview>();
        bool _active;

        public event Action<string> ModeOpened;

        public FakeGallery()
        {
            _previews["ring_escape"] = new ProceduralPreview(144, 256, 6, 1, new Color32(0x0B, 0x0D, 0x18, 0xFF));
            _previews["geometry_bounce"] = new ProceduralPreview(144, 256, 3, 2, new Color32(0x12, 0x08, 0x1C, 0xFF));
            _previews["helix_drop"] = new ProceduralPreview(144, 256, 1, 3, new Color32(0x06, 0x14, 0x14, 0xFF));
        }

        public IReadOnlyList<ModeCardData> Modes => _modes;

        public Texture GetPreview(string modeId)
        {
            return _previews.TryGetValue(modeId, out var p) ? p.Texture : null;
        }

        public void SetPreviewsActive(bool active)
        {
            _active = active;
        }

        public void OpenMode(string modeId)
        {
            ModeOpened?.Invoke(modeId);
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (!_active) return;
            foreach (var p in _previews.Values) p.Step(dt);
        }

        public void Dispose()
        {
            foreach (var p in _previews.Values) p.Dispose();
        }
    }

    sealed class FakeSimulation : FakeViewModelBase, ISimulationViewModel, IDisposable
    {
        static readonly float[] s_speeds = { 1f, 2f, 0.5f };
        readonly ProceduralPreview _render = new ProceduralPreview(270, 480, 8, 7, new Color32(0x0A, 0x0C, 0x16, 0xFF));
        readonly FakeParamPanel _params;
        readonly FakePresets _presets;
        readonly List<RunThumbData> _runs = new List<RunThumbData>();
        readonly GenerationChoice[] _styles;
        readonly GenerationChoice[] _durations;
        string _modeId = "ring_escape";
        int _speedIndex;
        float _runTime;
        bool _active;
        int _runCounter;

        public FakeSimulation()
        {
            _params = new FakeParamPanel(this);
            _presets = new FakePresets(_params);
            string[] styles = { "any", "chaos", "meditation", "tense_finale", "many_balls", "minimal" };
            _styles = new GenerationChoice[styles.Length];
            for (int i = 0; i < styles.Length; i++) _styles[i] = new GenerationChoice(styles[i], LocalizedText.FromKey("generate.style." + styles[i]));
            _durations = new[]
            {
                new GenerationChoice("auto", LocalizedText.FromKey("generate.duration.auto")),
                new GenerationChoice("15", LocalizedText.FromKey("generate.duration.seconds", 15)),
                new GenerationChoice("30", LocalizedText.FromKey("generate.duration.seconds", 30)),
                new GenerationChoice("60", LocalizedText.FromKey("generate.duration.seconds", 60)),
            };
            SelectedStyleId = "any";
            SelectedDurationId = "auto";
            ActiveHint = HintStep.Generate;
            Quest = new FirstVideoQuestData(true, false, false, false);
        }

        public string ModeId => _modeId;
        public string ModeNameKey => "mode." + _modeId + ".name";
        public Texture RenderTarget => _render.Texture;
        public SimPhaseView Phase { get; private set; } = SimPhaseView.Setup;
        public bool IsPlaying { get; private set; }
        public bool IsInteractive => _modeId == "helix_drop";
        public float Speed => s_speeds[_speedIndex];
        public bool IsGenerating { get; private set; }
        public float GenerationProgress { get; private set; } = -1f;
        public SimResultData Result { get; private set; }
        public IReadOnlyList<RunThumbData> PreviousRuns => _runs;
        public HintStep ActiveHint { get; private set; }
        public FirstVideoQuestData Quest { get; private set; }
        public IReadOnlyList<GenerationChoice> GenerationStyles => _styles;
        public IReadOnlyList<GenerationChoice> GenerationDurations => _durations;
        public string SelectedStyleId { get; private set; }
        public string SelectedDurationId { get; private set; }
        public IParamPanelViewModel Params => _params;
        public IPresetsViewModel Presets => _presets;
        public event Action<LocalizedText> Notice;

        public void Open(string modeId)
        {
            _modeId = modeId;
            _params.ResetForMode(modeId);
            _presets.ResetForMode();
            _runs.Clear();
            StartRun();
        }

        void StartRun()
        {
            CancelTimers();
            Result = null;
            _runTime = 0f;
            Phase = SimPhaseView.Running;
            IsPlaying = true;
            Notify();
        }

        public override void Tick(float dt)
        {
            base.Tick(dt);
            if (!_active) return;
            if (Phase == SimPhaseView.Running && IsPlaying)
            {
                _render.Step(dt * Speed);
                _runTime += dt * Speed;
                if (_runTime >= 12f) Finish();
            }
        }

        void Finish()
        {
            Phase = SimPhaseView.Finished;
            IsPlaying = false;
            _runCounter++;
            Result = new SimResultData(LocalizedText.FromKey("result.escaped", 7), new ColorRgba(0xFF, 0x3B, 0x5C), 12000,
                new[]
                {
                    new ResultStat("result.stat.rings", LocalizedText.FromLiteral("24 / 30")),
                    new ResultStat("result.stat.bounces", LocalizedText.FromLiteral("318")),
                });
            for (int i = 0; i < _runs.Count; i++) _runs[i] = new RunThumbData(_runs[i].Index, _runs[i].Thumbnail, _runs[i].DurationMs, false);
            _runs.Insert(0, new RunThumbData(_runCounter, _render.Texture, 12000, true));
            if (_runs.Count > 5) _runs.RemoveAt(_runs.Count - 1);
            Notify();
        }

        public void Back()
        {
            IsPlaying = false;
            Notify();
        }

        public void Retry()
        {
            StartRun();
        }

        public void TogglePlay()
        {
            if (Phase == SimPhaseView.Finished)
            {
                StartRun();
                return;
            }
            IsPlaying = !IsPlaying;
            Notify();
        }

        public void Pause()
        {
            IsPlaying = false;
            Notify();
        }

        public void Generate()
        {
            Generate(SelectedStyleId, SelectedDurationId);
        }

        public void Generate(string styleId, string durationId)
        {
            if (IsGenerating) return;
            SelectedStyleId = styleId;
            SelectedDurationId = durationId;
            IsGenerating = true;
            GenerationProgress = 0f;
            if (ActiveHint == HintStep.Generate) ActiveHint = HintStep.Settings;
            Notify();
            for (int i = 1; i <= 5; i++)
            {
                float p = i / 5f;
                After(0.25f * i, () =>
                {
                    GenerationProgress = p;
                    Notify();
                });
            }
            After(1.3f, () =>
            {
                IsGenerating = false;
                GenerationProgress = -1f;
                Quest = new FirstVideoQuestData(true, true, Quest != null && Quest.Exported, Quest != null && Quest.Collapsed);
                _params.Apply(CommonParams.Seed, ParamValue.Seed((ulong)UnityEngine.Random.Range(1, int.MaxValue)));
                StartRun();
            });
        }

        public void CycleSpeed()
        {
            _speedIndex = (_speedIndex + 1) % s_speeds.Length;
            Notify();
        }

        public void ToVideo()
        {
            if (ActiveHint == HintStep.ToVideo) ActiveHint = HintStep.None;
            IsPlaying = false;
            Notify();
        }

        public void SaveClip()
        {
        }

        public void SelectPreviousRun(int index)
        {
            for (int i = 0; i < _runs.Count; i++) _runs[i] = new RunThumbData(_runs[i].Index, _runs[i].Thumbnail, _runs[i].DurationMs, _runs[i].Index == index);
            Notify();
        }

        public void DismissHint()
        {
            switch (ActiveHint)
            {
                case HintStep.Generate: ActiveHint = HintStep.Settings; break;
                case HintStep.Settings: ActiveHint = HintStep.ToVideo; break;
                default: ActiveHint = HintStep.None; break;
            }
            Notify();
        }

        public void SetQuestCollapsed(bool collapsed)
        {
            if (Quest == null) return;
            Quest = new FirstVideoQuestData(Quest.RanSimulation, Quest.Generated, Quest.Exported, collapsed);
            Notify();
        }

        public void OnArenaPointer(ArenaPointerPhase phase, Vector2 uv, int pointerId)
        {
        }

        public void SetScreenActive(bool active)
        {
            _active = active;
            if (!active && IsGenerating)
            {
                CancelTimers();
                IsGenerating = false;
                GenerationProgress = -1f;
            }
        }

        internal void RaiseNotice(LocalizedText text)
        {
            Notice?.Invoke(text);
        }

        internal void Restart()
        {
            StartRun();
        }

        internal void RaiseChanged()
        {
            Notify();
        }

        public void Dispose()
        {
            _render.Dispose();
        }
    }

    sealed class FakeParamPanel : FakeViewModelBase, IParamPanelViewModel
    {
        readonly FakeSimulation _sim;

        public FakeParamPanel(FakeSimulation sim)
        {
            _sim = sim;
            ResetForMode("ring_escape");
        }

        public ParamSchema Schema { get; private set; }
        public ParamSet Values { get; private set; }
        public DeviceTier Tier => DeviceTier.B;
        public bool IsRunning => _sim.Phase == SimPhaseView.Running || _sim.Phase == SimPhaseView.Countdown || _sim.Phase == SimPhaseView.Finale;

        public void ResetForMode(string modeId)
        {
            // Mode layers come with the mode modules; the fake shows the common schema (spec §2.2) for every mode.
            Schema = CommonParams.ForMode(ModeCatalog.Get(ModeCode.M01));
            Values = Schema.CreateDefaults(Tier);
            Notify();
        }

        public void Apply(string id, ParamValue value)
        {
            Values = Values.With(id, Schema.SanitizeValue(id, value, Tier));
            Notify();
            _sim.RaiseChanged();
        }

        public void RestartWith(ParamSet values)
        {
            Values = values.Sanitize(Schema, Tier).Values;
            Notify();
            _sim.Restart();
        }

        public void ResetToPreset()
        {
            Values = Schema.CreateDefaults(Tier);
            Notify();
            _sim.RaiseChanged();
        }

        internal void SetAll(ParamSet values)
        {
            Values = values.Sanitize(Schema, Tier).Values;
            Notify();
        }
    }

    sealed class FakePresets : FakeViewModelBase, IPresetsViewModel
    {
        readonly FakeParamPanel _params;
        readonly List<PresetItemData> _items = new List<PresetItemData>();
        int _userCounter;

        public FakePresets(FakeParamPanel panel)
        {
            _params = panel;
            ResetForMode();
        }

        public IReadOnlyList<PresetItemData> Presets => _items;
        public string SelectedId { get; private set; }
        public LocalizedText CurrentName
        {
            get
            {
                foreach (var p in _items)
                    if (p.Id == SelectedId) return p.Name;
                return LocalizedText.FromKey("presets.custom");
            }
        }
        public bool IsModified => false;
        public int UserPresetCount
        {
            get
            {
                int n = 0;
                foreach (var p in _items)
                    if (p.Source == PresetSource.User) n++;
                return n;
            }
        }
        public int MaxUserPresets => UserPresetLibrary.MaxPerMode;

        public void ResetForMode()
        {
            _items.Clear();
            _items.Add(new PresetItemData("classic", LocalizedText.FromKey("preset.classic"), PresetSource.BuiltIn, false, null));
            _items.Add(new PresetItemData("neon_chaos", LocalizedText.FromKey("preset.neon_chaos"), PresetSource.BuiltIn, false, null));
            _items.Add(new PresetItemData("meditation", LocalizedText.FromKey("preset.meditation"), PresetSource.BuiltIn, false, null));
            _items.Add(new PresetItemData("rainbow", LocalizedText.FromKey("preset.rainbow"), PresetSource.Remote, true, null));
            SelectedId = "classic";
            Notify();
        }

        public void Apply(string id)
        {
            SelectedId = id;
            _params.ResetToPreset();
            Notify();
        }

        public UserPresetError SaveCurrentAs(string name)
        {
            string n = Preset.NormalizeName(name);
            if (string.IsNullOrEmpty(n) || n.Length > Preset.MaxNameLength) return UserPresetError.InvalidName;
            if (UserPresetCount >= MaxUserPresets) return UserPresetError.LimitReached;
            _userCounter++;
            string id = "user_" + _userCounter;
            _items.Add(new PresetItemData(id, LocalizedText.FromLiteral(n), PresetSource.User, false, null));
            SelectedId = id;
            Notify();
            return UserPresetError.None;
        }

        public UserPresetError Rename(string id, string name)
        {
            string n = Preset.NormalizeName(name);
            if (string.IsNullOrEmpty(n) || n.Length > Preset.MaxNameLength) return UserPresetError.InvalidName;
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Id != id) continue;
                _items[i] = new PresetItemData(id, LocalizedText.FromLiteral(n), PresetSource.User, false, null);
                Notify();
                return UserPresetError.None;
            }
            return UserPresetError.NotFound;
        }

        public void Delete(string id)
        {
            _items.RemoveAll(p => p.Id == id && p.Source == PresetSource.User);
            if (SelectedId == id) SelectedId = "classic";
            Notify();
        }
    }
}
