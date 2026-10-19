using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Ballverse.App;
using Ballverse.Core.Params;
using Ballverse.Core.Studio;
using Ballverse.Media;
using Ballverse.Render;
using Ballverse.Services;
using Ballverse.Sim;
using Ballverse.Studio;
using Ballverse.UI;
using Ballverse.UI.Localization;
using Ballverse.UI.Navigation;
using Ballverse.UI.ViewModels;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using Ballverse.Editor;
using UnityEditor.SceneManagement;
#endif
using AppContext = Ballverse.App.AppContext;
using Debug = UnityEngine.Debug;

namespace Ballverse.PlayModeTests
{
    /// <summary>
    /// Boots the REAL app (boot scene → <see cref="Bootstrap"/> → <see cref="AppHost"/> over <c>DeviceEnvironment</c>: Services
    /// installer, MediaBridgeFactory, Ballverse.Studio runtime, Ballverse.Sim live view / previews / audio, the UI Toolkit shell,
    /// the Unity runner) in Play Mode and drives the main flow through the real view models and the real UI navigator, the
    /// same calls the screens make on a tap: first run → Ring Escape, live run at 3×, 🎲, «В видео» → studio with the clip,
    /// export with the editor's FAKE media backend, back to the gallery, projects tab, every registered mode (gallery preview
    /// + a live run), pause / resume / low memory, shutdown.
    /// Fails on any error-class log message (Unity Test Framework's default LogAssert behaviour plus <see cref="SmokeLog"/>,
    /// which names the step), a module failure, or a step that does not finish within its (generous) timeout.
    /// See README.md for what this covers and what it cannot cover without a GPU / device.
    /// </summary>
    [PrebuildSetup(typeof(BootSceneSetup))]
    public sealed class AppBootSmokeTest
    {
        const string RingEscape = "ring_escape";

        /// <summary>
        /// The boot scene of player builds (Ballverse.Editor.ProjectSetup.BootScenePath; the editor branch reads that constant,
        /// players cannot reference the editor assembly).
        /// </summary>
        const string PlayerBootScenePath = "Assets/Scenes/Boot.unity";

        /// <summary>
        /// Command-line switch (GameCI customParameters) that turns a missing graphics device into a failure instead of soft
        /// GPU checks: CI expects the editor to have a device (Xvfb + Mesa llvmpipe, see README.md) and must say so loudly
        /// if that ever changes.
        /// </summary>
        public const string RequireGpuSwitch = "-ballverseSmokeRequireGpu";

        // Timeouts are ≥ 10× what each step needs on a laptop editor, so a slow CI runner (software GL, -debugCodeOptimization,
        // code coverage on) never makes the test flaky. Nothing asserts a duration.
        const double BootTimeoutS = 180;
        const double FrameStepTimeoutS = 240;
        const double StudioOpenTimeoutS = 300;
        const double ExportTimeoutS = 1800;
        const double NavigationTimeoutS = 60;
        /// <summary>Wait for a GPU-dependent outcome without a graphics device (it may never come; then it is a soft finding).</summary>
        const double SoftTimeoutS = 30;
        const int TestTimeoutMs = 60 * 60 * 1000;

        /// <summary>Real time the live run plays at 3× before 🎲 ("a few seconds of real frames").</summary>
        const double LiveRunRealSeconds = 3;
        const int LiveRunMinFrames = 90;
        /// <summary>Game time recorded before «В видео» (2.5 s at 60 ticks/s): a short clip keeps the software-GL export small.</summary>
        const int ClipTicks = 150;

        bool _hasGpu;
        SmokeLog _log;
        readonly List<string> _softFindings = new List<string>();
        readonly List<string> _timeline = new List<string>();
        readonly List<string> _toasts = new List<string>();
        /// <summary>Every notice the app showed (AppNotices toasts and the simulation screen's notices), for post-conditions.</summary>
        readonly List<LocalizedText> _shownNotices = new List<LocalizedText>();
        Stopwatch _stepClock;
        string _projectId;
        AppNotices _notices;
        SimulationViewModel _sim;
        AppSettingsStore _settings;
        /// <summary>Photosensitive modes this test marked as warned (removed again in teardown).</summary>
        readonly List<string> _photosensitivityMarked = new List<string>();

