using System.Linq;
using Ballverse.Core.Config;
using Xunit;
using static Ballverse.Core.Tests.Config.ConfigTestUtil;

namespace Ballverse.Core.Tests.Config
{
    /// <summary><c>free_modes</c>, <c>modes_disabled</c>, <c>gallery_order</c> (spec §5.1, §7.6, §4.3, §9.3).</summary>
    public class RemoteConfigModeListTests
    {
        [Fact]
        public void FreeModesExtension()
        {
            var r = Parse(RemoteConfigKeys.FreeModes, "[\"M01\",\"M02\",\"M03\",\"M04\",\"M05\",\"M09\"]");
            AssertNoIssues(r);
            Assert.Equal(Codes(1, 2, 3, 4, 5, 9), r.Values.FreeModes);
            Assert.True(r.Values.IsListedFree(ModeCode.M09));
            Assert.True(r.Values.IsEffectivelyFree(ModeCode.M09));
            Assert.False(r.Values.IsListedFree(ModeCode.M10));
        }

        [Fact]
        public void FreeModesAcceptsStableIdsAndPreservesOrder()
        {
            var r = Parse(RemoteConfigKeys.FreeModes, "[\"M05\",\"ring_escape\",\" M02 \",\"grow_bounce\",\"M03\",\"M04\",\"orbit\"]");
            Assert.Equal(Codes(5, 1, 2, 3, 4, 14), r.Values.FreeModes);
            Single(r, RemoteConfigIssueKind.DuplicateEntry, RemoteConfigIssueSeverity.Warning);
        }

        [Fact]
        public void FreeModesNarrowingIsAcceptedWithWarning()
        {
            var r = Parse(RemoteConfigKeys.FreeModes, "[\"M01\",\"M02\"]");
            Assert.Equal(Codes(1, 2), r.Values.FreeModes);
            var issue = Single(r, RemoteConfigIssueKind.NarrowedFreeModes, RemoteConfigIssueSeverity.Warning);
            Assert.Contains("M03,M04,M05", issue.Message);

            var empty = Parse(RemoteConfigKeys.FreeModes, "[]");
            Assert.Empty(empty.Values.FreeModes);
            Assert.Empty(empty.Values.EffectiveFreeModes);
            Single(empty, RemoteConfigIssueKind.NarrowedFreeModes, RemoteConfigIssueSeverity.Warning);
        }

