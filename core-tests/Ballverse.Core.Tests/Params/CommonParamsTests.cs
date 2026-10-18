using System.Linq;
using Ballverse.Core.Params;
using Xunit;

namespace Ballverse.Core.Tests.Params
{
    public class CommonParamsTests
    {
        static readonly ParamSchema S = CommonParams.Schema;

        [Fact]
        public void HasExactlyTheSpecTableInPanelOrder()
        {
            Assert.Equal(new[]
            {
                "timeScale", "ballCount", "ballSize", "theme",
                "gravity", "gravityAngle", "restitution", "friction", "maxSpeed",
                "ballColors", "ballSkin", "trail", "background", "glow", "impactFx", "labels", "labelNames", "hud",
                "soundPack", "melody", "sfxVolume", "haptics",
                "targetDuration", "forcedWinner", "finaleStyle", "countdown", "seed",
            }, S.Params.Select(p => p.Id));
        }

        [Fact]
        public void Groups()
        {
            void G(ParamGroup g, params string[] ids)
            {
                foreach (var id in ids) Assert.Equal(g, S.Get(id).Group);
            }
            G(ParamGroup.Basic, "timeScale", "ballCount", "ballSize", "theme");
            G(ParamGroup.Physics, "gravity", "gravityAngle", "restitution", "friction", "maxSpeed");
            G(ParamGroup.Look, "ballColors", "ballSkin", "trail", "background", "glow", "impactFx", "labels", "labelNames", "hud");
            G(ParamGroup.Sound, "soundPack", "melody", "sfxVolume", "haptics");
            G(ParamGroup.Director, "targetDuration", "forcedWinner", "finaleStyle", "countdown", "seed");
        }

        [Theory]
        [InlineData("timeScale", 0.25, 3.0, 1.0)]
        [InlineData("ballSize", 0.1, 1.5, 0.5)]
        [InlineData("gravity", 0.0, 3.0, 1.0)]
        [InlineData("restitution", 0.0, 1.2, 0.8)]
        [InlineData("friction", 0.0, 1.0, 0.2)]
        [InlineData("maxSpeed", 5.0, 60.0, 30.0)]
        [InlineData("glow", 0.0, 1.0, 0.5)]
        public void FloatRanges(string id, double min, double max, double def)
        {
            var p = Assert.IsType<FloatParam>(S.Get(id));
            Assert.Equal(min, p.Min);
            Assert.Equal(max, p.Max);
            Assert.Equal((float)def, p.DefaultValue);
            Assert.True(p.Step > 0);
        }

        [Theory]
        [InlineData("gravityAngle", 0, 359, 270)]
        [InlineData("sfxVolume", 0, 100, 80)]
        [InlineData("ballCount", 1, 100, 1)]
        [InlineData("forcedWinner", 0, 32, 0)]
        public void IntRanges(string id, int min, int max, int def)
        {
            var p = Assert.IsType<IntParam>(S.Get(id));
            Assert.Equal(min, p.Min);
            Assert.Equal(max, p.Max);
            Assert.Equal(def, p.Default.AsInt);
        }

        [Fact]
        public void TargetDurationIs10To180OrUnlimitedByDefault()
        {
            var p = Assert.IsType<IntParam>(S.Get("targetDuration"));
            Assert.Equal(10, p.Min);
            Assert.Equal(180, p.Max);
            Assert.True(p.AllowUnlimited);
            Assert.True(p.Default.IsUnlimited);
            Assert.Equal("s", p.Unit);
            Assert.True(S.CreateDefaults().IsUnlimited("targetDuration"));
        }

        [Theory]
        [InlineData("theme", "Neon", new[] { "Neon", "Pastel", "Mono", "Candy", "Space", "Ocean", "Sunset", "Retro", "Glass", "Contrast" })]
        [InlineData("soundPack", "Notes", new[] { "Notes", "Melody", "Pop", "Glass", "Drums", "Asmr", "Chiptune", "None" })]
        [InlineData("ballSkin", "solid", new[] { "solid", "gradient", "outline", "gloss", "emoji", "flag", "letter" })]
        [InlineData("trail", "short", new[] { "off", "short", "long", "rainbow" })]
        [InlineData("background", "theme", new[] { "theme", "color", "gradient", "grid", "stars", "blur" })]
        [InlineData("impactFx", "sparks", new[] { "off", "sparks", "confetti", "shards" })]
        [InlineData("labels", "off", new[] { "off", "numbers", "names" })]
        [InlineData("hud", "auto", new[] { "auto", "minimal", "full", "off" })]
        [InlineData("finaleStyle", "slowMo", new[] { "stop", "slowMo", "zoom", "confetti" })]
        public void EnumOptions(string id, string def, string[] options)
        {
            var p = Assert.IsType<EnumParam>(S.Get(id));
            Assert.Equal(options, p.Options);
            Assert.Equal(def, p.DefaultValue);
        }

