using System;
using System.Linq;
using Ballverse.Core.Params;
using Xunit;
using static Ballverse.Core.Tests.Params.TestSchemas;

namespace Ballverse.Core.Tests.Params
{
    public class ParamSchemaTests
    {
        // ---------- Merge ----------

        [Fact]
        public void MergeOverridesRangeAndDefaultButKeepsCommonAttributes()
        {
            var schema = TestSchemas.RingEscape();
            var ballCount = (IntParam)schema.Get(CommonParams.BallCount);
            Assert.Equal(1, ballCount.Min);
            Assert.Equal(8, ballCount.Max);
            Assert.Equal(ParamGroup.Basic, ballCount.Group);
            Assert.Equal(20, ballCount.Order);
            Assert.False(ballCount.Live);
            var restitution = (FloatParam)schema.Get(CommonParams.Restitution);
            Assert.Equal(1f, restitution.DefaultValue);
            Assert.Equal(ParamGroup.Physics, restitution.Group);
            Assert.Equal("params.restitution", restitution.LabelKey);
        }

        [Fact]
        public void MergeKeepsCommonUnitWhenModeOmitsIt()
        {
            var layer = new ParamSchemaBuilder().Add(new FloatParam("ballSize", 0.2, 1.0, 0.1, 0.4)).BuildLayer();
            var merged = ParamSchema.Merge(CommonParams.Schema, layer);
            Assert.Equal("m", ((FloatParam)merged.Get("ballSize")).Unit);
            Assert.Equal(0.2, ((FloatParam)merged.Get("ballSize")).Min);
        }

        [Fact]
        public void MergeAppendsModeParamsAfterCommonGroups()
        {
            var schema = TestSchemas.RingEscape();
            var ids = schema.Params.Select(p => p.Id).ToList();
            Assert.Equal(CommonParams.TimeScale, ids[0]);
            Assert.Equal(new[] { "ringCount", "gapAngle", "rotationSpeed", "rotationLaw", "gapCount", "ringsShrink", "shrinkSpeed" },
                ids.Skip(ids.Count - 7));
            Assert.All(schema.Params.Skip(ids.Count - 7), p => Assert.Equal(ParamGroup.Mode, p.Group));
        }

        [Fact]
        public void MergeCannotChangeType()
        {
            var layer = new ParamSchemaBuilder().Add(new FloatParam("ballCount", 1, 10, 1, 1)).BuildLayer();
            var e = Assert.Throws<ParamSchemaException>(() => ParamSchema.Merge(CommonParams.Schema, layer));
            Assert.Equal("ballCount", e.ParamId);
            Assert.Contains("type", e.Message);
        }

        [Fact]
        public void MergeOverridesOptions()
        {
            var layer = new ParamSchemaBuilder()
                .Add(new EnumParam("soundPack", new[] { "Glass", "None" }, "Glass"))
                .BuildLayer();
            Assert.Throws<ParamSchemaException>(() => ParamSchema.Merge(CommonParams.Schema, layer)); // melody's visibleIf needs "Melody"
            var layer2 = new ParamSchemaBuilder()
                .Add(new EnumParam("soundPack", new[] { "Glass", "Melody", "None" }, "Glass"))
                .BuildLayer();
            var merged = ParamSchema.Merge(CommonParams.Schema, layer2);
            Assert.Equal(new[] { "Glass", "Melody", "None" }, ((EnumParam)merged.Get("soundPack")).Options);
            Assert.True(merged.Get("soundPack").Live);
        }

        [Fact]
        public void LayerValidatesOverriddenValues()
        {
            Assert.Throws<ParamSchemaException>(() => new ParamSchemaBuilder()
                .Add(new IntParam("ballCount", 1, 2000, 1, 10).WithTierCap(new TierCap(3000, null, null))).BuildLayer());
            Assert.Throws<ParamSchemaException>(() => new ParamSchemaBuilder()
                .Add(new FloatParam("restitution", 0, 1.2, 0.01, 1.5)).BuildLayer());
        }

        [Fact]
        public void MergeExcludesCommonParams()
        {
            var layer = new ParamSchemaBuilder().Exclude(CommonParams.Countdown).BuildLayer();
            var merged = ParamSchema.Merge(CommonParams.Schema, layer);
            Assert.False(merged.Contains(CommonParams.Countdown));
            Assert.Equal(CommonParams.Schema.Count - 1, merged.Count);

            var bad = new ParamSchemaBuilder().Exclude("notCommon").BuildLayer();
            Assert.Throws<ParamSchemaException>(() => ParamSchema.Merge(CommonParams.Schema, bad));

            // excluding a visibleIf target breaks the dependent parameter
            var breaking = new ParamSchemaBuilder().Exclude(CommonParams.Labels).BuildLayer();
            Assert.Contains("unknown parameter", Assert.Throws<ParamSchemaException>(() => ParamSchema.Merge(CommonParams.Schema, breaking)).Message);
        }

        [Fact]
        public void MergeChecksLayerReferencesAgainstCommon()
        {
            var ok = new ParamSchemaBuilder().Add(new BoolParam("namesOnTop", true).WithVisibleIf(VisibleIf.Is("labels", "names"))).BuildLayer();
            Assert.True(ParamSchema.Merge(CommonParams.Schema, ok).Contains("namesOnTop"));
            var bad = new ParamSchemaBuilder().Add(new BoolParam("x", true).WithVisibleIf(VisibleIf.Is("labels", "emoji"))).BuildLayer();
            Assert.Throws<ParamSchemaException>(() => ParamSchema.Merge(CommonParams.Schema, bad));
            var missing = new ParamSchemaBuilder().Add(new BoolParam("x", true).WithVisibleIf(VisibleIf.IsTrue("ghost"))).BuildLayer();
            Assert.Throws<ParamSchemaException>(() => ParamSchema.Merge(CommonParams.Schema, missing));
        }

