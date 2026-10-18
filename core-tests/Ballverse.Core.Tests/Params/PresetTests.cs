using System;
using System.Linq;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Xunit;
using static Ballverse.Core.Tests.Params.TestSchemas;

namespace Ballverse.Core.Tests.Params
{
    public class PresetTests
    {
        // ---------- model ----------

        [Fact]
        public void CreateValidatesIdModeAndName()
        {
            var p = Preset.Create("my-preset_1.v2", "ring_escape", null, PresetSource.User, name: "  Chaos  ");
            Assert.Equal("Chaos", p.Name);
            Assert.Same(ParamSet.Empty, p.Values);
            Assert.False(p.IsNew);

            Assert.Throws<PresetException>(() => Preset.Create("", "ring_escape", null, PresetSource.User, name: "x"));
            Assert.Throws<PresetException>(() => Preset.Create("a b", "ring_escape", null, PresetSource.User, name: "x"));
            Assert.Throws<PresetException>(() => Preset.Create(new string('a', 65), "ring_escape", null, PresetSource.User, name: "x"));
            Assert.Throws<PresetException>(() => Preset.Create("id", "Ring-Escape", null, PresetSource.User, name: "x"));
            Assert.Throws<PresetException>(() => Preset.Create("id", null, null, PresetSource.User, name: "x"));
            Assert.Throws<PresetException>(() => Preset.Create("id", "ring_escape", null, PresetSource.User, nameKey: "presets.x"));
            Assert.Throws<PresetException>(() => Preset.Create("id", "ring_escape", null, PresetSource.BuiltIn));
            Assert.Throws<PresetException>(() => Preset.Create("id", "ring_escape", null, PresetSource.BuiltIn, nameKey: ""));
            Assert.Equal("presets.x", Preset.Create("id", "ring_escape", null, PresetSource.BuiltIn, nameKey: "presets.x").NameKey);
        }

        [Theory]
        [InlineData("a", "a")]
        [InlineData("  padded name \t", "padded name")]
        [InlineData("12345678901234567890123456789012", "12345678901234567890123456789012")]
        [InlineData("Ёлка 🎄 и шары", "Ёлка 🎄 и шары")]
        public void NameIsTrimmedAndUpTo32Chars(string input, string expected)
        {
            Assert.Equal(expected, Preset.NormalizeName(input));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("123456789012345678901234567890123")]
        public void InvalidNames(string input)
        {
            Assert.Null(Preset.NormalizeName(input));
        }

        [Fact]
        public void NameLengthCountsCodePoints()
        {
            string emoji32 = string.Concat(Enumerable.Repeat("\U0001F600", 32));
            Assert.Equal(emoji32, Preset.NormalizeName(emoji32));
            Assert.Null(Preset.NormalizeName(emoji32 + "x"));
        }

        [Fact]
        public void WithNameValidates()
        {
            var p = Preset.Create("id", "ring_escape", null, PresetSource.User, name: "A");
            Assert.Equal("B", p.WithName(" B ").Name);
            Assert.Throws<PresetException>(() => p.WithName(" "));
            Assert.Equal("A", p.Name);
        }

        // ---------- apply / reset ----------

        [Fact]
        public void ApplyIsDefaultsPlusPresetValuesSanitized()
        {
            var schema = TestSchemas.RingEscape();
            var preset = Preset.Create("p", "ring_escape", Set(
                ("ringCount", ParamValue.Int(30)),
                ("timeScale", ParamValue.Float(9)),
                ("ghost", ParamValue.Bool(true))), PresetSource.BuiltIn, nameKey: "k");
            var r = preset.Apply(schema, DeviceTier.A);
            var expected = schema.CreateDefaults().With("ringCount", ParamValue.Int(30)).With("timeScale", ParamValue.Float(3));
            Assert.Equal(expected, r.Values);
            Assert.Equal(new[] { ParamIssueCode.OutOfRange, ParamIssueCode.Unknown }, r.Issues.Select(i => i.Code));
            Assert.DoesNotContain(r.Issues, i => i.Code == ParamIssueCode.Missing);
        }

