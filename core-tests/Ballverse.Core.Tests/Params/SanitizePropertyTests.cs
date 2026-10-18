using System;
using System.Collections.Generic;
using System.Text;
using Ballverse.Core.Params;
using Xunit;

namespace Ballverse.Core.Tests.Params
{
    /// <summary>Rng-driven property tests over random raw inputs (deterministic seeds).</summary>
    public class SanitizePropertyTests
    {
        const int Iterations = 400;

        public static IEnumerable<object[]> Schemas()
        {
            yield return new object[] { "common" };
            yield return new object[] { "ring_escape" };
            yield return new object[] { "peg_drop" };
            yield return new object[] { "last_standing" };
            yield return new object[] { "synthetic" };
        }

        static ParamSchema SchemaFor(string name)
        {
            switch (name)
            {
                case "common": return CommonParams.Schema;
                case "ring_escape": return TestSchemas.RingEscape();
                case "peg_drop": return TestSchemas.PegDrop();
                case "last_standing": return TestSchemas.LastStanding();
                default: return Synthetic();
            }
        }

        /// <summary>Every type, odd steps, negative ranges, off-grid tier caps.</summary>
        static ParamSchema Synthetic()
        {
            return new ParamSchemaBuilder()
                .Add(new FloatParam("f1", -7.5, 12.25, 0.35, -0.5).WithTierCap(new TierCap(11.0, 3.33, -1.0)))
                .Add(new FloatParam("f2", 0.001, 0.01, 0.0003, 0.0019))
                .Add(new FloatParam("f3", 100, 5000, 0.5, 250))
                .Add(new FloatParam("f4", -1, -1, 0.1, -1))
                .Add(new IntParam("i1", -50, 50, 7, -50).WithTierCap(new TierCap(null, 20, -10)))
                .Add(new IntParam("i2", 10, 180, 1, 30).WithUnlimited())
                .Add(new IntParam("i3", -1073741000, int.MaxValue / 2, 1000, 0))
                .Add(new BoolParam("b", true))
                .Add(new EnumParam("e", new[] { "a", "b", "c" }, "b"))
                .Add(new TemplateParam("t", new[] { "zigzag", "funnel", "windmill" }, "funnel"))
                .Add(new ColorParam("c", new ColorRgba(1, 2, 3, 4)))
                .Add(new ColorListParam("cl", 2, 5, new[] { new ColorRgba(9, 9, 9), new ColorRgba(8, 8, 8, 8) }))
                .Add(new StringListParam("sl", 4, 6, new[] { "x" }))
                .Add(new SeedParam("s", 99))
                .Build();
        }

        [Theory]
        [MemberData(nameof(Schemas))]
        public void SanitizeIsIdempotent(string schemaName)
        {
            var schema = SchemaFor(schemaName);
            var rng = Rng.FromSeed(StableSeed(schemaName), 1);
            for (int i = 0; i < Iterations; i++)
            {
                var tier = (DeviceTier)rng.NextInt(0, 3);
                var raw = RandomSet(ref rng, schema);
                var first = raw.Sanitize(schema, tier);
                var second = first.Values.Sanitize(schema, tier);
                if (!first.Values.Equals(second.Values))
                    Assert.Fail("iteration " + i + ": " + raw + " -> " + first.Values + " -> " + second.Values);
                if (second.Issues.Count != 0) Assert.Fail("iteration " + i + ": " + string.Join("; ", second.Issues));
                Assert.Equal(schema.Count, first.Values.Count);

                // sanitizing is deterministic
                Assert.Equal(first.Values, raw.Sanitize(schema, tier).Values);

                // canonical JSON round trip is lossless and hash-stable
                string json = ParamJson.ToCanonicalJson(first.Values, schema);
                var reread = ParamJson.FromJson(json).Sanitize(schema, tier);
                if (reread.Issues.Count != 0) Assert.Fail("iteration " + i + ": " + json + " :: " + string.Join("; ", reread.Issues));
                Assert.Equal(first.Values, reread.Values);
                Assert.Equal(ParamJson.ParamsHash(first.Values, schema), ParamJson.ParamsHash(reread.Values, schema));

                // partial sanitizing is idempotent too
                var p1 = raw.SanitizePartial(schema);
                var p2 = p1.Values.SanitizePartial(schema);
                Assert.Equal(p1.Values, p2.Values);
                Assert.Empty(p2.Issues);
            }
        }

