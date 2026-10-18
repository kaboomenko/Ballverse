using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Xunit;
using static Ballverse.Core.Tests.Params.TestSchemas;

namespace Ballverse.Core.Tests.Params
{
    /// <summary>Float grids that cross zero must hit exactly 0 (no 5.55e-17 from double cancellation).</summary>
    public class FloatZeroGridTests
    {
        static ParamSchema Symmetric()
        {
            return new ParamSchemaBuilder()
                .Add(new FloatParam("bias", -0.3, 0.3, 0.1, 0.0))
                .Add(new FloatParam("spin", -0.7, 0.7, 0.1, 0.0))
                .Add(new FloatParam("wind", -2.1, 2.1, 0.3, 0.0))
                .Build();
        }

        [Fact]
        public void DeclaredZeroDefaultStaysZero()
        {
            var d = Symmetric().CreateDefaults();
            Assert.Equal(0.0, d.Get("bias").AsFloat);
            Assert.Equal(0.0, d.Get("spin").AsFloat);
            Assert.Equal(0.0, d.Get("wind").AsFloat);
            Assert.Equal("{\"bias\":0.0,\"spin\":0.0,\"wind\":0.0}", ParamJson.ToCanonicalJson(d, Symmetric()));
        }

        [Fact]
        public void ExactZeroInputIsNotSnapped()
        {
            var s = Symmetric();
            var r = Set(("bias", ParamValue.Float(0.0)), ("spin", ParamValue.Int(0)), ("wind", ParamValue.Float(-0.0))).Sanitize(s, DeviceTier.A);
            Assert.False(r.HasIssues);
            foreach (var id in new[] { "bias", "spin", "wind" })
            {
                Assert.Equal(0L, BitConverter.DoubleToInt64Bits(r.Values.Get(id).AsFloat)); // +0, not -0 or 1e-17
            }
        }

        [Fact]
        public void NearZeroInputSnapsToExactZero()
        {
            var s = Symmetric();
            var r = Set(("bias", ParamValue.Float(0.04)), ("spin", ParamValue.Float(-0.049))).SanitizePartial(s);
            Assert.Equal(0.0, r.Values.Get("bias").AsFloat);
            Assert.Equal(0.0, r.Values.Get("spin").AsFloat);
            Assert.All(r.Issues, i => Assert.Equal(ParamIssueCode.Snapped, i.Code));
            Assert.Equal(0f, ((FloatParam)s.Get("bias")).Snap(1e-12, DeviceTier.A));
        }

        [Fact]
        public void OtherGridValuesAreUnchanged()
        {
            var f = (FloatParam)Symmetric().Get("spin");
            for (int k = 0; k <= 14; k++)
            {
                float expected = k == 7 ? 0f : (float)(-0.7 + k * 0.1);
                Assert.Equal(expected, f.Snap(-0.7 + k * 0.1, DeviceTier.A));
            }
        }
    }

    /// <summary>Raw JSON shapes no parameter can hold become Invalid values and are reported, never dropped silently.</summary>
    public class InvalidRawValueTests
    {
        [Theory]
        [InlineData("{\"ringCount\":null}", "ringCount")]
        [InlineData("{\"ringCount\":{\"v\":5}}", "ringCount")]
        [InlineData("{\"ballColors\":[\"#FF0000\",5]}", "ballColors")]
        [InlineData("{\"labelNames\":[[\"a\"]]}", "labelNames")]
        [InlineData("{\"seed\":null}", "seed")]
        [InlineData("{\"haptics\":{}}", "haptics")]
        public void SanitizeReportsTypeMismatchAndUsesDefault(string json, string id)
        {
            var s = RingEscape();
            var r = ParamJson.FromJson(json).Sanitize(s, DeviceTier.A);
            var issue = Assert.Single(r.Issues, i => i.ParamId == id);
            Assert.Equal(ParamIssueCode.TypeMismatch, issue.Code);
            Assert.Equal(s.Get(id).Default, r.Values.Get(id));
        }

