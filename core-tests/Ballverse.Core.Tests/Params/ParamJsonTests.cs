using System;
using System.Linq;
using System.Text;
using Ballverse.Core.Params;
using Xunit;
using static Ballverse.Core.Tests.Params.TestSchemas;

namespace Ballverse.Core.Tests.Params
{
    public class ParamJsonTests
    {
        /// <summary>Non-default ring_escape values touching every value kind (raw, before sanitizing).</summary>
        internal static ParamSet RingSample()
        {
            return Set(
                ("timeScale", ParamValue.Float(1.35)),
                ("ballCount", ParamValue.Int(3)),
                ("ballSize", ParamValue.Float(0.3)),
                ("theme", ParamValue.String("Candy")),
                ("gravity", ParamValue.Float(0.15)),
                ("gravityAngle", ParamValue.Int(90)),
                ("restitution", ParamValue.Float(1.05)),
                ("ballColors", ParamValue.Strings(new[] { "#ff0066", "#00E5FFcc", "#FFFFFF" })),
                ("labels", ParamValue.String("names")),
                ("labelNames", ParamValue.Strings(new[] { "Анна", "Bob \"B\"", "Zoë\U0001F600", "back\\slash" })),
                ("soundPack", ParamValue.String("Melody")),
                ("melody", ParamValue.String("fur_elise")),
                ("sfxVolume", ParamValue.Int(65)),
                ("haptics", ParamValue.Bool(false)),
                ("targetDuration", ParamValue.Int(45)),
                ("countdown", ParamValue.Bool(true)),
                ("seed", ParamValue.String("18446744073709551557")),
                ("ringCount", ParamValue.Int(40)),
                ("rotationSpeed", ParamValue.Float(122.5)),
                ("rotationLaw", ParamValue.String("alternating")),
                ("ringsShrink", ParamValue.Bool(true)),
                ("shrinkSpeed", ParamValue.Float(0.7)));
        }

        const ulong CommonDefaultsHash = 0x56094870ac7ec9ffUL;   // cross-checked with the Python `xxhash` package
        const ulong RingSampleHash = 0x2ffe513bb0016e45UL;

        static string Golden(string name)
        {
            return ReadTestData(name).Replace("\r\n", "\n").TrimEnd('\n');
        }

        // ---------- canonical JSON & hash (spec §7.4) ----------

        [Fact]
        public void CommonDefaultsMatchGoldenJsonAndHash()
        {
            var s = CommonParams.Schema;
            var d = s.CreateDefaults();
            Assert.Equal(Golden("common_defaults.canonical.json"), ParamJson.ToCanonicalJson(d, s));
            Assert.Equal(CommonDefaultsHash, ParamJson.ParamsHash(d, s));
            Assert.Equal(CommonDefaultsHash, XxHash64.Hash(Encoding.UTF8.GetBytes(Golden("common_defaults.canonical.json"))));
        }

        [Fact]
        public void RingSampleMatchesGoldenJsonAndHash()
        {
            var s = TestSchemas.RingEscape();
            var v = RingSample().Sanitize(s, DeviceTier.A).Values;
            Assert.Equal(Golden("ring_escape_sample.canonical.json"), ParamJson.ToCanonicalJson(v, s));
            Assert.Equal(Golden("ring_escape_sample.indented.json"), ParamJson.ToCanonicalJson(v, s, indented: true));
            Assert.Equal(RingSampleHash, ParamJson.ParamsHash(v, s));
            Assert.Equal(Encoding.UTF8.GetBytes(Golden("ring_escape_sample.canonical.json")), ParamJson.ToCanonicalUtf8(v, s));
        }

        [Fact]
        public void CanonicalJsonIgnoresInsertionOrder()
        {
            var s = TestSchemas.RingEscape();
            var a = RingSample().Sanitize(s, DeviceTier.A).Values;
            var pairs = a.Ids.Reverse().Select(id => new System.Collections.Generic.KeyValuePair<string, ParamValue>(id, a.Get(id)));
            var b = new ParamSet(pairs);
            Assert.Equal(ParamJson.ToCanonicalJson(a, s), ParamJson.ToCanonicalJson(b, s));
            Assert.Equal(ParamJson.ParamsHash(a, s), ParamJson.ParamsHash(b, s));
        }

        [Fact]
        public void CanonicalJsonWritesDefaultsForMissingAndSkipsUnknown()
        {
            var s = CommonParams.Schema;
            var partial = Set(("bogus", ParamValue.Int(1)));
            Assert.Equal(Golden("common_defaults.canonical.json"), ParamJson.ToCanonicalJson(partial, s));
        }

        [Fact]
        public void HashChangesWithAnyValue()
        {
            var s = CommonParams.Schema;
            var d = s.CreateDefaults();
            var hashes = new System.Collections.Generic.HashSet<ulong> { ParamJson.ParamsHash(d, s) };
            Assert.True(hashes.Add(ParamJson.ParamsHash(d.With("seed", ParamValue.Seed(1)), s)));
            Assert.True(hashes.Add(ParamJson.ParamsHash(d.With("glow", ParamValue.Float((float)0.55)), s)));
            Assert.True(hashes.Add(ParamJson.ParamsHash(d.With("targetDuration", ParamValue.Int(180)), s)));
        }