        [Fact]
        public void ApplyRespectsTierCaps()
        {
            var schema = TestSchemas.PegDrop();
            var preset = Preset.Create("rain", "peg_drop", Set(("ballCount", ParamValue.Int(2000))), PresetSource.BuiltIn, nameKey: "k");
            Assert.Equal(2000, preset.Apply(schema, DeviceTier.A).Values.GetInt("ballCount"));
            Assert.Equal(400, preset.Apply(schema, DeviceTier.C).Values.GetInt("ballCount"));
        }

        [Fact]
        public void ResetToPresetRestoresPresetValuesButKeepsSeed()
        {
            var schema = TestSchemas.RingEscape();
            var preset = Preset.Create("p", "ring_escape", Set(("ringCount", ParamValue.Int(30))), PresetSource.BuiltIn, nameKey: "k");
            var current = preset.Apply(schema, DeviceTier.B).Values
                .With("ringCount", ParamValue.Int(77))
                .With("theme", ParamValue.String("Mono"))
                .With("seed", ParamValue.Seed(987654321));
            var reset = preset.ResetFrom(current, schema, DeviceTier.B);
            Assert.False(reset.HasIssues);
            Assert.Equal(30, reset.Values.GetInt("ringCount"));
            Assert.Equal("Neon", reset.Values.GetString("theme"));
            Assert.Equal(987654321UL, reset.Values.GetSeed("seed"));

            var seeded = preset.WithValues(preset.Values.With("seed", ParamValue.Seed(5)));
            Assert.Equal(5UL, seeded.ResetFrom(current, schema, DeviceTier.B).Values.GetSeed("seed"));
        }

        // ---------- JSON ----------

        [Fact]
        public void BuiltInListLoadsAndAppliesCleanly()
        {
            var schema = TestSchemas.RingEscape();
            var presets = PresetJson.ParseList(ReadTestData("ring_escape_builtin.json"), PresetSource.BuiltIn);
            Assert.Equal(new[] { "ring_escape.classic", "ring_escape.neon_chaos", "ring_escape.meditation", "ring_escape.hundred_rings" },
                presets.Select(p => p.Id));
            Assert.All(presets, p =>
            {
                Assert.Equal(PresetSource.BuiltIn, p.Source);
                Assert.False(p.IsNew);
                Assert.Null(p.Name);
                var r = p.Apply(schema, DeviceTier.A);
                Assert.False(r.HasIssues, p.Id + ": " + string.Join("; ", r.Issues));
            });
            var chaos = presets[1].Apply(schema, DeviceTier.A).Values;
            Assert.Equal(1.5f, chaos.GetFloat("timeScale"));
            Assert.Equal(4, chaos.GetColors("ballColors").Count);
            Assert.Equal("rainbow", chaos.GetString("trail"));
            Assert.Equal(60, presets[3].Apply(schema, DeviceTier.A).Values.GetInt("targetDuration"));
        }

        [Fact]
        public void SerializeMatchesGoldenAndRoundTrips()
        {
            var schema = TestSchemas.RingEscape();
            var values = Set(
                ("seed", ParamValue.Seed(ulong.MaxValue)),
                ("ballColors", ParamValue.Colors(new[] { new ColorRgba(255, 0, 170), new ColorRgba(0, 255, 238, 128) })),
                ("targetDuration", ParamValue.Unlimited),
                ("timeScale", ParamValue.Float((float)1.35)),
                ("labelNames", ParamValue.Strings(new[] { "Аня", "Bob" })),
                ("ringCount", ParamValue.Int(30)));
            var p = Preset.Create("user.1", "ring_escape", values, PresetSource.User, name: "Мой хаос");
            string json = PresetJson.Serialize(p, schema, indented: false);
            Assert.Equal("{\"id\":\"user.1\",\"name\":\"Мой хаос\",\"modeId\":\"ring_escape\",\"values\":{\"timeScale\":1.35," +
                "\"ballColors\":[\"#FF00AA\",\"#00FFEE80\"],\"labelNames\":[\"Аня\",\"Bob\"],\"targetDuration\":\"unlimited\"," +
                "\"seed\":\"18446744073709551615\",\"ringCount\":30}}", json);

            var back = PresetJson.Parse(json, PresetSource.User);
            Assert.Equal(p.Id, back.Id);
            Assert.Equal(p.Name, back.Name);
            Assert.Equal(p.ModeId, back.ModeId);
            Assert.Equal(p.Apply(schema, DeviceTier.A).Values, back.Apply(schema, DeviceTier.A).Values);
            Assert.False(back.Apply(schema, DeviceTier.A).HasIssues);
        }

