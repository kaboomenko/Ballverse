using System;
using Ballverse.Core;

namespace Ballverse.UI.Glass
{
    /// <summary>How glass surfaces are drawn (spec §4.5 "Деградация", §7.7 "Стекло UI").</summary>
    public enum GlassMode
    {
        /// <summary>Tiers A/B: blurred backdrop at 1/4 resolution + refraction + edge highlight + adaptive tint.</summary>
        Full = 0,
        /// <summary>Tier C: blur at 1/8 resolution without refraction (spec §4.5); the cheap edge highlight stays.</summary>
        Simplified = 1,
        /// <summary>"Reduce Transparency" (system on iOS, own switch on Android): solid matte surfaces at 92% opacity.</summary>
        Solid = 2,
    }

    /// <summary>Parameters of the shared blur/compose pass for one frame.</summary>
    public sealed class GlassQuality
    {
        public GlassMode Mode { get; }
        /// <summary>Backdrop buffer = screen / Downsample (4 → 1/4 resolution, 8 → 1/8).</summary>
        public int Downsample { get; }
        /// <summary>Dual-filter (Kawase) down/up pairs.</summary>
        public int Iterations { get; }
        /// <summary>Sample offset multiplier of the dual filter (fine radius control).</summary>
        public float Offset { get; }
        /// <summary>Refraction strength in screen pixels (0 = off).</summary>
        public float RefractionPx { get; }
        /// <summary>Edge highlight intensity 0–1 (0 = off).</summary>
        public float Highlight { get; }
        /// <summary>Whether the blur/compose pass runs at all.</summary>
        public bool UsesBackdrop => Mode != GlassMode.Solid;

        public GlassQuality(GlassMode mode, int downsample, int iterations, float offset, float refractionPx, float highlight)
        {
            Mode = mode;
            Downsample = downsample;
            Iterations = iterations;
            Offset = offset;
            RefractionPx = refractionPx;
            Highlight = highlight;
        }
    }

    /// <summary>Tier and accessibility → glass parameters (spec §4.5 table). Pure.</summary>
    public static class GlassQualityPolicy
    {
        public const float MinBlurRadiusPx = 8f;
        public const float MaxBlurRadiusPx = 32f;
        public const float DefaultBlurRadiusPx = 20f;
        public const float MaxRefractionPx = 12f;
        public const float DefaultRefractionPx = 8f;
        /// <summary>Edge highlight intensity (all backdrop tiers; spec §4.5 drops only refraction on tier C).</summary>
        public const float DefaultHighlight = 0.35f;
        /// <summary>Opacity of solid surfaces under "Reduce Transparency".</summary>
        public const float SolidOpacity = 0.92f;

        /// <param name="blurRadiusPx">Desired blur radius in screen pixels, clamped to 8–32 (spec §4.5).</param>
        public static GlassQuality For(DeviceTier tier, bool reduceTransparency, float blurRadiusPx = DefaultBlurRadiusPx,
            float refractionPx = DefaultRefractionPx)
        {
            if (reduceTransparency) return new GlassQuality(GlassMode.Solid, 0, 0, 0f, 0f, 0f);
            float radius = Math.Max(MinBlurRadiusPx, Math.Min(MaxBlurRadiusPx, blurRadiusPx));
            float refraction = Math.Max(0f, Math.Min(MaxRefractionPx, refractionPx));
            bool simplified = tier == DeviceTier.C;
            int downsample = simplified ? 8 : 4;
            int iterations;
            float offset;
            Solve(radius, downsample, simplified ? 2 : 3, out iterations, out offset);
            return simplified
                ? new GlassQuality(GlassMode.Simplified, downsample, iterations, offset, 0f, DefaultHighlight)
                : new GlassQuality(GlassMode.Full, downsample, iterations, offset, refraction, DefaultHighlight);
        }

        /// <summary>
        /// Picks dual-filter iterations and offset so that the effective radius ≈ downsample · 2^iterations · offset / 2
        /// screen pixels matches <paramref name="radiusPx"/>, preferring fewer iterations (cost).
        /// </summary>
        public static void Solve(float radiusPx, int downsample, int maxIterations, out int iterations, out float offset)
        {
            iterations = 1;
            offset = 1f;
            for (int i = 1; i <= Math.Max(1, maxIterations); i++)
            {
                float unit = downsample * (float)Math.Pow(2, i) * 0.5f;
                float o = radiusPx / unit;
                iterations = i;
                offset = o;
                if (o <= 1.5f) break;
            }
            offset = Math.Max(0.5f, Math.Min(2f, offset));
        }

        public static float EffectiveRadiusPx(GlassQuality q)
        {
            if (q == null || !q.UsesBackdrop) return 0f;
            return q.Downsample * (float)Math.Pow(2, q.Iterations) * 0.5f * q.Offset;
        }
    }

    /// <summary>WCAG contrast math for text on tinted glass (spec §4.5 "Контраст текста"). Pure.</summary>
    public static class GlassContrast
    {
        public const double MinTextContrast = 4.5;

        /// <summary>Relative luminance of a linear-light RGB colour (components 0–1).</summary>
        public static double LinearLuminance(double r, double g, double b)
        {
            return 0.2126 * r + 0.7152 * g + 0.0722 * b;
        }

        /// <summary>sRGB-encoded component (0–1) → linear.</summary>
        public static double SrgbToLinear(double c)
        {
            return c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        public static double ContrastRatio(double luminanceA, double luminanceB)
        {
            double hi = Math.Max(luminanceA, luminanceB);
            double lo = Math.Min(luminanceA, luminanceB);
            return (hi + 0.05) / (lo + 0.05);
        }

        /// <summary>
        /// Smallest tint opacity α so that text of luminance <paramref name="textLum"/> keeps
        /// <paramref name="minRatio"/> over a backdrop of luminance <paramref name="backdropLum"/> covered by a tint of
        /// luminance <paramref name="tintLum"/>: background = (1 − α)·backdrop + α·tint. Returns 0..1 (1 when even a
        /// full tint is not enough).
        /// </summary>
        public static double RequiredTintAlpha(double backdropLum, double tintLum, double textLum, double minRatio = MinTextContrast)
        {
            if (ContrastRatio(backdropLum, textLum) >= minRatio) return 0;
            bool lightText = textLum > tintLum;
            double target = lightText ? (textLum + 0.05) / minRatio - 0.05 : minRatio * (textLum + 0.05) - 0.05;
            double denom = tintLum - backdropLum;
            if (Math.Abs(denom) < 1e-9) return 1;
            double alpha = (target - backdropLum) / denom;
            return Math.Max(0, Math.Min(1, alpha));
        }
    }
}
