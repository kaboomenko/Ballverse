using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Ballverse.Core.Config;
using Xunit;
using static Ballverse.Core.Tests.Config.ConfigTestUtil;

namespace Ballverse.Core.Tests.Config
{
    /// <summary>
    /// Failure paths and contracts: all-invalid map entries keep built-in data, issue caps, open-map scaling, preset number
    /// fidelity, snapshot immutability, the Build-failure fallback, writer key order and lookup normalization (spec §9.3).
    /// </summary>
    public class RemoteConfigRobustnessTests
    {
        static readonly RemoteConfigValues D = RemoteConfigValues.Defaults;

        // ---- non-empty but entirely invalid map entries ----

        [Fact]
        public void SafeZonesNonEmptyAllInvalidKeepsDefaultEntry()
        {
            var r = Parse(RemoteConfigKeys.PlatformSafeZones,
                "{\"tiktok\":[{\"x\":\"0\",\"y\":\"0\",\"w\":\"1\",\"h\":\"0.08\"},7],\"snap\":[{\"x\":0,\"y\":0,\"w\":1,\"h\":0.1}]}");
            Assert.True(D.TryGetSafeZones("tiktok", out var expected));
            Assert.True(r.Values.TryGetSafeZones("tiktok", out var tiktok));
            Assert.NotEmpty(tiktok);
            Assert.Equal(expected.Select(x => (x.X, x.Y, x.W, x.H)), tiktok.Select(x => (x.X, x.Y, x.W, x.H)));
            var issue = Single(r, RemoteConfigIssueKind.NoValidEntries, RemoteConfigIssueSeverity.Error);
            Assert.Contains("tiktok", issue.Message);
            // Other delivered entries still apply.
            Assert.True(r.Values.TryGetSafeZones("snap", out var snap));
            Assert.Single(snap);
        }

        [Fact]
        public void SafeZonesExplicitEmptyArrayClearsEntry()
        {
            var r = Parse(RemoteConfigKeys.PlatformSafeZones, "{\"tiktok\":[]}");
            AssertNoIssues(r);
            Assert.True(r.Values.TryGetSafeZones("tiktok", out var tiktok));
            Assert.Empty(tiktok);
        }

        [Fact]
        public void TextTemplatesNonEmptyAllInvalidKeepsDefaultLanguage()
        {
            var r = Parse(RemoteConfigKeys.TextTemplates,
                "{\"ru\":[{\"key\":\"who_wins\",\"value\":\"Какой шар победит?\"}],\"de\":[{\"id\":\"part_2\",\"text\":\"Teil 2\"}]}");
            Assert.True(D.TryGetTextTemplates("ru", out var expected));
            Assert.True(r.Values.TryGetTextTemplates("ru", out var ru));
            Assert.Equal(expected.Select(t => t.Id + "=" + t.Text), ru.Select(t => t.Id + "=" + t.Text));
            var issue = Single(r, RemoteConfigIssueKind.NoValidEntries, RemoteConfigIssueSeverity.Error);
            Assert.Contains("ru", issue.Message);
            Assert.True(r.Values.TryGetTextTemplates("de", out var de));
            Assert.Equal("Teil 2", de.Single().Text);
        }

        [Fact]
        public void TextTemplatesExplicitEmptyArrayClearsLanguage()
        {
            var r = Parse(RemoteConfigKeys.TextTemplates, "{\"ru\":[]}");
            AssertNoIssues(r);
            Assert.True(r.Values.TryGetTextTemplates("ru", out var ru));
            Assert.Empty(ru);
        }

        // ---- issue caps ----

