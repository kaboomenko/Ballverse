using System;
using System.Globalization;

namespace Ballverse.UI.Glass
{
    /// <summary>
    /// Text colours used on glass surfaces (spec §4.5 "Текст на стекле — WCAG AA (4,5:1) в худшем случае"). Inside
    /// <c>.bv-glass</c> Theme.uss redefines every text colour token to an opaque glass value: the secondary and tertiary
    /// text and the accent / danger / success / warning text colours (<c>--color-*-text</c>; the fill tokens such as
    /// <c>--color-accent</c> are never used as text colours). The adaptive tint of the compose pass is computed for the
    /// weakest of them, so every text colour on glass keeps 4.5:1 however bright the backdrop is. Pure.
    /// </summary>
    public static class GlassTextPalette
    {
        public const string DarkPrimary = "#FFFFFF";
        public const string DarkSecondary = "#DCE1ED";
        public const string DarkTertiary = "#C4CAD8";
        public const string DarkAccentText = "#D2CCFF";
        public const string DarkDangerText = "#FFB8BF";
        public const string DarkSuccessText = "#9DF0C2";
        public const string DarkWarningText = "#FFD27A";

        public const string LightPrimary = "#14161A";
        public const string LightSecondary = "#3A3F48";
        public const string LightTertiary = "#525965";
        public const string LightAccentText = "#3B2FC9";
        public const string LightDangerText = "#A01828";
        public const string LightSuccessText = "#0A5E31";
        public const string LightWarningText = "#6E4300";

        /// <summary>
        /// Opaque control fills inside <c>.bv-glass</c> (<c>--color-control</c>, <c>--color-control-pressed</c>) for the
        /// dark, light and Contrast themes. A translucent fill under a label would be blended over the tinted glass and lift
        /// it above <see cref="BackgroundTarget"/> (UI Toolkit blends in linear space in this project, so 12% white alone
        /// adds 0.12 luminance); an opaque fill takes the backdrop out of the equation, and each of these keeps 4.5:1 with
        /// every glass text colour (<see cref="Colors"/>).
        /// </summary>
        public const string DarkControl = "#2A2F40";
        public const string DarkControlPressed = "#363C50";
        public const string DarkContrastControl = "#3A4157";
        public const string DarkContrastControlPressed = "#474F68";
        public const string LightControl = "#E3E6ED";
        public const string LightControlPressed = "#D2D6E0";
        public const string LightContrastControl = "#D6DAE3";
        public const string LightContrastControlPressed = "#CDD1DB";

        /// <summary>Fill tokens redefined (opaque) inside <c>.bv-glass</c>, in the order of <see cref="GlassFills"/>.</summary>
        public static readonly string[] FillTokenNames = { "--color-control", "--color-control-pressed" };

        /// <summary>Glass values of <see cref="FillTokenNames"/> for a theme (and the Contrast modifier).</summary>
        public static string[] GlassFills(bool darkTheme, bool contrast)
        {
            if (darkTheme) return contrast ? new[] { DarkContrastControl, DarkContrastControlPressed } : new[] { DarkControl, DarkControlPressed };
            return contrast ? new[] { LightContrastControl, LightContrastControlPressed } : new[] { LightControl, LightControlPressed };
        }

        /// <summary>
        /// Smallest opacity of a frosted glass fill (no backdrop pass, so no adaptive tint) that keeps 4.5:1 for the weakest
        /// glass text colour over the worst content behind it: white under light text (dark theme), black under dark text
        /// (light theme), whether the renderer blends in linear or in gamma space: the larger of
        /// <see cref="MinFrostedAlphaLinear"/> and <see cref="MinFrostedAlphaGamma"/>. Linear blending decides in dark theme
        /// (a dark fill over white is darker when blended in gamma space); gamma blending decides in light theme (a light
        /// fill over black is darker when blended in gamma space: #FAFAFC needs 0.825 there against 0.647 linear).
        /// Returns positive infinity when no opacity suffices.
        /// </summary>
        public static double MinFrostedAlpha(bool darkTheme, string fillHex)
        {
            return Math.Max(MinFrostedAlphaLinear(darkTheme, fillHex), MinFrostedAlphaGamma(darkTheme, fillHex));
        }

