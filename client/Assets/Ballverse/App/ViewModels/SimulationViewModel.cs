using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core;
using Ballverse.Core.Ads;
using Ballverse.Core.Audio;
using Ballverse.Core.Generation;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.Core.Projects;
using Ballverse.Core.Sim;
using Ballverse.Core.Studio;
using Ballverse.Sim;
using Ballverse.Studio;
using Ballverse.UI.Layout;
using Ballverse.UI.Localization;
using Ballverse.UI.ViewModels;
using UnityEngine;

namespace Ballverse.App
{
    /// <summary>
    /// Simulation screen (spec §2.1, §2.4, §4.3): runs the selected mode live through <see cref="LiveSimController"/>, the
    /// settings sheet (live edits recorded, structural edits restart), presets, 🎲 generation on a worker thread within the tier
    /// budget (cancelled when the screen hides), the result card with the last 5 runs, «Ещё раз» (same seed), «В видео»
    /// (run → project → studio), «Сохранить клип», arena gestures for interactive modes, first-run hints and the first-video
    /// quest. Interstitials only at natural breaks (IN2: «Ещё раз» / 🎲 on the result card, Back, to the gallery — never
    /// when ↻ or 🎲 is pressed while a run is still going). Modes come only through the <see cref="ModeRegistry"/> and only
    /// while Remote Config has not switched them off (<c>modes_disabled</c>). Main thread.
    /// </summary>
    public sealed class SimulationViewModel : ViewModelBase, ISimulationViewModel
    {
        /// <summary>Modes whose first start shows the photosensitivity warning (spec §4.8: release 1 — M05).</summary>
        public static readonly string[] PhotosensitiveModes = { "helix_drop" };

        public const int MaxRecentRuns = 5;
        const int ThumbnailWidth = 96;

        static readonly float[] s_speeds = { 1f, 2f, 0.5f };
        static readonly string[] s_styleIds = { "any", "chaos", "meditation", "tense_finale", "many_balls", "minimal" };
        static readonly string[] s_durationIds = { "auto", "15", "30", "60" };

        sealed class RecentRun
        {
            public string RunId;
            public int Index;
            public long DurationMs;
            public Texture Thumbnail;
        }

        readonly AppContext _ctx;
        readonly ParamPanelViewModel _params;
        readonly PresetsViewModel _presets;
        readonly List<RecentRun> _recent = new List<RecentRun>();
        readonly List<RunThumbData> _recentView = new List<RunThumbData>();
        readonly GenerationChoice[] _styles;
        readonly GenerationChoice[] _durations;

        IModeDefinition _mode;
        ParamSet _values;
        SimResultData _result;
        string _selectedRunId;
        int _runCounter;
        bool _screenActive;
        bool _userPaused;
        bool _blocked;
        IDisposable _awake;
        SimPhaseView _lastPhase = SimPhaseView.Setup;
        CancellationTokenSource _generation;
        readonly DisplayRateEstimator _displayRate = new DisplayRateEstimator();
        int _generationProgressBits = BitConverter.ToInt32(BitConverter.GetBytes(-1f), 0);

        public SimulationViewModel(AppContext ctx)
            : base(ctx.Notices.Report)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _params = new ParamPanelViewModel(this, ctx);
            _presets = new PresetsViewModel(this, ctx);
            _styles = new GenerationChoice[s_styleIds.Length];
            for (int i = 0; i < s_styleIds.Length; i++) _styles[i] = new GenerationChoice(s_styleIds[i], LocalizedText.FromKey("generate.style." + s_styleIds[i]));
            _durations = new GenerationChoice[s_durationIds.Length];
            _durations[0] = new GenerationChoice("auto", LocalizedText.FromKey("generate.duration.auto"));
            for (int i = 1; i < s_durationIds.Length; i++)
                _durations[i] = new GenerationChoice(s_durationIds[i], LocalizedText.FromKey("generate.duration.seconds", int.Parse(s_durationIds[i])));
            ctx.Live.RunFinished += OnRunFinished;
            ctx.Live.Error += (c, e) => ctx.Notices.Report(c, e, c == "sim.record" ? AppStrings.RecordingFailed : null);
            if (ctx.Surface != null) ctx.Surface.TargetChanged += Notify;
            ctx.Lifecycle.Paused += OnAppPaused;
        }

        /// <summary>The studio view model "В видео" opens projects in (set by the composition).</summary>
        public StudioViewModel Studio { get; set; }