        [Fact]
        public void UnknownIdWithInvalidShapeIsReportedUnknown()
        {
            var r = ParamJson.FromJson("{\"later\":null}").SanitizePartial(RingEscape());
            Assert.Equal(ParamIssueCode.Unknown, Assert.Single(r.Issues).Code);
        }

        [Fact]
        public void UserPresetWithInvalidShapeReportsOnApply()
        {
            var lib = UserPresetLibrary.FromJson(
                "{\"schemaVersion\":1,\"presets\":[{\"id\":\"u\",\"name\":\"n\",\"modeId\":\"ring_escape\",\"values\":{\"ringCount\":{\"x\":1}}}]}",
                out var problems);
            Assert.Empty(problems);
            var applied = lib.Find("u").Apply(RingEscape(), DeviceTier.A);
            Assert.Contains(applied.Issues, i => i.ParamId == "ringCount" && i.Code == ParamIssueCode.TypeMismatch);
            Assert.Equal(12, applied.Values.GetInt("ringCount"));
            // re-saving keeps the file valid JSON (the invalid value is written as null)
            var again = UserPresetLibrary.FromJson(lib.ToJson(), out problems);
            Assert.Empty(problems);
            Assert.Equal(ParamValue.Invalid("null"), again.Find("u").Values.Get("ringCount"));
        }

        [Fact]
        public void InvalidValuesHaveStructuralEquality()
        {
            Assert.Equal(ParamValue.Invalid("null"), ParamValue.Invalid("null"));
            Assert.NotEqual(ParamValue.Invalid("null"), ParamValue.Invalid("object"));
            Assert.NotEqual(ParamValue.Invalid("x"), ParamValue.String("x"));
            Assert.Equal(ParamValue.Invalid("null").GetHashCode(), ParamValue.Invalid("null").GetHashCode());
            Assert.Throws<ArgumentException>(() => ParamJson.WriteValue(ParamValue.Invalid("null")));
        }
    }

    /// <summary>Merge inheritance of attributes a mode override does not restate.</summary>
    public class MergeInheritanceTests
    {
        [Fact]
        public void OverridingTargetDurationKeepsUnlimited()
        {
            // a mode that only wants the §2.4 acceptance default of 30 s
            var layer = new ParamSchemaBuilder().Add(new IntParam("targetDuration", 10, 180, 1, 30)).BuildLayer();
            var s = ParamSchema.Merge(CommonParams.Schema, layer);
            var p = (IntParam)s.Get("targetDuration");
            Assert.True(p.AllowUnlimited);
            Assert.Equal(30, s.CreateDefaults().GetInt("targetDuration"));
            Assert.Equal("s", p.Unit);
            var r = Set(("targetDuration", ParamValue.String("unlimited"))).Sanitize(s, DeviceTier.A);
            Assert.True(r.Values.IsUnlimited("targetDuration"));
            Assert.DoesNotContain(r.Issues, i => i.ParamId == "targetDuration");
        }

        [Fact]
        public void ModeMayMakeUnlimitedTheDefaultOrAddIt()
        {
            var layer = new ParamSchemaBuilder().Add(new IntParam("extra", 1, 9, 1, 3).WithUnlimited(isDefault: true)).BuildLayer();
            Assert.True(ParamSchema.Merge(CommonParams.Schema, layer).CreateDefaults().IsUnlimited("extra"));
            // an unlimited default without AllowUnlimited is still an error
            Assert.Throws<ParamSchemaException>(() => new ParamSchemaBuilder()
                .Add(new IntParam("x", 1, 9, 1, 3).WithDefault(ParamValue.Unlimited)).Build());
        }