        [Fact]
        public void SerializeWithoutSchemaUsesOrdinalOrderAndWritesIsNew()
        {
            var p = Preset.Create("r", "orbit", Set(("b", ParamValue.Int(1)), ("a", ParamValue.Bool(true))), PresetSource.Remote, nameKey: "k", isNew: true);
            Assert.Equal("{\"id\":\"r\",\"nameKey\":\"k\",\"modeId\":\"orbit\",\"isNew\":true,\"values\":{\"a\":true,\"b\":1}}",
                PresetJson.Serialize(p, null, false));
            string indented = PresetJson.Serialize(p);
            Assert.Contains("\"modeId\": \"orbit\"", indented);
            Assert.True(PresetJson.Parse(indented, PresetSource.Remote).IsNew);
            Assert.False(PresetJson.Parse(PresetJson.Serialize(p.WithIsNew(false)), PresetSource.Remote).IsNew);
        }

        [Fact]
        public void SerializeListRoundTrips()
        {
            var presets = PresetJson.ParseList(ReadTestData("ring_escape_builtin.json"), PresetSource.BuiltIn);
            var again = PresetJson.ParseList(PresetJson.SerializeList(presets, TestSchemas.RingEscape()), PresetSource.BuiltIn);
            Assert.Equal(presets.Select(p => p.Id), again.Select(p => p.Id));
            for (int i = 0; i < presets.Count; i++) Assert.Equal(presets[i].Values, again[i].Values);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("[]")]
        [InlineData("{\"id\":\"a\",\"modeId\":\"ring_escape\"}")]
        [InlineData("{\"id\":\"a\",\"name\":\"n\",\"modeId\":\"ring_escape\",\"values\":5}")]
        [InlineData("{\"id\":{\"x\":1},\"name\":\"n\",\"modeId\":\"ring_escape\"}")]
        [InlineData("{\"id\":\"a\",\"name\":\"n\",\"modeId\":\"ring_escape\",\"isNew\":\"maybe\"}")]
        public void ParseRejectsInvalid(string json)
        {
            Assert.False(PresetJson.TryParse(json, PresetSource.BuiltIn, out var p, out var error));
            Assert.Null(p);
            Assert.False(string.IsNullOrEmpty(error));
            Assert.Throws<PresetException>(() => PresetJson.Parse(json, PresetSource.BuiltIn));
        }

        [Fact]
        public void ParseListRejectsDuplicatesAndNonArrays()
        {
            Assert.Throws<PresetException>(() => PresetJson.ParseList("{}", PresetSource.BuiltIn));
            Assert.Throws<PresetException>(() => PresetJson.ParseList(
                "[{\"id\":\"a\",\"nameKey\":\"k\",\"modeId\":\"m\"},{\"id\":\"a\",\"nameKey\":\"k\",\"modeId\":\"m\"}]", PresetSource.BuiltIn));
        }

        [Fact]
        public void MissingOrNullValuesMeanEmpty()
        {
            Assert.Equal(0, PresetJson.Parse("{\"id\":\"a\",\"nameKey\":\"k\",\"modeId\":\"m\"}", PresetSource.BuiltIn).Values.Count);
            Assert.Equal(0, PresetJson.Parse("{\"id\":\"a\",\"nameKey\":\"k\",\"modeId\":\"m\",\"values\":null}", PresetSource.BuiltIn).Values.Count);
        }
    }