        /// <summary>Raised when a project or clip was created (the projects tab refreshes).</summary>
        public event Action LibraryChanged;

        /// <summary>Raised when the first-video quest state changed.</summary>
        public event Action QuestChanged;

        public IModeDefinition Mode => _mode;
        public ParamSet CurrentValues => _values;
        public string ModeId => _mode?.Info.Id;
        public string ModeNameKey => _mode != null ? "mode." + _mode.Info.Id + ".name" : null;
        public Texture RenderTarget => _ctx.Surface?.RenderTarget;

        public SimPhaseView Phase
        {
            get
            {
                LiveSimController live = _ctx.Live;
                if (!live.HasRun) return SimPhaseView.Setup;
                if (live.IsCompleted || live.IsFinished) return SimPhaseView.Finished;
                return live.Phase == SimPhase.Finale ? SimPhaseView.Finale : SimPhaseView.Running;
            }
        }

        public bool IsPlaying => _ctx.Live.HasRun && !_ctx.Live.IsCompleted && !_userPaused && !_blocked;

        /// <summary>The run takes player touches: an interactive mode with a registered input mapper (M05 only with
        /// <c>control = player</c>; «Авто-ролик» and «Бот» are driven by the rules).</summary>
        public bool IsInteractive => _mode != null && _ctx.Live.TakesPlayerInput(_mode, _values);

        public float Speed => _ctx.Live.HasRun ? _ctx.Live.TimeScale : (_values != null && _values.TryGet(CommonParams.TimeScale, out ParamValue ts) ? (float)ts.AsFloat : 1f);

        public bool IsGenerating => _generation != null;

        public float GenerationProgress
        {
            get
            {
                if (_generation == null || _ctx.Tier != DeviceTier.C) return -1f;
                return BitConverter.ToSingle(BitConverter.GetBytes(Volatile.Read(ref _generationProgressBits)), 0);
            }
        }

        public SimResultData Result => _result;
        public IReadOnlyList<RunThumbData> PreviousRuns => _recentView;

        public HintStep ActiveHint
        {
            get
            {
                int step = _ctx.Settings.Data.HintStep;
                return step >= (int)HintStep.Generate && step <= (int)HintStep.ToVideo ? (HintStep)step : HintStep.None;
            }
        }

        public FirstVideoQuestData Quest
        {
            get
            {
                AppSettingsData d = _ctx.Settings.Data;
                if (d.QuestCelebrated) return null;
                return new FirstVideoQuestData(d.QuestRan, d.QuestGenerated, d.QuestExported, d.QuestCollapsed);
            }
        }

        public IReadOnlyList<GenerationChoice> GenerationStyles => _styles;
        public IReadOnlyList<GenerationChoice> GenerationDurations => _durations;
        public string SelectedStyleId => _ctx.Settings.Data.GenerationStyle ?? "any";
        public string SelectedDurationId => _ctx.Settings.Data.GenerationDuration ?? "auto";
        public IParamPanelViewModel Params => _params;
        public IPresetsViewModel Presets => _presets;
        public PresetsViewModel PresetsModel => _presets;

        public event Action<LocalizedText> Notice;

        // ------------------------------------------------------------------ opening

        /// <summary>
        /// Opens a mode with its last parameters, else its first built-in preset (spec §4.3 gallery tap, first run). False
        /// (toast) when the mode is unknown or switched off by Remote Config.
        /// </summary>
        public bool Open(string modeId)
        {
            if (!_ctx.Remote.IsAvailable(modeId, out IModeDefinition mode))
            {
                if (_ctx.Modes.TryGet(modeId, out _)) _ctx.Notices.Toast(AppStrings.ModeUnavailable);
                else _ctx.Notices.Toast(AppStrings.SimStartFailed);
                return false;
            }
            return OpenWith(mode, InitialValues(mode));
        }

        /// <summary>
        /// Opens a mode with given parameters (studio "Перегенерировать", a saved clip). False (toast, nothing started) when
        /// Remote Config switched the mode off (<c>modes_disabled</c>, spec §7.6 "аварийный выключатель").
        /// </summary>
        public bool OpenWith(IModeDefinition mode, ParamSet values)
        {
            if (mode == null) throw new ArgumentNullException(nameof(mode));
            if (!_ctx.Remote.IsAvailable(mode.Info.Id, out _))
            {
                _ctx.Notices.Toast(AppStrings.ModeUnavailable);
                return false;
            }
            CancelGeneration();
            EndRun(GameRunEnd.Abandoned, false);
            bool modeChanged = _mode != mode;
            _mode = mode;
            if (modeChanged) ClearRecent();
            _values = Prepare(values ?? ModeParams.Defaults(mode, _ctx.Tier));
            _ctx.Settings.Update(d => d.LastModeId = mode.Info.Id);
            _presets.Rebuild();
            _params.NotifyChanged();
            _userPaused = false;
            StartRun(_values);
            WarnPhotosensitivity();
            Notify();
            return true;
        }

