using System;
using Ballverse.Core;
using Ballverse.UI.Format;
using Ballverse.UI.Glass;
using Ballverse.UI.Layout;
using Ballverse.UI.Localization;
using Ballverse.UI.Theming;
using Xunit;

namespace Ballverse.UI.Tests
{
    public class SheetDetentSolverTests
    {
        static readonly float[] Detents = { 400f, 720f };

        [Fact]
        public void SnapsToNearestDetent()
        {
            Assert.Equal(0, SheetDetentSolver.Solve(Detents, 500f, 0f, true).DetentIndex);
            Assert.Equal(1, SheetDetentSolver.Solve(Detents, 600f, 0f, true).DetentIndex);
        }

        [Fact]
        public void DismissesBelowThresholdOrOnFlingFromLowest()
        {
            Assert.True(SheetDetentSolver.Solve(Detents, 200f, 0f, true).Dismiss);
            Assert.True(SheetDetentSolver.Solve(Detents, 390f, 2000f, true).Dismiss);
            Assert.False(SheetDetentSolver.Solve(Detents, 200f, 0f, false).Dismiss);
        }

        [Fact]
        public void FlingsMoveOneDetent()
        {
            var down = SheetDetentSolver.Solve(Detents, 700f, 2000f, true);
            Assert.False(down.Dismiss);
            Assert.Equal(0, down.DetentIndex);
            Assert.Equal(1, SheetDetentSolver.Solve(Detents, 420f, -2000f, true).DetentIndex);
        }

        [Fact]
        public void DragResistsAboveTheTop()
        {
            Assert.Equal(300f, SheetDetentSolver.DragHeight(400f, 100f, 720f));
            Assert.Equal(745f, SheetDetentSolver.DragHeight(720f, -100f, 720f));
            Assert.Equal(0f, SheetDetentSolver.DragHeight(100f, 500f, 720f));
        }
    }

    public class TimelineGeometryTests
    {
        const long S = TimelineGeometry.MicrosPerSecond;

        [Fact]
        public void MapsTimeAndPixels()
        {
            var g = new TimelineGeometry(10f, 30 * S, 300f);
            Assert.Equal(100f, g.ToX(10 * S), 3);
            Assert.Equal(10 * S, g.ToUs(100f));
            g.FitAll();
            Assert.Equal(10f, g.PixelsPerSecond, 3);
            Assert.Equal(30 * S, g.ClampedTime(1000f));
            Assert.Equal(0, g.ClampedTime(-5f));
        }

        [Fact]
        public void TrimKeepsOneSecondAndBounds()
        {
            var r = new TrimRange(2 * S, 10 * S);
            Assert.Equal(new TrimRange(0, 10 * S), TimelineGeometry.DragStart(r, -5 * S, 20 * S));
            Assert.Equal(new TrimRange(9 * S, 10 * S), TimelineGeometry.DragStart(r, 50 * S, 20 * S));
            Assert.Equal(new TrimRange(2 * S, 20 * S), TimelineGeometry.DragEnd(r, 50 * S, 20 * S));
            Assert.Equal(new TrimRange(2 * S, 3 * S), TimelineGeometry.DragEnd(r, -50 * S, 20 * S));
        }

        [Fact]
        public void FrameDuration()
        {
            Assert.Equal(16666, TimelineGeometry.FrameUs(60));
            Assert.Throws<ArgumentOutOfRangeException>(() => TimelineGeometry.FrameUs(0));
        }
    }

