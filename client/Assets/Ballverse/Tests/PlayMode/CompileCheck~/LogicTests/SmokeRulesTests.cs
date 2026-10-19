using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Ballverse.App;
using Ballverse.App.Tests;
using Ballverse.Core.Params;
using Ballverse.UI.Localization;
using NUnit.Framework;
using UnityEngine;

namespace Ballverse.PlayModeTests.LogicTests
{
    /// <summary>
    /// The 🎲 post-condition of AppBootSmokeTest step 5 (SmokeChecks.DiceFailure), run against the real SimulationViewModel and
    /// generator the way the smoke test drives them: open Ring Escape from the gallery with its first-run preset, speed it up
    /// to 3×, 🎲, wait. QA round 1: the old check (a run is shown) also passed when the 🎲 applied nothing.
    /// </summary>
    public sealed class DicePostConditionTests
    {
        const int WaitMs = 120000;
        TestApp _app;

        [TearDown]
        public void TearDown()
        {
            _app?.Dispose();
            _app = null;
        }

        TestApp BootOnRingEscape(List<LocalizedText> notices)
        {
            _app = new TestApp().Boot();
            // GalleryScreen card tap + SimulationScreen.OnShow (screen active, its Notice handler subscribed).
            _app.ViewModels.GalleryModel.OpenMode("ring_escape");
            _app.Sim.SetScreenActive(true);
            _app.Sim.Notice += notices.Add;
            _app.Context.Notices.Shown += notices.Add;
            _app.Sim.Params.Apply(CommonParams.TimeScale, ParamValue.Float(3.0));
            for (int i = 0; i < 30; i++) _app.Frame();
            Assert.IsTrue(_app.Context.Live.HasRun, "live run");
            return _app;
        }

        static void WaitForDice(TestApp app)
        {
            var watch = Stopwatch.StartNew();
            while ((app.Sim.IsGenerating || app.Sim.AwaitingWinnerDecision) && watch.ElapsedMilliseconds < WaitMs)
            {
                System.Threading.Thread.Sleep(2);
                app.Frame();
            }
            Assert.IsFalse(app.Sim.IsGenerating, "generation finished");
            Assert.IsFalse(app.Sim.AwaitingWinnerDecision, "no forced winner in the first-run preset, so no choice");
        }

        [Test]
        public void AGeneratedRunPasses()
        {
            var notices = new List<LocalizedText>();
            TestApp app = BootOnRingEscape(notices);
            LiveRunSnapshot before = LiveRunSnapshot.Of(app.Sim, app.Context);
            int start = notices.Count;
            app.Sim.Generate();
            Assert.IsTrue(app.Sim.IsGenerating);
            WaitForDice(app);
            LiveRunSnapshot after = LiveRunSnapshot.Of(app.Sim, app.Context);
            string failure = SmokeChecks.DiceFailure(before, after, false, notices.Skip(start).ToList(), k => app.Context.Notices.TextOf(k));
            Assert.IsNull(failure, failure + "; errors: " + string.Join("; ", app.Errors));
            Assert.AreEqual(PresetsViewModel.CustomId, after.PresetId);
        }

        /// <summary>QA's probe: 🎲 then cancel ends with no run applied; the old post-condition passed, the new one must not.</summary>
        [Test]
        public void ACancelledDiceFails()
        {
            var notices = new List<LocalizedText>();
            TestApp app = BootOnRingEscape(notices);
            LiveRunSnapshot before = LiveRunSnapshot.Of(app.Sim, app.Context);
            app.Sim.Generate();
            app.Sim.CancelGeneration();
            WaitForDice(app);
            for (int i = 0; i < 30; i++) app.Frame();
            Assert.IsTrue(app.Context.Live.HasRun, "the old check (a run is shown) still holds");
            string failure = SmokeChecks.DiceFailure(before, LiveRunSnapshot.Of(app.Sim, app.Context), false, notices,
                k => app.Context.Notices.TextOf(k));
            StringAssert.Contains("applied no generated run", failure);
        }