        ParamSet InitialValues(IModeDefinition mode)
        {
            if (_ctx.Settings.Data.LastParams.TryGetValue(mode.Info.Id, out string json)
                && ParamJson.TryFromJson(json, out ParamSet stored, out _))
            {
                return stored;
            }
            Preset first = mode.BuiltInPresets.Count > 0 ? mode.BuiltInPresets[0] : null;
            return first != null ? ModeParams.ApplyPreset(mode, first, _ctx.Tier).Values : ModeParams.Defaults(mode, _ctx.Tier);
        }

        internal ParamSet Prepare(ParamSet raw)
        {
            return ModeParams.Prepare(_mode, raw, _ctx.Tier).Values;
        }

        void WarnPhotosensitivity()
        {
            string id = _mode.Info.Id;
            if (Array.IndexOf(PhotosensitiveModes, id) < 0 || _ctx.Settings.Data.PhotosensitivityWarned.Contains(id) || _ctx.Ui == null) return;
            _blocked = true;
            _ctx.Ui.ShowPhotosensitivityWarning(() =>
            {
                _ctx.Settings.Update(d => d.PhotosensitivityWarned.Add(id));
                _blocked = false;
                Notify();
            });
        }

        // ------------------------------------------------------------------ runs

        internal void StartRun(ParamSet values)
        {
            if (_mode == null) return;
            _values = values;
            _result = null;
            _selectedRunId = null;
            ulong seed = values.TryGet(CommonParams.Seed, out ParamValue s) ? s.AsSeed : 0UL;
            ILiveRunRecording recording = null;
            try
            {
                ClipLibrary clips = _ctx.Studio.Clips; // null while the studio is unavailable: the run plays unrecorded
                if (clips != null) recording = new RunRecordingAdapter(clips.BeginRun());
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is ArgumentException)
            {
                _ctx.Notices.Report("sim.begin_run", e, AppStrings.RecordingFailed);
            }
            try
            {
                _ctx.Live.Begin(_mode, values, seed, recording);
            }
            catch (ArgumentException e)
            {
                // Stored parameters that no longer satisfy the mode (schema changed): fall back to the defaults.
                _ctx.Notices.Report("sim.begin", e, AppStrings.SimStartFailed);
                _values = ModeParams.Defaults(_mode, _ctx.Tier);
                try
                {
                    _ctx.Live.Begin(_mode, _values, seed, null);
                }
                catch (ArgumentException again)
                {
                    _ctx.Notices.Report("sim.begin_defaults", again);
                    return;
                }
            }
            ApplyRuntimeParams(_values);
            _ctx.Surface?.ApplyLook(_values);
            SaveLastParams();
            UpdateAwake();
            _lastPhase = Phase;
            Notify();
        }

        /// <summary>Ends the current run (if any) and reports it (ads GameCompleted, recent runs). Returns its outcome.</summary>
        LiveRunOutcome EndRun(GameRunEnd end, bool keep)
        {
            LiveSimController live = _ctx.Live;
            if (!live.HasRun || live.IsCompleted) return live.Outcome;
            LiveRunOutcome outcome = live.End(end, keep);
            HandleOutcome(outcome, false);
            return outcome;
        }

        void OnRunFinished(LiveRunOutcome outcome)
        {
            HandleOutcome(outcome, true);
        }

        void HandleOutcome(LiveRunOutcome outcome, bool finished)
        {
            if (outcome == null) return;
            if (outcome.Session != null) _ctx.Ads?.OnRunEnded(outcome.Run);
            if (outcome.RunId != null) AddRecent(outcome);
            if (finished)
            {
                _result = BuildResult(outcome);
                if (!_ctx.Settings.Data.QuestRan)
                {
                    _ctx.Settings.Update(d => d.QuestRan = true);
                    QuestChanged?.Invoke();
                }
            }
            UpdateAwake();
            Notify();
        }

