using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using Ballverse.UI.Glass;
using Xunit;

namespace Ballverse.UI.Tests
{
    /// <summary>
    /// QA round 5, major: making <c>--color-control</c> opaque inside <c>.bv-glass</c> gave the segmented-control track
    /// the colour of the selected segment (#2A2F40 in dark theme, ratio 1.000), so an icon-only segment's selection was
    /// invisible on glass sheets, and the selected text-layer row dropped to 1.33:1 against the glass. A selection must
    /// carry an indicator (outline or bar) that reaches WCAG 1.4.11's 3:1 against what surrounds it in every theme, on
    /// glass and off glass, and never the same colour as the track.
    /// </summary>
    public class SelectionOnGlassTests
    {
        const double MinIndicatorContrast = 3.0;

        static readonly (string Name, bool Dark, bool Contrast, bool Glass)[] Scopes =
        {
            ("dark", true, false, false),
            ("light", false, false, false),
            ("dark contrast", true, true, false),
            ("light contrast", false, true, false),
            ("dark glass", true, false, true),
            ("light glass", false, false, true),
            ("dark contrast glass", true, true, true),
            ("light contrast glass", false, true, true),
        };

        /// <summary>Custom properties seen by an element of the scope (later rules override earlier ones).</summary>
        static Dictionary<string, string> Tokens(Dictionary<string, Dictionary<string, string>> rules, bool dark, bool contrast, bool glass)
        {
            string theme = dark ? ".bv-theme-dark" : ".bv-theme-light";
            var chain = new List<string> { theme };
            if (contrast) chain.Add(theme + ".bv-contrast");
            if (glass) chain.Add(theme + " .bv-glass");
            if (contrast && glass) chain.Add(theme + ".bv-contrast .bv-glass");
            var tokens = new Dictionary<string, string>();
            foreach (string sel in chain)
            {
                Assert.True(rules.ContainsKey(sel), "missing " + sel);
                foreach (var kv in rules[sel])
                    if (kv.Key.StartsWith("--", StringComparison.Ordinal)) tokens[kv.Key] = kv.Value;
            }
            return tokens;
        }

        static string Var(Dictionary<string, Dictionary<string, string>> rules, string selector, string property)
        {
            Assert.True(rules.ContainsKey(selector), "missing " + selector);
            Assert.True(rules[selector].TryGetValue(property, out string value), selector + " lacks " + property);
            var m = Regex.Match(value, @"^var\((--[\w-]+)\)$");
            Assert.True(m.Success, selector + " " + property + " must be a theme token, is " + value);
            return m.Groups[1].Value;
        }

