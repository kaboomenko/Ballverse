using System.Linq;
using Ballverse.Core.Config;
using Newtonsoft.Json.Linq;
using Xunit;
using static Ballverse.Core.Tests.Config.ConfigTestUtil;

namespace Ballverse.Core.Tests.Config
{
    /// <summary>Tier overrides (spec §8.1), safe zones (§3.3), presets (§2.3), melodies (§2.7), text templates (§3.5).</summary>
    public class RemoteConfigMapTests
    {
        // ---- tier_overrides ----

        [Fact]
        public void TierOverridesHappyPathSortedAndCaseInsensitive()
        {
            var r = Parse(RemoteConfigKeys.TierOverrides, "{\"SM-A155F\":\"C\",\"iPhone11,8\":\"b\",\" Pixel 6a \":\"B\"}");
            AssertNoIssues(r);
            var v = r.Values;
            Assert.Equal(new[] { "iPhone11,8", "Pixel 6a", "SM-A155F" }, v.TierOverrides.Select(o => o.DeviceModel));
            Assert.True(v.TryGetTierOverride("sm-a155f", out var t));
            Assert.Equal(DeviceTier.C, t);
            Assert.True(v.TryGetTierOverride("Pixel 6a", out t));
            Assert.Equal(DeviceTier.B, t);
            Assert.False(v.TryGetTierOverride("Pixel 7", out _));
            Assert.False(v.TryGetTierOverride(null, out _));
        }

        [Fact]
        public void TierOverridesInvalidEntriesDropped()
        {
            var r = Parse(RemoteConfigKeys.TierOverrides, "{\"a\":\"D\",\"b\":1,\"\":\"A\",\"c\":null,\"ok\":\"A\",\"OK\":\"C\",\"d\":[\"A\"]}");
            Assert.Single(r.Values.TierOverrides);
            Assert.True(r.Values.TryGetTierOverride("ok", out var t));
            Assert.Equal(DeviceTier.A, t);
            Assert.Equal(5, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.InvalidEntry));
            Single(r, RemoteConfigIssueKind.DuplicateEntry, RemoteConfigIssueSeverity.Warning);

            var arr = Parse(RemoteConfigKeys.TierOverrides, "[\"A\"]");
            Single(arr, RemoteConfigIssueKind.InvalidType, RemoteConfigIssueSeverity.Error);
            Assert.Empty(arr.Values.TierOverrides);
        }

        // ---- platform_safe_zones ----

        [Fact]
        public void SafeZonesReplacePerPlatformAndKeepOthers()
        {
            var r = Parse(RemoteConfigKeys.PlatformSafeZones,
                "{\"TikTok\":[{\"x\":0.8,\"y\":0.3,\"w\":0.2,\"h\":0.5}],\"snapchat\":[{\"x\":0,\"y\":0.9,\"w\":1,\"h\":0.1}],\"shorts\":[]}");
            AssertNoIssues(r);
            var v = r.Values;
            Assert.Equal(new[] { "reels", "shorts", "snapchat", "TikTok" }, v.PlatformSafeZones.Select(p => p.Platform));
            Assert.True(v.TryGetSafeZones("tiktok", out var tiktok));
            var rect = Assert.Single(tiktok);
            Assert.Equal(0.8f, rect.X);
            Assert.Equal(0.3f, rect.Y);
            Assert.Equal(0.2f, rect.W);
            Assert.Equal(0.5f, rect.H);
            Assert.True(v.TryGetSafeZones("shorts", out var shorts));
            Assert.Empty(shorts);
            Assert.True(v.TryGetSafeZones("reels", out var reels));
            Assert.Equal(3, reels.Count);
        }

        [Fact]
        public void SafeZoneRectsClampedToFrame()
        {
            var r = Parse(RemoteConfigKeys.PlatformSafeZones,
                "{\"p\":[{\"x\":-0.1,\"y\":0.9,\"w\":0.5,\"h\":0.3},{\"x\":0.7,\"y\":0.7,\"w\":0.3,\"h\":0.3}]}");
            Assert.True(r.Values.TryGetSafeZones("p", out var rects));
            Assert.Equal(2, rects.Count);
            Assert.Equal(0f, rects[0].X);
            Assert.Equal(0.4f, rects[0].W, 6);
            Assert.Equal(0.9f, rects[0].Y);
            Assert.Equal(0.1f, rects[0].H, 6);
            Assert.True(rects[1].X + rects[1].W <= 1f);
            Assert.True(rects[1].Y + rects[1].H <= 1f);
            Single(r, RemoteConfigIssueKind.OutOfRange, RemoteConfigIssueSeverity.Warning);
        }

