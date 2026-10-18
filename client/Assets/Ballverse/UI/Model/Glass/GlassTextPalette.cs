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
            if (hex == null || hex.Length != 7 || hex[0] != '#') throw new FormatException("Expected #RRGGBB: " + hex);
            double r = GlassContrast.SrgbToLinear(int.Parse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);
            double g = GlassContrast.SrgbToLinear(int.Parse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);
            double b = GlassContrast.SrgbToLinear(int.Parse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0);
            return GlassContrast.LinearLuminance(r, g, b);
        }
    }
}
