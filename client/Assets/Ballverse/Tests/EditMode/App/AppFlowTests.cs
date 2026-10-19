using System.IO;
using System.Linq;
using Ballverse.Core;
using Ballverse.Core.Ads;
using Ballverse.Core.Monetization;
using Ballverse.Core.Projects;
using Ballverse.Services;
using Ballverse.Studio;
using Ballverse.UI.Layout;
using Ballverse.UI.ViewModels;
using NUnit.Framework;

namespace Ballverse.App.Tests
{
    /// <summary>The booted app with fakes: first run, the gallery → simulation → clip → studio → export flow, ads rules.</summary>
    public sealed class AppFlowTests
    {
        TestApp _app;

        [TearDown]
        public void TearDown()
        {
            _app?.Dispose();
            _app = null;
        }

        TestApp Boot(System.Action<TestApp> setup = null)
        {
            _app = new TestApp();
            setup?.Invoke(_app);
            return _app.Boot();
        }

        [Test]
        public void AppHostBootsEveryModuleWithFakesAndTheFirstRunOpensRingEscape()
        {
            TestApp app = Boot();
            Assert.IsEmpty(app.Host.Failures, string.Join("; ", app.Host.Failures.Select(f => f.ModuleName + " " + f.Stage + " " + f.Exception)));
            Assert.AreEqual(AppHostState.Running, app.Host.State);
            Assert.AreEqual(7, app.Host.StartedModules.Count);
            Assert.AreEqual(ServicesMode.Fake, app.Services.Mode);

            CollectionAssert.AreEqual(new[] { "ring_escape" }, app.Ui.OpenedModes);
            Assert.AreEqual("ring_escape", app.Sim.ModeId);
            Assert.IsTrue(app.Context.Live.HasRun);
            Assert.IsTrue(app.Context.Settings.Data.FirstRunDone);
            Assert.AreEqual(app.Context.Tier, app.Ui.Tier);
            Assert.AreSame(app.Context.Live, app.Surface.Bound);

            for (int i = 0; i < 30; i++) app.Frame();
            Assert.Greater(app.Context.Live.Tick, 0, "the live run advances per display frame");
            Assert.AreEqual(30, app.Surface.Presents);

            app.Ui.RaiseExit();
            Assert.AreEqual(1, app.Platform.MovedToBack, "Android back on the root moves the task to the background");
            Assert.IsEmpty(app.Errors, string.Join("; ", app.Errors));
        }

        [Test]
        public void LaterStartsOpenTheGalleryInstead()
        {
            string root = Path.Combine(Path.GetTempPath(), "bv-app-tests-" + System.Guid.NewGuid().ToString("N"));
            try
            {
                using (TestApp first = new TestApp(root).Boot())
                {
                    Assert.AreEqual(1, first.Ui.OpenedModes.Count);
                }
                using (TestApp second = new TestApp(root).Boot())
                {
                    Assert.IsEmpty(second.Ui.OpenedModes);
                    Assert.IsTrue(second.ViewModels.Gallery.Modes.Any(m => m.ModeId == "ring_escape"));
                }
            }
            finally
            {
                TestApp.DeleteRoot(root);
            }
        }