        static double Px(Dictionary<string, Dictionary<string, string>> rules, string selector, string property)
        {
            Assert.True(rules[selector].TryGetValue(property, out string value), selector + " lacks " + property);
            var m = Regex.Match(value, @"^([\d.]+)px$");
            Assert.True(m.Success, selector + " " + property + " = " + value);
            return double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        static double OpaqueLuminance(string value, string what)
        {
            Assert.True(Sources.TryColor(value, out double r, out double g, out double b, out double a), what + " = " + value);
            Assert.True(a >= 1.0, what + " must be opaque, is " + value);
            return GlassTextPalette.Luminance(Sources.Hex(r, g, b));
        }

        /// <summary>Luminances a (possibly translucent) fill can take over an opaque backdrop: linear and gamma blending.</summary>
        static IEnumerable<double> Composite(string fill, string backdrop)
        {
            Assert.True(Sources.TryColor(fill, out double fr, out double fg, out double fb, out double fa), fill);
            Assert.True(Sources.TryColor(backdrop, out double br, out double bg, out double bb, out double ba), backdrop);
            Assert.True(ba >= 1.0, "backdrop " + backdrop + " must be opaque");
            double fillLum = GlassTextPalette.Luminance(Sources.Hex(fr, fg, fb));
            double backLum = GlassTextPalette.Luminance(Sources.Hex(br, bg, bb));
            yield return GlassContrast.LinearBlendLuminance(backLum, fillLum, fa);
            yield return GlassContrast.GammaBlendLuminance(br, bg, bb, fr, fg, fb, fa);
        }

        /// <summary>
        /// Luminances of what lies next to a control: on glass the worst case is the compose bound (a dark theme's glass
        /// is never brighter, a light theme's never darker); off glass the page and sheet surfaces.
        /// </summary>
        static IEnumerable<(string Name, double Lum)> Surroundings(Dictionary<string, string> t, bool dark, bool glass)
        {
            if (glass)
            {
                yield return ("glass bound", GlassTextPalette.BackgroundTarget(dark));
                yield break;
            }
            foreach (string s in new[] { "--color-bg", "--color-surface", "--color-surface-2" })
                yield return (s, OpaqueLuminance(t[s], s));
        }

        /// <summary>The fill under a control: opaque on glass (checked), composited over the surfaces off glass.</summary>
        static IEnumerable<(string Name, double Lum)> FillLuminances(Dictionary<string, string> t, string token, bool glass)
        {
            if (glass)
            {
                yield return (token, OpaqueLuminance(t[token], token + " on glass"));
                yield break;
            }
            foreach (string s in new[] { "--color-bg", "--color-surface", "--color-surface-2" })
            foreach (double lum in Composite(t[token], t[s]))
                yield return (token + " over " + s, lum);
        }

        static void AssertIndicator(string scope, string what, double indicator, double surround, bool dark, bool glass, string against)
        {
            if (glass)
            {
                // The bound is the worst glass only if the indicator lies on the far side of it.
                if (dark) Assert.True(indicator > surround, scope + " " + what + " must be lighter than the glass bound");
                else Assert.True(indicator < surround, scope + " " + what + " must be darker than the glass bound");
            }
            double ratio = GlassContrast.ContrastRatio(indicator, surround);
            Assert.True(ratio >= MinIndicatorContrast, scope + " " + what + " vs " + against + ": " + ratio.ToString("F2", CultureInfo.InvariantCulture));
        }

        [Fact]
        public void TheQaCaseTheFillAloneCannotMarkTheSelectedSegmentOnDarkGlass()
        {
            // Why the selection needs an outline: on dark glass the track and the selected fill are the same colour.
            var rules = Sources.RulesBySelector();
            var t = Tokens(rules, true, false, true);
            double track = OpaqueLuminance(t[Var(rules, ".bv-segmented", "background-color")], "track");
            double selected = OpaqueLuminance(t[Var(rules, ".bv-segmented__item--selected", "background-color")], "selected fill");
            Assert.True(GlassContrast.ContrastRatio(track, selected) < MinIndicatorContrast);
        }

        [Fact]
        public void TheSelectedSegmentHasAnOutlineThatStandsOutFromTheTrackInEveryTheme()
        {
            var rules = Sources.RulesBySelector();
            string trackToken = Var(rules, ".bv-segmented", "background-color");
            string fillToken = Var(rules, ".bv-segmented__item--selected", "background-color");
            string outlineToken = Var(rules, ".bv-segmented__item--selected", "border-color");
            Assert.NotEqual(trackToken, outlineToken);
            Assert.NotEqual(fillToken, outlineToken);

            // Every item has the same border width (transparent when not selected), so selecting does not move the layout.
            double width = Px(rules, ".bv-segmented__item", "border-width");
            Assert.True(width >= 2, "outline must be at least 2px");
            Assert.False(rules[".bv-segmented__item--selected"].ContainsKey("border-width"));
            Assert.True(Sources.TryColor(rules[".bv-segmented__item"]["border-color"], out _, out _, out _, out double idleAlpha));
            Assert.Equal(0, idleAlpha);

            foreach (var (name, dark, contrast, glass) in Scopes)
            {
                var t = Tokens(rules, dark, contrast, glass);
                double outline = OpaqueLuminance(t[outlineToken], name + " outline");
                Assert.NotEqual(t[trackToken], t[outlineToken]);
                // Outside: the track (and, past its 2px padding, the glass or the surface it sits on).
                foreach (var (fill, lum) in FillLuminances(t, trackToken, glass))
                    AssertIndicator(name, "segment outline", outline, lum, dark, false, fill);
                foreach (var (around, lum) in Surroundings(t, dark, glass))
                    AssertIndicator(name, "segment outline", outline, lum, dark, glass, around);
                // Inside: the selected fill, which is opaque everywhere.
                double selectedFill = OpaqueLuminance(t[fillToken], name + " selected fill");
                AssertIndicator(name, "segment outline", outline, selectedFill, dark, false, fillToken);
            }
        }

        [Fact]
        public void TheSelectedSegmentLabelAndIconAreAccentTextOnTheOpaqueSelectedFill()
        {
            var rules = Sources.RulesBySelector();
            string fillToken = Var(rules, ".bv-segmented__item--selected", "background-color");
            string labelToken = Var(rules, ".bv-segmented__item--selected .bv-segmented__label", "color");
            Assert.Equal(labelToken, Var(rules, ".bv-segmented__item--selected .bv-segmented__icon", "color"));
            Assert.Equal("bold", rules[".bv-segmented__item--selected .bv-segmented__label"]["-unity-font-style"]);
            foreach (var (name, dark, contrast, glass) in Scopes)
            {
                var t = Tokens(rules, dark, contrast, glass);
                double label = OpaqueLuminance(t[labelToken], name + " " + labelToken);
                double fill = OpaqueLuminance(t[fillToken], name + " " + fillToken);
                double ratio = GlassContrast.ContrastRatio(label, fill);
                Assert.True(ratio >= GlassContrast.MinTextContrast, name + " selected segment label: " + ratio.ToString("F2", CultureInfo.InvariantCulture));
            }
        }

        [Fact]
        public void TheSelectedRowHasALeadingBarThatStandsOutFromItsFillAndTheGlass()
        {
            var rules = Sources.RulesBySelector();
            const string row = ".bv-row--selected";
            string fillToken = Var(rules, row, "background-color");
            string barToken = Var(rules, row, "border-left-color");
            Assert.NotEqual(fillToken, barToken);
            Assert.True(Px(rules, row, "border-left-width") >= 3, "the bar must be at least 3px wide");
            Assert.Equal("bold", rules[row + " .bv-row__label"]["-unity-font-style"]);
            foreach (var (name, dark, contrast, glass) in Scopes)
            {
                var t = Tokens(rules, dark, contrast, glass);
                double bar = OpaqueLuminance(t[barToken], name + " row bar");
                foreach (var (fill, lum) in FillLuminances(t, fillToken, glass))
                    AssertIndicator(name, "row bar", bar, lum, dark, false, fill);
                foreach (var (around, lum) in Surroundings(t, dark, glass))
                    AssertIndicator(name, "row bar", bar, lum, dark, glass, around);
            }
        }
    }
}