        [Fact]
        public void CanonicalRoundTripsThroughFromJson()
        {
            var s = TestSchemas.RingEscape();
            var v = RingSample().Sanitize(s, DeviceTier.A).Values;
            var back = ParamJson.FromJson(ParamJson.ToCanonicalJson(v, s)).Sanitize(s, DeviceTier.A);
            Assert.False(back.HasIssues, string.Join("; ", back.Issues));
            Assert.Equal(v, back.Values);
        }

        [Fact]
        public void SeedSurvivesAsDecimalString()
        {
            var s = CommonParams.Schema;
            var v = s.CreateDefaults().With("seed", ParamValue.Seed(ulong.MaxValue));
            string json = ParamJson.ToCanonicalJson(v, s);
            Assert.Contains("\"seed\":\"18446744073709551615\"", json);
            Assert.Equal(ulong.MaxValue, ParamJson.FromJson(json).Sanitize(s, DeviceTier.A).Values.GetSeed("seed"));
        }

        [Fact]
        public void LayerCannotBeSerializedCanonically()
        {
            Assert.Throws<InvalidOperationException>(() => ParamJson.ToCanonicalJson(ParamSet.Empty, RingEscapeLayer()));
        }

        // ---------- generic JSON ----------

        [Fact]
        public void ToJsonOrdersSchemaKeysFirstThenOrdinal()
        {
            var v = Set(("zz", ParamValue.Int(1)), ("seed", ParamValue.Seed(5)), ("Aa", ParamValue.Bool(true)), ("timeScale", ParamValue.Float(2)));
            Assert.Equal("{\"timeScale\":2.0,\"seed\":\"5\",\"Aa\":true,\"zz\":1}", ParamJson.ToJson(v, CommonParams.Schema, indented: false));
            Assert.Equal("{\"Aa\":true,\"seed\":\"5\",\"timeScale\":2.0,\"zz\":1}", ParamJson.ToJson(v, null, indented: false));
        }

        [Fact]
        public void FromJsonReadsRawKinds()
        {
            var v = ParamJson.FromJson("{\"i\":5,\"f\":1.5,\"e\":1e2,\"b\":false,\"s\":\"x\",\"l\":[\"a\",\"b\"],\"empty\":[]," +
                "\"big\":18446744073709551615,\"huge\":1844674407370955161600,\"neg\":-3,\"date\":\"2026-10-18T12:00:00Z\"}");
            Assert.Equal(ParamValue.Int(5), v.Get("i"));
            Assert.Equal(ParamValue.Float(1.5), v.Get("f"));
            Assert.Equal(ParamValue.Float(100), v.Get("e"));
            Assert.Equal(ParamValue.Bool(false), v.Get("b"));
            Assert.Equal(ParamValue.String("x"), v.Get("s"));
            Assert.Equal(ParamValue.Strings(new[] { "a", "b" }), v.Get("l"));
            Assert.Equal(ParamValue.Strings(new string[0]), v.Get("empty"));
            Assert.Equal(ParamValue.Seed(ulong.MaxValue), v.Get("big"));
            Assert.Equal(ParamValueKind.Float, v.Get("huge").Kind);
            Assert.Equal(ParamValue.Int(-3), v.Get("neg"));
            Assert.Equal(ParamValue.String("2026-10-18T12:00:00Z"), v.Get("date"));
        }

        [Fact]
        public void FromJsonSkipsUnsupportedTokensAndLastDuplicateWins()
        {
            var v = ParamJson.FromJson("{\"n\":null,\"o\":{\"x\":1},\"mixed\":[\"a\",1],\"d\":1,\"d\":2} // trailing comment");
            Assert.Equal(new[] { "d" }, v.Ids);
            Assert.Equal(ParamValue.Int(2), v.Get("d"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("[]")]
        [InlineData("42")]
        [InlineData("{\"a\":")]
        [InlineData("{\"a\":1} {\"b\":2}")]
        [InlineData("not json")]
        public void FromJsonRejectsMalformedWithoutThrowingFromTry(string json)
        {
            Assert.False(ParamJson.TryFromJson(json, out var set, out var error));
            Assert.Null(set);
            Assert.False(string.IsNullOrEmpty(error));
            Assert.Throws<FormatException>(() => ParamJson.FromJson(json));
        }

        [Fact]
        public void UnsanitizedNumbersStillSerialize()
        {
            var v = Set(("a", ParamValue.Float(0.1)), ("b", ParamValue.Float(1e300)), ("c", ParamValue.Float(3)));
            Assert.Equal("{\"a\":0.1,\"b\":1E+300,\"c\":3.0}", ParamJson.ToJson(v, null, false));
            Assert.Throws<ArgumentException>(() => ParamJson.ToJson(Set(("x", ParamValue.Float(double.NaN))), null, false));
        }

        [Fact]
        public void IndentedUsesLfNewlines()
        {
            string json = ParamJson.ToCanonicalJson(CommonParams.Schema.CreateDefaults(), CommonParams.Schema, indented: true);
            Assert.DoesNotContain("\r", json);
            Assert.StartsWith("{\n  \"timeScale\": 1.0,\n", json);
        }
    }
}