    public class ChromeVisibilityPolicyTests
    {
        [Fact]
        public void HidesOrFadesOnlyWhileRunning()
        {
            Assert.Equal(1f, ChromeVisibilityPolicy.Evaluate(SimPhaseView.Running, true, true, 1f, false).Opacity);
            var interactive = ChromeVisibilityPolicy.Evaluate(SimPhaseView.Running, true, true, 3f, false);
            Assert.Equal(0f, interactive.Opacity);
            Assert.False(interactive.Interactive);
            var auto = ChromeVisibilityPolicy.Evaluate(SimPhaseView.Running, true, false, 3f, false);
            Assert.Equal(0.4f, auto.Opacity);
            Assert.True(auto.Interactive);
            Assert.Equal(1f, ChromeVisibilityPolicy.Evaluate(SimPhaseView.Running, false, true, 9f, false).Opacity);
            Assert.Equal(1f, ChromeVisibilityPolicy.Evaluate(SimPhaseView.Finished, true, true, 9f, false).Opacity);
            Assert.Equal(1f, ChromeVisibilityPolicy.Evaluate(SimPhaseView.Running, true, true, 9f, true).Opacity);
        }
    }

    public class SafeAreaAndAspectTests
    {
        [Fact]
        public void InsetsFromEngineSafeArea()
        {
            // 1170×2532 screen, notch 141 px on top, home indicator 102 px at the bottom; panel 390 units wide.
            var i = PanelInsets.FromScreen(1170, 2532, 0, 102, 1170, 2532 - 102 - 141, 390f / 1170f);
            Assert.Equal(47f, i.Top, 2);
            Assert.Equal(34f, i.Bottom, 2);
            Assert.Equal(0f, i.Left, 2);
            Assert.Equal(0f, i.Right, 2);
        }

        [Fact]
        public void FitAndCover()
        {
            AspectFit.Fit(390, 500, 9f / 16f, out float w, out float h);
            Assert.Equal(500f, h, 2);
            Assert.Equal(281.25f, w, 2);
            AspectFit.CoverUv(9f / 16f, 9f / 19.5f, out float sx, out float sy, out float ox, out float oy);
            Assert.Equal(9f / 19.5f / (9f / 16f), sx, 4);
            Assert.Equal(1f, sy, 4);
            Assert.Equal((1f - sx) / 2f, ox, 4);
            Assert.Equal(0f, oy, 4);
        }
    }

    public class GlassPolicyTests
    {
        [Fact]
        public void TiersFollowTheSpecTable()
        {
            var a = GlassQualityPolicy.For(DeviceTier.A, false);
            Assert.Equal(GlassMode.Full, a.Mode);
            Assert.Equal(4, a.Downsample);
            Assert.True(a.RefractionPx > 0);
            var c = GlassQualityPolicy.For(DeviceTier.C, false);
            Assert.Equal(GlassMode.Simplified, c.Mode);
            Assert.Equal(8, c.Downsample);
            Assert.Equal(0f, c.RefractionPx);
            // Spec §4.5: tier C drops only refraction; the edge highlight stays.
            Assert.True(c.Highlight > 0f);
            Assert.Equal(a.Highlight, c.Highlight);
            var solid = GlassQualityPolicy.For(DeviceTier.A, true);
            Assert.Equal(GlassMode.Solid, solid.Mode);
            Assert.False(solid.UsesBackdrop);
        }

        [Theory]
        [InlineData(8f)]
        [InlineData(20f)]
        [InlineData(32f)]
        public void EffectiveRadiusIsNearTheRequest(float radius)
        {
            var q = GlassQualityPolicy.For(DeviceTier.A, false, radius);
            float r = GlassQualityPolicy.EffectiveRadiusPx(q);
            Assert.InRange(r, radius * 0.7f, radius * 1.3f);
        }

        [Fact]
        public void RequiredTintKeepsAaContrastForWhiteText()
        {
            for (double backdrop = 0; backdrop <= 1.0001; backdrop += 0.05)
            {
                double tintLum = 0.005;
                double a = GlassContrast.RequiredTintAlpha(backdrop, tintLum, 1.0);
                double bg = (1 - a) * backdrop + a * tintLum;
                Assert.True(GlassContrast.ContrastRatio(1.0, bg) >= 4.5 - 1e-6, "backdrop " + backdrop);
            }
        }