        [Fact]
        public void SafeZoneInvalidRectsDropped()
        {
            var r = Parse(RemoteConfigKeys.PlatformSafeZones,
                "{\"p\":[{\"x\":0,\"y\":0,\"w\":0,\"h\":0.1},{\"x\":0,\"y\":0,\"w\":-1,\"h\":0.1},{\"x\":1.5,\"y\":0,\"w\":0.2,\"h\":0.1},"
                + "{\"x\":\"0\",\"y\":0,\"w\":0.2,\"h\":0.1},{\"y\":0,\"w\":0.2,\"h\":0.1},[0,0,1,1],{\"x\":0,\"y\":0,\"w\":1e-50,\"h\":0.1},"
                + "{\"x\":0.1,\"y\":0.1,\"w\":0.1,\"h\":0.1}],"
                + "\"q\":{\"x\":0},\"\":[]}");
            Assert.True(r.Values.TryGetSafeZones("p", out var rects));
            Assert.Single(rects);
            Assert.False(r.Values.TryGetSafeZones("q", out _));
            Assert.Equal(9, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.InvalidEntry));
        }

        [Fact]
        public void SafeZonesFloatEdgeStaysInsideFrame()
        {
            var r = Parse(RemoteConfigKeys.PlatformSafeZones,
                "{\"p\":[{\"x\":0.3,\"y\":0.7,\"w\":0.7,\"h\":0.3},{\"x\":0.1,\"y\":0.2,\"w\":0.9,\"h\":0.8},{\"x\":0.6,\"y\":0.33,\"w\":0.4,\"h\":0.67}]}");
            AssertNoIssues(r);
            Assert.True(r.Values.TryGetSafeZones("p", out var rects));
            Assert.Equal(3, rects.Count);
            foreach (var rect in rects) Assert.True(NormalizedRect.IsValid(rect.X, rect.Y, rect.W, rect.H));
        }

        [Fact]
        public void SafeZonesDuplicatePlatformKeepsFirst()
        {
            var r = Parse(RemoteConfigKeys.PlatformSafeZones, "{\"reels\":[],\"REELS\":[{\"x\":0,\"y\":0,\"w\":1,\"h\":1}]}");
            Assert.True(r.Values.TryGetSafeZones("reels", out var rects));
            Assert.Empty(rects);
            Single(r, RemoteConfigIssueKind.DuplicateEntry, RemoteConfigIssueSeverity.Warning);
        }

        // ---- remote_presets ----

        [Fact]
        public void PresetsKeptAsCompactJsonObjects()
        {
            var r = Parse(RemoteConfigKeys.RemotePresets,
                "[ {\"modeId\":\"ring_escape\", \"name\":\"Neon\", \"created\":\"2026-01-02T03:04:05Z\", \"params\":{\"rings\":100, \"speed\":1.5}}, {\"modeId\":\"orbit\"} ]");
            AssertNoIssues(r);
            Assert.Equal(2, r.Values.RemotePresets.Count);
            Assert.Equal("{\"modeId\":\"ring_escape\",\"name\":\"Neon\",\"created\":\"2026-01-02T03:04:05Z\",\"params\":{\"rings\":100,\"speed\":1.5}}",
                r.Values.RemotePresets[0]);
            Assert.Equal("orbit", (string)JObject.Parse(r.Values.RemotePresets[1])["modeId"]);
        }

        [Fact]
        public void PresetsNonObjectsDropped()
        {
            var r = Parse(RemoteConfigKeys.RemotePresets, "[{\"a\":1},1,\"x\",null,[],{}]");
            Assert.Equal(new[] { "{\"a\":1}", "{}" }, r.Values.RemotePresets);
            Assert.Equal(4, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.InvalidEntry));

            var obj = Parse(RemoteConfigKeys.RemotePresets, "{\"a\":1}");
            Single(obj, RemoteConfigIssueKind.InvalidType, RemoteConfigIssueSeverity.Error);
            Assert.Empty(obj.Values.RemotePresets);
        }

        // ---- remote_melodies ----

        const string Ode = "{\"id\":\"ode\",\"name\":\"Ode to Joy\",\"notes\":[{\"pitch\":64,\"durWeight\":1,\"velocity\":0.8},{\"pitch\":64,\"durWeight\":1,\"velocity\":0.8},{\"pitch\":65,\"durWeight\":2,\"velocity\":1}]}";

        [Fact]
        public void MelodiesHappyPath()
        {
            var r = Parse(RemoteConfigKeys.RemoteMelodies, "[" + Ode + ",{\"id\":\"edge\",\"name\":\"Edge\",\"notes\":[{\"pitch\":0,\"durWeight\":0.001,\"velocity\":0},{\"pitch\":127,\"durWeight\":100,\"velocity\":1.0}]}]");
            AssertNoIssues(r);
            var m = r.Values.RemoteMelodies;
            Assert.Equal(new[] { "ode", "edge" }, m.Select(x => x.Id));
            Assert.Equal("Ode to Joy", m[0].Name);
            Assert.Equal(3, m[0].Notes.Count);
            Assert.Equal(65, m[0].Notes[2].Pitch);
            Assert.Equal(2f, m[0].Notes[2].DurWeight);
            Assert.Equal(0.8f, m[0].Notes[0].Velocity);
            Assert.Equal(127, m[1].Notes[1].Pitch);
        }

        [Theory]
        [InlineData("{\"id\":\"x\",\"name\":\"X\",\"notes\":[{\"pitch\":128,\"durWeight\":1,\"velocity\":0.5}]}")]
        [InlineData("{\"id\":\"x\",\"name\":\"X\",\"notes\":[{\"pitch\":-1,\"durWeight\":1,\"velocity\":0.5}]}")]
        [InlineData("{\"id\":\"x\",\"name\":\"X\",\"notes\":[{\"pitch\":60.5,\"durWeight\":1,\"velocity\":0.5}]}")]
        [InlineData("{\"id\":\"x\",\"name\":\"X\",\"notes\":[{\"pitch\":60,\"durWeight\":0,\"velocity\":0.5}]}")]
        [InlineData("{\"id\":\"x\",\"name\":\"X\",\"notes\":[{\"pitch\":60,\"durWeight\":-1,\"velocity\":0.5}]}")]
        [InlineData("{\"id\":\"x\",\"name\":\"X\",\"notes\":[{\"pitch\":60,\"durWeight\":1e-60,\"velocity\":0.5}]}")]
        [InlineData("{\"id\":\"x\",\"name\":\"X\",\"notes\":[{\"pitch\":60,\"durWeight\":1,\"velocity\":1.01}]}")]
        [InlineData("{\"id\":\"x\",\"name\":\"X\",\"notes\":[{\"pitch\":60,\"durWeight\":1,\"velocity\":-0.1}]}")]
        [InlineData("{\"id\":\"x\",\"name\":\"X\",\"notes\":[{\"pitch\":60,\"durWeight\":1}]}")]
        [InlineData("{\"id\":\"x\",\"name\":\"X\",\"notes\":[{\"pitch\":\"C4\",\"durWeight\":1,\"velocity\":0.5}]}")]
        [InlineData("{\"id\":\"x\",\"name\":\"X\",\"notes\":[[60,1,0.5]]}")]
        [InlineData("{\"id\":\"x\",\"name\":\"X\",\"notes\":[]}")]
        [InlineData("{\"id\":\"x\",\"name\":\"X\"}")]
        [InlineData("{\"id\":\"x\",\"name\":\"X\",\"notes\":{}}")]
        [InlineData("{\"id\":\" \",\"name\":\"X\",\"notes\":[{\"pitch\":60,\"durWeight\":1,\"velocity\":0.5}]}")]
        [InlineData("{\"id\":1,\"name\":\"X\",\"notes\":[{\"pitch\":60,\"durWeight\":1,\"velocity\":0.5}]}")]
        [InlineData("{\"id\":\"x\",\"notes\":[{\"pitch\":60,\"durWeight\":1,\"velocity\":0.5}]}")]
        [InlineData("\"melody\"")]
        public void MelodyWithAnyInvalidPartIsDropped(string bad)
        {
            var r = Parse(RemoteConfigKeys.RemoteMelodies, "[" + Ode + "," + bad + "]");
            Assert.Equal(new[] { "ode" }, r.Values.RemoteMelodies.Select(m => m.Id));
            Single(r, RemoteConfigIssueKind.InvalidEntry, RemoteConfigIssueSeverity.Warning);
        }

        [Fact]
        public void MelodiesDuplicateIdKeepsFirstAndInvalidJsonKeepsDefault()
        {
            var r = Parse(RemoteConfigKeys.RemoteMelodies, "[" + Ode + "," + Ode.Replace("Ode to Joy", "Other") + "]");
            Assert.Equal("Ode to Joy", Assert.Single(r.Values.RemoteMelodies).Name);
            Single(r, RemoteConfigIssueKind.DuplicateEntry, RemoteConfigIssueSeverity.Warning);

            var bad = Parse(RemoteConfigKeys.RemoteMelodies, "[" + Ode);
            Single(bad, RemoteConfigIssueKind.InvalidJson, RemoteConfigIssueSeverity.Error);
            Assert.Empty(bad.Values.RemoteMelodies);
        }

        // ---- text_templates ----

        [Fact]
        public void TextTemplatesMergePerLanguage()
        {
            var r = Parse(RemoteConfigKeys.TextTemplates,
                "{\"de\":[{\"id\":\"who_wins\",\"text\":\"Welche Kugel gewinnt?\"}],\"EN\":[{\"id\":\"part_3\",\"text\":\"Part 3\"}]}");
            AssertNoIssues(r);
            var v = r.Values;
            Assert.Equal(new[] { "de", "EN", "ru" }, v.TextTemplates.Select(s => s.Language));
            Assert.True(v.TryGetTextTemplates("en", out var en));
            Assert.Equal("Part 3", Assert.Single(en).Text);
            Assert.True(v.TryGetTextTemplates("ru", out var ru));
            Assert.Equal(6, ru.Count);
            Assert.True(v.TryGetTextTemplates("de", out var de));
            Assert.Equal("who_wins", de[0].Id);
        }

        [Fact]
        public void TextTemplatesInvalidEntriesDropped()
        {
            var r = Parse(RemoteConfigKeys.TextTemplates,
                "{\"fr\":[{\"id\":\"a\",\"text\":\"A\"},{\"id\":\"a\",\"text\":\"B\"},{\"id\":\"\",\"text\":\"C\"},{\"id\":\"d\"},\"e\",{\"id\":\"f\",\"text\":\"  \"}],"
                + "\"es\":\"hola\",\"\":[],\"FR\":[]}");
            var v = r.Values;
            Assert.True(v.TryGetTextTemplates("fr", out var fr));
            Assert.Equal("A", Assert.Single(fr).Text);
            Assert.False(v.TryGetTextTemplates("es", out _));
            Assert.Equal(6, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.InvalidEntry));
            Assert.Equal(2, r.Issues.Count(i => i.Kind == RemoteConfigIssueKind.DuplicateEntry));

            var arr = Parse(RemoteConfigKeys.TextTemplates, "[]");
            Single(arr, RemoteConfigIssueKind.InvalidType, RemoteConfigIssueSeverity.Error);
            Assert.Equal(2, arr.Values.TextTemplates.Count);
        }

        [Fact]
        public void TextTemplateTextKeptVerbatimIncludingEmoji()
        {
            var r = Parse(RemoteConfigKeys.TextTemplates, "{\"ru\":[{\"id\":\"c\",\"text\":\" Пиши свой цвет \\ud83d\\udc47 \"}]}");
            AssertNoIssues(r);
            Assert.True(r.Values.TryGetTextTemplates("ru", out var ru));
            Assert.Equal(" Пиши свой цвет \U0001F447 ", ru[0].Text);
        }
    }
}