        /// <summary>
        /// <see cref="MinFrostedAlpha"/> for linear blending only: panel = (1 − α)·content + α·fill in linear light.
        /// Positive infinity when no opacity suffices.
        /// </summary>
        public static double MinFrostedAlphaLinear(bool darkTheme, string fillHex)
        {
            double fill = Luminance(fillHex);
            double target = BackgroundTarget(darkTheme);
            if (darkTheme)
            {
                // (1 − α)·1 + α·fill ≤ target
                if (fill >= target) return double.PositiveInfinity;
                return (1.0 - target) / (1.0 - fill);
            }
            // (1 − α)·0 + α·fill ≥ target
            if (fill <= 0) return double.PositiveInfinity;
            return target / fill;
        }

        /// <summary>
        /// <see cref="MinFrostedAlpha"/> for gamma blending only: each sRGB channel is (1 − α)·content + α·fill before it is
        /// linearised (<see cref="GlassContrast.GammaBlendLuminance"/>). The luminance is monotonic in α (every channel moves
        /// from the content towards the fill), so a bisection finds the bound; the value returned is never below it.
        /// Positive infinity when no opacity suffices (at α = 1 both models give the fill itself).
        /// </summary>
        public static double MinFrostedAlphaGamma(bool darkTheme, string fillHex)
        {
            ParseHex(fillHex, out double r, out double g, out double b);
            double target = BackgroundTarget(darkTheme);
            double content = darkTheme ? 1.0 : 0.0;
            bool Ok(double a)
            {
                double lum = GlassContrast.GammaBlendLuminance(content, content, content, r, g, b, a);
                return darkTheme ? lum <= target : lum >= target;
            }
            if (!Ok(1.0)) return double.PositiveInfinity;
            if (Ok(0.0)) return 0.0;
            double lo = 0.0, hi = 1.0;
            for (int i = 0; i < 60; i++)
            {
                double mid = (lo + hi) / 2;
                if (Ok(mid)) hi = mid;
                else lo = mid;
            }
            return hi;
        }

        /// <summary>The USS custom properties redefined inside <c>.bv-glass</c>, in the order of <see cref="GlassTokens"/>.</summary>
        public static readonly string[] TokenNames =
        {
            "--color-text-2", "--color-text-3", "--color-accent-text", "--color-danger-text", "--color-success-text", "--color-warning-text",
        };

        /// <summary>Glass values of <see cref="TokenNames"/> for a theme.</summary>
        public static string[] GlassTokens(bool darkTheme)
        {
            return darkTheme
                ? new[] { DarkSecondary, DarkTertiary, DarkAccentText, DarkDangerText, DarkSuccessText, DarkWarningText }
                : new[] { LightSecondary, LightTertiary, LightAccentText, LightDangerText, LightSuccessText, LightWarningText };
        }

        public static string[] Colors(bool darkTheme)
        {
            var tokens = GlassTokens(darkTheme);
            var all = new string[tokens.Length + 1];
            all[0] = darkTheme ? DarkPrimary : LightPrimary;
            Array.Copy(tokens, 0, all, 1, tokens.Length);
            return all;
        }

        /// <summary>
        /// Luminance the tint must protect: the darkest of the light text colours (dark theme) or the lightest of the dark
        /// text colours (light theme).
        /// </summary>
        public static double WeakestLuminance(bool darkTheme)
        {
            double weakest = darkTheme ? double.MaxValue : double.MinValue;
            foreach (string hex in Colors(darkTheme))
            {
                double l = Luminance(hex);
                weakest = darkTheme ? Math.Min(weakest, l) : Math.Max(weakest, l);
            }
            return weakest;
        }

        /// <summary>
        /// Backdrop luminance bound for the compose pass: the tinted background must stay at or below it (dark theme,
        /// light text) or at or above it (light theme, dark text) for 4.5:1 with the weakest text colour.
        /// </summary>
        public static double BackgroundTarget(bool darkTheme)
        {
            double text = WeakestLuminance(darkTheme);
            return darkTheme
                ? (text + 0.05) / GlassContrast.MinTextContrast - 0.05
                : GlassContrast.MinTextContrast * (text + 0.05) - 0.05;
        }

        /// <summary>Relative luminance of an opaque "#RRGGBB" colour.</summary>
        public static double Luminance(string hex)
        {
            ParseHex(hex, out double r, out double g, out double b);
            return GlassContrast.LinearLuminance(GlassContrast.SrgbToLinear(r), GlassContrast.SrgbToLinear(g), GlassContrast.SrgbToLinear(b));
        }

        /// <summary>sRGB components (0–1) of an opaque "#RRGGBB" colour.</summary>
        static void ParseHex(string hex, out double r, out double g, out double b)
        {
            if (hex == null || hex.Length != 7 || hex[0] != '#') throw new FormatException("Expected #RRGGBB: " + hex);
            r = int.Parse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            g = int.Parse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            b = int.Parse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
        }
    }
}