        [Fact]
        public void MergeRequiresCompleteCommonSchema()
        {
            Assert.Throws<ArgumentException>(() => ParamSchema.Merge(RingEscapeLayer(), RingEscapeLayer()));
            Assert.Throws<ArgumentNullException>(() => ParamSchema.Merge(null, RingEscapeLayer()));
        }

        [Fact]
        public void MergeAcceptsCompleteModeSchemaToo()
        {
            var mode = new ParamSchemaBuilder().Add(new IntParam("ballCount", 2, 16, 1, 6)).Build();
            Assert.Equal(6, ParamSchema.Merge(CommonParams.Schema, mode).CreateDefaults().GetInt("ballCount"));
        }

        [Fact]
        public void TierCapOverrideFromBodyBudget()
        {
            var schema = TestSchemas.PegDrop();
            var set = Set(("ballCount", ParamValue.Int(2000)));
            Assert.Equal(2000, set.Sanitize(schema, DeviceTier.A).Values.GetInt("ballCount"));
            Assert.Equal(1000, set.Sanitize(schema, DeviceTier.B).Values.GetInt("ballCount"));
            Assert.Equal(400, set.Sanitize(schema, DeviceTier.C).Values.GetInt("ballCount"));
        }

        // ---------- visibility & live ----------

        [Fact]
        public void VisibleIfEqualsOnEnum()
        {
            var schema = CommonParams.Schema;
            var defaults = schema.CreateDefaults();
            Assert.False(schema.IsVisible(CommonParams.Melody, defaults));
            Assert.True(schema.IsVisible(CommonParams.Melody, defaults.With(CommonParams.SoundPack, ParamValue.String("Melody"))));
            Assert.False(schema.IsVisible(CommonParams.LabelNames, defaults.With(CommonParams.Labels, ParamValue.String("numbers"))));
            Assert.True(schema.IsVisible(CommonParams.LabelNames, defaults.With(CommonParams.Labels, ParamValue.String("names"))));
        }

        [Fact]
        public void VisibleIfInAndIsTrueAndChains()
        {
            var schema = new ParamSchemaBuilder()
                .Add(new EnumParam("arena", new[] { "circle", "square", "star" }, "circle"))
                .Add(new BoolParam("spin", false).WithVisibleIf(VisibleIf.In("arena", "square", "star")))
                .Add(new FloatParam("spinSpeed", 0, 10, 1, 5).WithVisibleIf(VisibleIf.IsTrue("spin")))
                .Add(new IntParam("n", 0, 4, 1, 0))
                .Add(new BoolParam("nIsTwo", false).WithVisibleIf(VisibleIf.Is("n", ParamValue.Int(2))))
                .Build();
            var d = schema.CreateDefaults();
            Assert.False(schema.IsVisible("spin", d));
            Assert.False(schema.IsVisible("spinSpeed", d.With("spin", ParamValue.Bool(true)))); // parent hidden -> hidden
            var star = d.With("arena", ParamValue.String("star"));
            Assert.True(schema.IsVisible("spin", star));
            Assert.False(schema.IsVisible("spinSpeed", star));
            Assert.True(schema.IsVisible("spinSpeed", star.With("spin", ParamValue.Bool(true))));
            Assert.True(schema.IsVisible("nIsTwo", d.With("n", ParamValue.Int(2))));
            Assert.False(schema.IsVisible("nIsTwo", d));
            // missing target -> default used
            Assert.False(schema.IsVisible("spin", ParamSet.Empty));
            Assert.Equal(new[] { "arena", "n" }, schema.GetVisible(ParamSet.Empty).Select(p => p.Id));
        }

        [Fact]
        public void GetVisibleKeepsSchemaOrder()
        {
            var schema = CommonParams.Schema;
            var all = schema.CreateDefaults().With(CommonParams.SoundPack, ParamValue.String("Melody"))
                .With(CommonParams.Labels, ParamValue.String("names"));
            Assert.Equal(schema.Params.Select(p => p.Id), schema.GetVisible(all).Select(p => p.Id));
            Assert.Equal(schema.Count - 2, schema.GetVisible(schema.CreateDefaults()).Count);
        }

        [Fact]
        public void VisibilityInLayerTreatsExternalTargetsAsSatisfied()
        {
            var layer = new ParamSchemaBuilder().Add(new BoolParam("x", true).WithVisibleIf(VisibleIf.Is("labels", "names"))).BuildLayer();
            Assert.True(layer.IsVisible("x", ParamSet.Empty));
        }

        [Fact]
        public void LiveEditableFollowsDefinition()
        {
            var schema = TestSchemas.RingEscape();
            Assert.True(schema.IsLiveEditable(CommonParams.TimeScale));
            Assert.False(schema.IsLiveEditable(CommonParams.BallCount));
            Assert.False(schema.IsLiveEditable("rotationSpeed"));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => schema.IsLiveEditable("nope"));
        }

        [Fact]
        public void CreateDefaultsOnLayerThrows()
        {
            Assert.Throws<InvalidOperationException>(() => RingEscapeLayer().CreateDefaults());
        }
    }
}