        [Fact]
        public void RequiredTintKeepsAaContrastForDarkText()
        {
            for (double backdrop = 0; backdrop <= 1.0001; backdrop += 0.05)
            {
                double tintLum = 0.9;
                double a = GlassContrast.RequiredTintAlpha(backdrop, tintLum, 0.0075);
                double bg = (1 - a) * backdrop + a * tintLum;
                Assert.True(GlassContrast.ContrastRatio(0.0075, bg) >= 4.5 - 1e-6, "backdrop " + backdrop);
            }
        }

        [Fact]
        public void NoTintNeededWhenAlreadyReadable()
        {
            Assert.Equal(0, GlassContrast.RequiredTintAlpha(0.02, 0.005, 1.0));
        }
    }

    public class AppearanceTests
    {
        [Fact]
        public void DarkByDefaultAndSystemFollowing()
        {
            var r = ResolvedAppearance.Resolve(UiPreferences.Default, SystemAppearanceState.Unknown);
            Assert.Equal(ResolvedTheme.Dark, r.Theme);
            Assert.Equal("en", r.Language);
            // Spec §4.4: dark by default even on a device in light mode; light only when the user picks it or "System".
            var lightDevice = new SystemAppearanceState(false, true, true, 1.3f, "ru-RU");
            Assert.Equal(ResolvedTheme.Dark, ResolvedAppearance.Resolve(UiPreferences.Default, lightDevice).Theme);
            Assert.Equal(ThemePreference.Dark, default(ThemePreference));
            var followSystem = new UiPreferences(ThemePreference.System, LanguagePreference.System, ReduceMotionPreference.System, false, false);
            var light = ResolvedAppearance.Resolve(followSystem, lightDevice);
            Assert.Equal(ResolvedTheme.Light, light.Theme);
            Assert.True(light.ReduceMotion);
            Assert.True(light.ReduceTransparency);
            Assert.Equal(TextScaleStep.Largest, light.TextScale);
            Assert.Equal("ru", light.Language);
        }

        [Fact]
        public void UserChoicesWin()
        {
            var prefs = new UiPreferences(ThemePreference.Dark, LanguagePreference.English, ReduceMotionPreference.Off, false, true);
            var r = ResolvedAppearance.Resolve(prefs, new SystemAppearanceState(false, true, false, 1f, "ru"));
            Assert.Equal(ResolvedTheme.Dark, r.Theme);
            Assert.Equal("en", r.Language);
            Assert.False(r.ReduceMotion);
            Assert.True(r.HighContrast);
        }

        [Theory]
        [InlineData(1.0f, TextScaleStep.Normal)]
        [InlineData(1.1f, TextScaleStep.Large)]
        [InlineData(1.3f, TextScaleStep.Largest)]
        [InlineData(2.0f, TextScaleStep.Largest)]
        public void TextSteps(float scale, TextScaleStep expected)
        {
            Assert.Equal(expected, ResolvedAppearance.StepFor(scale));
        }
    }

    public class DisplayFormatTests
    {
        [Fact]
        public void ClockAndSizes()
        {
            Assert.Equal("0:00.0", DisplayFormat.Clock(0, true));
            Assert.Equal("1:05.3", DisplayFormat.Clock(65_350_000, true));
            Assert.Equal("2:00", DisplayFormat.DurationMs(120_000));
            var en = new Localizer(new FileTables(), "en");
            var ru = new Localizer(new FileTables(), "ru");
            Assert.Equal("~25 s left", DisplayFormat.Remaining(24_100, en));
            Assert.Equal("Осталось ~2 мин", DisplayFormat.Remaining(100_000, ru));
            Assert.Equal("12 MB", DisplayFormat.Bytes(12L * 1024 * 1024, en));
            Assert.Equal("1,5 МБ", DisplayFormat.Bytes(3L * 512 * 1024, ru));
            Assert.Equal("0.5×", DisplayFormat.Multiplier(0.5, en));
            Assert.Equal("2×", DisplayFormat.Multiplier(2, en));
        }
    }
}