        [Fact]
        public void UnitIsInheritedAndTierCapComesFromTheMode()
        {
            var common = new ParamSchemaBuilder()
                .Add(new IntParam("n", 1, 100, 1, 5, "pcs").WithTierCap(new TierCap(80, 60, 40)))
                .Add(new FloatParam("f", 0, 10, 0.5, 1, "m").WithTierCap(new TierCap(9, 8, 7)))
                .Build();
            var layer = new ParamSchemaBuilder()
                .Add(new IntParam("n", 1, 50, 1, 10))
                .Add(new FloatParam("f", 0, 5, 0.5, 2, "cm").WithTierCap(new TierCap(null, 4, 3)))
                .BuildLayer();
            var s = ParamSchema.Merge(common, layer);
            var n = (IntParam)s.Get("n");
            var f = (FloatParam)s.Get("f");
            Assert.Equal("pcs", n.Unit);       // inherited (mode gave none)
            Assert.Null(n.TierCap);            // tier caps are overridable: the mode gave none
            Assert.Equal(50, n.EffectiveMax(DeviceTier.C));
            Assert.Equal("cm", f.Unit);        // mode's own unit wins
            Assert.Equal(new TierCap(null, 4, 3), f.TierCap);
            Assert.Equal(5.0, f.EffectiveMax(DeviceTier.A));
        }
    }

    /// <summary>Canonical JSON / ParamsHash must not depend on raw value kinds (spec §7.4).</summary>
    public class CanonicalNormalizationTests
    {
        [Fact]
        public void EquivalentRawKindsGiveTheSameHash()
        {
            var s = CommonParams.Schema;
            var raw = s.CreateDefaults()
                .With("maxSpeed", ParamValue.Int(30))
                .With("ballColors", ParamValue.Strings(new[] { "#ff0066" }))
                .With("seed", ParamValue.String("5"))
                .With("timeScale", ParamValue.Float(1.35))
                .With("backgroundColor", ParamValue.String("#101018ff"));
            var sanitized = raw.Sanitize(s, DeviceTier.A);
            Assert.False(sanitized.HasIssues);
            Assert.Equal(ParamJson.ToCanonicalJson(sanitized.Values, s), ParamJson.ToCanonicalJson(raw, s));
            Assert.Equal(ParamJson.ParamsHash(sanitized.Values, s), ParamJson.ParamsHash(raw, s));
            Assert.Contains("\"maxSpeed\":30.0", ParamJson.ToCanonicalJson(raw, s));
        }

        [Theory]
        [InlineData("ballCount", 1000L)]
        [InlineData("theme", "Vaporwave")]
        [InlineData("gravity", 0.123)]
        public void UnsanitizedValuesAreRefused(string id, object value)
        {
            var s = CommonParams.Schema;
            ParamValue v = value is long l ? ParamValue.Int(l) : value is double d ? ParamValue.Float(d) : ParamValue.String((string)value);
            var ex = Assert.Throws<ArgumentException>(() => ParamJson.ParamsHash(s.CreateDefaults().With(id, v), s));
            Assert.Contains(id, ex.Message);
        }

        [Fact]
        public void TierCappedValuesAreCanonical()
        {
            var s = PegDrop();
            var c = Set(("ballCount", ParamValue.Int(2000))).Sanitize(s, DeviceTier.C).Values;
            Assert.Equal(400, c.GetInt("ballCount"));
            Assert.Contains("\"ballCount\":400", ParamJson.ToCanonicalJson(c, s));
        }
    }

    /// <summary>Single-value codec for replay PARM events (spec §7.4) and live edits.</summary>
    public class SingleValueCodecTests
    {
        [Fact]
        public void WriteAndReadRoundTripEveryKind()
        {
            var values = new[]
            {
                ParamValue.Float(1.35f), ParamValue.Float(0.0), ParamValue.Int(-7), ParamValue.Unlimited, ParamValue.Bool(true),
                ParamValue.String("Neon"), ParamValue.Color(new ColorRgba(1, 2, 3, 4)),
                ParamValue.Colors(new[] { new ColorRgba(255, 0, 0) }), ParamValue.Strings(new[] { "Анна", "a\"b" }),
                ParamValue.Seed(ulong.MaxValue),
            };
            var expected = new[]
            {
                "1.35", "0.0", "-7", "\"unlimited\"", "true", "\"Neon\"", "\"#01020304\"", "[\"#FF0000\"]", "[\"Анна\",\"a\\\"b\"]",
                "\"18446744073709551615\"",
            };
            for (int i = 0; i < values.Length; i++) Assert.Equal(expected[i], ParamJson.WriteValue(values[i]));
            Assert.Equal(ParamValue.Int(-7), ParamJson.ReadValue("-7"));
            Assert.Equal(ParamValue.Float(1.35), ParamJson.ReadValue("1.35"));
            Assert.Equal(ParamValue.Invalid("null"), ParamJson.ReadValue("null"));
            Assert.Throws<FormatException>(() => ParamJson.ReadValue("1 2"));
            Assert.Throws<FormatException>(() => ParamJson.ReadValue("{"));
        }

