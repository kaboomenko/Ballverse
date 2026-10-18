using System.Collections.Generic;
using System.IO;
using Ballverse.Core.Params;

namespace Ballverse.Core.Tests.Params
{
    /// <summary>Shared fixtures: example mode layers in the spirit of spec §2.9.</summary>
    internal static class TestSchemas
    {
        /// <summary>M01 ring_escape: ring count 3–100 (12), gap 10–90° (30), rotation 10–360°/s (60), etc.</summary>
        public static ParamSchema RingEscapeLayer()
        {
            return new ParamSchemaBuilder()
                .Add(new IntParam("ballCount", 1, 8, 1, 1))
                .Add(new FloatParam("restitution", 0.0, 1.2, 0.01, 1.0))
                .Add(new IntParam("ringCount", 3, 100, 1, 12).InGroup(ParamGroup.Mode, 10))
                .Add(new IntParam("gapAngle", 10, 90, 1, 30, "°").InGroup(ParamGroup.Mode, 20))
                .Add(new FloatParam("rotationSpeed", 10, 360, 5, 60, "°/s").InGroup(ParamGroup.Mode, 30).AsLive(false))
                .Add(new EnumParam("rotationLaw", new[] { "constant", "accelerating", "alternating" }, "constant").InGroup(ParamGroup.Mode, 40))
                .Add(new IntParam("gapCount", 1, 3, 1, 1).InGroup(ParamGroup.Mode, 50))
                .Add(new BoolParam("ringsShrink", false).InGroup(ParamGroup.Mode, 60))
                .Add(new FloatParam("shrinkSpeed", 0.1, 2.0, 0.1, 0.5).InGroup(ParamGroup.Mode, 70)
                    .WithVisibleIf(VisibleIf.IsTrue("ringsShrink")))
                .BuildLayer();
        }

        public static ParamSchema RingEscape()
        {
            return ParamSchema.Merge(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M01)), RingEscapeLayer());
        }

        /// <summary>M04 peg_drop: ball count 1–2000 capped by the §2.9 body budget 2000 / 1000 / 400.</summary>
        public static ParamSchema PegDrop()
        {
            var layer = new ParamSchemaBuilder()
                .Add(new IntParam("ballCount", 1, 2000, 1, 10).WithTierCap(BodyBudgets.BallCap(ModeCode.M04)))
                .Add(new IntParam("pegRows", 6, 20, 1, 12).InGroup(ParamGroup.Mode, 10))
                .Add(new TemplateParam("layout", new[] { "triangle", "diamond", "funnel" }, "triangle").InGroup(ParamGroup.Mode, 20))
                .BuildLayer();
            return ParamSchema.Merge(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M04)), layer);
        }

        /// <summary>M07 last_standing: names-driven, has a winner.</summary>
        public static ParamSchema LastStanding()
        {
            var layer = new ParamSchemaBuilder()
                .Add(new IntParam("ballCount", 4, 32, 1, 12))
                .Add(new EnumParam("arena", new[] { "shrinkingCircle", "rotatingPolygon", "fallingTiles", "wheel" }, "shrinkingCircle")
                    .InGroup(ParamGroup.Mode, 10))
                .BuildLayer();
            return ParamSchema.Merge(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M07)), layer);
        }

        public static ParamSet Set(params (string id, ParamValue value)[] values)
        {
            var list = new List<KeyValuePair<string, ParamValue>>();
            foreach (var (id, value) in values) list.Add(new KeyValuePair<string, ParamValue>(id, value));
            return new ParamSet(list);
        }

        public static string ReadTestData(string name)
        {
            return File.ReadAllText(Path.Combine(System.AppContext.BaseDirectory, "TestData", "Params", name));
        }
    }
}
