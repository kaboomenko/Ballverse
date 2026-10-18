using System;
using Ballverse.Core.Params;
using Xunit;

namespace Ballverse.Core.Tests.Params
{
    public class ParamSchemaBuilderTests
    {
        static ParamSchemaException BuildFails(params ParamDef[] defs)
        {
            return Assert.Throws<ParamSchemaException>(() => new ParamSchemaBuilder().AddRange(defs).Build());
        }

        [Fact]
        public void RejectsDuplicateIds()
        {
            var e = BuildFails(new BoolParam("a", true), new IntParam("a", 0, 1, 1, 0));
            Assert.Equal("a", e.ParamId);
            Assert.Contains("duplicate", e.Message);
        }

        [Theory]
        [InlineData("")]
        [InlineData("1abc")]
        [InlineData("a-b")]
        [InlineData("a b")]
        [InlineData("_a")]
        public void RejectsInvalidIds(string id)
        {
            BuildFails(new BoolParam(id, true));
        }

        [Fact]
        public void RejectsNullId()
        {
            BuildFails(new BoolParam(null, true));
        }

        [Fact]
        public void FloatRules()
        {
            Assert.Contains("greater than max", BuildFails(new FloatParam("f", 2, 1, 0.1, 1.5)).Message);
            Assert.Contains("step", BuildFails(new FloatParam("f", 0, 1, 0, 0.5)).Message);
            Assert.Contains("step", BuildFails(new FloatParam("f", 0, 1, -0.1, 0.5)).Message);
            Assert.Contains("step", BuildFails(new FloatParam("f", 0, 1, double.NaN, 0.5)).Message);
            Assert.Contains("outside", BuildFails(new FloatParam("f", 0, 1, 0.1, 1.5)).Message);
            Assert.Contains("outside", BuildFails(new FloatParam("f", 0, 1, 0.1, -0.5)).Message);
            Assert.Contains("outside", BuildFails(new FloatParam("f", 0, 1, 0.1, double.NaN)).Message);
            Assert.Contains("grid", BuildFails(new FloatParam("f", 0, 1, 0.1, 0.55)).Message);
            Assert.Contains("finite", BuildFails(new FloatParam("f", 0, double.PositiveInfinity, 0.1, 0.5)).Message);
            Assert.Contains("too small", BuildFails(new FloatParam("f", 0, 1000, 0.000001, 1)).Message);
            Assert.Contains("cap", BuildFails(new FloatParam("f", 0, 1, 0.1, 0.5).WithTierCap(new TierCap(null, -0.5, null))).Message);
            Assert.Contains("cap", BuildFails(new FloatParam("f", 0, 1, 0.1, 0.5).WithTierCap(new TierCap(null, double.NaN, null))).Message);
            Assert.Contains("number", BuildFails(new FloatParam("f", 0, 1, 0.1, 0.5).WithDefault(ParamValue.String("x"))).Message);
        }

        [Fact]
        public void IntRules()
        {
            Assert.Contains("greater than max", BuildFails(new IntParam("i", 5, 1, 1, 3)).Message);
            Assert.Contains("step", BuildFails(new IntParam("i", 0, 10, 0, 3)).Message);
            Assert.Contains("outside", BuildFails(new IntParam("i", 0, 10, 1, 11)).Message);
            Assert.Contains("grid", BuildFails(new IntParam("i", 0, 10, 5, 3)).Message);
            Assert.Contains("reserved", BuildFails(new IntParam("i", 0, int.MaxValue, 1, 3)).Message);
            Assert.Contains("cap", BuildFails(new IntParam("i", 0, 10, 1, 3).WithTierCap(new TierCap(2.5, null, null))).Message);
            Assert.Contains("cap", BuildFails(new IntParam("i", 5, 10, 1, 5).WithTierCap(new TierCap(null, null, 4))).Message);
            // unlimited default needs AllowUnlimited
            Assert.Contains("outside", BuildFails(new IntParam("i", 0, 10, 1, 3).WithDefault(ParamValue.Unlimited)).Message);
        }