        [Test]
        public void FullFlowGalleryToSimulationToClipToStudioToExport()
        {
            TestApp app = Boot();
            app.StartAds();

            // Gallery → simulation; a finished run goes to the recent-runs ring.
            app.ViewModels.GalleryModel.OpenMode("ring_escape");
            app.OpenQuickRun();
            app.RunToEnd();
            Assert.IsNotNull(app.Sim.Result, "result card");
            Assert.AreEqual(SimPhaseView.Finished, app.Sim.Phase);
            Assert.AreEqual(1, app.Studio.Clips.ListRuns().Count);
            Assert.AreEqual(1, app.Sim.PreviousRuns.Count);

            // Save as clip.
            app.Sim.SaveClip();
            Assert.AreEqual(1, app.Studio.Clips.ListClips().Count);

            // «В видео» → studio with the run as project.
            app.Sim.ToVideo();
            Assert.IsTrue(app.StudioModel.HasProject);
            string projectId = app.StudioModel.CurrentProjectId;
            Assert.IsNotNull(projectId);
            Assert.IsNotNull(app.Studio.LastThumbnails, "timeline thumbnails opened");
            Assert.Greater(app.StudioModel.Timeline.Sim.SourceUs, 0);
            app.StudioModel.SetScreenActive(true);

            // Export: settings → rewarded offer → watch the ad → export with the fake writer.
            app.Export.Begin();
            Assert.AreEqual(ExportStage.Settings, app.Export.Stage);
            app.Export.Confirm();
            Assert.AreEqual(ExportStage.Gate, app.Export.Stage, "first export of a project offers the rewarded ad");
            app.Export.WatchAdAndExport();
            for (int i = 0; i < 5 && app.Export.Stage != ExportStage.Done; i++) app.Frame();
            Assert.AreEqual(ExportStage.Done, app.Export.Stage, string.Join("; ", app.Errors));
            Assert.AreEqual(1, app.Studio.Exports);
            Assert.IsTrue(app.Studio.LastGateAnswer, "the pipeline gate approved exactly this export");
            Assert.IsTrue(File.Exists(app.Export.LastResult.OutputPath));
            Assert.IsTrue(app.Context.Settings.Data.QuestExported);
            Assert.AreEqual(1, app.Context.Settings.Data.ExportCount);
            Assert.AreEqual(1, app.Services.FakeAds.ShowCalls);

            // The project is listed with its export.
            app.Frame();
            Assert.IsTrue(app.ViewModels.Projects.Projects.Any(p => p.Id == projectId));
            Assert.IsTrue(app.ViewModels.Projects.Clips.Count == 1);
        }

        [Test]
        public void AnEvictedRenderStreamIsRebuiltFromTheReplayWhenTheStudioOpens()
        {
            TestApp app = Boot();
            app.Studio.Rebuilder = new ModeStreamRebuilder(app.Context.Modes);
            app.OpenQuickRun();
            app.RunToEnd();
            string runId = app.Context.Live.Outcome.RunId;
            Assert.AreEqual(ClipReadiness.Ready, app.Studio.RunReadiness(runId));

            string dir = app.Studio.Clips.FullDirectory(app.Studio.Clips.GetRun(runId));
            File.Delete(Path.Combine(dir, ProjectPaths.ClipRenderFile));
            Assert.AreEqual(ClipReadiness.NeedsRebuild, app.Studio.RunReadiness(runId));

            app.Sim.ToVideo();
            Assert.IsTrue(app.StudioModel.HasProject, string.Join("; ", app.Errors));
            for (int i = 0; i < 600 && app.Studio.LastThumbnails == null; i++)
            {
                System.Threading.Thread.Sleep(5);
                app.Frame();
            }
            Assert.IsNotNull(app.Studio.LastThumbnails, "preview and thumbnails open after the rebuild; " + string.Join("; ", app.Errors));
            Assert.IsTrue(File.Exists(Path.Combine(app.Studio.ProjectDirectory(app.StudioModel.CurrentProjectId), ProjectPaths.ClipRenderFile)));
            Assert.IsEmpty(app.Errors, string.Join("; ", app.Errors));
        }

        [Test]
        public void WithoutARebuilderAnEvictedRunCannotBecomeAVideo()
        {
            TestApp app = Boot();
            app.OpenQuickRun();
            app.RunToEnd();
            string runId = app.Context.Live.Outcome.RunId;
            File.Delete(Path.Combine(app.Studio.Clips.FullDirectory(app.Studio.Clips.GetRun(runId)), ProjectPaths.ClipRenderFile));
            Assert.AreEqual(ClipReadiness.Unavailable, app.Studio.RunReadiness(runId));
        }

        [Test]
        public void AFinishedLiveRunIsReportedAsGameCompleted()
        {
            TestApp app = Boot();
            app.OpenQuickRun();
            app.RunToEnd();
            Assert.AreEqual(1, app.Services.Interstitials.Policy.SnapshotState().TotalCompletedGames);

            // A run abandoned before the finale never counts.
            app.Sim.Retry();
            for (int i = 0; i < 400; i++) app.Frame();
            app.Sim.Back();
            Assert.AreEqual(1, app.Services.Interstitials.Policy.SnapshotState().TotalCompletedGames);
        }

