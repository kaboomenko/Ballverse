using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Ballverse.Core;
using Ballverse.Core.Generation;
using Ballverse.Core.Modes;
using Ballverse.Core.Monetization;
using Ballverse.Core.Params;
using Ballverse.Media;
using Ballverse.Platform;
using Ballverse.Services;
using NUnit.Framework;

namespace Ballverse.App.Tests
{
    /// <summary>
    /// The whole app booted through the real <see cref="AppHost"/> and <see cref="AppModuleCatalog"/> over a test
    /// <see cref="AppEnvironment"/>: Services in fake mode on a manual clock, the fake media bridge, the GPU-free studio backend
    /// (real clip library and project files in a temporary folder), a recording UI and surface. <see cref="Frame"/> plays one
    /// display frame the way the Unity runner does (services tick, composition Tick and LateTick, posted callbacks).
    /// </summary>
    public sealed class TestApp : IDisposable
    {
        public const float FrameSeconds = 1f / 60f;

        readonly bool _ownsRoot;
        TestPump _pump;
        SynchronizationContext _previousContext;

        public TestApp(string root = null)
        {
            _ownsRoot = root == null;
            Root = root ?? Path.Combine(Path.GetTempPath(), "bv-app-tests-" + Guid.NewGuid().ToString("N"));
        }

        public string Root { get; }
        public readonly ManualClock Clock = new ManualClock();
        public readonly ManualAppClock AppClock = new ManualAppClock();
        public readonly MemoryKeyValueStore Store = new MemoryKeyValueStore();
        public readonly FakeConnectivity Connectivity = new FakeConnectivity();
        public readonly FakeUi Ui = new FakeUi();
        public readonly FakeSurface Surface = new FakeSurface();
        public readonly FakeDevice Device = new FakeDevice();
        public readonly FakePlatform Platform = new FakePlatform();
        public readonly NullTextures Textures = new NullTextures();
        public readonly List<string> Errors = new List<string>();

        /// <summary>Fake store setup (owned products); null = empty account.</summary>
        public FakeStoreConfig StoreConfig;

        /// <summary>Raw Remote Config applied at start; null = build defaults.</summary>
        public Dictionary<string, string> Remote;

        /// <summary>Fake media setup (device signals, picker); null = defaults.</summary>
        public FakeMediaOptions MediaOptions;

        /// <summary>Replaces the GPU-free studio backend (e.g. a factory that throws); null = <see cref="FakeStudioBackend"/>.</summary>
        public Func<StudioEnvironment, IStudioBackend> StudioFactory;

        /// <summary>The platform's ATT authorization (iOS-like when NotDetermined); null = the services' default.</summary>
        public ITrackingAuthorization Tracking;

        /// <summary>No UI shell (the composition runs headless).</summary>
        public bool Headless;

        public AppHost Host { get; private set; }
        public AppLifecycle Lifecycle { get; private set; }
        public FakeStudioBackend Studio { get; private set; }

        public BallverseServices Services => Host.Services.Get<BallverseServices>();
        public AppComposition Composition => Host.Services.Get<AppExperience>().Composition;
        public AppContext Context => Composition.Context;
        public BallverseViewModels ViewModels => Composition.ViewModels;
        public SimulationViewModel Sim => ViewModels.SimulationModel;
        public StudioViewModel StudioModel => ViewModels.StudioModel;
        public ExportViewModel Export => ViewModels.ExportModel;

