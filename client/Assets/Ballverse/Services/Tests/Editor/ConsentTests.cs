using System;
using Ballverse.Services.Editor;
using NUnit.Framework;

namespace Ballverse.Services.Tests
{
    public sealed class ConsentTests
    {
        sealed class ScriptedPrePrompt : IAttPrePrompt
        {
            public Action<bool> Pending;
            public int Calls;
            public int Dismissals;
            public bool Throw;

            public void Show(Action<bool> onContinue)
            {
                Calls++;
                if (Throw) throw new InvalidOperationException("screen failed");
                Pending = onContinue;
            }

            public void Dismiss()
            {
                Dismissals++;
            }
        }

        sealed class ThrowingTracking : ITrackingAuthorization
        {
            public TrackingAuthorizationStatus Status => TrackingAuthorizationStatus.NotDetermined;

            public void Request(Action<TrackingAuthorizationStatus> done)
            {
                throw new InvalidOperationException("native bridge failed");
            }
        }

        /// <summary>ATT whose system dialog stays open until the test answers (or never: lost native callback).</summary>
        sealed class HeldTracking : ITrackingAuthorization
        {
            public Action<TrackingAuthorizationStatus> Pending;
            public int RequestCount;

            public TrackingAuthorizationStatus Status { get; set; } = TrackingAuthorizationStatus.NotDetermined;

            public void Request(Action<TrackingAuthorizationStatus> done)
            {
                RequestCount++;
                Pending = done;
            }
        }

        static readonly string AllPurposes = "11111111111";
        static readonly string NoPurposes = "00000000000";

        // ---- ATT flow ----

        [Test]
        public void ThrowingPrePromptEndsTheFlowForEveryCaller()
        {
            var clock = new ManualClock();
            var tracking = new FakeTrackingAuthorization();
            var flow = new TrackingDecisionFlow(tracking, clock, new MemoryServiceLog());
            var prePrompt = new ScriptedPrePrompt { Throw = true };
            int done = 0;
            flow.Ensure(prePrompt, () => done++);
            Assert.AreEqual(1, done);
            Assert.IsFalse(flow.IsRunning);
            Assert.AreEqual(0, tracking.RequestCount, "no system dialog without the explanation screen");

            flow.Ensure(prePrompt, () => done++);
            Assert.AreEqual(2, done, "later ads continue at once");
            Assert.AreEqual(1, prePrompt.Calls, "asked at most once per session");
        }

        [Test]
        public void ThrowingSystemRequestEndsTheFlow()
        {
            var flow = new TrackingDecisionFlow(new ThrowingTracking(), new ManualClock(), new MemoryServiceLog());
            int done = 0;
            flow.Ensure(null, () => done++);
            Assert.AreEqual(1, done);
            flow.Ensure(null, () => done++);
            Assert.AreEqual(2, done);
        }

        [Test]
        public void UnansweredPrePromptIsAbandonedAfterTheTimeout()
        {
            var clock = new ManualClock();
            var tracking = new FakeTrackingAuthorization();
            var flow = new TrackingDecisionFlow(tracking, clock, new MemoryServiceLog());
            var prePrompt = new ScriptedPrePrompt();
            int first = 0, second = 0, third = 0;
            flow.Ensure(prePrompt, () => first++);
            Assert.IsTrue(flow.IsRunning);

            clock.Advance(TrackingDecisionFlow.FlowTimeoutMs - 1);
            flow.Ensure(prePrompt, () => second++);
            Assert.AreEqual(0, second, "still within the time the user may take");

            clock.Advance(1);
            flow.Ensure(prePrompt, () => third++);
            Assert.AreEqual(1, third, "a lost flow no longer blocks later ads");
            Assert.AreEqual(1, first);
            Assert.AreEqual(1, second);
            Assert.IsFalse(flow.IsRunning);
            Assert.AreEqual(1, prePrompt.Dismissals, "the stale screen is closed");

            // The screen answering much later does not open the system dialog or call anyone again.
            prePrompt.Pending(true);
            Assert.AreEqual(0, tracking.RequestCount);
            Assert.AreEqual(1, first);
            Assert.AreEqual(1, second);
            Assert.AreEqual(1, third);
        }