        [Fact]
        public void MinEqualsMaxIsAllowed()
        {
            var s = new ParamSchemaBuilder().Add(new IntParam("i", 4, 4, 1, 4)).Add(new FloatParam("f", 1, 1, 0.5, 1)).Build();
            Assert.Equal(2, s.Count);
        }

        [Fact]
        public void OptionRules()
        {
            Assert.Contains("at least one", BuildFails(new EnumParam("e", new string[0], "a")).Message);
            Assert.Contains("duplicate option", BuildFails(new EnumParam("e", new[] { "a", "a" }, "a")).Message);
            Assert.Contains("non-empty", BuildFails(new EnumParam("e", new[] { "a", "" }, "a")).Message);
            Assert.Contains("not one of", BuildFails(new EnumParam("e", new[] { "a", "b" }, "c")).Message);
            Assert.Contains("not one of", BuildFails(new TemplateParam("t", new[] { "a" }, "A")).Message);
        }

        [Fact]
        public void ListRules()
        {
            var red = new ColorRgba(255, 0, 0);
            Assert.Contains("minCount", BuildFails(new ColorListParam("c", -1, 3, new ColorRgba[0])).Message);
            Assert.Contains("maxCount", BuildFails(new ColorListParam("c", 3, 2, new[] { red, red, red })).Message);
            Assert.Contains("maxCount", BuildFails(new ColorListParam("c", 0, 0, new ColorRgba[0])).Message);
            Assert.Contains("default has", BuildFails(new ColorListParam("c", 2, 3, new[] { red })).Message);
            Assert.Contains("maxItems", BuildFails(new StringListParam("s", 0, 3, new string[0])).Message);
            Assert.Contains("maxLen", BuildFails(new StringListParam("s", 3, 0, new string[0])).Message);
            Assert.Contains("default has", BuildFails(new StringListParam("s", 1, 3, new[] { "a", "b" })).Message);
            Assert.Contains("longer", BuildFails(new StringListParam("s", 2, 3, new[] { "abcd" })).Message);
        }

        [Fact]
        public void VisibleIfMustReferenceKnownParam()
        {
            var e = BuildFails(new BoolParam("a", true).WithVisibleIf(VisibleIf.IsTrue("nope")));
            Assert.Contains("unknown parameter 'nope'", e.Message);
        }

        [Fact]
        public void VisibleIfSelfReferenceIsRejected()
        {
            Assert.Contains("itself", BuildFails(new BoolParam("a", true).WithVisibleIf(VisibleIf.IsTrue("a"))).Message);
        }

        [Fact]
        public void VisibleIfCyclesAreRejected()
        {
            var e = BuildFails(
                new BoolParam("a", true).WithVisibleIf(VisibleIf.IsTrue("b")),
                new BoolParam("b", true).WithVisibleIf(VisibleIf.IsTrue("c")),
                new BoolParam("c", true).WithVisibleIf(VisibleIf.IsTrue("a")));
            Assert.Contains("cycle", e.Message);
        }

        [Fact]
        public void VisibleIfValuesMustBeValidForTarget()
        {
            var mode = new EnumParam("mode", new[] { "x", "y" }, "x");
            Assert.Contains("never a valid value", BuildFails(mode, new BoolParam("b", true).WithVisibleIf(VisibleIf.Is("mode", "z"))).Message);
            Assert.Contains("never a valid value", BuildFails(mode, new BoolParam("b", true).WithVisibleIf(VisibleIf.In("mode", "x", "q"))).Message);
            Assert.Contains("Bool target", BuildFails(mode, new BoolParam("b", true).WithVisibleIf(VisibleIf.IsTrue("mode"))).Message);
            Assert.Contains("never a valid value", BuildFails(new IntParam("n", 0, 10, 2, 0),
                new BoolParam("b", true).WithVisibleIf(VisibleIf.Is("n", ParamValue.Int(3)))).Message);
            // a valid int condition builds
            new ParamSchemaBuilder().Add(new IntParam("n", 0, 10, 2, 0))
                .Add(new BoolParam("b", true).WithVisibleIf(VisibleIf.Is("n", ParamValue.Int(4)))).Build();
        }