        [Test]
        public void InterstitialShowsAtANaturalBreakAfterThreeGamesButNeverAtToVideoOrInTheStudio()
        {
            TestApp app = Boot();
            app.StartAds();
            NaturalBreakKind? lastKind = null;
            InterstitialDecision lastDecision = default;
            app.Context.Ads.Evaluated += (k, d) =>
            {
                lastKind = k;
                lastDecision = d;
            };
            for (int i = 0; i < 3; i++) app.Context.Ads.OnRunEnded(GameRunSummary.FinishedRegular(600));

            // IN3: «В видео» and the studio keep it pending.
            app.Context.Ads.At(NaturalBreakKind.ToVideo, null);
            Assert.AreEqual(InterstitialDecisionKind.Suppressed, lastDecision.Kind);
            app.Context.Ads.InStudio = true;
            app.Context.Ads.At(NaturalBreakKind.Retry, null);
            Assert.AreEqual(InterstitialDecisionKind.Suppressed, lastDecision.Kind);
            app.Context.Ads.InStudio = false;
            Assert.AreEqual(0, app.Ui.AdNotices);

            // Retry on the result card (a natural pause, IN2): «Реклама» plaque, the ad, then the run restarts.
            bool? shown = null;
            app.Context.Ads.InterstitialDone += s => shown = s;
            app.OpenQuickRun();
            app.RunToEnd();
            Assert.IsNotNull(app.Sim.Result, "result card");
            app.Sim.Retry();
            Assert.AreEqual(NaturalBreakKind.Retry, lastKind);
            Assert.IsTrue(lastDecision.IsShow);
            Assert.AreEqual(1, app.Ui.AdNotices);
            Assert.AreEqual(true, shown);
            Assert.IsTrue(app.Context.Live.HasRun && !app.Context.Live.IsCompleted, "the action continues after the ad");

            // The counter restarted: the next break shows nothing.
            app.RunToEnd();
            app.Sim.Retry();
            Assert.AreEqual(InterstitialDecisionKind.None, lastDecision.Kind);
            Assert.AreEqual(1, app.Ui.AdNotices);

            // 🎲 on the result card is a natural break too (after three more games and the 60 s minimum interval).
            for (int i = 0; i < 3; i++) app.Context.Ads.OnRunEnded(GameRunSummary.FinishedRegular(600));
            app.Clock.Advance(61_000);
            app.Services.Tick();
            app.RunToEnd();
            app.Sim.Generate();
            Assert.AreEqual(NaturalBreakKind.Generate, lastKind);
            Assert.IsTrue(lastDecision.IsShow);
            Assert.AreEqual(2, app.Ui.AdNotices);
            app.Sim.CancelGeneration();
        }

        [Test]
        public void NoInterstitialEntitlementSuppressesEveryBreak()
        {
            TestApp app = Boot(a =>
            {
                a.StoreConfig = new FakeStoreConfig();
                a.StoreConfig.InitiallyOwned.Add(ProductIds.RemoveInterstitialAds);
            });
            app.StartAds();
            for (int i = 0; i < 3; i++) app.Context.Ads.OnRunEnded(GameRunSummary.FinishedRegular(600));
            app.Sim.Retry();
            Assert.AreEqual(0, app.Ui.AdNotices);
        }

        [Test]
        public void NoExportAdsEntitlementExportsWithoutTheOffer()
        {
            TestApp app = Boot(a =>
            {
                a.StoreConfig = new FakeStoreConfig();
                a.StoreConfig.InitiallyOwned.Add(ProductIds.RemoveExportAds);
            });
            app.StartAds();
            Assert.IsTrue(app.Services.Entitlements.Contains(Entitlement.NoExportAds));
            OpenProjectFromRun(app);

            app.Export.Begin();
            app.Export.Confirm();
            Assert.AreEqual(ExportStage.Done, app.Export.Stage);
            Assert.AreEqual(0, app.Services.FakeAds.ShowCalls);
            Assert.IsTrue(app.Studio.LastGateAnswer);
        }