        [Test]
        public void NothingFoundAndFailedNoticesFailEvenIfTheRunChanged()
        {
            var notices = new List<LocalizedText>();
            TestApp app = BootOnRingEscape(notices);
            AppNotices n = app.Context.Notices;
            var before = new LiveRunSnapshot(app.Sim.CurrentValues, 1UL, "default");
            var after = new LiveRunSnapshot(app.Sim.CurrentValues.With(CommonParams.Seed, ParamValue.Seed(2UL)), 2UL, PresetsViewModel.CustomId);
            Assert.IsNull(SmokeChecks.DiceFailure(before, after, false, new[] { n.TextOf(AppStrings.GenerateWeak) }, k => n.TextOf(k)),
                "«try a longer duration» still starts the run");
            // "app.*" keys arrive as resolved literals (AppText); a key-only text must match too.
            foreach (string key in new[] { AppStrings.GenerateNone, AppStrings.GenerateFailed })
            {
                StringAssert.Contains(key, SmokeChecks.DiceFailure(before, after, false, new[] { n.TextOf(key) }, k => n.TextOf(k)));
                StringAssert.Contains(key, SmokeChecks.DiceFailure(before, after, false, new[] { LocalizedText.FromKey(key) }, k => n.TextOf(k)));
            }
        }

        [Test]
        public void AClosedWinnerChoiceMustKeepTheRun()
        {
            var notices = new List<LocalizedText>();
            TestApp app = BootOnRingEscape(notices);
            ParamSet values = app.Sim.CurrentValues;
            var before = new LiveRunSnapshot(values, 7UL, "default");
            Assert.IsNull(SmokeChecks.DiceFailure(before, new LiveRunSnapshot(values, 7UL, "default"), true, null, null));
            Assert.IsNotNull(SmokeChecks.DiceFailure(before, new LiveRunSnapshot(values.With(CommonParams.Seed, ParamValue.Seed(8UL)), 8UL, "custom"), true, null, null));
        }
    }

    /// <summary>SmokeLog's rules (QA round 1: GPU errors outside the GPU-flagged steps, the "missing" shader text).</summary>
    public sealed class SmokeLogRulesTests
    {
        const string Unsupported = "[Ballverse.Render] Shader 'Hidden/Ballverse/SdfBall' is unsupported on this device; that layer is skipped.";
        const string MissingOrUnsupported = "[Ballverse.Render] Shader 'Hidden/Ballverse/Batch' is missing or unsupported on this device; that layer is skipped.";
        const string RendererIncomplete = "[Ballverse.Render] Some renderer shaders failed to load; missing layers are skipped.";
        const string LiveViewStack = "Ballverse.Render.BallverseRenderer:Render (Ballverse.Render.RenderFrame)\nBallverse.Sim.LiveSimView:Present (int,int)\nBallverse.App.SimulationViewModel:LateTick (single)\n";
        const string CoverStack = "UnityEngine.Debug:LogError (object)\nBallverse.Studio.StudioServices:RenderCover (string,int)\nBallverse.App.StudioBackend:RenderCover (string)\nBallverse.App.ProjectsViewModel:Tick (single)\n";
        const string LogicStack = "UnityEngine.Debug:LogError (object)\nBallverse.App.ExportViewModel:Confirm ()\n";

        static SmokeLogClass C(LogType type, string message, string stack, bool hasGpu, bool gpuStep) =>
            SmokeLog.Classify(type, message, stack, hasGpu, gpuStep);

        [Test]
        public void WithAGraphicsDeviceEveryErrorFails()
        {
            Assert.AreEqual(SmokeLogClass.Unexpected, C(LogType.Error, Unsupported, LiveViewStack, true, true));
            Assert.AreEqual(SmokeLogClass.Unexpected, C(LogType.Error, RendererIncomplete, "", true, false));
            Assert.AreEqual(SmokeLogClass.Unexpected, C(LogType.Exception, "NullReferenceException", LiveViewStack, true, false));
            Assert.AreEqual(SmokeLogClass.Unexpected, C(LogType.Error, "x", CoverStack, true, false));
            Assert.AreEqual(SmokeLogClass.Ignored, C(LogType.Warning, "x", LiveViewStack, true, false));
        }

