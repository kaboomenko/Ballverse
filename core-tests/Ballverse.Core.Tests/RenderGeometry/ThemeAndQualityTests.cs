using System;
using System.Collections.Generic;
using Ballverse.Core.Params;
using Ballverse.Core.RenderGeometry;
using Xunit;

namespace Ballverse.Core.Tests.RenderGeometry
{
    public class ThemeAndQualityTests
    {
        [Fact]
        public void TenReleaseThemesMatchTheThemeParameterAndNeonIsDefault()
        {
            IReadOnlyList<RenderTheme> themes = RenderThemeCatalog.BuiltIn;
            Assert.Equal(CommonParams.Themes.Count, themes.Count);
            for (int i = 0; i < themes.Count; i++) Assert.Equal(CommonParams.Themes[i], themes[i].Id);
            Assert.Equal("Neon", RenderThemeCatalog.Default.Id);
            Assert.True(RenderThemeCatalog.Default.BloomIntensity >= 1f);              // strong glow
            Assert.Equal(0f, new RenderThemeCatalog().GetOrDefault("Pastel").BloomIntensity);   // no glow
            Assert.Equal(16, new RenderThemeCatalog().GetOrDefault("Retro").PaletteCount);
            Assert.True(new RenderThemeCatalog().GetOrDefault("Retro").PixelGrid > 0);
            Assert.Equal(BallFinish.Glass, new RenderThemeCatalog().GetOrDefault("Glass").BallFinish);
            Assert.Equal(BallFinish.Planet, new RenderThemeCatalog().GetOrDefault("Space").BallFinish);
            Assert.Equal(BackgroundStyle.Stars, new RenderThemeCatalog().GetOrDefault("Space").Background);
            Assert.Same(RenderThemeCatalog.Default, new RenderThemeCatalog().GetOrDefault("Removed"));
        }

        [Fact]
        public void ContrastThemeUsesTheOkabeItoPalette()
        {
            RenderTheme c = new RenderThemeCatalog().GetOrDefault("Contrast");
            Assert.Equal(ColorMath.ParseHex("#E69F00"), c.Palette[0]);
            Assert.Equal(ColorMath.ParseHex("#56B4E9"), c.Palette[1]);
            Assert.Equal(ColorMath.ParseHex("#009E73"), c.Palette[2]);
        }

        [Fact]
        public void ThemeJsonRoundTripsAndDataThemesOverride()
        {
            foreach (RenderTheme t in RenderThemeCatalog.BuiltIn)
            {
                string json = RenderThemeJson.ToJson(t);
                RenderTheme back = RenderThemeJson.Parse(json);
                Assert.Equal(json, RenderThemeJson.ToJson(back));
            }
            var catalog = new RenderThemeCatalog();
            RenderTheme custom = RenderThemeJson.Parse("{\"id\":\"Neon\",\"palette\":[\"#112233\",\"#445566AA\"],\"background\":\"Grid\",\"pixelGrid\":0}");
            catalog.Register(custom);
            Assert.Equal(10, catalog.Themes.Count);
            Assert.Same(custom, catalog.GetOrDefault("Neon"));
            Assert.Equal(0x445566AAu, custom.Palette[1]);
            Assert.Equal(BackgroundStyle.Grid, custom.Background);
            catalog.Register(RenderThemeJson.Parse("{\"id\":\"Forest\",\"palette\":[\"#228B22\"]}"));
            Assert.Equal(11, catalog.Themes.Count);
        }

        [Theory]
        [InlineData("{")]
        [InlineData("{\"palette\":[\"#000000\"]}")]
        [InlineData("{\"id\":\"X\",\"palette\":[]}")]
        [InlineData("{\"id\":\"X\",\"palette\":[\"red\"]}")]
        [InlineData("{\"id\":\"X\",\"palette\":[\"#000000\"],\"bloomIntensity\":99}")]
        [InlineData("{\"id\":\"X\",\"palette\":[\"#000000\"],\"pixelGrid\":5}")]
        [InlineData("{\"id\":\"X\",\"palette\":[\"#000000\"],\"arena\":\"#12\"}")]
        public void InvalidThemeJsonThrowsFormatException(string json)
        {
            Assert.Throws<FormatException>(() => RenderThemeJson.Parse(json));
        }

        [Fact]
        public void LookFromDefaultParamsMatchesSpecDefaults()
        {
            ParamSet defaults = CommonParams.Schema.CreateDefaults();
            LookSettings look = LookSettings.FromParams(defaults, new RenderThemeCatalog());
            Assert.Equal("Neon", look.Theme.Id);
            Assert.Equal(BallSkin.Solid, look.Skin);
            Assert.Equal(TrailStyle.Short, look.Trail);
            Assert.Equal(BackgroundOption.Theme, look.Background);
            Assert.Equal(0.5f, look.Glow, 3);
            Assert.Equal(ImpactFx.Sparks, look.Impact);
            Assert.Equal(LabelMode.Off, look.Labels);
            Assert.True(look.Hud);
            Assert.Empty(look.BallColors);
        }