        [Fact]
        public void SanitizeValueAppliesRangeAndTierCap()
        {
            var s = PegDrop();
            var issues = new List<ParamIssue>();
            Assert.Equal(ParamValue.Int(400), s.SanitizeValue("ballCount", ParamJson.ReadValue("5000"), DeviceTier.C, issues));
            Assert.Equal(ParamIssueCode.OutOfRange, Assert.Single(issues).Code);
            Assert.Equal(ParamValue.Unlimited, s.SanitizeValue("targetDuration", ParamJson.ReadValue("\"unlimited\""), DeviceTier.A));
            Assert.Equal(ParamValue.Float(1.35f), s.SanitizeValue("timeScale", ParamJson.ReadValue("1.35"), DeviceTier.A));
            Assert.Throws<KeyNotFoundException>(() => s.SanitizeValue("nope", ParamValue.Int(1), DeviceTier.A));
            Assert.Throws<InvalidOperationException>(() => RingEscapeLayer().SanitizeValue("ringCount", ParamValue.Int(1), DeviceTier.A));
        }
    }

    /// <summary>Lone UTF-16 surrogates would not survive the UTF-8 paramsJson / preset files.</summary>
    public class LoneSurrogateTests
    {
        [Fact]
        public void StringListItemsAreRepairedAndReported()
        {
            var s = CommonParams.Schema;
            var r = s.CreateDefaults().With("labelNames", ParamValue.Strings(new[] { "ab\uD83D", "\uDE00x", "ok\U0001F600" })).Sanitize(s, DeviceTier.A);
            Assert.Equal(new[] { "ab\uFFFD", "\uFFFDx", "ok\U0001F600" }, r.Values.GetStrings("labelNames"));
            Assert.Equal(2, r.Issues.Count(i => i.Code == ParamIssueCode.InvalidText));
            // now lossless through the UTF-8 bytes and idempotent
            var back = ParamJson.FromJson(Encoding.UTF8.GetString(ParamJson.ToCanonicalUtf8(r.Values, s))).Sanitize(s, DeviceTier.A);
            Assert.Empty(back.Issues);
            Assert.Equal(r.Values, back.Values);
        }

        [Fact]
        public void RemotePresetWithLoneSurrogateEscapeIsAcceptedRepaired()
        {
            string json = "[{\"id\":\"r\",\"name\":\"n\",\"modeId\":\"ring_escape\",\"values\":{\"labelNames\":[\"a\\ud83d\"]}}]";
            var r = RemotePresets.Load(json, m => m == "ring_escape" ? RingEscape() : null);
            // the JSON reader already turns an unpaired \u escape into U+FFFD; programmatic values are repaired by Sanitize
            Assert.Equal("a\uFFFD", Assert.Single(r.Accepted).Values.GetStrings("labelNames")[0]);
            Assert.Empty(r.Rejected);
        }

        [Fact]
        public void PresetNamesAreRepaired()
        {
            Assert.Equal("x\uFFFD", Preset.NormalizeName(" x\uD800 "));
            var p = Preset.Create("p", "ring_escape", null, PresetSource.User, name: "\uDC00abc");
            Assert.Equal("\uFFFDabc", p.Name);
        }

        [Fact]
        public void DefaultWithLoneSurrogateIsASchemaError()
        {
            Assert.Throws<ParamSchemaException>(() => new ParamSchemaBuilder()
                .Add(new StringListParam("names", 4, 8, new[] { "a\uD800" })).Build());
        }
    }

