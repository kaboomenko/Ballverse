using System;
using System.Linq;
using Ballverse.Core.Params;
using Xunit;
using static Ballverse.Core.Tests.Params.TestSchemas;

namespace Ballverse.Core.Tests.Params
{
    public class SanitizeTests
    {
        static readonly ColorRgba Red = new ColorRgba(255, 0, 0);
        static readonly ColorRgba Green = new ColorRgba(0, 255, 0);
        static readonly ColorRgba Blue = new ColorRgba(0, 0, 255);

        static ParamSchema One(ParamDef def)
        {
            return new ParamSchemaBuilder().Add(def).Build();
        }

        static (ParamValue value, ParamIssueCode[] codes) Run(ParamDef def, ParamValue raw, DeviceTier tier = DeviceTier.A)
        {
            var schema = One(def);
            var r = Set((def.Id, raw)).Sanitize(schema, tier);
            return (r.Values.Get(def.Id), r.Issues.Select(i => i.Code).ToArray());
        }

        static void AssertFloat(float expected, ParamValue v)
        {
            Assert.Equal(ParamValueKind.Float, v.Kind);
            Assert.Equal(BitConverter.SingleToInt32Bits(expected), BitConverter.SingleToInt32Bits((float)v.AsFloat));
            Assert.Equal((double)(float)v.AsFloat, v.AsFloat);
        }

        // ---------- whole-set rules ----------

        [Fact]
        public void FillsMissingWithDefaultsAndReportsThem()
        {
            var schema = CommonParams.Schema;
            var r = ParamSet.Empty.Sanitize(schema, DeviceTier.A);
            Assert.Equal(schema.Count, r.Values.Count);
            Assert.Equal(schema.Count, r.Issues.Count);
            Assert.All(r.Issues, i => Assert.Equal(ParamIssueCode.Missing, i.Code));
            Assert.Equal(schema.CreateDefaults(), r.Values);
            Assert.Equal(schema.Params.Select(p => p.Id), r.Issues.Select(i => i.ParamId));
        }

        [Fact]
        public void DropsUnknownIdsInOrdinalOrder()
        {
            var schema = One(new BoolParam("a", true));
            var r = Set(("zeta", ParamValue.Bool(true)), ("a", ParamValue.Bool(false)), ("Alpha", ParamValue.Int(1))).Sanitize(schema, DeviceTier.A);
            Assert.Equal(new[] { "a" }, r.Values.Ids);
            Assert.False(r.Values.GetBool("a"));
            Assert.Equal(new[] { "Alpha", "zeta" }, r.Issues.Select(i => i.ParamId));
            Assert.All(r.Issues, i => Assert.Equal(ParamIssueCode.Unknown, i.Code));
        }

        [Fact]
        public void LayerCannotSanitize()
        {
            Assert.Throws<InvalidOperationException>(() => ParamSet.Empty.Sanitize(RingEscapeLayer(), DeviceTier.A));
        }

        [Fact]
        public void ValidSetIsUnchangedWithoutIssues()
        {
            var schema = TestSchemas.RingEscape();
            var defaults = schema.CreateDefaults();
            var r = defaults.Sanitize(schema, DeviceTier.C);
            Assert.False(r.HasIssues);
            Assert.Equal(defaults, r.Values);
        }

        // ---------- Float ----------

        [Fact]
        public void FloatClampsToRange()
        {
            var def = new FloatParam("f", 0.25, 3.0, 0.05, 1.0);
            var (v, codes) = Run(def, ParamValue.Float(10));
            AssertFloat(3f, v);
            Assert.Equal(new[] { ParamIssueCode.OutOfRange }, codes);
            (v, codes) = Run(def, ParamValue.Float(-1));
            AssertFloat(0.25f, v);
            Assert.Equal(new[] { ParamIssueCode.OutOfRange }, codes);
            (v, codes) = Run(def, ParamValue.Float(1e300));
            AssertFloat(3f, v);
            Assert.Equal(new[] { ParamIssueCode.OutOfRange }, codes);
        }

