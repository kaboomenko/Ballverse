using System;
using System.Globalization;

namespace Ballverse.UI.Glass
{
    /// <summary>
    /// Text colours used on glass surfaces (spec §4.5 "Текст на стекле — WCAG AA (4,5:1) в худшем случае"). On glass the
    /// secondary and tertiary text colours are opaque (Theme.uss redefines <c>--color-text-2/-3</c> inside
    /// <c>.bv-glass</c>), and the adaptive tint of the compose pass is computed for the weakest of them, so every text
    /// colour on glass keeps 4.5:1 however bright the backdrop is. Pure.
    /// </summary>
    public static class GlassTextPalette
    {
        public const string DarkPrimary = "#FFFFFF";
        public const string DarkSecondary = "#DCE1ED";
        public const string DarkTertiary = "#C4CAD8";

        public const string LightPrimary = "#14161A";
        public const string LightSecondary = "#3A3F48";
        public const string LightTertiary = "#525965";

        public static string[] Colors(bool darkTheme)
        {
            return darkTheme
                ? new[] { DarkPrimary, DarkSecondary, DarkTertiary }
                : new[] { LightPrimary, LightSecondary, LightTertiary };
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