        public TestApp Boot()
        {
            // Our own main-thread context for the app's lifetime (also inside Unity, whose context only runs posted work between
            // editor frames): async continuations of the studio (stream rebuild, preview opening) run when a frame drains it.
            _previousContext = SynchronizationContext.Current;
            _pump = _previousContext as TestPump ?? new TestPump();
            SynchronizationContext.SetSynchronizationContext(_pump);
            Directory.CreateDirectory(Root);
            Lifecycle = new AppLifecycle(Report);
            var platform = new AppPlatform(Lifecycle, new FrameRatePolicy(_ => { }), new ScreenAwake(_ => { }), new SafeAreaProvider(Report),
                new AppPaths(Path.Combine(Root, "data"), Path.Combine(Root, "cache")));
            var env = new AppEnvironment
            {
                CreateServices = () => ServicesInstaller.Create(new ServicesOptions
                {
                    Mode = ServicesMode.Fake,
                    Clock = Clock,
                    Store = Store,
                    Log = new MemoryServiceLog(),
                    Connectivity = Connectivity,
                    DeveloperBuild = true,
                    Monetization = MonetizationConfig.Release1,
                    FakeStore = StoreConfig,
                    InitialRemoteConfig = Remote,
                    Tracking = Tracking,
                }),
                CreateMedia = () => MediaBridgeFactory.CreateFake(MediaOptions),
                CreateStudio = StudioFactory ?? (s => Studio = new FakeStudioBackend(s.Paths, s.Gate) { Tier = s.Tier }),
                CreateSurface = () => Surface,
                Device = Device,
                Platform = Platform,
                Textures = Textures,
                Clock = AppClock,
                CreateUi = Headless ? (Func<UI.ViewModels.IBallverseViewModels, IAppUi>)null : vms =>
                {
                    Ui.ViewModels = vms;
                    return Ui;
                },
            };
            Host = new AppHost(AppModuleCatalog.Create(platform, env), Report);
            Host.Boot();
            Host.StartDeferred();
            return this;
        }

        void Report(string context, Exception e)
        {
            Errors.Add(context + ": " + e?.GetType().Name + " " + e?.Message);
        }

        /// <summary>One display frame at 60 Hz.</summary>
        public void Frame(float dt = FrameSeconds)
        {
            Clock.Advance((long)Math.Round(dt * 1000f));
            AppClock.NowSeconds += dt;
            Services.Tick();
            Composition.Tick(dt);
            Composition.LateTick(dt);
            _pump?.Drain();
        }

        /// <summary>Runs the services start sequence until the ads are initialized (≥ 3 s after the first frame).</summary>
        public void StartAds()
        {
            Frame();
            Clock.Advance(ServicesStartup.AdsStartDelayMs);
            Frame();
            Frame();
            Assert.AreEqual(AdsServiceState.Ready, Services.Ads.State, "fake ads ready");
        }

        /// <summary>Plays frames until the live run completed (finale done) or <paramref name="maxFrames"/> passed.</summary>
        public void RunToEnd(int maxFrames = 6000)
        {
            for (int i = 0; i < maxFrames && Context.Live.HasRun && !Context.Live.IsCompleted; i++) Frame();
            Assert.IsTrue(Context.Live.IsCompleted, "the live run completed");
        }

        /// <summary>Ring Escape with a fixed 10 s time limit (100 rings, narrow gaps): finishes at tick 780 (core test fixture).</summary>
        public static ParamSet QuickRingEscape(IModeDefinition mode, DeviceTier tier)
        {
            ParamSet s = mode.Schema.CreateDefaults()
                .With(CommonParams.TargetDuration, ParamValue.Int(10))
                .With("ringCount", ParamValue.Int(100))
                .With("gapSize", ParamValue.Float(26));
            return ModeParams.Prepare(mode, s, tier).Values;
        }

        /// <summary>Session seeds handed out by <see cref="PinViableSessionSeeds"/>, in 🎲 order.</summary>
        public readonly List<ulong> PinnedSessionSeeds = new List<ulong>();