    public class RemotePresetsTests
    {
        static ParamSchema SchemaFor(string modeId)
        {
            switch (modeId)
            {
                case "ring_escape": return TestSchemas.RingEscape();
                case "peg_drop": return TestSchemas.PegDrop();
                default: return null;
            }
        }

        [Fact]
        public void ValidatesPayload()
        {
            var r = RemotePresets.Load(ReadTestData("remote_presets.json"), SchemaFor);

            Assert.Equal(new[] { "remote.rainbow_rush", "remote.peg_rain", "remote.future_shape" }, r.Accepted.Select(p => p.Id));
            var rush = r.Accepted[0];
            Assert.Equal(PresetSource.Remote, rush.Source);
            Assert.True(rush.IsNew);
            Assert.Equal(new[] { "ringCount", "seed", "timeScale", "trail" }, rush.Values.Ids);
            Assert.Equal(100, rush.Values.GetInt("ringCount"));
            Assert.Equal(123456789012345678UL, rush.Values.GetSeed("seed"));
            Assert.Equal((float)(0.25 + 22 * 0.05), rush.Values.GetFloat("timeScale"));
            var rain = r.Accepted[1];
            Assert.False(rain.IsNew);
            Assert.Equal(2000, rain.Values.GetInt("ballCount")); // tier caps applied later, by Apply
            Assert.Equal(400, rain.Apply(TestSchemas.PegDrop(), DeviceTier.C).Values.GetInt("ballCount"));

            Assert.Equal(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 10, 11, 12, 13, 14 }, r.Rejected.Select(x => x.Index));
            string Reason(int index) => r.Rejected.Single(x => x.Index == index).Reason;
            Assert.Contains("TypeMismatch", Reason(1));
            Assert.Equal("remote.bad_type", r.Rejected[0].PresetId);
            Assert.Contains("InvalidOption", Reason(2));
            Assert.Contains("Unknown mode 'trampolines'", Reason(3));
            Assert.Contains("Invalid preset id", Reason(4));
            Assert.Null(r.Rejected.Single(x => x.Index == 4).PresetId);
            Assert.Contains("name or a name key", Reason(5));
            Assert.Contains("'values' must be an object", Reason(6));
            Assert.Contains("Expected a JSON object", Reason(7));
            Assert.Contains("Duplicate", Reason(8));
            Assert.Contains("InvalidValue", Reason(10));
            Assert.Contains("name must be", Reason(11));
            // null, nested object and mixed array for known params are type mismatches, never silently dropped
            Assert.Contains("ringCount: TypeMismatch", Reason(12));
            Assert.Contains("null", Reason(12));
            Assert.Contains("ringCount: TypeMismatch", Reason(13));
            Assert.Contains("object", Reason(13));
            Assert.Contains("ballColors: TypeMismatch", Reason(14));
            Assert.Equal(new[] { "trail" }, r.Accepted[2].Values.Ids);

            var warnings = r.Warnings.Select(w => w.PresetId + ":" + w.Issue.ParamId + ":" + w.Issue.Code).ToArray();
            Assert.Equal(new[]
            {
                "remote.rainbow_rush:timeScale:Snapped",
                "remote.rainbow_rush:ringCount:OutOfRange",
                "remote.rainbow_rush:futureParam:Unknown",
                "remote.future_shape:futureShape:Unknown",
            }, warnings);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void EmptyPayloadMeansNoPresets(string json)
        {
            var r = RemotePresets.Load(json, SchemaFor);
            Assert.Empty(r.Accepted);
            Assert.Empty(r.Rejected);
        }

        [Theory]
        [InlineData("{")]
        [InlineData("{\"presets\":[]}")]
        [InlineData("\"x\"")]
        public void MalformedPayloadIsRejectedWholeWithoutThrowing(string json)
        {
            var r = RemotePresets.Load(json, SchemaFor);
            Assert.Empty(r.Accepted);
            Assert.Equal(-1, Assert.Single(r.Rejected).Index);
        }