        [Fact]
        public void OtherTypes()
        {
            Assert.True(Assert.IsType<BoolParam>(S.Get("haptics")).DefaultValue);
            Assert.False(Assert.IsType<BoolParam>(S.Get("countdown")).DefaultValue);
            Assert.Equal(0UL, Assert.IsType<SeedParam>(S.Get("seed")).DefaultValue);
            var names = Assert.IsType<StringListParam>(S.Get("labelNames"));
            Assert.Equal(32, names.MaxItems);
            Assert.Equal(16, names.MaxLength);
            var colors = Assert.IsType<ColorListParam>(S.Get("ballColors"));
            Assert.Equal(0, colors.MinCount);
            Assert.Empty(colors.DefaultValue);
            var melody = Assert.IsType<EnumParam>(S.Get("melody"));
            Assert.Equal("ode_to_joy", melody.DefaultValue);
            Assert.Equal(VisibleIfKind.Is, melody.VisibleIf.Kind);
            Assert.Equal("soundPack", melody.VisibleIf.TargetId);
            Assert.Equal("labels", names.VisibleIf.TargetId);
        }

        [Fact]
        public void LiveVersusStructural()
        {
            var live = new[]
            {
                "timeScale", "theme", "ballColors", "ballSkin", "trail", "background", "glow", "impactFx", "labels", "labelNames",
                "hud", "soundPack", "melody", "sfxVolume", "haptics",
            };
            foreach (var p in S.Params)
                Assert.True(live.Contains(p.Id) == p.Live, p.Id + " live flag");
            Assert.False(S.IsLiveEditable("ballCount"));
            Assert.False(S.IsLiveEditable("ballSize"));
            Assert.False(S.IsLiveEditable("gravity"));
            Assert.False(S.IsLiveEditable("seed"));
        }

        [Fact]
        public void ForModeDropsForcedWinnerForModesWithoutWinner()
        {
            Assert.False(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M01)).Contains("forcedWinner"));
            Assert.True(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M03)).Contains("forcedWinner"));
            Assert.Same(CommonParams.Schema, CommonParams.ForMode(ModeCatalog.Get(ModeCode.M07)));
            foreach (var m in ModeCatalog.All)
                Assert.Equal(m.HasWinner, CommonParams.ForMode(m).Contains("forcedWinner"));
        }

        [Fact]
        public void BuildAcceptsDataDrivenOptionLists()
        {
            var s = CommonParams.Build(themes: CommonParams.Themes.Concat(new[] { "Aurora" }),
                melodies: CommonParams.Melodies.Concat(new[] { "moonlight" }));
            Assert.Contains("Aurora", ((EnumParam)s.Get("theme")).Options);
            Assert.Contains("moonlight", ((EnumParam)s.Get("melody")).Options);
            Assert.Throws<ParamSchemaException>(() => CommonParams.Build(themes: new[] { "Aurora" }));
        }

        [Fact]
        public void LabelKeysDefaultToParamsPrefix()
        {
            Assert.All(S.Params, p => Assert.Equal("params." + p.Id, p.LabelKey));
            Assert.Equal("params.theme.Neon", ((EnumParam)S.Get("theme")).OptionLabelKey("Neon"));
        }

        [Fact]
        public void DefaultsSanitizeCleanOnEveryTier()
        {
            foreach (DeviceTier t in new[] { DeviceTier.A, DeviceTier.B, DeviceTier.C })
            {
                var d = S.CreateDefaults(t);
                Assert.False(d.Sanitize(S, t).HasIssues);
            }
        }
    }

    public class BodyBudgetsTests
    {
        [Theory]
        [InlineData(ModeCode.M01, 300, 200, 120)]
        [InlineData(ModeCode.M02, 600, 400, 200)]
        [InlineData(ModeCode.M04, 2000, 1000, 400)]
        [InlineData(ModeCode.M08, 120, 80, 50)]
        [InlineData(ModeCode.M09, 2500, 1200, 500)]
        [InlineData(ModeCode.M11, 600, 350, 200)]
        [InlineData(ModeCode.M12, 400, 250, 150)]
        [InlineData(ModeCode.M13, 40, 40, 30)]
        [InlineData(ModeCode.M14, 1000, 600, 300)]
        [InlineData(ModeCode.M15, 3000, 1500, 700)]
        public void MatchSpecCards(ModeCode mode, int a, int b, int c)
        {
            Assert.Equal(a, BodyBudgets.Get(mode, DeviceTier.A));
            Assert.Equal(b, BodyBudgets.Get(mode, DeviceTier.B));
            Assert.Equal(c, BodyBudgets.Get(mode, DeviceTier.C));
            Assert.Equal(new TierCap(a, b, c), BodyBudgets.AsTierCap(mode));
        }

        [Fact]
        public void EveryModeHasNonIncreasingBudgets()
        {
            foreach (var m in ModeCatalog.All)
            {
                int a = BodyBudgets.Get(m.Code, DeviceTier.A), b = BodyBudgets.Get(m.Code, DeviceTier.B), c = BodyBudgets.Get(m.Code, DeviceTier.C);
                Assert.True(a >= b && b >= c && c > 0, m.Id);
            }
            Assert.Throws<System.ArgumentOutOfRangeException>(() => BodyBudgets.Get((ModeCode)99, DeviceTier.A));
        }
    }
}