        [Fact]
        public void FloatSnapsHalfUpToExactGridValues()
        {
            var def = new FloatParam("f", 0.25, 3.0, 0.05, 1.0);
            var (v, codes) = Run(def, ParamValue.Float(1.01));
            AssertFloat((float)(0.25 + 15 * 0.05), v);
            Assert.Equal(new[] { ParamIssueCode.Snapped }, codes);
            (v, _) = Run(def, ParamValue.Float(1.04));
            AssertFloat((float)(0.25 + 16 * 0.05), v);
            // exact tie (0.25 + 15.5 steps) rounds up
            (v, _) = Run(new FloatParam("g", 0, 10, 0.5, 0), ParamValue.Float(1.25));
            AssertFloat(1.5f, v);
            (v, _) = Run(new FloatParam("g", 0, 10, 0.5, 0), ParamValue.Float(1.2499));
            AssertFloat(1.0f, v);
        }

        [Fact]
        public void FloatGridValuesAreComputedInDoubleThenCast()
        {
            var def = new FloatParam("f", 0.1, 1.5, 0.01, 0.5);
            for (int k = 0; k <= 140; k++)
            {
                float expected = (float)(0.1 + k * 0.01);
                var (v, codes) = Run(def, ParamValue.Float(0.1 + k * 0.01 + 0.004));
                AssertFloat(expected, v);
                // the float itself (widened to double) is accepted silently
                (v, codes) = Run(def, ParamValue.Float(expected));
                AssertFloat(expected, v);
                Assert.Empty(codes);
            }
        }

        [Fact]
        public void FloatAcceptsIntegersSilentlyWhenOnGrid()
        {
            var (v, codes) = Run(new FloatParam("f", 0, 60, 1, 30), ParamValue.Int(42));
            AssertFloat(42f, v);
            Assert.Empty(codes);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void FloatRejectsNonFinite(double raw)
        {
            var (v, codes) = Run(new FloatParam("f", 0, 3, 0.05, 1), ParamValue.Float(raw));
            AssertFloat(1f, v);
            Assert.Equal(new[] { ParamIssueCode.NonFinite }, codes);
        }

        [Fact]
        public void FloatTypeMismatchFallsBackToDefault()
        {
            var (v, codes) = Run(new FloatParam("f", 0, 3, 0.05, 1), ParamValue.String("1.5"));
            AssertFloat(1f, v);
            Assert.Equal(new[] { ParamIssueCode.TypeMismatch }, codes);
        }

        [Fact]
        public void FloatNormalizesNegativeZero()
        {
            var (v, _) = Run(new FloatParam("f", -1, 1, 0.5, 0), ParamValue.Float(-0.0));
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(v.AsFloat));
            (v, _) = Run(new FloatParam("f", -1, 1, 0.5, 0), ParamValue.Float(-0.1));
            Assert.Equal(0L, BitConverter.DoubleToInt64Bits(v.AsFloat));
        }

        [Fact]
        public void FloatTierCapClampsPerTier()
        {
            var def = new FloatParam("glowish", 0, 1, 0.05, 0.5).WithTierCap(new TierCap(null, 0.8, 0.33));
            var (v, codes) = Run(def, ParamValue.Float(1.0), DeviceTier.A);
            AssertFloat(1f, v);
            Assert.Empty(codes);
            (v, codes) = Run(def, ParamValue.Float(1.0), DeviceTier.B);
            AssertFloat((float)(0 + 16 * 0.05), v);
            Assert.Equal(new[] { ParamIssueCode.TierCapped }, codes);
            // cap 0.33 is off-grid: the largest grid value below it is used
            (v, codes) = Run(def, ParamValue.Float(1.0), DeviceTier.C);
            AssertFloat((float)(0 + 6 * 0.05), v);
            Assert.Equal(new[] { ParamIssueCode.TierCapped }, codes);
            Assert.Equal(0.8, ((FloatParam)One(def).Get("glowish")).EffectiveMax(DeviceTier.B));
        }