    /// <summary>JSON integers in (long.MaxValue, ulong.MaxValue] are read as seeds; numeric params clamp them.</summary>
    public class HugeIntegerTests
    {
        [Theory]
        [InlineData("9223372036854775807")]
        [InlineData("9223372036854775808")]
        [InlineData("10000000000000000000")]
        [InlineData("18446744073709551615")]
        [InlineData("18446744073709551616")]
        public void HugeIntegersClampToMax(string literal)
        {
            var s = CommonParams.Schema;
            var r = ParamJson.FromJson("{\"ballCount\":" + literal + ",\"maxSpeed\":" + literal + "}").Sanitize(s, DeviceTier.A);
            Assert.Equal(100, r.Values.GetInt("ballCount"));
            Assert.Equal(60f, r.Values.GetFloat("maxSpeed"));
            Assert.Contains(r.Issues, i => i.ParamId == "ballCount" && i.Code == ParamIssueCode.OutOfRange);
            Assert.Contains(r.Issues, i => i.ParamId == "maxSpeed" && i.Code == ParamIssueCode.OutOfRange);
        }

        [Fact]
        public void RemotePresetNeighboursBehaveAlike()
        {
            string json = "[{\"id\":\"a\",\"name\":\"a\",\"modeId\":\"ring_escape\",\"values\":{\"ringCount\":10000000000000000000}}," +
                          "{\"id\":\"b\",\"name\":\"b\",\"modeId\":\"ring_escape\",\"values\":{\"ringCount\":9223372036854775807}}]";
            var r = RemotePresets.Load(json, m => m == "ring_escape" ? RingEscape() : null);
            Assert.Empty(r.Rejected);
            Assert.All(r.Accepted, p => Assert.Equal(100, p.Values.GetInt("ringCount")));
        }

        [Fact]
        public void SmallSeedKindIsReadAsANumber()
        {
            var r = Set(("ballCount", ParamValue.Seed(7)), ("maxSpeed", ParamValue.Seed(20))).Sanitize(CommonParams.Schema, DeviceTier.A);
            Assert.Equal(7, r.Values.GetInt("ballCount"));
            Assert.Equal(20f, r.Values.GetFloat("maxSpeed"));
            Assert.DoesNotContain(r.Issues, i => i.ParamId == "ballCount" || i.ParamId == "maxSpeed");
        }
    }

    /// <summary>Immutable schema / values must not leak mutable backing arrays.</summary>
    public class ImmutabilityTests
    {
        [Fact]
        public void ValueListsCannotBeCastToArrays()
        {
            var schema = new ParamSchemaBuilder()
                .Add(new ColorListParam("cl", 0, 4, new[] { new ColorRgba(1, 2, 3), new ColorRgba(4, 5, 6) }))
                .Add(new StringListParam("sl", 4, 8, new[] { "a" }))
                .Build();
            Assert.False(schema.Get("cl").Default.AsColors is ColorRgba[]);
            Assert.False(schema.Get("sl").Default.AsStrings is string[]);
            Assert.False(ParamValue.Strings(new string[0]).AsStrings is string[]);
            Assert.Throws<NotSupportedException>(() => ((IList<ColorRgba>)schema.Get("cl").Default.AsColors)[0] = default);
        }