        /// <summary>
        /// Every later 🎲 of this app gets a session seed that is proven to select a candidate, instead of the random default
        /// <see cref="SimulationViewModel.SessionSeedSource"/>. QA: with a 2-candidate budget some random seeds found no
        /// acceptable Ring Escape candidate («Couldn't find a working variant»), so tests that need a generated run were flaky.
        /// The source runs on the main thread when the generation starts, after the view model set
        /// <see cref="SimulationViewModel.LastGenerationStyle"/> / <see cref="SimulationViewModel.LastGenerationGoal"/>, so the
        /// search uses exactly the request the view model is about to send. A test that needs a failing seed pins its own.
        /// The request is a copy of SimulationViewModel.StartGeneration's; QaRegressionTests.PinnedSeedHelpersMirrorTheViewModelsGenerationRequest
        /// fails when the two drift apart.
        /// </summary>
        public void PinViableSessionSeeds()
        {
            Sim.SessionSeedSource = () =>
            {
                IModeDefinition mode = Sim.Mode;
                ParamSet values = Sim.CurrentValues;
                string duration = Sim.SelectedDurationId;
                if (duration != "auto" && mode.Schema.Contains(CommonParams.TargetDuration))
                    values = values.With(CommonParams.TargetDuration, ParamValue.Int(int.Parse(duration, System.Globalization.CultureInfo.InvariantCulture)));
                DirectorGoal goal = Sim.LastGenerationGoal;
                // As SimulationViewModel.StartGeneration: the LastSecond goal keeps the user's colours.
                IModeDefinition generator = goal == DirectorGoal.LastSecond ? new ParticipantsKeepingMode(mode) : mode;
                ulong seed = ViableSessionSeed(generator, values, Context.Tier, Context.Remote.Budgets, Context.Remote.Profiles,
                    Sim.LastGenerationStyle, goal);
                PinnedSessionSeeds.Add(seed);
                return seed;
            };
        }

        /// <summary>
        /// The first session seed whose candidate 0 is viable for the request: not rejected, and with the forced winner if one is
        /// set. Candidate 0 is always run (GenerationSession.NextRequest hands it out before any time check), so a 🎲 with this
        /// seed selects a candidate (Success or BelowThreshold) whatever the candidate and time budgets cut. Deterministic: one
        /// candidate budget, no clock.
        /// </summary>
        public static ulong ViableSessionSeed(IModeDefinition generator, ParamSet values, DeviceTier tier, GenerationBudgetTable budgets,
            SpectacleProfileTable profiles, GenerationStyle style, DirectorGoal goal)
        {
            for (ulong seed = 1; seed <= 64; seed++)
            {
                ModeGenerationResult r = ModeGenerator.Generate(generator, new ModeGenerationRequest
                {
                    BaseValues = values,
                    Tier = tier,
                    SessionSeed = seed,
                    Style = style,
                    Goal = goal,
                    Budgets = budgets,
                    Profiles = profiles,
                    Budget = new GenerationBudget(1, 600_000),
                    AutoTakeNearest = false,
                });
                IReadOnlyList<CandidateEvaluation> evaluations = r.Session.Evaluations;
                if (evaluations.Count > 0 && !evaluations[0].IsRejected && evaluations[0].MatchesWinner) return seed;
            }
            Assert.Fail("no session seed in 1..64 gives a viable first candidate for style " + style + ", goal " + goal);
            return 0;
        }

        /// <summary>Opens Ring Escape with <see cref="QuickRingEscape"/> on the simulation screen.</summary>
        public void OpenQuickRun()
        {
            Assert.IsTrue(Context.Modes.TryGet("ring_escape", out IModeDefinition mode));
            Sim.OpenWith(mode, QuickRingEscape(mode, Context.Tier));
            Sim.SetScreenActive(true);
        }

        public void Dispose()
        {
            try
            {
                Host?.Shutdown();
            }
            finally
            {
                if (_pump != null) SynchronizationContext.SetSynchronizationContext(_previousContext);
                if (_ownsRoot) DeleteRoot(Root);
            }
        }

        public static void DeleteRoot(string root)
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch (IOException)
            {
                // temporary folder; the OS cleans it
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