        [Fact]
        public void FloatAboveMaxReportsOutOfRangeEvenWithCap()
        {
            var def = new FloatParam("f", 0, 1, 0.1, 0.5).WithTierCap(new TierCap(0.5, 0.5, 0.5));
            var (v, codes) = Run(def, ParamValue.Float(7), DeviceTier.B);
            AssertFloat(0.5f, v);
            Assert.Equal(new[] { ParamIssueCode.OutOfRange }, codes);
        }

        [Fact]
        public void FloatMaxNotOnGridIsNeverExceeded()
        {
            var def = new FloatParam("f", 0, 1, 0.3, 0.3);
            var (v, _) = Run(def, ParamValue.Float(1));
            AssertFloat((float)0.9, v);
            (v, _) = Run(def, ParamValue.Float(0.98));
            AssertFloat((float)0.9, v);
        }

        [Fact]
        public void FloatSnapIsPublicAndMatchesSanitize()
        {
            var def = (FloatParam)One(new FloatParam("f", 0.25, 3, 0.05, 1)).Get("f");
            Assert.Equal((float)(0.25 + 15 * 0.05), def.Snap(1.01, DeviceTier.A));
            Assert.Equal(3f, def.Snap(99, DeviceTier.A));
        }

        // ---------- Int ----------

        [Fact]
        public void IntClampsAndSnaps()
        {
            var def = new IntParam("i", 10, 180, 5, 30);
            var (v, codes) = Run(def, ParamValue.Int(7));
            Assert.Equal(10, v.AsInt);
            Assert.Equal(new[] { ParamIssueCode.OutOfRange }, codes);
            (v, codes) = Run(def, ParamValue.Int(1000));
            Assert.Equal(180, v.AsInt);
            Assert.Equal(new[] { ParamIssueCode.OutOfRange }, codes);
            (v, codes) = Run(def, ParamValue.Int(32));
            Assert.Equal(30, v.AsInt);
            Assert.Equal(new[] { ParamIssueCode.Snapped }, codes);
            (v, _) = Run(def, ParamValue.Int(33));
            Assert.Equal(35, v.AsInt);
            (v, _) = Run(def, ParamValue.Int(long.MinValue));
            Assert.Equal(10, v.AsInt);
            (v, _) = Run(def, ParamValue.Int(long.MaxValue));
            Assert.Equal(180, v.AsInt);
        }

        [Fact]
        public void IntTieRoundsUp()
        {
            var (v, _) = Run(new IntParam("i", 0, 100, 10, 0), ParamValue.Int(25));
            Assert.Equal(30, v.AsInt);
            (v, _) = Run(new IntParam("i", 0, 100, 10, 0), ParamValue.Float(24.999));
            Assert.Equal(20, v.AsInt);
        }

        [Fact]
        public void IntAcceptsFloatsAndReportsFractions()
        {
            var def = new IntParam("i", 0, 359, 1, 270);
            var (v, codes) = Run(def, ParamValue.Float(90.0));
            Assert.Equal(ParamValue.Int(90), v);
            Assert.Empty(codes);
            (v, codes) = Run(def, ParamValue.Float(90.6));
            Assert.Equal(ParamValue.Int(91), v);
            Assert.Equal(new[] { ParamIssueCode.Snapped }, codes);
            (v, codes) = Run(def, ParamValue.Float(double.NaN));
            Assert.Equal(ParamValue.Int(270), v);
            Assert.Equal(new[] { ParamIssueCode.NonFinite }, codes);
            (v, codes) = Run(def, ParamValue.Float(-1e30));
            Assert.Equal(ParamValue.Int(0), v);
            Assert.Equal(new[] { ParamIssueCode.OutOfRange }, codes);
        }