        [Test]
        public void WithoutAGraphicsDeviceOnlyTheRendererShaderMessagesAreWhitelisted()
        {
            Assert.AreEqual(SmokeLogClass.Whitelisted, C(LogType.Error, Unsupported, "", false, false));
            Assert.AreEqual(SmokeLogClass.Whitelisted, C(LogType.Error, RendererIncomplete, "", false, false));
            // The same text covers a shader missing from the project: still whitelisted by text, which is why the smoke test
            // asserts up front that every ExpectedLogs.RendererShaders entry resolves (AppBootSmokeTest step 0).
            Assert.AreEqual(SmokeLogClass.Whitelisted, C(LogType.Error, MissingOrUnsupported, "", false, false));
            // Exact regex and type: anything else is not whitelisted (it is still a soft finding: the renderer's tag is GPU-side).
            Assert.AreEqual(SmokeLogClass.Soft, C(LogType.Error, Unsupported + " extra", LogicStack, false, false), "exact regex");
            Assert.AreEqual(SmokeLogClass.Soft, C(LogType.Exception, Unsupported, LogicStack, false, false), "type must match");
            Assert.IsNull(ExpectedLogs.Find(LogType.Error, Unsupported + " extra", false));
            Assert.IsNull(ExpectedLogs.Find(LogType.Exception, Unsupported, false));
            Assert.IsNull(ExpectedLogs.Find(LogType.Error, Unsupported, true), "nothing is whitelisted with a device");
        }

        [Test]
        public void WithoutAGraphicsDeviceGpuSideErrorsAreSoftInAnyStep()
        {
            // The live view renders every frame, also in the logic steps (first run, live run at 3x).
            Assert.AreEqual(SmokeLogClass.Soft, C(LogType.Exception, "InvalidOperationException: x", LiveViewStack, false, false));
            // Covers render from ProjectsViewModel.Tick as soon as the Projects tab shows (step 'projects tab', a logic step).
            Assert.AreEqual(SmokeLogClass.Soft, C(LogType.Error, "cover failed", CoverStack, false, false));
            // An exception text carrying its own stack (AppNotices.Report) counts too.
            Assert.AreEqual(SmokeLogClass.Soft, C(LogType.Error, "[Ballverse.App] sim.surface: " + LiveViewStack, "", false, false));
            Assert.AreEqual(SmokeLogClass.Soft, C(LogType.Error, "RenderTexture.Create failed", "UnityEngine.RenderTexture:Create ()\n", false, false));
        }

        [Test]
        public void WithoutAGraphicsDeviceLogicErrorsFailOutsideGpuSteps()
        {
            Assert.AreEqual(SmokeLogClass.Unexpected, C(LogType.Error, "export gate broke", LogicStack, false, false));
            Assert.AreEqual(SmokeLogClass.Unexpected, C(LogType.Assert, "assert", "Ballverse.Studio.ClipLibrary:Open (string)\n", false, false),
                "studio storage is logic, not GPU");
            // Inside a GPU-flagged step's time window everything is soft without a device (documented; CI requires a device).
            Assert.AreEqual(SmokeLogClass.Soft, C(LogType.Error, "export gate broke", LogicStack, false, true));
        }

        [Test]
        public void TheRendererShaderListIsComplete()
        {
            Assert.AreEqual(6, ExpectedLogs.RendererShaders.Count);
            Assert.AreEqual(6, ExpectedLogs.RendererShaders.Select(p => p.Key).Distinct().Count());
            CollectionAssert.Contains(ExpectedLogs.RendererShaders.Select(p => p.Value).ToList(), Ballverse.Render.RenderResources.BallShaderName);
            CollectionAssert.Contains(ExpectedLogs.RendererShaders.Select(p => p.Value).ToList(), Ballverse.Render.RenderResources.ParticleShaderName);
        }
    }
}