        [Fact]
        public void SetIdsSchemaParamsAndLibraryAreReadOnly()
        {
            var set = Set(("a", ParamValue.Int(1)), ("b", ParamValue.Int(2)));
            Assert.False(set.Ids is string[]);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)set.Ids)[0] = "zzz");
            var schema = new ParamSchemaBuilder().Add(new IntParam("a", 0, 10, 1, 1)).Build();
            Assert.False(schema.Params is ParamDef[]);
            Assert.Throws<NotSupportedException>(() => ((IList<ParamDef>)schema.Params)[0] = new IntParam("a", 0, 10, 1, 9));
            Assert.False(RingEscapeLayer().Excluded is string[]);
            var lib = new UserPresetLibrary();
            Assert.False(lib.All is List<Preset>);
            Assert.False(VisibleIf.In("x", "a", "b").Values is ParamValue[]);
        }
    }

    /// <summary>visibleIf values are compared in their sanitized form.</summary>
    public class VisibleIfNormalizationTests
    {
        [Fact]
        public void FloatLiteralConditionMatchesTheStoredFloat()
        {
            var s = new ParamSchemaBuilder()
                .Add(new FloatParam("g", 0, 3, 0.05, 1.0))
                .Add(new BoolParam("x", false).WithVisibleIf(VisibleIf.Is("g", ParamValue.Float(0.15))))
                .Build();
            var v = Set(("g", ParamValue.Float(0.15))).Sanitize(s, DeviceTier.A).Values;
            Assert.True(s.IsVisible("x", v));
            Assert.False(s.IsVisible("x", s.CreateDefaults()));
            Assert.Equal(ParamValue.Float(0.15f), s.Get("x").VisibleIf.Values[0]);
        }

        [Fact]
        public void EquivalentKindsAreNormalized()
        {
            var s = new ParamSchemaBuilder()
                .Add(new IntParam("n", 0, 10, 1, 1))
                .Add(new ColorParam("c", new ColorRgba(0, 0, 0)))
                .Add(new BoolParam("x", false).WithVisibleIf(VisibleIf.Is("n", ParamValue.Float(3.0))))
                .Add(new BoolParam("y", false).WithVisibleIf(VisibleIf.Is("c", ParamValue.String("#ff0000"))))
                .Build();
            var v = Set(("n", ParamValue.Int(3)), ("c", ParamValue.Color(new ColorRgba(255, 0, 0)))).Sanitize(s, DeviceTier.A).Values;
            Assert.True(s.IsVisible("x", v));
            Assert.True(s.IsVisible("y", v));
        }

        [Fact]
        public void OffGridOrOutOfRangeConditionIsStillAnError()
        {
            var ex = Assert.Throws<ParamSchemaException>(() => new ParamSchemaBuilder()
                .Add(new FloatParam("g", 0, 3, 0.05, 1.0))
                .Add(new BoolParam("x", false).WithVisibleIf(VisibleIf.Is("g", ParamValue.Float(0.17))))
                .Build());
            Assert.Contains("step grid", ex.Message);
            Assert.Throws<ParamSchemaException>(() => new ParamSchemaBuilder()
                .Add(new IntParam("n", 0, 10, 1, 1))
                .Add(new BoolParam("x", false).WithVisibleIf(VisibleIf.Is("n", ParamValue.Int(11))))
                .Build());
        }
    }

    /// <summary>User preset library file versioning.</summary>
    public class UserPresetLibraryVersionTests
    {
        const string Entry = "{\"id\":\"u1\",\"name\":\"n\",\"modeId\":\"ring_escape\",\"values\":{}}";

        [Fact]
        public void NewerSchemaVersionIsReportedButPresetsAreKept()
        {
            var lib = UserPresetLibrary.FromJson("{\"schemaVersion\":99,\"presets\":[" + Entry + "]}", out var problems);
            Assert.Single(lib.All);
            Assert.Contains("schemaVersion 99", Assert.Single(problems));
        }

        [Theory]
        [InlineData("{\"schemaVersion\":\"one\",\"presets\":[" + Entry + "]}")]
        [InlineData("{\"schemaVersion\":0,\"presets\":[" + Entry + "]}")]
        [InlineData("{\"schemaVersion\":1.5,\"presets\":[" + Entry + "]}")]
        [InlineData("{\"schemaVersion\":100000000000000000000000,\"presets\":[" + Entry + "]}")]
        public void InvalidSchemaVersionIsReported(string json)
        {
            var lib = UserPresetLibrary.FromJson(json, out var problems);
            Assert.Single(problems);
            Assert.Single(lib.All);
        }

        [Theory]
        [InlineData("{\"schemaVersion\":1,\"presets\":[" + Entry + "]}")]
        [InlineData("{\"presets\":[" + Entry + "]}")]
        public void CurrentOrMissingVersionLoadsCleanly(string json)
        {
            var lib = UserPresetLibrary.FromJson(json, out var problems);
            Assert.Empty(problems);
            Assert.Single(lib.All);
        }
    }
}