        [Fact]
        public void IntTierCapUsesLargestGridValueUnderCap()
        {
            var def = new IntParam("n", 0, 200, 10, 50).WithTierCap(new TierCap(null, 125, 40));
            var (v, codes) = Run(def, ParamValue.Int(200), DeviceTier.A);
            Assert.Equal(200, v.AsInt);
            Assert.Empty(codes);
            (v, codes) = Run(def, ParamValue.Int(200), DeviceTier.B);
            Assert.Equal(120, v.AsInt);
            Assert.Equal(new[] { ParamIssueCode.TierCapped }, codes);
            (v, codes) = Run(def, ParamValue.Int(125), DeviceTier.B);
            Assert.Equal(120, v.AsInt);
            Assert.Equal(new[] { ParamIssueCode.Snapped }, codes);
            Assert.Equal(120, ((IntParam)One(def).Get("n")).EffectiveMax(DeviceTier.B));
            // defaults above the tier cap are clamped too
            var r = ParamSet.Empty.Sanitize(One(def), DeviceTier.C);
            Assert.Equal(40, r.Values.GetInt("n"));
            Assert.Equal(40, One(def).CreateDefaults(DeviceTier.C).GetInt("n"));
        }

        [Fact]
        public void IntTypeMismatch()
        {
            var (v, codes) = Run(new IntParam("i", 0, 10, 1, 3), ParamValue.Bool(true));
            Assert.Equal(3, v.AsInt);
            Assert.Equal(new[] { ParamIssueCode.TypeMismatch }, codes);
            (v, codes) = Run(new IntParam("i", 0, 10, 1, 3), ParamValue.String("unlimited"));
            Assert.Equal(3, v.AsInt);
            Assert.Equal(new[] { ParamIssueCode.TypeMismatch }, codes);
        }

        [Fact]
        public void IntUnlimited()
        {
            var def = new IntParam("d", 10, 180, 1, 30).WithUnlimited();
            var (v, codes) = Run(def, ParamValue.String("unlimited"));
            Assert.True(v.IsUnlimited);
            Assert.Empty(codes);
            (v, codes) = Run(def, ParamValue.Unlimited);
            Assert.True(v.IsUnlimited);
            Assert.Empty(codes);
            (v, codes) = Run(def, ParamValue.Int(500));
            Assert.Equal(180, v.AsInt);
            Assert.Equal(new[] { ParamIssueCode.OutOfRange }, codes);
            (v, codes) = Run(def, ParamValue.String("Unlimited"));
            Assert.Equal(30, v.AsInt);
            Assert.Equal(new[] { ParamIssueCode.TypeMismatch }, codes);
            // without AllowUnlimited the sentinel is just a large number
            (v, _) = Run(new IntParam("d", 10, 180, 1, 30), ParamValue.Unlimited);
            Assert.Equal(180, v.AsInt);
        }

        // ---------- Bool / Enum / Template ----------

        [Fact]
        public void BoolRules()
        {
            Assert.Equal(ParamValue.Bool(false), Run(new BoolParam("b", true), ParamValue.Bool(false)).value);
            var (v, codes) = Run(new BoolParam("b", true), ParamValue.Int(0));
            Assert.Equal(ParamValue.Bool(true), v);
            Assert.Equal(new[] { ParamIssueCode.TypeMismatch }, codes);
        }

        [Fact]
        public void EnumCoercesInvalidToDefault()
        {
            var def = new EnumParam("e", new[] { "Neon", "Pastel" }, "Neon");
            Assert.Equal("Pastel", Run(def, ParamValue.String("Pastel")).value.AsString);
            var (v, codes) = Run(def, ParamValue.String("pastel"));
            Assert.Equal("Neon", v.AsString);
            Assert.Equal(new[] { ParamIssueCode.InvalidOption }, codes);
            (v, codes) = Run(def, ParamValue.Int(1));
            Assert.Equal("Neon", v.AsString);
            Assert.Equal(new[] { ParamIssueCode.TypeMismatch }, codes);
        }