        [Test]
        public void OfflineExportRunsWithoutAnAd()
        {
            TestApp app = Boot();
            app.StartAds();
            OpenProjectFromRun(app);
            app.Connectivity.IsOffline = true;

            app.Export.Begin();
            app.Export.Confirm();
            Assert.AreEqual(ExportStage.Gate, app.Export.Stage);
            app.Export.WatchAdAndExport();
            Assert.AreEqual(ExportStage.Done, app.Export.Stage, "Р14 / §5.4: offline never blocks the export");
            Assert.AreEqual(0, app.Services.FakeAds.ShowCalls);
        }

        [Test]
        public void BuyingRemoveExportAdsFromTheOfferExportsAtOnce()
        {
            TestApp app = Boot();
            app.StartAds();
            OpenProjectFromRun(app);
            app.Export.Begin();
            app.Export.Confirm();
            Assert.AreEqual(ExportStage.Gate, app.Export.Stage);

            app.Export.BuyRemoveExportAds();
            for (int i = 0; i < 5 && app.Export.Stage != ExportStage.Done; i++) app.Frame();
            Assert.AreEqual(ExportStage.Done, app.Export.Stage, string.Join("; ", app.Errors));
            Assert.IsTrue(app.Services.Entitlements.Contains(Entitlement.NoExportAds));
            Assert.IsFalse(app.Context.Ads.InPurchaseFlow);
            Assert.IsTrue(app.Ui.Toasts.Any(t => t.Key == AppStrings.PurchaseSuccess));
        }

        [Test]
        public void ExportFailureShowsTheFailedStageAndRetryCreditSkipsTheAd()
        {
            TestApp app = Boot(a =>
            {
                a.StoreConfig = new FakeStoreConfig();
                a.StoreConfig.InitiallyOwned.Add(ProductIds.RemoveExportAds);
            });
            app.StartAds();
            OpenProjectFromRun(app);
            app.Studio.FailNextExportWith = true;
            app.Export.Begin();
            app.Export.Confirm();
            Assert.AreEqual(ExportStage.Failed, app.Export.Stage);
            Assert.AreEqual(AppStrings.ExportFailed, app.Export.FailureMessage.Key);
            Assert.IsFalse(app.Context.Ads.Exporting);

            app.Export.RetryFailed(false);
            if (app.Export.Stage == ExportStage.Settings) app.Export.Confirm();
            Assert.AreEqual(ExportStage.Done, app.Export.Stage);
        }

        [Test]
        public void PauseSavesSettingsAndLowMemoryDropsCaches()
        {
            TestApp app = Boot();
            app.Frame();
            app.Lifecycle.NotifyPause(true);
            Assert.IsTrue(File.Exists(Path.Combine(app.Root, "data", "settings", AppSettingsStore.FileName)));
            app.Lifecycle.NotifyPause(false);
            app.Lifecycle.NotifyLowMemory();
            Assert.AreEqual(1, app.Studio.CacheClears);
            Assert.IsEmpty(app.Errors, string.Join("; ", app.Errors));
        }

        [Test]
        public void WeakDeviceGetsTierC()
        {
            TestApp app = Boot(a =>
            {
                a.Device.GpuName = "Mali-G52 MC2";
                a.Device.SystemMemoryMb = 3072;
            });
            Assert.AreEqual(DeviceTier.C, app.Context.Tier);
            Assert.AreEqual(DeviceTier.C, app.Ui.Tier);
            Assert.AreEqual(DeviceTier.C, app.Studio.Tier);
            Assert.AreEqual(DeviceTier.C, app.Surface.Tier);
        }

        static void OpenProjectFromRun(TestApp app)
        {
            app.OpenQuickRun();
            for (int i = 0; i < 400; i++) app.Frame();
            app.Sim.ToVideo();
            Assert.IsTrue(app.StudioModel.HasProject, string.Join("; ", app.Errors));
            app.StudioModel.SetScreenActive(true);
        }
    }
}
