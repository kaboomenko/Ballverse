using System.Collections.Generic;
using NUnit.Framework;

namespace Ballverse.Services.Tests
{
    public sealed class AnalyticsTests
    {
        [Test]
        public void EventNamesFollowFirebaseRules()
        {
            Assert.IsTrue(AnalyticsSanitizer.IsValidEventName(AnalyticsEvents.AdRewardedOffer));
            Assert.IsFalse(AnalyticsSanitizer.IsValidEventName("Ad_Show"));
            Assert.IsFalse(AnalyticsSanitizer.IsValidEventName("1st"));
            Assert.IsFalse(AnalyticsSanitizer.IsValidEventName("firebase_custom"));
            Assert.IsFalse(AnalyticsSanitizer.IsValidEventName(new string('a', 41)));
            Assert.IsFalse(AnalyticsSanitizer.IsValidEventName(""));
        }

        [Test]
        public void UnknownParametersAreDroppedAndPathsRedacted()
        {
            var input = new List<AnalyticsParam>
            {
                AnalyticsParam.Str(AnalyticsParams.ModeId, "M01"),
                AnalyticsParam.Str("participant_name", "Alice"),
                AnalyticsParam.Str(AnalyticsParams.ErrorCode, "/var/mobile/Media/DCIM/clip.mov"),
                AnalyticsParam.Str(AnalyticsParams.Source, "user@example.com"),
                AnalyticsParam.Str(AnalyticsParams.Target, new string('x', 150)),
                AnalyticsParam.Num(AnalyticsParams.Progress, double.NaN),
                AnalyticsParam.Str(AnalyticsParams.ModeId, "M02"),
            };
            Assert.IsTrue(AnalyticsSanitizer.TrySanitizeEvent("export_fail", input, out var clean, out var dropped));
            Assert.AreEqual(4, clean.Count);
            Assert.AreEqual("M01", clean[0].StringValue);
            Assert.AreEqual(AnalyticsSanitizer.Redacted, clean[1].StringValue);
            Assert.AreEqual(AnalyticsSanitizer.Redacted, clean[2].StringValue);
            Assert.AreEqual(AnalyticsSanitizer.MaxStringValueLength, clean[3].StringValue.Length);
            CollectionAssert.Contains(dropped, "participant_name");
            CollectionAssert.Contains(dropped, AnalyticsParams.Progress);
        }

        [Test]
        public void AtMost25Parameters()
        {
            var names = new[]
            {
                AnalyticsParams.ModeId, AnalyticsParams.IsFree, AnalyticsParams.IsOwned, AnalyticsParams.Source, AnalyticsParams.PresetId,
                AnalyticsParams.IsGenerated, AnalyticsParams.TargetDuration, AnalyticsParams.BallCount, AnalyticsParams.Tier,
                AnalyticsParams.Style, AnalyticsParams.HasForcedWinner, AnalyticsParams.Candidates, AnalyticsParams.Ms,
                AnalyticsParams.QualityOk, AnalyticsParams.ParamId, AnalyticsParams.Group, AnalyticsParams.DurationS,
                AnalyticsParams.Interactive, AnalyticsParams.Result, AnalyticsParams.FromHistory, AnalyticsParams.HasUserVideo,
                AnalyticsParams.Res, AnalyticsParams.Hdr, AnalyticsParams.Layout, AnalyticsParams.Swap, AnalyticsParams.TemplateId,
                AnalyticsParams.Fps,
            };
            var input = new List<AnalyticsParam>();
            foreach (string n in names) input.Add(AnalyticsParam.Int(n, 1));
            Assert.IsTrue(AnalyticsSanitizer.TrySanitizeEvent("sim_start", input, out var clean, out var dropped));
            Assert.AreEqual(AnalyticsSanitizer.MaxParams, clean.Count);
            Assert.AreEqual(2, dropped.Count);
        }

        [Test]
        public void EventsAreBufferedUntilConsentDecision()
        {
            var a = new RecordingAnalyticsService();
            Assert.AreEqual(false, a.AppliedCollectionEnabled, "backend collection stays off until the decision");
            a.LogEvent(AnalyticsEvents.StudioOpen, AnalyticsParam.Bool(AnalyticsParams.HasUserVideo, true));
            a.LogEvent("Bad Name");
            Assert.AreEqual(0, a.Sent.Count);
            Assert.AreEqual(1, a.BufferedCount);

            a.SetCollectionEnabled(true);
            Assert.AreEqual(1, a.Sent.Count);
            Assert.AreEqual(AnalyticsEvents.StudioOpen, a.Sent[0].Name);
            Assert.IsTrue(a.Sent[0].TryGet(AnalyticsParams.HasUserVideo, out var p));
            Assert.AreEqual(1L, p.LongValue);
            Assert.AreEqual(true, a.AppliedCollectionEnabled);

            a.LogEvent(AnalyticsEvents.ShareTap, AnalyticsParam.Str(AnalyticsParams.Target, "tiktok"));
            Assert.AreEqual(2, a.Sent.Count);
        }

        [Test]
        public void DisabledCollectionDropsEverything()
        {
            var a = new RecordingAnalyticsService();
            a.LogEvent(AnalyticsEvents.StudioOpen);
            a.SetCollectionEnabled(false);
            a.LogEvent(AnalyticsEvents.StudioOpen);
            Assert.AreEqual(0, a.Sent.Count);
            Assert.AreEqual(0, a.BufferedCount);
        }

        [Test]
        public void UserPropertiesAreWhitelisted()
        {
            var a = new RecordingAnalyticsService();
            a.SetUserProperty(AnalyticsUserProperties.HasNoExportAds, "true");
            a.SetUserProperty("email", "user@example.com");
            Assert.AreEqual("true", a.SentUserProperties[AnalyticsUserProperties.HasNoExportAds]);
            Assert.IsFalse(a.SentUserProperties.ContainsKey("email"));
        }

        [Test]
        public void ExportsTotalBuckets()
        {
            Assert.AreEqual("0", AnalyticsUserProperties.ExportsTotalBucket(0));
            Assert.AreEqual("1", AnalyticsUserProperties.ExportsTotalBucket(1));
            Assert.AreEqual("2-5", AnalyticsUserProperties.ExportsTotalBucket(5));
            Assert.AreEqual("6-20", AnalyticsUserProperties.ExportsTotalBucket(6));
            Assert.AreEqual("21+", AnalyticsUserProperties.ExportsTotalBucket(21));
        }
    }
}