        [Fact]
        public void NeverThrowsEvenWhenSchemaProviderFails()
        {
            var r = RemotePresets.Load("[{\"id\":\"a\",\"name\":\"n\",\"modeId\":\"ring_escape\"}]", _ => throw new InvalidOperationException("boom"));
            Assert.Empty(r.Accepted);
            Assert.Contains("boom", Assert.Single(r.Rejected).Reason);
            Assert.Single(RemotePresets.Load("[]", null).Rejected);
            var layer = RemotePresets.Load("[{\"id\":\"a\",\"name\":\"n\",\"modeId\":\"ring_escape\"}]", _ => TestSchemas.RingEscapeLayer());
            Assert.Contains("Unknown mode", Assert.Single(layer.Rejected).Reason);
        }

        [Fact]
        public void FuzzedPayloadsNeverThrow()
        {
            string baseJson = ReadTestData("remote_presets.json");
            var rng = Rng.FromSeed(77, 3);
            for (int i = 0; i < 300; i++)
            {
                var chars = baseJson.ToCharArray();
                int edits = 1 + (int)rng.NextUInt(6);
                for (int e = 0; e < edits; e++)
                    chars[rng.NextUInt((uint)chars.Length)] = "{}[]\",:0a-#\\ "[(int)rng.NextUInt(13)];
                var r = RemotePresets.Load(new string(chars), SchemaFor);
                Assert.NotNull(r);
                Assert.All(r.Accepted, p => Assert.Equal(PresetSource.Remote, p.Source));
            }
        }
    }

    public class UserPresetLibraryTests
    {
        static ParamSet V(int rings)
        {
            return Set(("ringCount", ParamValue.Int(rings)));
        }

        [Fact]
        public void AddRenameDelete()
        {
            var lib = new UserPresetLibrary();
            Assert.Equal(UserPresetError.None, lib.TryAdd("u1", "ring_escape", "  First ", V(10), out var p1));
            Assert.Equal("First", p1.Name);
            Assert.Equal(PresetSource.User, p1.Source);
            Assert.Equal(UserPresetError.None, lib.TryAdd("u2", "peg_drop", "Second", null, out _));
            Assert.Equal(new[] { "u1", "u2" }, lib.All.Select(p => p.Id));
            Assert.Equal(new[] { "u1" }, lib.ForMode("ring_escape").Select(p => p.Id));

            Assert.Equal(UserPresetError.None, lib.TryRename("u1", "Renamed", out var renamed));
            Assert.Equal("Renamed", renamed.Name);
            Assert.Equal("Renamed", lib.Find("u1").Name);
            Assert.Equal(V(10), lib.Find("u1").Values);
            Assert.Equal(UserPresetError.InvalidName, lib.TryRename("u1", "   ", out _));
            Assert.Equal(UserPresetError.InvalidName, lib.TryRename("u1", new string('x', 33), out _));
            Assert.Equal(UserPresetError.NotFound, lib.TryRename("nope", "x", out _));

            Assert.Equal(UserPresetError.None, lib.TryUpdateValues("u1", V(50), out var updated));
            Assert.Equal(V(50), updated.Values);
            Assert.Equal(UserPresetError.NotFound, lib.TryUpdateValues("nope", V(1), out _));

            Assert.True(lib.Delete("u1"));
            Assert.False(lib.Delete("u1"));
            Assert.Null(lib.Find("u1"));
            Assert.Equal(new[] { "u2" }, lib.All.Select(p => p.Id));
        }

        [Fact]
        public void AddValidation()
        {
            var lib = new UserPresetLibrary();
            Assert.Equal(UserPresetError.InvalidId, lib.TryAdd("bad id", "ring_escape", "n", null, out _));
            Assert.Equal(UserPresetError.InvalidMode, lib.TryAdd("a", "", "n", null, out _));
            Assert.Equal(UserPresetError.InvalidName, lib.TryAdd("a", "ring_escape", "", null, out _));
            Assert.Equal(UserPresetError.InvalidName, lib.TryAdd("a", "ring_escape", null, null, out _));
            Assert.Equal(UserPresetError.None, lib.TryAdd("a", "ring_escape", "n", null, out _));
            Assert.Equal(UserPresetError.DuplicateId, lib.TryAdd("a", "peg_drop", "n", null, out var p));
            Assert.Null(p);
        }