        [Fact]
        public void ModeListDropsUnknownNonStringAndDuplicates()
        {
            var r = Parse(RemoteConfigKeys.ModesDisabled, "[\"M07\",\"M16\",\"m08\",\"nope\",7,null,{},\"M07\",\"last_standing\",\"M08\"]");
            Assert.Equal(Codes(7, 8), r.Values.ModesDisabled);
            Assert.Equal(3, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.UnknownMode));
            Assert.Equal(3, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.InvalidEntry));
            Assert.Equal(2, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.DuplicateEntry));
            Assert.All(r.Issues, i => Assert.Equal(RemoteConfigIssueSeverity.Warning, i.Severity));
        }

        [Theory]
        [InlineData(RemoteConfigKeys.FreeModes)]
        [InlineData(RemoteConfigKeys.ModesDisabled)]
        [InlineData(RemoteConfigKeys.GalleryOrder)]
        public void ModeListInvalidJsonOrTypeKeepsDefault(string key)
        {
            var bad = Parse(key, "[\"M01\"");
            Single(bad, RemoteConfigIssueKind.InvalidJson, RemoteConfigIssueSeverity.Error);
            Assert.Equal(RawDictionary(RemoteConfigValues.Defaults), RawDictionary(bad.Values));

            foreach (var wrongType in new[] { "{\"M01\":true}", "\"M01\"", "1", "null", "true" })
            {
                var r = Parse(key, wrongType);
                Single(r, RemoteConfigIssueKind.InvalidType, RemoteConfigIssueSeverity.Error);
                Assert.Equal(RawDictionary(RemoteConfigValues.Defaults), RawDictionary(r.Values));
            }

            var unquoted = Parse(key, "M01,M02");
            Single(unquoted, RemoteConfigIssueKind.InvalidJson, RemoteConfigIssueSeverity.Error);
            var trailing = Parse(key, "[\"M01\"] x");
            Single(trailing, RemoteConfigIssueKind.InvalidJson, RemoteConfigIssueSeverity.Error);
        }

        [Fact]
        public void JsonCommentsAndWhitespaceAreTolerated()
        {
            var r = Parse(RemoteConfigKeys.ModesDisabled, " /* hotfix */ [ \"M11\" ] // ok \n");
            AssertNoIssues(r);
            Assert.Equal(Codes(11), r.Values.ModesDisabled);
        }

        [Fact]
        public void EffectiveFreeSetIsFreeMinusDisabled()
        {
            var r = Parse(
                (RemoteConfigKeys.FreeModes, "[\"M01\",\"M02\",\"M03\",\"M04\",\"M05\",\"M06\"]"),
                (RemoteConfigKeys.ModesDisabled, "[\"M02\",\"M06\",\"M10\"]"));
            var v = r.Values;
            Assert.Equal(Codes(1, 3, 4, 5), v.EffectiveFreeModes);
            Assert.True(v.IsListedFree(ModeCode.M02));
            Assert.True(v.IsModeDisabled(ModeCode.M02));
            Assert.False(v.IsEffectivelyFree(ModeCode.M02));
            Assert.False(v.IsEffectivelyFree(ModeCode.M06));
            Assert.True(v.IsModeDisabled(ModeCode.M10));
            Assert.False(v.IsEffectivelyFree(ModeCode.M10));
            Assert.True(v.IsEffectivelyFree(ModeCode.M01));
            Assert.False(v.IsModeDisabled((ModeCode)0));
            Assert.False(v.IsListedFree((ModeCode)99));
        }

        [Fact]
        public void EffectiveFreeSetExhaustiveOverDisabledSubsetsOfDefaults()
        {
            // All 2^5 subsets of the default free modes disabled.
            for (int mask = 0; mask < 32; mask++)
            {
                var disabled = Enumerable.Range(1, 5).Where(i => (mask & (1 << (i - 1))) != 0).Select(i => "\"M0" + i + "\"");
                var r = Parse(RemoteConfigKeys.ModesDisabled, "[" + string.Join(",", disabled) + "]");
                var expected = Enumerable.Range(1, 5).Where(i => (mask & (1 << (i - 1))) == 0).Select(i => (ModeCode)i);
                Assert.Equal(expected, r.Values.EffectiveFreeModes);
                foreach (var m in ModeCatalog.All)
                {
                    Assert.Equal(r.Values.EffectiveFreeModes.Contains(m.Code), r.Values.IsEffectivelyFree(m.Code));
                }
            }
        }

        [Fact]
        public void GalleryOrderFullPermutationAccepted()
        {
            var order = new[] { 15, 14, 13, 12, 11, 10, 9, 8, 7, 6, 5, 4, 3, 2, 1 };
            var r = Parse(RemoteConfigKeys.GalleryOrder, "[" + string.Join(",", order.Select(i => "\"M" + i.ToString("00") + "\"")) + "]");
            AssertNoIssues(r);
            Assert.Equal(Codes(order), r.Values.GalleryOrder);
        }

        [Fact]
        public void GalleryOrderPartialIsCompletedInCatalogOrder()
        {
            var r = Parse(RemoteConfigKeys.GalleryOrder, "[\"M09\",\"M03\",\"M09\",\"M99\",\"marble_race\",\"M12\"]");
            Assert.Equal(Codes(9, 3, 12, 1, 2, 4, 5, 6, 7, 8, 10, 11, 13, 14, 15), r.Values.GalleryOrder);
            var issue = Single(r, RemoteConfigIssueKind.IncompleteGalleryOrder, RemoteConfigIssueSeverity.Warning);
            Assert.Contains("M01,M02,M04", issue.Message);
            Assert.Equal(2, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.DuplicateEntry));
            Single(r, RemoteConfigIssueKind.UnknownMode, RemoteConfigIssueSeverity.Warning);
        }

        [Fact]
        public void GalleryOrderEmptyBecomesCatalogOrder()
        {
            var r = Parse(RemoteConfigKeys.GalleryOrder, "[]");
            Assert.Equal(AllCodes(), r.Values.GalleryOrder);
            Single(r, RemoteConfigIssueKind.IncompleteGalleryOrder, RemoteConfigIssueSeverity.Warning);
        }

        [Fact]
        public void GalleryOrderIsAlwaysAPermutationForRandomInputs()
        {
            var rng = Rng.FromSeed(0xA11E7UL, 1);
            for (int iter = 0; iter < 300; iter++)
            {
                int count = rng.NextInt(0, 25);
                var items = Enumerable.Range(0, count).Select(_ =>
                {
                    int k = rng.NextInt(0, 20);
                    return k < 17 ? "\"M" + k.ToString("00") + "\"" : (k == 17 ? "3" : (k == 18 ? "\"ring_escape\"" : "null"));
                }).ToArray();
                var r = Parse(RemoteConfigKeys.GalleryOrder, "[" + string.Join(",", items) + "]");
                Assert.Equal(15, r.Values.GalleryOrder.Count);
                Assert.Equal(AllCodes(), r.Values.GalleryOrder.OrderBy(c => (int)c));
            }
        }
    }
}