        [Test]
        public void AbandonEndsTheFlowDismissesThePrePromptAndIgnoresALateAnswer()
        {
            var tracking = new FakeTrackingAuthorization();
            var flow = new TrackingDecisionFlow(tracking, new ManualClock(), new MemoryServiceLog());
            var prePrompt = new ScriptedPrePrompt();
            int done = 0;
            flow.Ensure(prePrompt, () => done++);

            flow.Abandon("test");
            Assert.IsFalse(flow.IsRunning);
            Assert.AreEqual(1, done);
            Assert.AreEqual(1, prePrompt.Dismissals);

            prePrompt.Pending(true);
            Assert.AreEqual(0, tracking.RequestCount, "a late continue does not show the system dialog");
            flow.Abandon("again");
            Assert.AreEqual(1, prePrompt.Dismissals, "no-op when idle");
            int later = 0;
            flow.Ensure(prePrompt, () => later++);
            Assert.AreEqual(1, later, "not asked again this session");
            Assert.AreEqual(1, prePrompt.Calls);
        }

        [Test]
        public void AbandonWhileTheSystemDialogIsOpenLeavesTheAnsweredPrePromptAlone()
        {
            var tracking = new HeldTracking();
            var flow = new TrackingDecisionFlow(tracking, new ManualClock(), new MemoryServiceLog());
            var prePrompt = new ScriptedPrePrompt();
            int done = 0;
            flow.Ensure(prePrompt, () => done++);
            prePrompt.Pending(true); // the user answered "continue": the screen closed itself, the system dialog is up
            Assert.AreEqual(1, tracking.RequestCount);
            Assert.IsTrue(flow.IsRunning);

            flow.Abandon("watchdog");
            Assert.AreEqual(1, done);
            Assert.IsFalse(flow.IsRunning);
            Assert.AreEqual(0, prePrompt.Dismissals, "an answered pre-prompt is not dismissed (the UI may show another screen)");

            tracking.Pending(TrackingAuthorizationStatus.Denied); // the lost callback arrives late
            Assert.AreEqual(1, done, "waiters are not called twice");
        }

        [Test]
        public void DeclinedPrePromptIsNotDismissedLater()
        {
            var clock = new ManualClock();
            var tracking = new HeldTracking();
            var flow = new TrackingDecisionFlow(tracking, clock, new MemoryServiceLog());
            var prePrompt = new ScriptedPrePrompt();
            int first = 0, later = 0;
            flow.Ensure(prePrompt, () => first++);
            prePrompt.Pending(false); // "not now": the flow ends at once
            Assert.AreEqual(1, first);
            Assert.AreEqual(0, tracking.RequestCount);

            var second = new ScriptedPrePrompt();
            flow.Ensure(second, () => later++);
            Assert.AreEqual(1, later, "asked once per session");
            flow.Abandon("idle");
            Assert.AreEqual(0, prePrompt.Dismissals + second.Dismissals);
            Assert.AreEqual(0, second.Calls);
        }

        [Test]
        public void PrePromptAnsweringTwiceRequestsOnce()
        {
            var tracking = new FakeTrackingAuthorization { Answer = TrackingAuthorizationStatus.Authorized };
            var flow = new TrackingDecisionFlow(tracking, new ManualClock(), null);
            var prePrompt = new ScriptedPrePrompt();
            int done = 0;
            flow.Ensure(prePrompt, () => done++);
            prePrompt.Pending(true);
            prePrompt.Pending(true);
            Assert.AreEqual(1, tracking.RequestCount);
            Assert.AreEqual(1, done);
            Assert.AreEqual(TrackingAuthorizationStatus.Authorized, flow.Status);
        }

        // ---- Consent Mode (spec §9.1) ----

        [Test]
        public void OutsideConsentRegionsEverythingIsGranted()
        {
            var d = ConsentModeRules.Decide(ConsentRequirement.NotRequired, default);
            Assert.IsTrue(d.HasDecision);
            Assert.AreEqual(true, d.Consent.AnalyticsStorage);
            Assert.AreEqual(true, d.Consent.AdStorage);
        }