        [Fact]
        public void LookReadsOverridesAndResolvesThePalette()
        {
            ParamSet p = CommonParams.Schema.CreateDefaults()
                .With(CommonParams.Theme, ParamValue.String("Mono"))
                .With(CommonParams.BallColors, ParamValue.Colors(new[] { new ColorRgba(255, 0, 0), new ColorRgba(0, 255, 0) }))
                .With(CommonParams.Trail, ParamValue.String("rainbow"))
                .With(CommonParams.ImpactFx, ParamValue.String("confetti"))
                .With(CommonParams.Labels, ParamValue.String("names"))
                .With(CommonParams.Background, ParamValue.String("stars"))
                .With(LookSettings.BreakFxParam, ParamValue.String("dissolve"));
            LookSettings look = LookSettings.FromParams(p, new RenderThemeCatalog());
            Assert.Equal("Mono", look.Theme.Id);
            Assert.Equal(TrailStyle.Rainbow, look.Trail);
            Assert.Equal(ImpactFx.Confetti, look.Impact);
            Assert.Equal(LabelMode.Names, look.Labels);
            Assert.Equal(BreakFx.Dissolve, look.Break);
            Assert.Equal(BackgroundStyle.Stars, look.EffectiveBackground);
            var table = new uint[256];
            look.ResolvePalette(table);
            Assert.Equal(0xFF0000FFu, table[0]);
            Assert.Equal(0x00FF00FFu, table[1]);
            Assert.Equal(0xFF0000FFu, table[2]);                       // cycles ballColors
            Assert.Equal(look.Theme.Palette[0], table[PaletteSlots.ThemeDecorFirst]);   // decor ignores ballColors
            Assert.Equal(look.Theme.Arena, table[PaletteSlots.Arena]);
            Assert.Equal(look.Theme.Danger, table[PaletteSlots.Danger]);
            Assert.Equal(look.Theme.Arena, table[255]);
        }

        [Fact]
        public void ColorMathHelpers()
        {
            Assert.Equal("#FF8000", ColorMath.ToHex(ColorMath.Pack(255, 128, 0)));
            Assert.Equal("#FF800080", ColorMath.ToHex(ColorMath.Pack(255, 128, 0, 128)));
            Assert.Equal(ColorMath.Pack(255, 128, 0, 64), ColorMath.WithAlpha(ColorMath.Pack(255, 128, 0, 128), 0.5f));
            Assert.Equal(ColorMath.Pack(128, 128, 128), ColorMath.Lerp(ColorMath.Black, ColorMath.White, 0.5f) & 0xFEFEFEFF | 0x000000FF);
            Assert.Equal(ColorMath.White, ColorMath.ContrastText(ColorMath.Black));
            Assert.NotEqual(ColorMath.White, ColorMath.ContrastText(ColorMath.White));
            Assert.Equal(ColorMath.Pack(255, 0, 0), ColorMath.FromHsv(0f, 1f, 1f));
            Assert.Equal(ColorMath.Pack(0, 255, 0), ColorMath.FromHsv(1f / 3f, 1f, 1f));
            Assert.Equal(ColorMath.Pack(255, 0, 0), ColorMath.FromHsv(2f, 1f, 1f));
            Assert.False(ColorMath.TryParseHex("#GG0000", out _));
        }

        // ------------------------------------------------------------------ quality

        [Fact]
        public void QualityTableMatchesSpec77()
        {
            Assert.Equal(BloomMode.Full, QualityProfile.A.Bloom);
            Assert.Equal(BloomMode.Half, QualityProfile.B.Bloom);
            Assert.Equal(BloomMode.Off, QualityProfile.C.Bloom);
            Assert.Equal(BloomMode.Full, QualityProfile.Export.Bloom);
            Assert.Equal(1f, QualityProfile.A.TrailFraction);
            Assert.Equal(0.7f, QualityProfile.B.TrailFraction);
            Assert.Equal(0.4f, QualityProfile.C.TrailFraction);
            Assert.Equal(1f, QualityProfile.Export.TrailFraction);
            Assert.InRange(QualityProfile.C.RenderScale, 0.75f, 0.85f);
            Assert.Equal(1f, QualityProfile.Export.RenderScale);
            Assert.Equal(new[] { 600, 400, 200 }, new[] { QualityProfile.A.MaxParticles, QualityProfile.B.MaxParticles, QualityProfile.C.MaxParticles });
            Assert.False(QualityProfile.Export.IsRealtime);
            Assert.Same(QualityProfile.B, QualityProfile.ForTier(DeviceTier.B));
            Assert.True(QualityProfile.C.SimplifiedGlass);
        }

        [Fact]
        public void GovernorStepsDownAfterThreeSlowSecondsOnly()
        {
            var g = new QualityGovernor(QualityLevel.A);
            for (int i = 0; i < 400; i++) Assert.False(g.AddFrame(1f / 60f));   // 60 fps: stays
            Assert.Equal(QualityLevel.A, g.Level);
            bool changed = false;
            for (int i = 0; i < 160 && !changed; i++) changed = g.AddFrame(1f / 50f);   // 50 fps for 3.2 s
            Assert.True(changed);
            Assert.Equal(QualityLevel.B, g.Level);
            for (int i = 0; i < 1000; i++) g.AddFrame(1f / 30f);
            Assert.Equal(QualityLevel.C, g.Level);
            Assert.Equal(2, g.Downgrades);
            // Hitches (> 0.5 s) restart the window instead of counting.
            var h = new QualityGovernor(QualityLevel.A);
            for (int i = 0; i < 10; i++) h.AddFrame(1f);
            Assert.Equal(QualityLevel.A, h.Level);
            Assert.Throws<ArgumentException>(() => new QualityGovernor(QualityLevel.Export));
        }
    }
}