        [Theory]
        [MemberData(nameof(Schemas))]
        public void SanitizedValuesRespectRangesCapsAndGrid(string schemaName)
        {
            var schema = SchemaFor(schemaName);
            var rng = Rng.FromSeed(StableSeed(schemaName), 2);
            for (int i = 0; i < Iterations; i++)
            {
                var tier = (DeviceTier)rng.NextInt(0, 3);
                var values = RandomSet(ref rng, schema).Sanitize(schema, tier).Values;
                foreach (var def in schema.Params)
                {
                    var v = values.Get(def.Id);
                    switch (def)
                    {
                        case FloatParam f:
                        {
                            Assert.Equal(ParamValueKind.Float, v.Kind);
                            double x = v.AsFloat;
                            Assert.Equal((double)(float)x, x);
                            double k = Math.Round((x - f.Min) / f.Step);
                            Assert.Equal((float)(f.Min + k * f.Step), (float)x);
                            Assert.True(k >= 0 && f.Min + k * f.Step <= f.EffectiveMax(tier) + 1e-9);
                            break;
                        }
                        case IntParam n:
                            Assert.Equal(ParamValueKind.Int, v.Kind);
                            if (n.AllowUnlimited && v.IsUnlimited) break;
                            Assert.InRange(v.AsInt, n.Min, n.EffectiveMax(tier));
                            Assert.Equal(0, (v.AsInt - n.Min) % n.Step);
                            break;
                        case OptionParam o:
                            Assert.True(o.HasOption(v.AsString));
                            break;
                        case ColorListParam cl:
                            Assert.InRange(v.AsColors.Count, cl.MinCount, cl.MaxCount);
                            break;
                        case StringListParam sl:
                            Assert.InRange(v.AsStrings.Count, 0, sl.MaxItems);
                            foreach (var s in v.AsStrings) Assert.True(CodePoints(s) <= sl.MaxLength);
                            break;
                        case BoolParam _:
                            Assert.Equal(ParamValueKind.Bool, v.Kind);
                            break;
                        case ColorParam _:
                            Assert.Equal(ParamValueKind.Color, v.Kind);
                            break;
                        case SeedParam _:
                            Assert.Equal(ParamValueKind.Seed, v.Kind);
                            break;
                    }
                }
            }
        }

        static ulong StableSeed(string s)
        {
            return XxHash64.Hash(Encoding.UTF8.GetBytes(s));
        }

        static int CodePoints(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
                n++;
            }
            return n;
        }

        static ParamSet RandomSet(ref Rng rng, ParamSchema schema)
        {
            var pairs = new Dictionary<string, ParamValue>(StringComparer.Ordinal);
            foreach (var def in schema.Params)
            {
                if (rng.NextUInt(100) < 15) continue;
                pairs[def.Id] = rng.NextUInt(100) < 65 ? RandomFor(ref rng, def) : RandomAny(ref rng);
            }
            int unknown = (int)rng.NextUInt(3);
            for (int i = 0; i < unknown; i++) pairs["unknown" + rng.NextUInt(1000)] = RandomAny(ref rng);
            return new ParamSet(pairs);
        }