        [Fact]
        public void TemplateCoercesInvalidToDefault()
        {
            var def = new TemplateParam("t", new[] { "zigzag", "funnel" }, "funnel");
            Assert.Equal("zigzag", Run(def, ParamValue.String("zigzag")).value.AsString);
            var (v, codes) = Run(def, ParamValue.String("windmill"));
            Assert.Equal("funnel", v.AsString);
            Assert.Equal(new[] { ParamIssueCode.InvalidOption }, codes);
        }

        // ---------- Color / ColorList ----------

        [Fact]
        public void ColorParsesStrings()
        {
            var def = new ColorParam("c", Red);
            Assert.Equal(ParamValue.Color(Green), Run(def, ParamValue.String("#00ff00")).value);
            Assert.Equal(ParamValue.Color(Blue), Run(def, ParamValue.Color(Blue)).value);
            var (v, codes) = Run(def, ParamValue.String("green"));
            Assert.Equal(ParamValue.Color(Red), v);
            Assert.Equal(new[] { ParamIssueCode.InvalidValue }, codes);
            (v, codes) = Run(def, ParamValue.Int(0xFF));
            Assert.Equal(ParamValue.Color(Red), v);
            Assert.Equal(new[] { ParamIssueCode.TypeMismatch }, codes);
        }

        [Fact]
        public void ColorListDropsInvalidEntriesTruncatesAndPads()
        {
            var def = new ColorListParam("cl", 2, 3, new[] { Red, Green });
            var (v, codes) = Run(def, ParamValue.Strings(new[] { "#0000FF", "bad", "#FF0000" }));
            Assert.Equal(new[] { Blue, Red }, v.AsColors);
            Assert.Equal(new[] { ParamIssueCode.InvalidValue }, codes);

            (v, codes) = Run(def, ParamValue.Colors(new[] { Blue, Blue, Blue, Blue, Red }));
            Assert.Equal(new[] { Blue, Blue, Blue }, v.AsColors);
            Assert.Equal(new[] { ParamIssueCode.ListTruncated }, codes);

            (v, codes) = Run(def, ParamValue.Colors(new[] { Blue }));
            Assert.Equal(new[] { Blue, Green }, v.AsColors);
            Assert.Equal(new[] { ParamIssueCode.ListPadded }, codes);

            (v, codes) = Run(def, ParamValue.Strings(new string[0]));
            Assert.Equal(new[] { Red, Green }, v.AsColors);
            Assert.Equal(new[] { ParamIssueCode.ListPadded }, codes);

            (v, codes) = Run(def, ParamValue.String("#FF0000"));
            Assert.Equal(new[] { Red, Green }, v.AsColors);
            Assert.Equal(new[] { ParamIssueCode.TypeMismatch }, codes);
        }

        [Fact]
        public void EmptyBallColorsMeansThemePaletteAndIsValid()
        {
            var r = TestSchemas.Set((CommonParams.BallColors, ParamValue.Strings(new string[0]))).Sanitize(CommonParams.Schema, DeviceTier.A);
            Assert.Empty(r.Values.GetColors(CommonParams.BallColors));
            Assert.DoesNotContain(r.Issues, i => i.ParamId == CommonParams.BallColors);
        }

        // ---------- StringList ----------

        [Fact]
        public void StringListTruncatesListAndItems()
        {
            var def = new StringListParam("names", 32, 16, new string[0]);
            var many = Enumerable.Range(0, 40).Select(i => "n" + i).ToArray();
            var (v, codes) = Run(def, ParamValue.Strings(many));
            Assert.Equal(many.Take(32), v.AsStrings);
            Assert.Equal(new[] { ParamIssueCode.ListTruncated }, codes);

            (v, codes) = Run(def, ParamValue.Strings(new[] { "Anna", "Maximilian-Alexander", "" }));
            Assert.Equal(new[] { "Anna", "Maximilian-Alexa", "" }, v.AsStrings);
            Assert.Equal(new[] { ParamIssueCode.StringTruncated }, codes);
        }