        [Test]
        public void RefusalDeniesAnalyticsStorage()
        {
            var d = ConsentModeRules.Decide(ConsentRequirement.Obtained, new TcfSignals(true, NoPurposes));
            Assert.IsTrue(d.HasDecision);
            Assert.AreEqual(false, d.Consent.AnalyticsStorage);
            Assert.IsNull(d.Consent.AdStorage, "ad signals come from the TCF string");
            Assert.IsNull(d.Consent.AdPersonalization);
        }

        [Test]
        public void ConsentToTheMeasurementPurposesGrantsAnalyticsStorage()
        {
            Assert.AreEqual(true, ConsentModeRules.Decide(ConsentRequirement.Obtained, new TcfSignals(true, AllPurposes)).Consent.AnalyticsStorage);
            // Purposes 1, 8 and 9 only.
            Assert.IsTrue(ConsentModeRules.AnalyticsStorageGranted(new TcfSignals(true, "10000001100")));
            Assert.IsFalse(ConsentModeRules.AnalyticsStorageGranted(new TcfSignals(true, "00000001100")), "no device storage");
            Assert.IsFalse(ConsentModeRules.AnalyticsStorageGranted(new TcfSignals(true, "10000000100")), "no purpose 8");
            Assert.IsFalse(ConsentModeRules.AnalyticsStorageGranted(new TcfSignals(true, "10000001000")), "no purpose 9");
            Assert.IsFalse(ConsentModeRules.AnalyticsStorageGranted(new TcfSignals(true, "1")), "short string");
        }

        [Test]
        public void UsStateMessagesKeepAnalyticsAndUnreadableTcfDeniesIt()
        {
            Assert.IsTrue(ConsentModeRules.AnalyticsStorageGranted(new TcfSignals(false, null)));
            Assert.IsFalse(ConsentModeRules.AnalyticsStorageGranted(default));
            Assert.IsFalse(ConsentModeRules.AnalyticsStorageGranted(new TcfSignals(null, AllPurposes)));
        }

        [Test]
        public void WithoutADecisionNothingIsDecided()
        {
            Assert.IsFalse(ConsentModeRules.Decide(ConsentRequirement.Required, new TcfSignals(true, AllPurposes)).HasDecision);
            Assert.IsFalse(ConsentModeRules.Decide(ConsentRequirement.Unknown, default).HasDecision);
        }

        // ---- Info.plist (ATT usage description build step) ----

        const string Plist = "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<plist version=\"1.0\">\n<dict>\n\t<key>CFBundleName</key>\n\t<string>Ballverse</string>\n\t<key>Nested</key>\n\t<dict>\n\t\t<key>A</key>\n\t\t<true/>\n\t</dict>\n</dict>\n</plist>\n";

        [Test]
        public void UsageDescriptionIsAddedToTheRootDictWhenMissing()
        {
            string patched = InfoPlistText.EnsureString(Plist, "NSUserTrackingUsageDescription", "Ads & you <3");
            Assert.IsTrue(InfoPlistText.HasKey(patched, "NSUserTrackingUsageDescription"));
            StringAssert.Contains("<string>Ads &amp; you &lt;3</string>", patched);
            int keyAt = patched.IndexOf("NSUserTrackingUsageDescription", StringComparison.Ordinal);
            Assert.Greater(keyAt, patched.IndexOf("<key>A</key>", StringComparison.Ordinal), "after the nested dict");
            Assert.Less(keyAt, patched.LastIndexOf("</dict>", StringComparison.Ordinal), "inside the root dict");
        }

        [Test]
        public void ExistingUsageDescriptionIsKept()
        {
            string withKey = InfoPlistText.EnsureString(Plist, "NSUserTrackingUsageDescription", "owner text");
            Assert.AreEqual(withKey, InfoPlistText.EnsureString(withKey, "NSUserTrackingUsageDescription", "other"));
            Assert.Throws<FormatException>(() => InfoPlistText.EnsureString("<plist></plist>", "K", "v"));
        }
    }
}
