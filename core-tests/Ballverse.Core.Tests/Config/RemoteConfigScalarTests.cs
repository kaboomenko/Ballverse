using System;
using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Config;
using Xunit;
using static Ballverse.Core.Tests.Config.ConfigTestUtil;

namespace Ballverse.Core.Tests.Config
{
    /// <summary>Integer and boolean keys (spec §5.3 IN1/IN5/IN6, RW3/RW4/RW7, §5.2, §3.10, §9.3).</summary>
    public class RemoteConfigScalarTests
    {
        static readonly Dictionary<string, Func<RemoteConfigValues, int>> s_ints = new Dictionary<string, Func<RemoteConfigValues, int>>
        {
            { RemoteConfigKeys.InterstitialEveryNGames, v => v.InterstitialEveryNGames },
            { RemoteConfigKeys.InterstitialMinIntervalSec, v => v.InterstitialMinIntervalSec },
            { RemoteConfigKeys.InterstitialGraceGames, v => v.InterstitialGraceGames },
            { RemoteConfigKeys.RewardedWaitTimeoutSec, v => v.RewardedWaitTimeoutSec },
            { RemoteConfigKeys.RewardedRetryWindowMin, v => v.RewardedRetryWindowMin },
            { RemoteConfigKeys.PaidModeTrialSeconds, v => v.PaidModeTrialSeconds },
            { RemoteConfigKeys.RatePromptAfterExports, v => v.RatePromptAfterExports },
        };

        static readonly Dictionary<string, Func<RemoteConfigValues, bool>> s_bools = new Dictionary<string, Func<RemoteConfigValues, bool>>
        {
            { RemoteConfigKeys.RewardedExportEnabled, v => v.RewardedExportEnabled },
            { RemoteConfigKeys.PaidModeTrialEnabled, v => v.PaidModeTrialEnabled },
            { RemoteConfigKeys.WatermarkEnabled, v => v.WatermarkEnabled },
        };

        public static IEnumerable<object[]> IntRanges()
        {
            yield return new object[] { RemoteConfigKeys.InterstitialEveryNGames, 1, 20, 3 };
            yield return new object[] { RemoteConfigKeys.InterstitialMinIntervalSec, 0, 600, 60 };
            yield return new object[] { RemoteConfigKeys.InterstitialGraceGames, 0, 50, 0 };
            yield return new object[] { RemoteConfigKeys.RewardedWaitTimeoutSec, 1, 30, 5 };
            yield return new object[] { RemoteConfigKeys.RewardedRetryWindowMin, 0, 60, 10 };
            yield return new object[] { RemoteConfigKeys.PaidModeTrialSeconds, 10, 120, 30 };
            yield return new object[] { RemoteConfigKeys.RatePromptAfterExports, 1, 20, 2 };
        }

        public static IEnumerable<object[]> BoolKeys()
        {
            yield return new object[] { RemoteConfigKeys.RewardedExportEnabled };
            yield return new object[] { RemoteConfigKeys.PaidModeTrialEnabled };
            yield return new object[] { RemoteConfigKeys.WatermarkEnabled };
        }

        [Theory]
        [MemberData(nameof(IntRanges))]
        public void IntAcceptsWholeRange(string key, int min, int max, int def)
        {
            for (int n = min; n <= max; n++)
            {
                var r = Parse(key, n.ToString(System.Globalization.CultureInfo.InvariantCulture));
                AssertNoIssues(r);
                Assert.Equal(n, s_ints[key](r.Values));
            }
            Assert.Equal(def, s_ints[key](RemoteConfigValues.Defaults));
        }