        [Fact]
        public void ExcludeIsOnlyForLayers()
        {
            Assert.Throws<ParamSchemaException>(() => new ParamSchemaBuilder().Exclude("x").Build());
            var layer = new ParamSchemaBuilder().Exclude("x").Exclude("x").BuildLayer();
            Assert.Equal(new[] { "x" }, layer.Excluded);
            Assert.Throws<ParamSchemaException>(() => new ParamSchemaBuilder().Add(new BoolParam("x", true)).Exclude("x").BuildLayer());
        }

        [Fact]
        public void LayerAllowsExternalVisibleIfButStillChecksLocalOnes()
        {
            var layer = new ParamSchemaBuilder().Add(new BoolParam("b", true).WithVisibleIf(VisibleIf.Is("labels", "names"))).BuildLayer();
            Assert.True(layer.IsLayer);
            Assert.Throws<ParamSchemaException>(() => new ParamSchemaBuilder()
                .Add(new BoolParam("a", true).WithVisibleIf(VisibleIf.IsTrue("b")))
                .Add(new BoolParam("b", true).WithVisibleIf(VisibleIf.IsTrue("a"))).BuildLayer());
        }

        [Fact]
        public void OrderIsGroupThenOrderThenInsertion()
        {
            var s = new ParamSchemaBuilder()
                .Add(new BoolParam("m1", true))
                .Add(new BoolParam("look", true).InGroup(ParamGroup.Look, 0))
                .Add(new BoolParam("basic2", true).InGroup(ParamGroup.Basic, 20))
                .Add(new BoolParam("basic1", true).InGroup(ParamGroup.Basic, 10))
                .Add(new BoolParam("basic1b", true).InGroup(ParamGroup.Basic, 10))
                .Add(new BoolParam("m0", true).InGroup(ParamGroup.Mode, -5))
                .Build();
            Assert.Equal(new[] { "basic1", "basic1b", "basic2", "look", "m0", "m1" }, Array.ConvertAll(ToArray(s), d => d.Id));
        }

        [Fact]
        public void SchemaIsImmutableAfterBuild()
        {
            var b = new ParamSchemaBuilder().Add(new BoolParam("a", true));
            var s = b.Build();
            b.Add(new BoolParam("b", true));
            Assert.Equal(1, s.Count);
            Assert.False(s.Contains("b"));
        }

        [Fact]
        public void FloatDefaultIsNormalizedToGrid()
        {
            var s = new ParamSchemaBuilder().Add(new FloatParam("f", 0.1, 1.5, 0.01, 0.3)).Build();
            var def = (FloatParam)s.Get("f");
            Assert.Equal((float)(0.1 + 20 * 0.01), def.DefaultValue);
            Assert.Equal(ParamValueKind.Float, def.Default.Kind);
        }

        [Fact]
        public void DefinitionsAreCopiedNotShared()
        {
            var original = new FloatParam("f", 0, 1, 0.1, 0.5);
            var moved = original.InGroup(ParamGroup.Physics, 3).AsLive().WithLabelKey("x.y");
            Assert.Equal(ParamGroup.Mode, original.Group);
            Assert.False(original.Live);
            Assert.Equal("params.f", original.LabelKey);
            Assert.Equal(ParamGroup.Physics, moved.Group);
            Assert.Equal(3, moved.Order);
            Assert.True(moved.Live);
            Assert.Equal("x.y", moved.LabelKey);
        }

        [Fact]
        public void GetUnknownThrows()
        {
            var s = new ParamSchemaBuilder().Add(new BoolParam("a", true)).Build();
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => s.Get("b"));
            Assert.False(s.TryGet(null, out _));
            Assert.False(s.Contains(null));
        }

        static ParamDef[] ToArray(ParamSchema s)
        {
            var a = new ParamDef[s.Count];
            for (int i = 0; i < a.Length; i++) a[i] = s.Params[i];
            return a;
        }
    }
}
