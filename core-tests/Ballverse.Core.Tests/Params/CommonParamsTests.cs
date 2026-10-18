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
                "ballColors", "ballSkin", "trail", "background", "backgroundColor", "glow", "impactFx", "labels", "labelNames",
                "hud", "hudTimer", "hudCounter", "hudPercent", "hudLeaders",
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
            G(ParamGroup.Look, "ballColors", "ballSkin", "trail", "background", "backgroundColor", "glow", "impactFx", "labels",
                "labelNames", "hud", "hudTimer", "hudCounter", "hudPercent", "hudLeaders");
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
                "timeScale", "theme", "ballColors", "ballSkin", "trail", "background", "backgroundColor", "glow", "impactFx", "labels",
                "labelNames", "hud", "hudTimer", "hudCounter", "hudPercent", "hudLeaders", "soundPack", "melody", "sfxVolume", "haptics",
            };
            foreach (var p in S.Params)
                Assert.True(live.Contains(p.Id) == p.Live, p.Id + " live flag");
            Assert.False(S.IsLiveEditable("ballCount"));
            Assert.False(S.IsLiveEditable("ballSize"));
            Assert.False(S.IsLiveEditable("gravity"));
            Assert.False(S.IsLiveEditable("seed"));
        }

        [Fact]
        public void ForModeOffersForcedWinnerExactlyInTheSpec25Modes()
        {
            // spec §2.5: M03, M06, M07, M08, M09; §2.4: never interactive modes (M10 and M14 have only an optional winner)
            var expected = new[] { ModeCode.M03, ModeCode.M06, ModeCode.M07, ModeCode.M08, ModeCode.M09 };
            var actual = ModeCatalog.All.Where(m => CommonParams.ForMode(m).Contains("forcedWinner")).Select(m => m.Code);
            Assert.Equal(expected, actual);
            Assert.Equal(expected, ModeCatalog.All.Where(CommonParams.SupportsForcedWinner).Select(m => m.Code));
            Assert.False(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M10)).Contains("forcedWinner"));
            Assert.False(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M14)).Contains("forcedWinner"));
            Assert.Same(CommonParams.Schema, CommonParams.ForMode(ModeCatalog.Get(ModeCode.M07)));
            Assert.Equal(S.Count - 1, CommonParams.ForMode(ModeCatalog.Get(ModeCode.M01)).Count);
        }

        [Fact]
        public void HudIsAMasterSwitchPlusPerElementToggles()
        {
            // spec §2.2: elements timer, counter, percentages, leader names; "можно выключить всё"
            Assert.True(Assert.IsType<BoolParam>(S.Get("hud")).DefaultValue);
            Assert.Null(S.Get("hud").VisibleIf);
            foreach (var id in new[] { "hudTimer", "hudCounter", "hudPercent", "hudLeaders" })
            {
                var p = Assert.IsType<BoolParam>(S.Get(id));
                Assert.Equal(VisibleIfKind.IsTrue, p.VisibleIf.Kind);
                Assert.Equal("hud", p.VisibleIf.TargetId);
            }
            var d = S.CreateDefaults();
            Assert.True(S.IsVisible("hudTimer", d));
            var off = d.With("hud", ParamValue.Bool(false));
            Assert.False(S.IsVisible("hudTimer", off));
            Assert.False(S.IsVisible("hudLeaders", off));
            // "timer on, percentages off" is expressible
            var mixed = d.With("hudTimer", ParamValue.Bool(true)).With("hudPercent", ParamValue.Bool(false)).Sanitize(S, DeviceTier.A);
            Assert.False(mixed.HasIssues);
            Assert.True(mixed.Values.GetBool("hudTimer"));
            Assert.False(mixed.Values.GetBool("hudPercent"));
        }

        [Fact]
        public void HudElementDefaultsCanBeOverriddenByAMode()
        {
            var layer = new ParamSchemaBuilder()
                .Add(new BoolParam("hudPercent", true))
                .Exclude("hudLeaders")
                .BuildLayer();
            var s = ParamSchema.Merge(S, layer);
            Assert.True(s.CreateDefaults().GetBool("hudPercent"));
            Assert.False(s.Contains("hudLeaders"));
            Assert.Equal("hud", s.Get("hudPercent").VisibleIf.TargetId);
            Assert.True(s.IsLiveEditable("hudPercent"));
        }

        [Fact]
        public void BackgroundColorIsShownOnlyForTheColorBackground()
        {
            var p = Assert.IsType<ColorParam>(S.Get("backgroundColor"));
            Assert.Equal(CommonParams.DefaultBackgroundColor, p.DefaultValue);
            var d = S.CreateDefaults();
            Assert.False(S.IsVisible("backgroundColor", d));
            Assert.True(S.IsVisible("backgroundColor", d.With("background", ParamValue.String("color"))));
            Assert.False(S.IsVisible("backgroundColor", d.With("background", ParamValue.String("stars"))));
        }

        [Fact]
        public void OptionListsCannotBeMutatedThroughCasts()
        {
            Assert.False(CommonParams.Themes is string[]);
            Assert.False(((EnumParam)S.Get("theme")).Options is string[]);
            Assert.False(CommonParams.ForcedWinnerModes is ModeCode[]);
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
        // Release-1 cards (Р18/Р19, spec §2.9 "Физика и бюджет"): kinematic walls and VFX particles are not counted.
        [InlineData(ModeCode.M01, BodyKind.Balls, 20, 12, 8)]
        [InlineData(ModeCode.M02, BodyKind.Balls, 200, 120, 60)]
        [InlineData(ModeCode.M05, BodyKind.Balls, 1, 1, 1)]
        [InlineData(ModeCode.M04, BodyKind.Balls, 2000, 1000, 400)]
        [InlineData(ModeCode.M08, BodyKind.Balls, 120, 80, 50)]
        [InlineData(ModeCode.M09, BodyKind.Balls, 2500, 1200, 500)]
        [InlineData(ModeCode.M10, BodyKind.Balls, 20, 20, 20)]
        [InlineData(ModeCode.M11, BodyKind.Shards, 600, 350, 200)]
        [InlineData(ModeCode.M12, BodyKind.Bodies, 400, 250, 150)]
        [InlineData(ModeCode.M13, BodyKind.Balls, 40, 40, 30)]
        [InlineData(ModeCode.M14, BodyKind.Bodies, 1000, 600, 300)]
        [InlineData(ModeCode.M15, BodyKind.Bodies, 3000, 1500, 700)]
        public void SingleNumberBudgetsMatchSpecCards(ModeCode mode, BodyKind kind, int a, int b, int c)
        {
            Assert.Equal(a, BodyBudgets.Total(mode, DeviceTier.A));
            Assert.Equal(b, BodyBudgets.Total(mode, DeviceTier.B));
            Assert.Equal(c, BodyBudgets.Total(mode, DeviceTier.C));
            Assert.Equal(a, BodyBudgets.Get(mode, DeviceTier.A, kind));
            Assert.Equal(c, BodyBudgets.Get(mode, DeviceTier.C, kind));
            Assert.Equal(new TierCap(a, b, c), BodyBudgets.AsTierCap(mode, kind));
        }

        [Fact]
        public void CompositeBudgetsKeepTheirParts()
        {
            // spec §2.9 M03 "16 шаров + до 60 подвижных препятствий на всех тирах"
            Assert.Equal(new TierCap(16, 16, 16), BodyBudgets.AsTierCap(ModeCode.M03, BodyKind.Balls));
            Assert.Equal(new TierCap(60, 60, 60), BodyBudgets.AsTierCap(ModeCode.M03, BodyKind.Obstacles));
            Assert.Equal(76, BodyBudgets.Total(ModeCode.M03, DeviceTier.C));
            // Release-1 M05 Helix Drop: "1 динамический шар"; platforms are kinematic and shards are VFX particles.
            Assert.Equal(1, BodyBudgets.Get(ModeCode.M05, DeviceTier.B, BodyKind.Balls));
            Assert.Null(BodyBudgets.Get(ModeCode.M05, DeviceTier.B, BodyKind.Platforms));
            Assert.False(BodyBudgets.Has(ModeCode.M05, BodyKind.Shards));
            Assert.Equal(1, BodyBudgets.Total(ModeCode.M05, DeviceTier.B));
            // M06 "8 бойцов + оружие (сенсоры) + стрелы до 40"
            Assert.Equal(new TierCap(8, 8, 8), BodyBudgets.BallCap(ModeCode.M06));
            Assert.Equal(new TierCap(40, 40, 40), BodyBudgets.AsTierCap(ModeCode.M06, BodyKind.Projectiles));
            // M07 "32 шара + плитки до 200 / 150 / 100"
            Assert.Equal(new TierCap(32, 32, 32), BodyBudgets.BallCap(ModeCode.M07));
            Assert.Equal(new TierCap(200, 150, 100), BodyBudgets.AsTierCap(ModeCode.M07, BodyKind.Tiles));
            Assert.Null(BodyBudgets.Get(ModeCode.M07, DeviceTier.A, BodyKind.Obstacles));
            Assert.False(BodyBudgets.Has(ModeCode.M03, BodyKind.Tiles));
            Assert.Throws<System.ArgumentException>(() => BodyBudgets.AsTierCap(ModeCode.M03, BodyKind.Tiles));
        }

        [Fact]
        public void BallCapFallsBackToTheSharedBodyBudget()
        {
            Assert.Equal(new TierCap(400, 250, 150), BodyBudgets.BallCap(ModeCode.M12));
            Assert.Equal(new TierCap(1000, 600, 300), BodyBudgets.BallCap(ModeCode.M14));
            Assert.Equal(new TierCap(2000, 1000, 400), BodyBudgets.BallCap(ModeCode.M04));
            Assert.Equal(new TierCap(20, 12, 8), BodyBudgets.BallCap(ModeCode.M01));
            Assert.Throws<System.ArgumentException>(() => BodyBudgets.BallCap(ModeCode.M11));
        }

        [Fact]
        public void BudgetsApplyToCountParamsWhoseMaxIsBelowTheBudget()
        {
            // M03 ballCount 2–16 with its 16-ball budget; M07 tiles param; common ballCount 1–100 with M14's 1000 (no-op cap)
            var m03 = ParamSchema.Merge(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M03)), new ParamSchemaBuilder()
                .Add(new IntParam("ballCount", 2, 16, 1, 6).WithTierCap(BodyBudgets.BallCap(ModeCode.M03)))
                .Add(new IntParam("obstacleCount", 0, 60, 1, 20).WithTierCap(BodyBudgets.AsTierCap(ModeCode.M03, BodyKind.Obstacles)))
                .BuildLayer());
            Assert.Equal(16, ((IntParam)m03.Get("ballCount")).EffectiveMax(DeviceTier.C));
            Assert.Equal(60, ((IntParam)m03.Get("obstacleCount")).EffectiveMax(DeviceTier.C));

            var m07 = ParamSchema.Merge(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M07)), new ParamSchemaBuilder()
                .Add(new IntParam("ballCount", 4, 32, 1, 12).WithTierCap(BodyBudgets.BallCap(ModeCode.M07)))
                .Add(new IntParam("tileCount", 20, 400, 10, 120).WithTierCap(BodyBudgets.AsTierCap(ModeCode.M07, BodyKind.Tiles)))
                .BuildLayer());
            var r = TestSchemas.Set(("tileCount", ParamValue.Int(400)), ("ballCount", ParamValue.Int(32))).Sanitize(m07, DeviceTier.C);
            Assert.Equal(100, r.Values.GetInt("tileCount"));
            Assert.Equal(32, r.Values.GetInt("ballCount"));
            Assert.Contains(r.Issues, i => i.ParamId == "tileCount" && i.Code == ParamIssueCode.TierCapped);

            var m06 = ParamSchema.Merge(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M06)), new ParamSchemaBuilder()
                .Add(new IntParam("ballCount", 2, 8, 1, 4).WithTierCap(BodyBudgets.BallCap(ModeCode.M06)))
                .BuildLayer());
            Assert.Equal(8, ((IntParam)m06.Get("ballCount")).EffectiveMax(DeviceTier.C));

            var m14 = ParamSchema.Merge(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M14)), new ParamSchemaBuilder()
                .Add(new IntParam("ballCount", 1, 100, 1, 1).WithTierCap(BodyBudgets.BallCap(ModeCode.M14)))
                .BuildLayer());
            Assert.Equal(100, ((IntParam)m14.Get("ballCount")).EffectiveMax(DeviceTier.A));
            Assert.Equal(100, ((IntParam)m14.Get("ballCount")).EffectiveMax(DeviceTier.B));
            Assert.Equal(100, ((IntParam)m14.Get("ballCount")).EffectiveMax(DeviceTier.C));

            // Release-1 M01: ballCount 1–20 capped 20 / 12 / 8 (spec §2.9 "ограничение тира 20 / 12 / 8").
            var m01 = ParamSchema.Merge(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M01)), new ParamSchemaBuilder()
                .Add(new IntParam("ballCount", 1, 20, 1, 1).WithTierCap(BodyBudgets.BallCap(ModeCode.M01)))
                .BuildLayer());
            Assert.Equal(20, ((IntParam)m01.Get("ballCount")).EffectiveMax(DeviceTier.A));
            Assert.Equal(12, ((IntParam)m01.Get("ballCount")).EffectiveMax(DeviceTier.B));
            Assert.Equal(8, ((IntParam)m01.Get("ballCount")).EffectiveMax(DeviceTier.C));
        }

        [Fact]
        public void EveryModeHasNonIncreasingBudgets()
        {
            foreach (var m in ModeCatalog.All)
            {
                int a = BodyBudgets.Total(m.Code, DeviceTier.A), b = BodyBudgets.Total(m.Code, DeviceTier.B), c = BodyBudgets.Total(m.Code, DeviceTier.C);
                Assert.True(a >= b && b >= c && c > 0, m.Id);
            }
            Assert.Throws<System.ArgumentOutOfRangeException>(() => BodyBudgets.Total((ModeCode)99, DeviceTier.A));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => BodyBudgets.Get(ModeCode.M01, (DeviceTier)7, BodyKind.Balls));
        }
    }
}