        [Theory]
        [MemberData(nameof(IntRanges))]
        public void IntClampsOutOfRange(string key, int min, int max, int def)
        {
            var low = Parse(key, (min - 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(min, s_ints[key](low.Values));
            Assert.Equal(key, Single(low, RemoteConfigIssueKind.OutOfRange, RemoteConfigIssueSeverity.Warning).Key);

            var high = Parse(key, (max + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
            Assert.Equal(max, s_ints[key](high.Values));
            Single(high, RemoteConfigIssueKind.OutOfRange, RemoteConfigIssueSeverity.Warning);

            var huge = Parse(key, "99999999999999999999999");
            Assert.Equal(max, s_ints[key](huge.Values));
            Single(huge, RemoteConfigIssueKind.OutOfRange, RemoteConfigIssueSeverity.Warning);
            var negHuge = Parse(key, "-99999999999999999999999");
            Assert.Equal(min, s_ints[key](negHuge.Values));
            Single(negHuge, RemoteConfigIssueKind.OutOfRange, RemoteConfigIssueSeverity.Warning);

            var blank = Parse(key, " \t ");
            Assert.Equal(def, s_ints[key](blank.Values));
            Single(blank, RemoteConfigIssueKind.EmptyValue, RemoteConfigIssueSeverity.Info);
        }

        [Theory]
        [MemberData(nameof(IntRanges))]
        public void IntInvalidKeepsDefault(string key, int min, int max, int def)
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var fractions = new[] { min.ToString(inv) + ".5", max.ToString(inv) + ".5" };
            foreach (var bad in new[] { "abc", "3.5", "1,5", "NaN", "Infinity", "1e400", "0x10", "[3]", "\"3\"", "true", "3 4" }.Concat(fractions))
            {
                var r = Parse(key, bad);
                Assert.Equal(def, s_ints[key](r.Values));
                var issue = Single(r, RemoteConfigIssueKind.InvalidType, RemoteConfigIssueSeverity.Error);
                Assert.Equal(key, issue.Key);
                Assert.Contains("default kept", issue.Message);
            }
        }

        [Theory]
        [InlineData(" 7 ", 7)]
        [InlineData("+7", 7)]
        [InlineData("7.0", 7)]
        [InlineData("7e0", 7)]
        [InlineData("1E1", 10)]
        public void IntAcceptsIntegralForms(string text, int expected)
        {
            var r = Parse(RemoteConfigKeys.InterstitialEveryNGames, text);
            AssertNoIssues(r);
            Assert.Equal(expected, r.Values.InterstitialEveryNGames);
        }

        [Theory]
        [MemberData(nameof(BoolKeys))]
        public void BoolHappyPath(string key)
        {
            foreach (var t in new[] { "true", "TRUE", " True ", "1" })
            {
                var r = Parse(key, t);
                AssertNoIssues(r);
                Assert.True(s_bools[key](r.Values));
            }
            foreach (var f in new[] { "false", "False", " FALSE", "0" })
            {
                var r = Parse(key, f);
                AssertNoIssues(r);
                Assert.False(s_bools[key](r.Values));
            }
        }

        [Theory]
        [MemberData(nameof(BoolKeys))]
        public void BoolInvalidKeepsDefaultTrue(string key)
        {
            foreach (var bad in new[] { "yes", "no", "2", "tru", "[true]", "\"false\"", "off" })
            {
                var r = Parse(key, bad);
                Assert.True(s_bools[key](r.Values));
                Single(r, RemoteConfigIssueKind.InvalidType, RemoteConfigIssueSeverity.Error);
            }
        }

        [Fact]
        public void KillSwitchesTurnOff()
        {
            var r = Parse(
                (RemoteConfigKeys.RewardedExportEnabled, "false"),
                (RemoteConfigKeys.WatermarkEnabled, "false"),
                (RemoteConfigKeys.PaidModeTrialEnabled, "false"));
            AssertNoIssues(r);
            Assert.False(r.Values.RewardedExportEnabled);
            Assert.False(r.Values.WatermarkEnabled);
            Assert.False(r.Values.PaidModeTrialEnabled);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void EmptyValueIsInfoAndKeepsDefault(string text)
        {
            foreach (var key in RemoteConfigKeys.All)
            {
                var r = Parse(key, text);
                var issue = Single(r, RemoteConfigIssueKind.EmptyValue, RemoteConfigIssueSeverity.Info);
                Assert.Equal(key, issue.Key);
                Assert.Equal(RawDictionary(RemoteConfigValues.Defaults), RawDictionary(r.Values));
            }
        }

        [Fact]
        public void UnknownKeysAreInfoSortedOrdinal()
        {
            var r = Parse(("zeta", "1"), ("Alpha", "x"), (RemoteConfigKeys.InterstitialEveryNGames, "4"), ("alpha", "[]"), ("FREE_MODES", "[]"));
            Assert.Equal(4, r.Values.InterstitialEveryNGames);
            Assert.Equal(new[] { "Alpha", "FREE_MODES", "alpha", "zeta" }, r.Issues.Select(i => i.Key));
            Assert.All(r.Issues, i =>
            {
                Assert.Equal(RemoteConfigIssueKind.UnknownKey, i.Kind);
                Assert.Equal(RemoteConfigIssueSeverity.Info, i.Severity);
            });
            Assert.Equal(DefaultFree(), r.Values.FreeModes);
        }

        [Fact]
        public void IssuesFollowKeyTableOrder()
        {
            var r = Parse(
                (RemoteConfigKeys.RatePromptAfterExports, "x"),
                ("unknown", "1"),
                (RemoteConfigKeys.FreeModes, "x"),
                (RemoteConfigKeys.InterstitialGraceGames, "x"));
            Assert.Equal(new[] { RemoteConfigKeys.FreeModes, RemoteConfigKeys.InterstitialGraceGames, RemoteConfigKeys.RatePromptAfterExports, "unknown" },
                r.Issues.Select(i => i.Key));
        }

        [Fact]
        public void OneBadKeyDoesNotAffectOthers()
        {
            var r = Parse(
                (RemoteConfigKeys.InterstitialEveryNGames, "oops"),
                (RemoteConfigKeys.InterstitialMinIntervalSec, "120"),
                (RemoteConfigKeys.ExportBitrates, "{broken"));
            Assert.Equal(3, r.Values.InterstitialEveryNGames);
            Assert.Equal(120, r.Values.InterstitialMinIntervalSec);
            Assert.Equal(16000, r.Values.ExportBitrates.P1080Fps60Kbps);
            Assert.Equal(2, r.Issues.Count(i => i.Severity == RemoteConfigIssueSeverity.Error));
        }

        [Fact]
        public void IssueToStringNamesKeyAndKind()
        {
            var r = Parse(RemoteConfigKeys.InterstitialEveryNGames, "oops");
            string s = r.Issues[0].ToString();
            Assert.Contains("interstitial_every_n_games", s);
            Assert.Contains("InvalidType", s);
            Assert.Contains("Error", s);
        }
    }
}