        [Fact]
        public void StringListCountsCodePointsAndNeverSplitsSurrogates()
        {
            var def = new StringListParam("names", 4, 3, new string[0]);
            string emoji = "\U0001F600"; // 2 UTF-16 units, 1 code point
            var (v, codes) = Run(def, ParamValue.Strings(new[] { emoji + emoji + emoji, emoji + emoji + emoji + "x", "ab" + emoji + "c" }));
            Assert.Equal(new[] { emoji + emoji + emoji, emoji + emoji + emoji, "ab" + emoji }, v.AsStrings);
            Assert.Equal(new[] { ParamIssueCode.StringTruncated, ParamIssueCode.StringTruncated }, codes);
        }

        [Fact]
        public void StringListTypeMismatch()
        {
            var (v, codes) = Run(new StringListParam("s", 2, 5, new[] { "a" }), ParamValue.String("x"));
            Assert.Equal(new[] { "a" }, v.AsStrings);
            Assert.Equal(new[] { ParamIssueCode.TypeMismatch }, codes);
        }

        [Fact]
        public void SplitPastedTextHandlesCommasAndLines()
        {
            Assert.Equal(new[] { "Anna", "Bob", "Carl", "Dana" },
                StringListParam.SplitPastedText(" Anna, Bob\r\nCarl,,\n  \nDana "));
            Assert.Empty(StringListParam.SplitPastedText(null));
            Assert.Empty(StringListParam.SplitPastedText(""));
        }

        [Fact]
        public void NullStringItemsBecomeEmpty()
        {
            Assert.Equal(new[] { "", "a" }, ParamValue.Strings(new[] { null, "a" }).AsStrings);
        }

        // ---------- Seed ----------

        [Fact]
        public void SeedAcceptsDecimalStringsAndNonNegativeInts()
        {
            var def = new SeedParam("seed");
            Assert.Equal(ParamValue.Seed(ulong.MaxValue), Run(def, ParamValue.String("18446744073709551615")).value);
            Assert.Equal(ParamValue.Seed(42), Run(def, ParamValue.Int(42)).value);
            Assert.Equal(ParamValue.Seed(7), Run(def, ParamValue.Seed(7)).value);
            foreach (var bad in new[] { ParamValue.String("18446744073709551616"), ParamValue.String("-1"), ParamValue.String(" 1"),
                         ParamValue.String("1e3"), ParamValue.String(""), ParamValue.Int(-5) })
            {
                var (v, codes) = Run(def, bad);
                Assert.Equal(ParamValue.Seed(0), v);
                Assert.Equal(new[] { ParamIssueCode.InvalidValue }, codes);
            }
            var (fv, fcodes) = Run(def, ParamValue.Float(3.0));
            Assert.Equal(ParamValue.Seed(0), fv);
            Assert.Equal(new[] { ParamIssueCode.TypeMismatch }, fcodes);
        }

        // ---------- partial ----------

        [Fact]
        public void SanitizePartialKeepsOnlyPresentKnownIdsAndIgnoresTierCaps()
        {
            var schema = TestSchemas.PegDrop();
            var r = Set(("ballCount", ParamValue.Int(5000)), ("pegRows", ParamValue.Int(12)), ("bogus", ParamValue.Bool(true)))
                .SanitizePartial(schema);
            Assert.Equal(new[] { "ballCount", "pegRows" }, r.Values.Ids);
            Assert.Equal(2000, r.Values.GetInt("ballCount"));
            Assert.Equal(new[] { ParamIssueCode.OutOfRange, ParamIssueCode.Unknown }, r.Issues.Select(i => i.Code));
        }
    }
}
