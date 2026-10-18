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
            public bool Throw;

            public void Show(Action<bool> onContinue)
            {
                Calls++;
                if (Throw) throw new InvalidOperationException("screen failed");
                Pending = onContinue;
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

            // The screen answering much later does not open the system dialog or call anyone again.
            prePrompt.Pending(true);
            Assert.AreEqual(0, tracking.RequestCount);
            Assert.AreEqual(1, first);
            Assert.AreEqual(1, second);
            Assert.AreEqual(1, third);
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