        SimResultData BuildResult(LiveRunOutcome outcome)
        {
            SimResult r = outcome.Result;
            long durationMs = outcome.Ticks * 1000L / SimConstants.TickRate;
            LocalizedText headline;
            ColorRgba? color = null;
            if (r.Outcome == SimOutcome.Win && r.Winner >= 0)
            {
                headline = LocalizedText.FromKey("result.winner", (r.Winner + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (outcome.Values != null && outcome.Values.TryGet(CommonParams.BallColors, out ParamValue colors) && colors.AsColors.Count > 0)
                    color = colors.AsColors[r.Winner % colors.AsColors.Count];
            }
            else
            {
                headline = LocalizedText.FromKey("result.finished");
            }
            var stats = new List<ResultStat>(2);
            if (r.Score > 0) stats.Add(new ResultStat(AppStrings.ResultStatScore, LocalizedText.FromLiteral(r.Score.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            if (r.Detail > 0) stats.Add(new ResultStat(AppStrings.ResultStatProgress, LocalizedText.FromLiteral(r.Detail.ToString(System.Globalization.CultureInfo.InvariantCulture))));
            return new SimResultData(headline, color, durationMs, stats);
        }

        void AddRecent(LiveRunOutcome outcome)
        {
            var run = new RecentRun
            {
                RunId = outcome.RunId,
                Index = ++_runCounter,
                DurationMs = outcome.Ticks * 1000L / SimConstants.TickRate,
                Thumbnail = _ctx.Surface?.CaptureThumbnail(),
            };
            _recent.Insert(0, run);
            PruneRecent();
        }

        /// <summary>Keeps the strip in step with the studio's ring (oldest runs are evicted there).</summary>
        void PruneRecent()
        {
            var alive = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                ClipLibrary clips = _ctx.Studio.Clips;
                if (clips != null) foreach (ClipEntry e in clips.ListRuns()) alive.Add(e.Id);
                else foreach (RecentRun r in _recent) alive.Add(r.RunId);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _ctx.Notices.Report("sim.list_runs", e);
                foreach (RecentRun r in _recent) alive.Add(r.RunId);
            }
            for (int i = _recent.Count - 1; i >= 0; i--)
            {
                if (i >= MaxRecentRuns || !alive.Contains(_recent[i].RunId))
                {
                    _ctx.Surface?.ReleaseThumbnail(_recent[i].Thumbnail);
                    if (_recent[i].RunId == _selectedRunId) _selectedRunId = null;
                    _recent.RemoveAt(i);
                }
            }
            RebuildRecentView();
        }

        void ClearRecent()
        {
            foreach (RecentRun r in _recent) _ctx.Surface?.ReleaseThumbnail(r.Thumbnail);
            _recent.Clear();
            _selectedRunId = null;
            RebuildRecentView();
        }

        void RebuildRecentView()
        {
            _recentView.Clear();
            string current = CurrentRunId();
            foreach (RecentRun r in _recent) _recentView.Add(new RunThumbData(r.Index, r.Thumbnail, r.DurationMs, r.RunId == current));
        }

        string CurrentRunId()
        {
            if (_selectedRunId != null) return _selectedRunId;
            return _ctx.Live.Outcome?.RunId;
        }

        /// <summary>Low memory: drop the run thumbnails.</summary>
        public void ReleaseThumbnails()
        {
            foreach (RecentRun r in _recent)
            {
                _ctx.Surface?.ReleaseThumbnail(r.Thumbnail);
                r.Thumbnail = null;
            }
            RebuildRecentView();
            Notify();
        }

        // ------------------------------------------------------------------ per frame

        /// <summary>Steps the live run for this display frame (main thread, before the UI update).</summary>
        public void Tick(float deltaSeconds)
        {
            // The measured display rate, not the target: at 30 FPS each frame runs two ticks so the run plays in real time.
            int rate = _displayRate.Sample(deltaSeconds);
            if (_screenActive && IsPlaying && !_ctx.AppPaused)
            {
                _ctx.Live.AdvanceDisplayFrame(rate, _ctx.Clock.NowSeconds);
            }
            SimPhaseView phase = Phase;
            if (phase != _lastPhase)
            {
                _lastPhase = phase;
                _params.NotifyChanged();
                Notify();
            }
        }

        /// <summary>Draws the run (after Update, before rendering).</summary>
        public void LateTick(float deltaSeconds)
        {
            if (_screenActive && _ctx.Live.HasRun) _ctx.Surface?.Present(deltaSeconds);
        }

        // ------------------------------------------------------------------ actions

        public void Back()
        {
            NaturalBreakKind kind = _result != null ? NaturalBreakKind.BackFromResult : NaturalBreakKind.ToGallery;
            CancelGeneration();
            EndRun(GameRunEnd.Abandoned, false);
            SaveLastParams();
            _ctx.Settings.Save();
            _ctx.Ads?.At(kind, null);
            Notify();
        }

        /// <summary>
        /// The result card is up: a natural pause where a pending interstitial may show (IN2). ↻ / 🎲 pressed while the run is
        /// still going are not one.
        /// </summary>
        internal bool AtNaturalBreak => _result != null && Phase == SimPhaseView.Finished;

        public void Retry()
        {
            if (_mode == null) return;
            bool atBreak = AtNaturalBreak;
            CancelGeneration();
            EndRun(GameRunEnd.RestartedBeforeFinale, false);
            ParamSet values = _values;
            if (atBreak && _ctx.Ads != null) _ctx.Ads.At(NaturalBreakKind.Retry, () => StartRun(values));
            else StartRun(values);
        }

        public void TogglePlay()
        {
            if (_mode == null) return;
            if (_ctx.Live.IsCompleted || !_ctx.Live.HasRun)
            {
                StartRun(_values);
                return;
            }
            _userPaused = !_userPaused;
            UpdateAwake();
            Notify();
        }

        public void Pause()
        {
            if (_userPaused) return;
            _userPaused = true;
            UpdateAwake();
            Notify();
        }

        public void Generate()
        {
            Generate(SelectedStyleId, SelectedDurationId);
        }

        public void Generate(string styleId, string durationId)
        {
            if (_mode == null || _generation != null) return;
            styleId = Array.IndexOf(s_styleIds, styleId) >= 0 ? styleId : "any";
            durationId = Array.IndexOf(s_durationIds, durationId) >= 0 ? durationId : "auto";
            _ctx.Settings.Update(d =>
            {
                d.GenerationStyle = styleId;
                d.GenerationDuration = durationId;
                if (d.HintStep == (int)HintStep.Generate) d.HintStep = (int)HintStep.Settings;
            });
            var cts = new CancellationTokenSource();
            _generation = cts;
            SetProgress(0f);
            Notify();
            if (AtNaturalBreak && _ctx.Ads != null) _ctx.Ads.At(NaturalBreakKind.Generate, () => StartGeneration(cts, styleId, durationId));
            else StartGeneration(cts, styleId, durationId);
        }

        void StartGeneration(CancellationTokenSource cts, string styleId, string durationId)
        {
            if (cts.IsCancellationRequested || _generation != cts) return;
            IModeDefinition mode = _mode;
            ParamSet baseValues = _values;
            if (durationId != "auto" && mode.Schema.Contains(CommonParams.TargetDuration))
                baseValues = baseValues.With(CommonParams.TargetDuration, ParamValue.Int(int.Parse(durationId, System.Globalization.CultureInfo.InvariantCulture)));
            GenerationBudget budget = _ctx.Remote.Budgets.Get(_ctx.Tier);
            var request = new ModeGenerationRequest
            {
                BaseValues = baseValues,
                Tier = _ctx.Tier,
                SessionSeed = NewSessionSeed(),
                Style = StyleOf(styleId),
                Budgets = _ctx.Remote.Budgets,
                Profiles = _ctx.Remote.Profiles,
                AutoTakeNearest = true,
                IsCancelled = () => cts.IsCancellationRequested,
            };
            var watch = System.Diagnostics.Stopwatch.StartNew();
            long budgetMs = Math.Max(1, budget.TimeMs);
            request.ElapsedMs = () =>
            {
                long ms = watch.ElapsedMilliseconds;
                SetProgress(Math.Min(1f, ms / (float)budgetMs));
                return ms;
            };
            CancellationToken token = cts.Token;
            Task.Run(() =>
            {
                try
                {
                    ModeGenerationResult result = ModeGenerator.Generate(mode, request);
                    _ctx.Main.Post(() => OnGenerated(cts, mode, result, null));
                }
                catch (Exception e)
                {
                    _ctx.Main.Post(() => OnGenerated(cts, mode, null, e));
                }
            }, token);
        }

        void OnGenerated(CancellationTokenSource cts, IModeDefinition mode, ModeGenerationResult result, Exception error)
        {
            if (_generation != cts) return;
            _generation = null;
            bool cancelled = cts.IsCancellationRequested;
            cts.Dispose();
            SetProgress(-1f);
            if (cancelled || mode != _mode)
            {
                Notify();
                return;
            }
            if (error != null)
            {
                _ctx.Notices.Report("sim.generate", error, AppStrings.GenerateFailed);
                Notify();
                return;
            }
            if (result == null || !result.HasSelection || result.Values == null)
            {
                RaiseNotice(_ctx.Notices.TextOf(AppStrings.GenerateWeak));
                Notify();
                return;
            }
            if (result.Result != null && result.Result.QualityBelowThreshold) RaiseNotice(_ctx.Notices.TextOf(AppStrings.GenerateWeak));
            if (!_ctx.Settings.Data.QuestGenerated)
            {
                _ctx.Settings.Update(d => d.QuestGenerated = true);
                QuestChanged?.Invoke();
            }
            EndRun(GameRunEnd.RestartedBeforeFinale, false);
            ParamSet values = Prepare(result.Values.With(CommonParams.Seed, ParamValue.Seed(result.Seed)));
            _presets.MarkCustom();
            StartRun(values);
            _params.NotifyChanged();
        }

        void SetProgress(float value)
        {
            Volatile.Write(ref _generationProgressBits, BitConverter.ToInt32(BitConverter.GetBytes(value), 0));
        }

        public void CancelGeneration()
        {
            CancellationTokenSource cts = _generation;
            if (cts == null) return;
            _generation = null;
            cts.Cancel();
            SetProgress(-1f);
        }

        static ulong NewSessionSeed()
        {
            byte[] bytes = Guid.NewGuid().ToByteArray();
            return BitConverter.ToUInt64(bytes, 0) ^ BitConverter.ToUInt64(bytes, 8);
        }

        static GenerationStyle StyleOf(string id)
        {
            switch (id)
            {
                case "chaos": return GenerationStyle.Chaos;
                case "meditation": return GenerationStyle.Meditation;
                case "tense_finale": return GenerationStyle.TenseFinale;
                case "many_balls": return GenerationStyle.ManyBalls;
                case "minimal": return GenerationStyle.Minimalism;
                default: return GenerationStyle.Any;
            }
        }

        public void CycleSpeed()
        {
            if (_mode == null) return;
            float current = Speed;
            int index = 0;
            for (int i = 0; i < s_speeds.Length; i++)
            {
                if (Math.Abs(s_speeds[i] - current) < 0.01f) index = i;
            }
            float next = s_speeds[(index + 1) % s_speeds.Length];
            if (_mode.Schema.TryGet(CommonParams.TimeScale, out ParamDef def) && def.Live)
            {
                ApplyParam(CommonParams.TimeScale, ParamValue.Float(next));
            }
            else
            {
                _ctx.Live.SetPacing(next);
                Notify();
            }
        }

        public void ToVideo()
        {
            if (_mode == null) return;
            CancelGeneration();
            _ctx.Ads?.At(NaturalBreakKind.ToVideo, null);
            string runId = _selectedRunId;
            if (runId == null)
            {
                LiveRunOutcome outcome = _ctx.Live.HasRun && !_ctx.Live.IsCompleted ? EndRun(GameRunEnd.Abandoned, true) : _ctx.Live.Outcome;
                runId = outcome?.RunId;
            }
            if (_ctx.Settings.Data.HintStep == (int)HintStep.ToVideo) _ctx.Settings.Update(d => d.HintStep = (int)HintStep.None);
            if (runId == null)
            {
                _ctx.Notices.Toast(AppStrings.RunTooShort);
                Notify();
                return;
            }
            ProjectDocument doc;
            try
            {
                doc = _ctx.Studio.CreateFromRun(runId);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is ArgumentException)
            {
                _ctx.Notices.Report("sim.to_video", e, _ctx.Studio is UnavailableStudioBackend ? AppStrings.StudioUnavailable : AppStrings.OpenProjectFailed);
                Notify();
                return;
            }
            LibraryChanged?.Invoke();
            Studio?.OpenProject(doc.Id, true);
            Notify();
        }

        public void SaveClip()
        {
            string runId = CurrentRunId();
            if (runId == null)
            {
                _ctx.Notices.Toast(AppStrings.RunTooShort);
                return;
            }
            ClipLibrary library = _ctx.Studio.Clips;
            if (library == null)
            {
                _ctx.Notices.Toast(AppStrings.StudioUnavailable);
                return;
            }
            try
            {
                library.SaveRunAsClip(runId);
                RaiseNotice(_ctx.Notices.TextOf(AppStrings.ClipSaved));
                LibraryChanged?.Invoke();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException || e is ArgumentException)
            {
                _ctx.Notices.Report("sim.save_clip", e, AppStrings.ProjectActionFailed);
            }
        }

        public void SelectPreviousRun(int index)
        {
            foreach (RecentRun r in _recent)
            {
                if (r.Index != index) continue;
                _selectedRunId = r.RunId == _ctx.Live.Outcome?.RunId ? null : r.RunId;
                RebuildRecentView();
                Notify();
                return;
            }
        }

        public void DismissHint()
        {
            int step = _ctx.Settings.Data.HintStep;
            int next = step >= (int)HintStep.ToVideo || step <= 0 ? (int)HintStep.None : step + 1;
            _ctx.Settings.Update(d => d.HintStep = next);
            Notify();
        }

        public void SetQuestCollapsed(bool collapsed)
        {
            _ctx.Settings.Update(d => d.QuestCollapsed = collapsed);
            Notify();
        }

        public void OnArenaPointer(ArenaPointerPhase phase, Vector2 uv, int pointerId)
        {
            if (!IsInteractive) return;
            _ctx.Live.OnTouch((ArenaTouchPhase)(int)phase, uv.x, uv.y, pointerId, _ctx.Clock.NowSeconds);
        }

        public void SetScreenActive(bool active)
        {
            _screenActive = active;
            if (!active)
            {
                CancelGeneration();
                _ctx.Live.ResetInput();
                SaveLastParams();
            }
            UpdateAwake();
            Notify();
        }

        // ------------------------------------------------------------------ parameters

        /// <summary>One edited value from the settings sheet (spec §2.2 panel rules).</summary>
        internal void ApplyParam(string id, ParamValue value)
        {
            if (_mode == null || value == null || !_mode.Schema.TryGet(id, out ParamDef def)) return;
            LiveSimController live = _ctx.Live;
            if (def.Live)
            {
                ParamValue applied;
                if (live.HasRun && !live.IsCompleted)
                {
                    try
                    {
                        applied = live.SetLiveParam(id, value);
                    }
                    catch (ArgumentException e)
                    {
                        _ctx.Notices.Report("sim.live_param", e);
                        return;
                    }
                    _values = _values.With(id, applied);
                }
                else
                {
                    _values = Prepare(_values.With(id, value));
                    if (id == CommonParams.TimeScale && _values.TryGet(id, out ParamValue ts)) live.SetPacing((float)ts.AsFloat);
                }
                ApplyRuntimeParams(_values);
                _ctx.Surface?.ApplyLook(_values);
                SaveLastParams();
                _presets.NotifyChanged();
                _params.NotifyChanged();
                Notify();
                return;
            }
            // Structural: rebuild the scene with the same seed (Setup / Finished, or a confirmed restart).
            ParamSet next = Prepare(_values.With(id, value));
            EndRun(GameRunEnd.RestartedBeforeFinale, false);
            StartRun(next);
            _presets.NotifyChanged();
            _params.NotifyChanged();
        }

        /// <summary>"Перезапустить" with the collected structural edits.</summary>
        internal void RestartWith(ParamSet values)
        {
            if (_mode == null || values == null) return;
            ParamSet next = Prepare(values);
            EndRun(GameRunEnd.RestartedBeforeFinale, false);
            StartRun(next);
            _presets.NotifyChanged();
            _params.NotifyChanged();
        }

        /// <summary>A preset was chosen: restart with its parameters.</summary>
        internal void ApplyPresetValues(ParamSet values)
        {
            EndRun(GameRunEnd.RestartedBeforeFinale, false);
            StartRun(values);
            _params.NotifyChanged();
        }

        /// <summary>Sound pack, melody, volume and haptics of the run (live parameters of the Sound group).</summary>
        void ApplyRuntimeParams(ParamSet values)
        {
            if (values == null) return;
            string packId = values.TryGet(CommonParams.SoundPack, out ParamValue p) ? p.AsString : "Notes";
            string melodyId = values.TryGet(CommonParams.Melody, out ParamValue m) ? m.AsString : null;
            float volume = values.TryGet(CommonParams.SfxVolume, out ParamValue v) ? (float)v.AsInt / 100f : 0.8f;
            bool haptics = !values.TryGet(CommonParams.Haptics, out ParamValue h) || h.AsBool;
            LiveSoundSetup.Apply(_ctx, packId, melodyId, volume, haptics);
        }

        void SaveLastParams()
        {
            if (_mode == null || _values == null) return;
            string json;
            try
            {
                json = ParamJson.ToJson(_values, _mode.Schema, false);
            }
            catch (ArgumentException e)
            {
                _ctx.Notices.Report("sim.save_params", e);
                return;
            }
            string id = _mode.Info.Id;
            _ctx.Settings.Update(d => d.LastParams[id] = json);
        }

        // ------------------------------------------------------------------ misc

        void UpdateAwake()
        {
            bool want = _screenActive && IsPlaying;
            if (want && _awake == null) _awake = _ctx.ScreenAwake.Acquire("simulation");
            else if (!want && _awake != null)
            {
                _awake.Dispose();
                _awake = null;
            }
        }

        void OnAppPaused()
        {
            _displayRate.Reset(Math.Max(1, _ctx.FrameRate.TargetFps));
            CancelGeneration();
            SaveLastParams();
        }

        internal void RaiseNotice(LocalizedText text)
        {
            Action<LocalizedText> handler = Notice;
            if (handler != null) handler(text);
            else _ctx.Notices.Toast(text);
        }

        internal void NotifyChanged() => Notify();
    }

    /// <summary>Whether the player controls a mode right now (spec §2.6).</summary>
    public static class ModeInteraction
    {
        /// <summary>Name of the control parameter of modes with automatic, bot and player variants (M05 <c>control</c>).</summary>
        public const string ControlParam = ArenaControl.Param;

        /// <summary>
        /// An interactive catalog mode is player-controlled when it has no <c>control</c> parameter or that parameter is
        /// exactly <c>player</c> (spec §2.9 M05: «Авто-ролик» and «Бот» run without the player and record no touches).
        /// Whether touches actually reach the run also needs a registered input mapper (<c>LiveSimController.TakesPlayerInput</c>).
        /// </summary>
        public static bool IsPlayerControlled(IModeDefinition mode, ParamSet values)
        {
            if (mode == null || !mode.Info.Interactive) return false;
            return ArenaControl.IsPlayer(values);
        }
    }

    /// <summary>Configures the live audio and haptics for the run's sound parameters (banks are built once per pack and cached).</summary>
    public static class LiveSoundSetup
    {
        static readonly Dictionary<SoundPackId, InMemorySampleBank> s_banks = new Dictionary<SoundPackId, InMemorySampleBank>();
        static readonly object s_lock = new object();

        public static void Apply(AppContext ctx, string packId, string melodyId, float volume, bool haptics)
        {
            SoundPackId id = ProceduralSampleBank.ParsePack(packId);
            if (string.Equals(packId, "None", StringComparison.OrdinalIgnoreCase)) id = SoundPackId.None;
            SoundPack pack = SoundPackCatalog.Get(id);
            if (ctx.Audio != null)
            {
                Melody melody = null;
                if (id == SoundPackId.Melody) BuiltInMelodies.TryGet(melodyId, out melody);
                if (!ctx.Audio.HasPack || ctx.Audio.PackId != id || ctx.Audio.MelodyId != melody?.Id)
                {
                    ctx.Audio.SetPack(pack, BankFor(id), melody);
                }
                ctx.Audio.Gain = (ctx.Settings.Data.SoundEnabled ? 1f : 0f) * Math.Max(0f, Math.Min(1f, volume));
            }
            if (ctx.Haptics != null)
            {
                ctx.Haptics.SetPack(pack);
                ctx.Haptics.RunEnabled = haptics;
                ctx.Haptics.SettingEnabled = ctx.Settings.Data.HapticsEnabled;
            }
        }

        public static InMemorySampleBank BankFor(SoundPackId id)
        {
            lock (s_lock)
            {
                if (!s_banks.TryGetValue(id, out InMemorySampleBank bank))
                {
                    bank = ProceduralSampleBank.Create(id);
                    s_banks[id] = bank;
                }
                return bank;
            }
        }

        /// <summary>Low memory: drop every cached bank except the one in use.</summary>
        public static void TrimBanks(SoundPackId keep)
        {
            lock (s_lock)
            {
                var drop = new List<SoundPackId>();
                foreach (SoundPackId id in s_banks.Keys)
                {
                    if (id != keep) drop.Add(id);
                }
                foreach (SoundPackId id in drop) s_banks.Remove(id);
            }
        }
    }
}