        static ParamValue RandomFor(ref Rng rng, ParamDef def)
        {
            switch (def)
            {
                case FloatParam f:
                {
                    double span = Math.Max(f.Max - f.Min, f.Step);
                    switch (rng.NextUInt(6))
                    {
                        case 0: return ParamValue.Float(f.Min - span + rng.NextDouble() * 3 * span);
                        case 1: return ParamValue.Float(f.Min + rng.NextUInt(1 + (uint)Math.Min(1e6, span / f.Step)) * f.Step);
                        case 2: return ParamValue.Float((float)(f.Min + rng.NextDouble() * span));
                        case 3: return ParamValue.Int(rng.NextInt(-1000, 1000));
                        case 4: return ParamValue.Float(f.Min + (rng.NextUInt(40) + 0.5) * f.Step);
                        default: return RandomNumber(ref rng);
                    }
                }
                case IntParam n:
                    switch (rng.NextUInt(5))
                    {
                        case 0: return ParamValue.Int(rng.NextInt(n.Min / 2 - 100, n.Max / 2 + 100) * 2L);
                        case 1: return ParamValue.String("unlimited");
                        case 2: return ParamValue.Int(IntParam.UnlimitedValue);
                        case 3: return ParamValue.Float(n.Min + rng.NextDouble() * ((double)n.Max - n.Min));
                        default: return RandomNumber(ref rng);
                    }
                case OptionParam o:
                    return rng.NextBool() ? ParamValue.String(o.Options[(int)rng.NextUInt((uint)o.Options.Count)]) : ParamValue.String(RandomString(ref rng, 8));
                case ColorParam _:
                    return rng.NextBool() ? ParamValue.Color(ColorRgba.FromRgba32(rng.NextUInt())) : ParamValue.String(RandomColorText(ref rng));
                case ColorListParam _:
                {
                    int count = (int)rng.NextUInt(40);
                    if (rng.NextBool())
                    {
                        var list = new List<ColorRgba>();
                        for (int i = 0; i < count; i++) list.Add(ColorRgba.FromRgba32(rng.NextUInt()));
                        return ParamValue.Colors(list);
                    }
                    var texts = new List<string>();
                    for (int i = 0; i < count; i++) texts.Add(RandomColorText(ref rng));
                    return ParamValue.Strings(texts);
                }
                case StringListParam _:
                {
                    int count = (int)rng.NextUInt(40);
                    var list = new List<string>();
                    for (int i = 0; i < count; i++) list.Add(RandomString(ref rng, 30));
                    return ParamValue.Strings(list);
                }
                case SeedParam _:
                    switch (rng.NextUInt(4))
                    {
                        case 0: return ParamValue.Seed(rng.NextULong());
                        case 1: return ParamValue.String(rng.NextULong().ToString(System.Globalization.CultureInfo.InvariantCulture));
                        case 2: return ParamValue.Int((long)rng.NextULong());
                        default: return ParamValue.String(RandomString(ref rng, 5));
                    }
                default:
                    return ParamValue.Bool(rng.NextBool());
            }
        }

        static ParamValue RandomNumber(ref Rng rng)
        {
            switch (rng.NextUInt(9))
            {
                case 0: return ParamValue.Float(double.NaN);
                case 1: return ParamValue.Float(double.PositiveInfinity);
                case 2: return ParamValue.Float(double.NegativeInfinity);
                case 3: return ParamValue.Float(rng.NextBool() ? 1e300 : -1e300);
                case 4: return ParamValue.Float(-0.0);
                case 5: return ParamValue.Int(rng.NextBool() ? long.MaxValue : long.MinValue);
                case 6: return ParamValue.Float(BitConverter.Int64BitsToDouble((long)rng.NextULong()));
                case 7: return ParamValue.Float(BitConverter.Int32BitsToSingle((int)rng.NextUInt()));
                default: return ParamValue.Int((long)rng.NextULong());
            }
        }

        static ParamValue RandomAny(ref Rng rng)
        {
            switch (rng.NextUInt(8))
            {
                case 0: return RandomNumber(ref rng);
                case 1: return ParamValue.Int(rng.NextInt(-500, 500));
                case 2: return ParamValue.Bool(rng.NextBool());
                case 3: return ParamValue.String(RandomString(ref rng, 10));
                case 4: return ParamValue.Color(ColorRgba.FromRgba32(rng.NextUInt()));
                case 5: return ParamValue.Colors(new[] { ColorRgba.FromRgba32(rng.NextUInt()) });
                case 6: return ParamValue.Strings(new[] { RandomString(ref rng, 4), RandomColorText(ref rng) });
                default: return ParamValue.Seed(rng.NextULong());
            }
        }

        static string RandomColorText(ref Rng rng)
        {
            switch (rng.NextUInt(4))
            {
                case 0: return "#" + rng.NextUInt().ToString("x8");
                case 1: return "#" + (rng.NextUInt() & 0xFFFFFF).ToString("X6");
                case 2: return "#" + rng.NextUInt().ToString("x7");
                default: return RandomString(ref rng, 9);
            }
        }

        static string RandomString(ref Rng rng, int maxLen)
        {
            int len = (int)rng.NextUInt((uint)maxLen + 1);
            var sb = new StringBuilder();
            for (int i = 0; i < len; i++)
            {
                switch (rng.NextUInt(6))
                {
                    case 0: sb.Append("\U0001F600"); break;            // surrogate pair
                    case 1: sb.Append((char)(0xD800 + rng.NextUInt(0x800))); break; // lone surrogate
                    case 2: sb.Append((char)(0x410 + rng.NextUInt(64))); break;     // Cyrillic
                    case 3: sb.Append("\"\\\n\t#,"[(int)rng.NextUInt(6)]); break;
                    default: sb.Append((char)('a' + rng.NextUInt(26))); break;
                }
            }
            return sb.ToString();
        }
    }
}