        [Fact]
        public void LimitIsTwentyPerMode()
        {
            var lib = new UserPresetLibrary();
            for (int i = 0; i < UserPresetLibrary.MaxPerMode; i++)
                Assert.Equal(UserPresetError.None, lib.TryAdd("r" + i, "ring_escape", "Preset " + i, V(i + 3), out _));
            Assert.Equal(20, lib.CountForMode("ring_escape"));
            Assert.Equal(UserPresetError.LimitReached, lib.TryAdd("r20", "ring_escape", "One too many", null, out _));
            Assert.Equal(UserPresetError.None, lib.TryAdd("p0", "peg_drop", "Other mode", null, out _));
            Assert.True(lib.Delete("r5"));
            Assert.Equal(UserPresetError.None, lib.TryAdd("r20", "ring_escape", "Fits again", null, out _));
        }

        [Fact]
        public void JsonRoundTrip()
        {
            var lib = new UserPresetLibrary();
            lib.TryAdd("u1", "ring_escape", "Хаос", Set(("ringCount", ParamValue.Int(30)), ("timeScale", ParamValue.Float((float)0.35)),
                ("seed", ParamValue.Seed(ulong.MaxValue))), out _);
            lib.TryAdd("u2", "peg_drop", "Rain", null, out _);
            string json = lib.ToJson();
            Assert.Contains("\"schemaVersion\": 1", json);
            Assert.Contains("\"timeScale\": 0.35", json);
            var back = UserPresetLibrary.FromJson(json, out var problems);
            Assert.Empty(problems);
            Assert.Equal(new[] { "u1", "u2" }, back.All.Select(p => p.Id));
            var schema = TestSchemas.RingEscape();
            Assert.Equal(lib.Find("u1").Apply(schema, DeviceTier.A).Values, back.Find("u1").Apply(schema, DeviceTier.A).Values);
            Assert.Equal("Хаос", back.Find("u1").Name);
            Assert.All(back.All, p => Assert.Equal(PresetSource.User, p.Source));
            Assert.Equal(json, back.ToJson());
        }

        [Fact]
        public void FromJsonSkipsInvalidEntriesAndEnforcesLimit()
        {
            var entries = Enumerable.Range(0, 22).Select(i => "{\"id\":\"r" + i + "\",\"name\":\"P" + i + "\",\"modeId\":\"ring_escape\"}").ToList();
            entries.Add("{\"id\":\"r0\",\"name\":\"dup\",\"modeId\":\"peg_drop\"}");
            entries.Add("{\"id\":\"k\",\"nameKey\":\"only.key\",\"modeId\":\"peg_drop\"}");
            entries.Add("{\"id\":\"ok\",\"name\":\"Fine\",\"modeId\":\"peg_drop\"}");
            var lib = UserPresetLibrary.FromJson("{\"schemaVersion\":1,\"presets\":[" + string.Join(",", entries) + "]}", out var problems);
            Assert.Equal(20, lib.CountForMode("ring_escape"));
            Assert.Equal(new[] { "ok" }, lib.ForMode("peg_drop").Select(p => p.Id));
            Assert.Equal(4, problems.Count); // 2 over the limit, 1 duplicate id, 1 without a name
        }

        [Theory]
        [InlineData(null)]
        [InlineData("garbage")]
        [InlineData("[]")]
        [InlineData("{\"presets\":{}}")]
        public void FromJsonNeverThrows(string json)
        {
            var lib = UserPresetLibrary.FromJson(json, out var problems);
            Assert.Empty(lib.All);
            Assert.Single(problems);
        }
    }
}