        [Fact]
        public void IssuesAreCappedPerKeyButErrorsAreKept()
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < 100000; i++) sb.Append(i == 0 ? "\"zz\"" : ",\"zz\"");
            sb.Append(']');
            var r = Parse(RemoteConfigKeys.ModesDisabled, sb.ToString());
            Assert.Equal(RemoteConfigParser.MaxIssuesPerKey, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.UnknownMode));
            var summary = Single(r, RemoteConfigIssueKind.IssuesSuppressed, RemoteConfigIssueSeverity.Warning);
            Assert.Contains((100000 - RemoteConfigParser.MaxIssuesPerKey).ToString(System.Globalization.CultureInfo.InvariantCulture), summary.Message);
            Single(r, RemoteConfigIssueKind.NoValidEntries, RemoteConfigIssueSeverity.Error);
            Assert.Equal(RemoteConfigParser.MaxIssuesPerKey + 2, r.Issues.Count);
            Assert.Empty(r.Values.ModesDisabled);
        }

        [Theory]
        [InlineData(RemoteConfigKeys.PlatformSafeZones)]
        [InlineData(RemoteConfigKeys.TextTemplates)]
        public void PerEntryErrorsAreCappedToo(string key)
        {
            // QA residual (config): one NoValidEntries Error per broken map entry used to grow without bound.
            var sb = new StringBuilder("{");
            for (int i = 0; i < 100000; i++) sb.Append(i == 0 ? "" : ",").Append("\"p").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("\":[1]");
            sb.Append('}');
            var r = Parse(key, sb.ToString());
            Assert.True(r.Issues.Count <= 2 * RemoteConfigParser.MaxIssuesPerKey + 1, "issues=" + r.Issues.Count);
            Assert.Equal(RemoteConfigParser.MaxIssuesPerKey, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.NoValidEntries));
            var summary = Single(r, RemoteConfigIssueKind.IssuesSuppressed, RemoteConfigIssueSeverity.Error);
            // 100000 warnings + 100000 errors, minus the listed ones.
            Assert.Contains((200000 - 2 * RemoteConfigParser.MaxIssuesPerKey).ToString(System.Globalization.CultureInfo.InvariantCulture), summary.Message);
            // The defaults are untouched: every broken entry kept its previous value (none existed for p*).
            Assert.Equal(RawDictionary(RemoteConfigValues.Defaults)[key], Raw(r.Values, key));
        }

        [Fact]
        public void UnknownKeysAreCapped()
        {
            var d = new Dictionary<string, string>();
            for (int i = 0; i < 1000; i++) d["unknown_" + i.ToString("0000", System.Globalization.CultureInfo.InvariantCulture)] = "1";
            var r = RemoteConfigParser.Parse(d);
            var unknown = r.Issues.Where(i => i.Kind == RemoteConfigIssueKind.UnknownKey).ToList();
            Assert.Equal(RemoteConfigParser.MaxIssuesPerKey, unknown.Count);
            Assert.Equal("unknown_0000", unknown[0].Key);
            Single(r, RemoteConfigIssueKind.IssuesSuppressed, RemoteConfigIssueSeverity.Info);
            Assert.Equal(RemoteConfigParser.MaxIssuesPerKey + 1, r.Issues.Count);
        }

        [Fact]
        public void FewIssuesAreNotSummarized()
        {
            var r = Parse(RemoteConfigKeys.ModesDisabled, "[\"zz\",\"M01\"]");
            Single(r, RemoteConfigIssueKind.UnknownMode, RemoteConfigIssueSeverity.Warning);
            Assert.DoesNotContain(r.Issues, i => i.Kind == RemoteConfigIssueKind.IssuesSuppressed);
        }

        // ---- scaling: see RemoteConfigScalingTests (serial collection, timing-based) ----

        // ---- presets ----

        [Fact]
        public void PresetWithNumberOutsideDoubleRangeIsDropped()
        {
            var r = Parse(RemoteConfigKeys.RemotePresets,
                "[{\"modeId\":\"orbit\",\"params\":{\"speed\":1e400}},{\"modeId\":\"orbit\",\"params\":{\"speed\":-1e999}},{\"modeId\":\"orbit\",\"seed\":1.5}]");
            Assert.Equal(new[] { "{\"modeId\":\"orbit\",\"seed\":1.5}" }, r.Values.RemotePresets);
            Assert.Equal(2, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.InvalidEntry && i.Severity == RemoteConfigIssueSeverity.Warning));
            Assert.DoesNotContain(r.Values.RemotePresets, p => p.Contains("Infinity"));
        }

        [Fact]
        public void BuilderRejectsPresetWithNonFiniteNumber()
        {
            var b = new RemoteConfigBuilder { RemotePresets = new List<string> { "{\"speed\":1e400}" } };
            Assert.Throws<ArgumentException>(() => b.Build());
        }

        [Fact]
        public void PresetFractionsKeepDoublePrecision()
        {
            // QA residual: numbers are read as doubles, exactly as Params reads presets (the only consumer). Every value
            // survives at double precision (the old decimal pass turned 1e-30 into 0 and fell back to double for all presets
            // when one number exceeded the decimal range).
            const string raw = "[{\"seed\":12345678901234567.5,\"k\":0.1234567890123456789,\"e\":1.50}]";
            var r = Parse(RemoteConfigKeys.RemotePresets, raw);
            AssertNoIssues(r);
            Assert.Equal("{\"seed\":12345678901234568.0,\"k\":0.12345678901234568,\"e\":1.5}", r.Values.RemotePresets[0]);
            // Writer → parser → writer is stable, and the builder keeps the same text.
            var again = RemoteConfigParser.Parse(RawDictionary(r.Values));
            Assert.Equal(r.Values.RemotePresets, again.Values.RemotePresets);
            Assert.Equal(r.Values.RemotePresets, new RemoteConfigBuilder(r.Values).Build().RemotePresets);
        }

        [Fact]
        public void PresetTinyNumbersSurviveAndPresetsAreIndependent()
        {
            // QA residual probes B1/B2: 1e-30 must not become 0, and a 1e30 in one preset must not change another preset's text.
            var tiny = Parse(RemoteConfigKeys.RemotePresets, "[{\"eps\":1e-30,\"b\":1.5e-29}]");
            AssertNoIssues(tiny);
            Assert.Equal("{\"eps\":1E-30,\"b\":1.5E-29}", tiny.Values.RemotePresets[0]);

            var alone = Parse(RemoteConfigKeys.RemotePresets, "[{\"k\":0.1234567890123456789}]").Values.RemotePresets[0];
            var mixed = Parse(RemoteConfigKeys.RemotePresets, "[{\"k\":0.1234567890123456789},{\"big\":1e30},{\"x\":1e400}]");
            Assert.Equal(alone, mixed.Values.RemotePresets[0]);
            Assert.Equal("{\"big\":1E+30}", mixed.Values.RemotePresets[1]);
            Assert.Equal(2, mixed.Values.RemotePresets.Count);
            Single(mixed, RemoteConfigIssueKind.InvalidEntry, RemoteConfigIssueSeverity.Warning);
        }

        [Fact]
        public void PresetBigIntegersStayVerbatim()
        {
            var r = Parse(RemoteConfigKeys.RemotePresets, "[{\"seed\":123456789012345678901234567890}]");
            AssertNoIssues(r);
            Assert.Equal("{\"seed\":123456789012345678901234567890}", r.Values.RemotePresets[0]);
        }

        // ---- immutability ----

        [Fact]
        public void SnapshotHasNoSettersAndOnlyReadOnlyFields()
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var p in typeof(RemoteConfigValues).GetProperties(all))
            {
                Assert.True(p.SetMethod == null, p.Name + " has a setter");
            }
            foreach (var f in typeof(RemoteConfigValues).GetFields(all))
            {
                Assert.True(f.IsInitOnly, f.Name + " is not readonly");
            }
            Assert.DoesNotContain(typeof(RemoteConfigValues).GetMethods(all), m => m.Name == "Freeze");
        }

        [Fact]
        public void LookupCachesAgreeWithLists()
        {
            var r = Parse(
                (RemoteConfigKeys.FreeModes, "[\"M09\",\"M01\"]"),
                (RemoteConfigKeys.ModesDisabled, "[\"M02\",\"M09\"]"));
            foreach (var v in new[] { D, r.Values })
            {
                foreach (var m in ModeCatalog.All)
                {
                    Assert.Equal(v.FreeModes.Contains(m.Code), v.IsListedFree(m.Code));
                    Assert.Equal(v.ModesDisabled.Contains(m.Code), v.IsModeDisabled(m.Code));
                    Assert.Equal(v.EffectiveFreeModes.Contains(m.Code), v.IsEffectivelyFree(m.Code));
                }
            }
        }

        // ---- Build-failure fallback ----

        [Fact]
        public void BuildFailureReturnsDefaultsWithInternalError()
        {
            var raw = new Dictionary<string, string> { { RemoteConfigKeys.InterstitialEveryNGames, "7" } };
            var r = RemoteConfigParser.Parse(raw, _ => throw new InvalidOperationException("boom"));
            Assert.Same(RemoteConfigValues.Defaults, r.Values);
            var issue = Single(r, RemoteConfigIssueKind.InternalError, RemoteConfigIssueSeverity.Error);
            Assert.Equal(string.Empty, issue.Key);
            Assert.Contains("snapshot build failed", issue.Message);
            Assert.Contains("boom", issue.Message);

            var nullBuild = RemoteConfigParser.Parse(raw, _ => null);
            Assert.Same(RemoteConfigValues.Defaults, nullBuild.Values);
            Single(nullBuild, RemoteConfigIssueKind.InternalError, RemoteConfigIssueSeverity.Error);
        }

        // ---- writer order ----

        [Fact]
        public void WriterFollowsKeyTableOrder()
        {
            Assert.Equal(RemoteConfigKeys.All, RemoteConfigWriter.ToRaw(D).Select(kv => kv.Key));
            var parsed = Parse(RemoteConfigKeys.WatermarkEnabled, "false").Values;
            Assert.Equal(RemoteConfigKeys.All, RemoteConfigWriter.ToRaw(parsed).Select(kv => kv.Key));
        }

        // ---- lookups ----

        [Fact]
        public void LookupsTrimAndIgnoreCaseOfQuery()
        {
            var v = Parse(RemoteConfigKeys.TierOverrides, "{\"SM-A155F\":\"C\"}").Values;
            Assert.True(v.TryGetTierOverride("  sm-a155f \t", out var tier));
            Assert.Equal(DeviceTier.C, tier);
            Assert.False(v.TryGetTierOverride("SM-A155", out _));
            Assert.False(v.TryGetTierOverride(null, out _));

            Assert.True(D.TryGetSafeZones(" TikTok ", out var rects));
            Assert.NotEmpty(rects);
            Assert.False(D.TryGetSafeZones(null, out _));

            Assert.True(D.TryGetTextTemplates("\tRU ", out var templates));
            Assert.Equal("who_wins", templates[0].Id);
            Assert.False(D.TryGetTextTemplates("de", out _));
            Assert.False(D.TryGetTextTemplates(null, out _));
        }
    }
}