        [UnityTest, Timeout(TestTimeoutMs)]
        public IEnumerator RealAppBootsAndRunsTheMainFlow()
        {
            // ---- 0. Environment -------------------------------------------------------------------------------------------
            _hasGpu = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            _log = new SmokeLog(_hasGpu);
            Step("environment", false);
            Info("Unity " + Application.unityVersion + ", platform " + Application.platform + ", batch mode " + Application.isBatchMode
                 + ", graphics " + SystemInfo.graphicsDeviceType + " (" + SystemInfo.graphicsDeviceName + ", " + SystemInfo.graphicsDeviceVersion + ")"
                 + ", screen " + Screen.width + "x" + Screen.height + ", data " + Application.persistentDataPath);
            if (!_hasGpu)
            {
                Assert.IsFalse(Environment.GetCommandLineArgs().Contains(RequireGpuSwitch),
                    "No graphics device (GraphicsDeviceType.Null) although " + RequireGpuSwitch + " was passed. GameCI runs the editor under " +
                    "xvfb-run with Mesa (software OpenGL); check the unityci/editor image and the runner's -nographics usage (README.md).");
                // Soft GPU mode: SmokeLog enforces the log rules (whitelist + soft GPU steps) instead of LogAssert.
                LogAssert.ignoreFailingMessages = true;
                Debug.LogWarning("[Smoke] No graphics device: GPU-dependent checks are soft (logged, not failed); all logic still runs.");
            }
            // Packaging, not GPU: every renderer shader and instancing template must be in the project. RenderResources logs a
            // missing shader with the same "missing or unsupported" text the no-GPU whitelist accepts, so this keeps a missing
            // shader a hard failure with or without a device (SmokeLog.cs, ExpectedLogs).
            foreach (KeyValuePair<string, string> shader in ExpectedLogs.RendererShaders)
            {
                Shader found = Resources.Load<Shader>(RenderResources.ShaderFolder + shader.Key);
                if (found == null) found = Shader.Find(shader.Value);
                Assert.IsNotNull(found, "renderer shader '" + shader.Value + "' is not in the project (Resources/" + RenderResources.ShaderFolder + shader.Key + ")");
            }
            Assert.IsNotNull(RenderResources.LoadTemplate(RenderResources.BallTemplate, RenderResources.BallShaderName),
                "instancing template " + RenderResources.BallTemplate + " (shader " + RenderResources.BallShaderName + ", GPU instancing on)");
            Assert.IsNotNull(RenderResources.LoadTemplate(RenderResources.ParticleTemplate, RenderResources.ParticleShaderName),
                "instancing template " + RenderResources.ParticleTemplate + " (shader " + RenderResources.ParticleShaderName + ", GPU instancing on)");

            // ---- 1. Boot scene → Bootstrap → AppHost ------------------------------------------------------------------------
            Step("boot scene", false);
            string settingsFile = Path.Combine(AppPaths.ForDevice().Settings, AppSettingsStore.FileName);
            bool firstRun = !File.Exists(settingsFile);
            Info("first run expected: " + firstRun + " (" + settingsFile + ")");
            Assert.IsNull(Bootstrap.Host, "no app may be running before the boot scene loads");
#if UNITY_EDITOR
            Assert.AreEqual(ProjectSetup.BootScenePath, PlayerBootScenePath, "player boot scene path follows ProjectSetup");
            AsyncOperation load = EditorSceneManager.LoadSceneAsyncInPlayMode(ProjectSetup.BootScenePath, new LoadSceneParameters(LoadSceneMode.Single));
#else
            AsyncOperation load = SceneManager.LoadSceneAsync(PlayerBootScenePath, LoadSceneMode.Single);
#endif
            Assert.IsNotNull(load, "the boot scene could not be loaded (is it in the build scene list?)");
            yield return WaitUntil("boot scene loaded", () => load.isDone, BootTimeoutS);
            yield return WaitUntil("Bootstrap.Awake booted the host", () => Bootstrap.Host != null, BootTimeoutS);
            AppHost host = Bootstrap.Host;
            yield return WaitUntil("deferred modules started (host Running)", () => host.State != AppHostState.Booted, BootTimeoutS);
            Assert.AreEqual(AppHostState.Running, host.State, "host state after the first frame");
            Assert.IsEmpty(host.Failures, "module failures:\n" + string.Join("\n", host.Failures.Select(f => f.ModuleName + " " + f.Stage + ": " + f.Exception)));
            Assert.AreEqual(host.Modules.Count, host.StartedModules.Count,
                "started modules: " + string.Join(", ", host.StartedModules.Select(m => m.Name)) + " of " + string.Join(", ", host.Modules.Select(m => m.Name)));
            Assert.IsNotNull(Bootstrap.Platform, "Bootstrap.Platform");

            // ---- 2. Real wiring: services, media, studio, experience, Unity objects -----------------------------------------
            Step("wiring", false);
            BallverseServices services = host.Services.Get<BallverseServices>();
            IMediaBridge media = host.Services.Get<IMediaBridge>();
            StudioRuntime studioRuntime = host.Services.Get<StudioRuntime>();
            AppExperience experience = host.Services.Get<AppExperience>();
            Info("services mode " + services.Mode + ", media backend " + media.Backend + ", studio backend " + studioRuntime.Backend?.GetType().Name);
#if UNITY_EDITOR
            // ServicesInstaller: the editor resolves Auto to ServicesConfig.editorMode (fakes); no SDK is contacted.
            Assert.AreEqual(ServicesMode.Fake, services.Mode, "editor services mode");
            // Media/Api/MediaBridgeFactory.cs: UNITY_ANDROID / UNITY_IOS bridges are compiled only with !UNITY_EDITOR, so the
            // editor always gets the FAKE backend (synthetic test patterns, PNG sequence + WAV instead of MP4).
            Assert.AreEqual(MediaBackend.Fake, media.Backend, "editor media backend");
#endif
            Assert.IsInstanceOf<StudioBackend>(studioRuntime.Backend,
                "the real studio started (UnavailableStudioBackend means StudioServices.Create failed; see the error log)");
            AppComposition composition = experience.Composition;
            Assert.IsNotNull(composition, "AppExperience.Composition");
            Assert.IsInstanceOf<UnityAppUi>(experience.Ui, "UI shell host (null = BallverseUIRoot.Create failed)");
            UiShell shell = ((UnityAppUi)experience.Ui).Shell;
            Assert.IsNotNull(shell, "UiShell");
            AppContext ctx = composition.Context;
            Assert.IsInstanceOf<UnitySimSurface>(ctx.Surface, "live sim surface (LiveSimView)");
            Assert.IsInstanceOf<UnityGalleryPreviews>(ctx.Previews, "gallery previews (GalleryPreviewRenderer)");
            Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<AppRunner>(), "AppRunner (per-frame Tick / LateTick)");
            Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<ServicesRunner>(), "ServicesRunner");
            Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<LiveAudioOutput>(), "LiveAudioOutput");
            // DeviceAudio adds the app's only listener to the camera-less boot scene, on an object of its own.
            string audioPlacement = DeviceAudio.FindPlacementProblem();
            Assert.IsNull(audioPlacement, audioPlacement);
            Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<BallverseUIRoot>(), "BallverseUIRoot");
            Assert.IsNotNull(UnityEngine.Object.FindAnyObjectByType<StudioRunner>(), "StudioRunner (export / preview loop)");
            _notices = ctx.Notices;
            _notices.Shown += OnToast;
            _settings = ctx.Settings;
            Info("tier " + ctx.Tier + ", navigator at " + shell.Navigator.Current + ", export stage " + composition.ViewModels.ExportModel.Stage);

            BallverseViewModels vms = composition.ViewModels;
            SimulationViewModel sim = vms.SimulationModel;
            StudioViewModel studio = vms.StudioModel;
            ExportViewModel export = vms.ExportModel;
            UiNavigator nav = shell.Navigator;
            // SimulationViewModel.RaiseNotice sends the 🎲 / clip notices to the visible SimulationScreen's handler instead of
            // AppNotices; listening here too keeps them in the diagnostics and the 🎲 post-condition (the screen's own
            // handler is subscribed whenever this test is on the simulation screen, so the routing does not change).
            _sim = sim;
            _sim.Notice += OnToast;

            // ---- 3. First run lands on Ring Escape (later starts: the gallery tap does) --------------------------------------
            Step("first run → Ring Escape", false);
            if (firstRun)
            {
                Assert.AreEqual(ScreenId.Simulation, nav.Current, "spec §4.3: the first cold start opens the simulation directly");
                Assert.AreEqual(RingEscape, sim.ModeId, "first-run mode");
            }
            else
            {
                Info("not a first run (settings exist): opening Ring Escape from the gallery like a card tap");
                yield return BackToGallery(nav);
                // GalleryScreen card tap: _vm.OpenMode(id); Ctx.Navigator.Push(ScreenId.Simulation).
                vms.GalleryModel.OpenMode(RingEscape);
                nav.Push(ScreenId.Simulation);
                Assert.AreEqual(RingEscape, sim.ModeId, "opened mode");
            }
            Assert.IsTrue(ctx.Live.HasRun, "a live run started");
            Assert.IsTrue(ctx.Settings.Data.FirstRunDone, "first-run flag stored");

            // ---- 4. Live simulation for a few seconds of real frames at 3× ---------------------------------------------------
            Step("live run at 3x", false);
            // The settings sheet's time-scale slider: IParamPanelViewModel.Apply (common param timeScale, live, 0.25-3).
            sim.Params.Apply(CommonParams.TimeScale, ParamValue.Float(3.0));
            // The session paces at the sanitized value at once (ModeSession.SetLiveParam); the tier may clamp it below 3.
            Assert.IsTrue(sim.Params.Values.TryGet(CommonParams.TimeScale, out ParamValue appliedScale), "timeScale in the applied values");
            Info("time scale applied: " + appliedScale.AsFloat + " (speed " + sim.Speed + ")");
            Assert.AreEqual((float)appliedScale.AsFloat, sim.Speed, 0.01f, "the live run paces at the applied time scale");
            Assert.Greater(sim.Speed, 1.01f, "the 3x request sped the live run up");
            int startTick = ctx.Live.Tick;
            int frames = 0;
            var played = Stopwatch.StartNew();
            yield return WaitUntil("3 s of real frames", () =>
            {
                frames++;
                return played.Elapsed.TotalSeconds >= LiveRunRealSeconds && frames >= LiveRunMinFrames;
            }, FrameStepTimeoutS);
            int ticks = ctx.Live.Tick - startTick;
            Info("played " + frames + " frames in " + played.Elapsed.TotalSeconds.ToString("F1") + " s: " + ticks + " ticks (phase " + sim.Phase
                 + ", completed " + ctx.Live.IsCompleted + ")");
            Assert.IsTrue(ticks > 0 || ctx.Live.IsCompleted, "the live run advanced with real frames");
            if (Screen.width > 0 && Screen.height > 0) Check(ctx.Surface.RenderTarget != null, "LiveSimView produced a render target");
            else Soft("Screen size is " + Screen.width + "x" + Screen.height + ": the live surface does not present without a screen");

            // ---- 5. 🎲 within the tier budget -------------------------------------------------------------------------------
            Step("generate (dice)", false);
            long budgetMs = ctx.Remote.Budgets.Get(ctx.Tier).TimeMs;
            LiveRunSnapshot beforeDice = LiveRunSnapshot.Of(sim, ctx);
            int noticesBeforeDice = _shownNotices.Count;
            // A session seed proven to select a candidate: with a random one, "nothing found" is a legitimate outcome on a slow
            // runner, and this step checks the wiring, not luck. Everything else (budget, worker, posting back) stays real.
            var pinnedSeeds = new List<ulong>();
            sim.SessionSeedSource = SmokeChecks.ViableSessionSeedSource(sim, ctx, pinnedSeeds);
            sim.Generate();
            Assert.IsTrue(sim.IsGenerating, "🎲 started (the result is posted back to the main thread on a later frame)");
            double generationTimeout = Math.Max(FrameStepTimeoutS, 20.0 * budgetMs / 1000.0);
            var generating = Stopwatch.StartNew();
            bool declinedChoice = false;
            yield return WaitUntil("generation finished (budget " + budgetMs + " ms, tier " + ctx.Tier + ")", () =>
            {
                // A forced winner not found shows a modal choice; system back closes it (= keep the current run).
                if (!sim.IsGenerating && sim.AwaitingWinnerDecision)
                {
                    declinedChoice = true;
                    nav.Back();
                }
                return !sim.IsGenerating && !sim.AwaitingWinnerDecision;
            }, generationTimeout);
            LiveRunSnapshot afterDice = LiveRunSnapshot.Of(sim, ctx);
            Info("generation took " + generating.ElapsedMilliseconds + " ms; session seed " + string.Join(", ", pinnedSeeds) + ", style " + sim.LastGenerationStyle + ", phase " + sim.Phase
                 + ", " + beforeDice + " → " + afterDice + (declinedChoice ? " (winner choice closed)" : "") + ", toasts so far: " + string.Join(", ", _toasts));
            Assert.IsTrue(ctx.Live.HasRun, "a run is shown after 🎲");
            string diceFailure = SmokeChecks.DiceFailure(beforeDice, afterDice, declinedChoice,
                _shownNotices.Skip(noticesBeforeDice).ToList(), key => ctx.Notices.TextOf(key));
            Assert.IsNull(diceFailure, diceFailure);

            // ---- 6. «В видео» → studio with the clip -------------------------------------------------------------------------
            Step("to video → studio", false);
            yield return WaitUntil("clip has " + ClipTicks + " ticks", () => ctx.Live.Tick >= ClipTicks || ctx.Live.IsCompleted, FrameStepTimeoutS);
            // SimulationScreen.ToVideo: _vm.ToVideo(); Ctx.Navigator.Push(ScreenId.Studio).
            sim.ToVideo();
            nav.Push(ScreenId.Studio);
            Assert.IsTrue(studio.HasProject, "«В видео» opened a project; toasts: " + string.Join(", ", _toasts));
            _projectId = studio.CurrentProjectId;
            Assert.AreEqual(ScreenId.Studio, nav.Current, "studio screen");
            Info("project " + _projectId + ", clip " + studio.Timeline.Sim.SourceUs / 1000 + " ms");
            Assert.Greater(studio.Timeline.Sim.SourceUs, 0, "the clip has a duration");

            Step("studio preview", true);
            yield return WaitUntil("studio preview opened", () => studio.Preview != null, _hasGpu ? StudioOpenTimeoutS : SoftTimeoutS, true);
            if (studio.Preview != null)
            {
                // StudioScreen "play" button.
                studio.TogglePlay();
                Check(studio.IsPlaying, "the studio preview plays");
                yield return Frames(60);
                if (studio.IsPlaying) studio.TogglePlay();
            }

            // ---- 7. Export (rewarded gate → fake ad → FAKE media writer) -----------------------------------------------------
            Step("export", true);
            // StudioScreen "export" button: Ctx.ViewModels.Export.Begin(); the ExportFlowController opens the settings sheet.
            export.Begin();
            Assert.AreEqual(ExportStage.Settings, export.Stage, "export settings sheet");
            Info("export settings " + export.Resolution + " / " + export.Fps + " fps, estimate " + export.EstimatedBytes + " bytes, " + export.EstimatedMs + " ms");
            export.Confirm();
            // «Экспорт» leaves the sheet at once (gate, ad wait or progress); staying means it refused (e.g. too little space).
            Assert.AreNotEqual(ExportStage.Settings, export.Stage,
                "Confirm stayed on the settings sheet (enough space: " + export.EnoughSpace + "); toasts: " + string.Join(", ", _toasts));
            bool watched = false;
            var exporting = Stopwatch.StartNew();
            yield return WaitUntil("export finished", () =>
            {
                ExportStage s = export.Stage;
                if (s == ExportStage.Gate && !watched)
                {
                    // Gate dialog's main button «Смотреть рекламу»: the editor's fake ad network grants the reward at once.
                    watched = true;
                    export.WatchAdAndExport();
                    s = export.Stage;
                }
                // Back on the sheet or the offer after the ad = the gate did not approve (declined / ad failed): stop waiting.
                return s == ExportStage.Done || s == ExportStage.Failed || s == ExportStage.Idle
                    || watched && (s == ExportStage.Settings || s == ExportStage.Gate);
            }, _hasGpu ? ExportTimeoutS : SoftTimeoutS, true);
            StudioExportResult result = export.LastResult;
            Info("export stage " + export.Stage + " after " + exporting.Elapsed.TotalSeconds.ToString("F1") + " s (ad watched: " + watched + "): "
                 + (result == null ? "no result" : result.Error + " " + result.Message + ", " + result.FrameCount + " frames, synthetic " + result.IsSynthetic
                    + ", " + result.OutputPath));
            if (export.Stage == ExportStage.Done)
            {
                Assert.IsNotNull(result, "export result");
                Assert.IsTrue(result.Succeeded, "export result " + result.Error);
                Assert.Greater(result.FrameCount, 0, "exported frames");
                Assert.IsTrue(File.Exists(result.OutputPath) || Directory.Exists(result.OutputPath), "export output exists: " + result.OutputPath);
                Assert.AreEqual(ScreenId.ExportDone, nav.Current, "export done screen");
            }
            else
            {
                Check(false, "export ended in stage " + export.Stage + " (" + export.FailureMessage.Key + ", " + result?.Error + " " + result?.Message + ")");
                // Soft mode only (Check fails with a device): leave no export running behind the next steps.
                if (export.Stage == ExportStage.Progress) export.CancelExport();
                yield return WaitUntil("export stopped", () => export.Stage != ExportStage.Progress, FrameStepTimeoutS);
            }

            // ---- 8. Back to the gallery (system back through every screen) -------------------------------------------------
            Step("back to gallery", false);
            yield return BackToGallery(nav);
            Assert.IsFalse(studio.HasProject, "studio closed by back");
            Assert.IsFalse(sim.IsPlaying, "simulation left by back");

            // ---- 9. Projects tab lists the project, then back to the Modes tab with live previews -------------------------
            Step("projects tab", false);
            nav.SelectTab(TabId.Projects);
            Assert.AreEqual(ScreenId.Projects, nav.Current, "projects tab");
            string projectId = _projectId;
            yield return WaitUntil("project listed", () => vms.Projects.Projects.Any(p => p.Id == projectId), NavigationTimeoutS);
            Step("project cover", true);
            yield return WaitUntil("project cover rendered", () => vms.Projects.Projects.Any(p => p.Id == projectId && p.Cover != null), _hasGpu ? NavigationTimeoutS : SoftTimeoutS, true);

            nav.SelectTab(TabId.Modes);
            Assert.AreEqual(ScreenId.Gallery, nav.Current, "modes tab");

            // ---- 9b. Every registered mode: gallery preview + a live run (M02 / M05 join by registration only) -------------
            string[] modeIds = vms.Gallery.Modes.Select(m => m.ModeId).ToArray();
            Info("registered modes: " + string.Join(", ", modeIds));
            Assert.Contains(RingEscape, modeIds, "Ring Escape card in the gallery");
            foreach (string id in modeIds)
            {
                Step("gallery preview " + id, true);
                yield return WaitUntil(id + " gallery preview", () => vms.Gallery.GetPreview(id) != null, _hasGpu ? NavigationTimeoutS : SoftTimeoutS, true);
            }
            yield return Frames(30);
            foreach (string id in modeIds)
            {
                if (id == RingEscape) continue; // the main flow above ran it end to end
                Step("mode " + id, false);
                // The photosensitivity warning (SimulationViewModel.PhotosensitiveModes, e.g. helix_drop) is a non-cancellable
                // UI Toolkit dialog whose OK button has no public hook outside the visual tree: mark it acknowledged up front
                // (the setting its OK writes) so the run plays; teardown removes the mark again. See README, "cannot cover".
                if (Array.IndexOf(SimulationViewModel.PhotosensitiveModes, id) >= 0 && !ctx.Settings.Data.PhotosensitivityWarned.Contains(id))
                {
                    ctx.Settings.Update(d => d.PhotosensitivityWarned.Add(id));
                    _photosensitivityMarked.Add(id);
                }
                // GalleryScreen card tap: _vm.OpenMode(id); Ctx.Navigator.Push(ScreenId.Simulation).
                vms.GalleryModel.OpenMode(id);
                nav.Push(ScreenId.Simulation);
                Assert.AreEqual(id, sim.ModeId, "opened mode; toasts: " + string.Join(", ", _toasts));
                Assert.AreEqual(ScreenId.Simulation, nav.Current, "simulation screen for " + id);
                Assert.IsTrue(ctx.Live.HasRun, id + ": a live run started");
                Assert.IsTrue(sim.IsPlaying, id + ": the live run plays (phase " + sim.Phase + ")");
                int modeStart = ctx.Live.Tick;
                yield return WaitUntil(id + ": 60 live ticks", () => ctx.Live.Tick >= modeStart + 60 || ctx.Live.IsCompleted, FrameStepTimeoutS);
                Info(id + ": tick " + ctx.Live.Tick + ", phase " + sim.Phase);
                if (Screen.width > 0 && Screen.height > 0) Check(ctx.Surface.RenderTarget != null, id + ": LiveSimView produced a render target");
                yield return BackToGallery(nav);
                Assert.IsFalse(sim.IsPlaying, id + ": simulation left by back");
            }

            // ---- 10. Pause / resume / low memory through the real lifecycle --------------------------------------------------
            Step("pause, resume, low memory", false);
            Bootstrap.Platform.Lifecycle.NotifyPause(true);
            Assert.IsFalse(ctx.Settings.IsDirty, "pause saved the settings");
            Assert.IsTrue(File.Exists(settingsFile), "settings file after pause: " + settingsFile);
            yield return Frames(2);
            Bootstrap.Platform.Lifecycle.NotifyPause(false);
            Bootstrap.Platform.Lifecycle.NotifyLowMemory();
            yield return Frames(30);
            Assert.AreEqual(AppHostState.Running, host.State);
            Assert.IsEmpty(host.Failures, "module failures after the flow");

            // ---- 11. Clean up the project this test created -------------------------------------------------------------
            Step("cleanup", false);
            Assert.IsTrue(ctx.Studio.DeleteProject(projectId), "smoke project deleted");
            _projectId = null;
            yield return Frames(2);
            _log.ThrowIfUnexpected();
            Info("flow complete");
        }

        [UnityTearDown]
        public IEnumerator ShutDown()
        {
            if (_log != null) _log.Enter("shutdown", false);
            if (_notices != null) _notices.Shown -= OnToast;
            if (_sim != null) _sim.Notice -= OnToast;
            if (_settings != null && _photosensitivityMarked.Count > 0 && Bootstrap.Host != null)
            {
                // Leave a local editor's app data as it was: the warning shows again on the next real open.
                var marked = _photosensitivityMarked.ToArray();
                _settings.Update(d => d.PhotosensitivityWarned.RemoveAll(m => Array.IndexOf(marked, m) >= 0));
                _settings.Save();
            }
            _photosensitivityMarked.Clear();
            AppHost host = Bootstrap.Host;
            if (host != null && _projectId != null)
            {
                // The flow failed after creating its project: remove it so a local editor keeps no smoke leftovers.
                try
                {
                    host.Services.Get<StudioRuntime>().Backend?.DeleteProject(_projectId);
                }
                catch (Exception e)
                {
                    Debug.LogWarning("[Smoke] could not delete project " + _projectId + ": " + e.Message);
                }
                _projectId = null;
            }
            Bootstrap bootstrap = UnityEngine.Object.FindAnyObjectByType<Bootstrap>();
            if (bootstrap != null)
            {
                // Bootstrap.OnDestroy shuts the host down (modules in reverse order) like the end of the app's process.
                UnityEngine.Object.Destroy(bootstrap.gameObject);
                yield return null;
                yield return null;
                if (host != null) Assert.AreEqual(AppHostState.ShutDown, host.State, "host shut down with Bootstrap");
                Assert.IsNull(Bootstrap.Host, "Bootstrap.Host after shutdown");
                ReportLeftovers();
            }
            try
            {
                if (_log != null)
                {
                    foreach (SmokeLog.Entry e in _log.Whitelisted) _softFindings.Add("whitelisted: " + e.Type + " " + e.Message);
                    foreach (SmokeLog.Entry e in _log.Soft) _softFindings.Add("soft (no GPU): " + e);
                    if (_softFindings.Count > 0) Debug.LogWarning("[Smoke] soft findings:\n" + string.Join("\n", _softFindings));
                    Debug.Log("[Smoke] timeline:\n" + string.Join("\n", _timeline));
                    _log.ThrowIfUnexpected();
                }
            }
            finally
            {
                _log?.Dispose();
                _log = null;
            }
        }

        // ------------------------------------------------------------------ helpers

        void Step(string name, bool gpuDependent)
        {
            _log.ThrowIfUnexpected();
            if (_stepClock != null) _timeline[_timeline.Count - 1] += " (" + _stepClock.Elapsed.TotalSeconds.ToString("F1") + " s)";
            _stepClock = Stopwatch.StartNew();
            _timeline.Add(name);
            _log.Enter(name, gpuDependent);
            Debug.Log("[Smoke] step: " + name);
        }

        static void Info(string message) => Debug.Log("[Smoke]   " + message);

        /// <summary>A GPU-dependent check: hard with a graphics device, a logged soft finding without one.</summary>
        void Check(bool ok, string what)
        {
            if (ok) return;
            if (_hasGpu) Assert.Fail(what);
            Soft(what);
        }

        void Soft(string what)
        {
            _softFindings.Add(_log.CurrentStep + ": " + what);
            Debug.LogWarning("[Smoke] soft: " + what);
        }

        void OnToast(LocalizedText text)
        {
            _shownNotices.Add(text);
            _toasts.Add(text.Key ?? text.Literal ?? "?");
        }

        /// <summary>
        /// Yields frames until <paramref name="done"/> holds; fails on an unexpected error log. After the timeout it fails, except
        /// for a <paramref name="gpuDependent"/> wait without a graphics device, which becomes a soft finding.
        /// </summary>
        IEnumerator WaitUntil(string what, Func<bool> done, double timeoutSeconds, bool gpuDependent = false)
        {
            var clock = Stopwatch.StartNew();
            int frames = 0;
            while (true)
            {
                _log.ThrowIfUnexpected();
                if (done()) yield break;
                if (clock.Elapsed.TotalSeconds > timeoutSeconds)
                {
                    string message = "Timed out after " + timeoutSeconds + " s (" + frames + " frames) waiting for: " + what + "\n" + Diagnostics();
                    if (gpuDependent) Check(false, message);
                    else Assert.Fail(message);
                    yield break;
                }
                frames++;
                yield return null;
            }
        }

        IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                _log.ThrowIfUnexpected();
                yield return null;
            }
            _log.ThrowIfUnexpected();
        }

        /// <summary>System back (Android back / the screens' back buttons) until the Modes root shows.</summary>
        IEnumerator BackToGallery(UiNavigator nav)
        {
            var clock = Stopwatch.StartNew();
            int presses = 0;
            while (nav.Current != ScreenId.Gallery)
            {
                if (presses >= 12 || clock.Elapsed.TotalSeconds > NavigationTimeoutS)
                    Assert.Fail("Could not get back to the gallery after " + presses + " back presses\n" + Diagnostics());
                if (nav.Stack.CurrentTab != TabId.Modes) nav.SelectTab(TabId.Modes);
                else nav.Back();
                presses++;
                yield return Frames(3);
            }
            Info("gallery after " + presses + " back press(es)");
        }

        string Diagnostics()
        {
            var sb = new StringBuilder();
            sb.Append("step '").Append(_log?.CurrentStep).Append("'");
            AppHost host = Bootstrap.Host;
            sb.Append("; host ").Append(host != null ? host.State.ToString() : "null");
            if (host != null)
            {
                foreach (AppModuleFailure f in host.Failures) sb.Append("\n  module failure: ").Append(f.ModuleName).Append(' ').Append(f.Stage).Append(": ").Append(f.Exception);
                if (host.Services.TryGet(out AppExperience experience) && experience.Composition != null)
                {
                    AppComposition c = experience.Composition;
                    SimulationViewModel s = c.ViewModels.SimulationModel;
                    sb.Append("; screen ").Append(experience.Ui is UnityAppUi ui && ui.Shell != null ? ui.Shell.Navigator.Current.ToString() : "?");
                    sb.Append("; sim ").Append(s.ModeId).Append(' ').Append(s.Phase).Append(" tick ").Append(c.Context.Live.Tick)
                        .Append(" generating ").Append(s.IsGenerating).Append(" decision ").Append(s.AwaitingWinnerDecision);
                    sb.Append("; studio project ").Append(c.ViewModels.StudioModel.CurrentProjectId).Append(" preview ").Append(c.ViewModels.StudioModel.Preview != null);
                    sb.Append("; export ").Append(c.ViewModels.ExportModel.Stage).Append(' ').Append(c.ViewModels.ExportModel.Progress.ToString("F2"));
                }
            }
            sb.Append("; toasts: ").Append(string.Join(", ", _toasts));
            return sb.ToString();
        }

        /// <summary>Objects the app creates that should be gone after shutdown (informational: logged and removed).</summary>
        void ReportLeftovers()
        {
            var left = new List<UnityEngine.Object>();
            void Collect<T>() where T : UnityEngine.Object
            {
                T found = UnityEngine.Object.FindAnyObjectByType<T>();
                if (found != null) left.Add(found);
            }
            Collect<AppRunner>();
            Collect<ServicesRunner>();
            Collect<LiveAudioOutput>();
            Collect<BallverseUIRoot>();
            Collect<StudioRunner>();
            if (left.Count == 0) return;
            _softFindings.Add("objects left after shutdown: " + string.Join(", ", left.Select(o => o.GetType().Name)));
            foreach (UnityEngine.Object o in left)
            {
                if (o is Component c) UnityEngine.Object.Destroy(c.gameObject);
            }
        }
    }
}
