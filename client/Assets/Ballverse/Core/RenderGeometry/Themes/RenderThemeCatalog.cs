using System;
using System.Collections.Generic;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>
    /// The ten release themes of spec §2.10 (ids equal the <c>theme</c> parameter options, <c>CommonParams.Themes</c>).
    /// <c>Neon</c> is the default. Themes delivered as data are registered with <see cref="Register"/> and override a
    /// built-in theme of the same id.
    /// </summary>
    public sealed class RenderThemeCatalog
    {
        public const string DefaultThemeId = "Neon";

        static readonly RenderTheme[] s_builtIn = CreateBuiltIn();

        readonly List<RenderTheme> _themes = new List<RenderTheme>();

        public RenderThemeCatalog()
        {
            _themes.AddRange(s_builtIn);
        }

        /// <summary>The built-in themes in spec order.</summary>
        public static IReadOnlyList<RenderTheme> BuiltIn => s_builtIn;

        public IReadOnlyList<RenderTheme> Themes => _themes;

        /// <summary>The built-in <c>Neon</c> theme.</summary>
        public static RenderTheme Default => s_builtIn[0];

        /// <summary>Adds a theme or replaces the one with the same id (ordinal).</summary>
        public void Register(RenderTheme theme)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            for (int i = 0; i < _themes.Count; i++)
            {
                if (string.Equals(_themes[i].Id, theme.Id, StringComparison.Ordinal))
                {
                    _themes[i] = theme;
                    return;
                }
            }
            _themes.Add(theme);
        }

        public bool TryGet(string id, out RenderTheme theme)
        {
            if (id != null)
            {
                for (int i = 0; i < _themes.Count; i++)
                {
                    if (string.Equals(_themes[i].Id, id, StringComparison.Ordinal))
                    {
                        theme = _themes[i];
                        return true;
                    }
                }
            }
            theme = null;
            return false;
        }

        /// <summary>The theme of <paramref name="id"/>, or <see cref="Default"/> for an unknown id (an old project naming a removed theme).</summary>
        public RenderTheme GetOrDefault(string id)
        {
            return TryGet(id, out RenderTheme t) ? t : Default;
        }

        static RenderTheme[] CreateBuiltIn()
        {
            return new[]
            {
                // Dark background, saturated neon balls, strong glow.
                Make("Neon", new[] { "#00F0FF", "#FF2BD6", "#39FF14", "#FFE600", "#FF6B00", "#8A2BFF", "#FF0055", "#00FF9C" },
                    arena: "#7DF9FF", arenaAlt: "#B026FF", danger: "#FF1744", accent: "#FFE600", muted: "#3A3A5A", text: "#FFFFFF",
                    gold: "#FFD54A", top: "#0B0B1A", bottom: "#1A0B2E", bg: BackgroundStyle.Gradient, grid: "#FFFFFF14", stars: "#FFFFFF",
                    finish: BallFinish.None, bloom: 1.2f, threshold: 0.9f, ballEm: 1.6f, shapeEm: 1.5f, trailEm: 1.6f,
                    pixelGrid: 0, vignette: 0.25f, backShade: 0.45f, font: "default", saturation: 1.12f, contrast: 1.05f),
                // Light background, soft pastel tones, no glow.
                Make("Pastel", new[] { "#FF9AA2", "#FFB347", "#FFE066", "#9ED9A0", "#8EC5FF", "#C3A6FF", "#FFB7D5", "#7FD1C7" },
                    arena: "#B8A9C9", arenaAlt: "#D9CDE6", danger: "#FF6F7D", accent: "#FFB347", muted: "#E4DDEA", text: "#4A3F55",
                    gold: "#F2C14E", top: "#FFF7F0", bottom: "#F1E6FF", bg: BackgroundStyle.Gradient, grid: "#4A3F5514", stars: "#FFFFFF",
                    finish: BallFinish.None, bloom: 0f, threshold: 1f, ballEm: 1f, shapeEm: 1f, trailEm: 1f,
                    pixelGrid: 0, vignette: 0f, backShade: 0.75f, font: "rounded", saturation: 0.92f, contrast: 0.95f, tint: "#FFF8F2"),
                // Black and white, high contrast, minimal. The palette itself is grey; the grade keeps saturation 1 so colours the
                // user picks (ballColors, background colour) survive (the grade runs on the whole frame).
                Make("Mono", new[] { "#FFFFFF", "#BDBDBD", "#8C8C8C", "#E0E0E0", "#A6A6A6", "#F5F5F5", "#737373", "#CFCFCF" },
                    arena: "#FFFFFF", arenaAlt: "#9E9E9E", danger: "#FFFFFF", accent: "#FFFFFF", muted: "#3D3D3D", text: "#FFFFFF",
                    gold: "#FFFFFF", top: "#000000", bottom: "#000000", bg: BackgroundStyle.Solid, grid: "#FFFFFF10", stars: "#FFFFFF",
                    finish: BallFinish.None, bloom: 0.25f, threshold: 1f, ballEm: 1f, shapeEm: 1f, trailEm: 1f,
                    pixelGrid: 0, vignette: 0f, backShade: 0.5f, font: "default", contrast: 1.12f),
                // Bright candy gradients, gloss.
                Make("Candy", new[] { "#FF4F9A", "#FFB000", "#7CE85B", "#3DD6FF", "#B66BFF", "#FF6B6B", "#FFE66D", "#4ECDC4" },
                    arena: "#FFFFFF", arenaAlt: "#FFD1E8", danger: "#E8173C", accent: "#FFE66D", muted: "#F3B8D4", text: "#5B1F48",
                    gold: "#FFC93C", top: "#FF9ECF", bottom: "#9EE7FF", bg: BackgroundStyle.Gradient, grid: "#FFFFFF20", stars: "#FFFFFF",
                    finish: BallFinish.Gloss, bloom: 0.4f, threshold: 1.1f, ballEm: 1f, shapeEm: 1f, trailEm: 1.1f,
                    pixelGrid: 0, vignette: 0.1f, backShade: 0.7f, font: "rounded", saturation: 1.1f, contrast: 1.02f, tint: "#FFF6FA"),
                // Star field, planets instead of balls.
                Make("Space", new[] { "#E8B04A", "#C1440E", "#4F7CAC", "#9FC5E8", "#D9D2C3", "#B5651D", "#6FA8DC", "#F6E3B4" },
                    arena: "#9FB4FF", arenaAlt: "#4A5A9A", danger: "#FF5A36", accent: "#FFD27F", muted: "#2A3050", text: "#E8ECFF",
                    gold: "#FFD27F", top: "#02030A", bottom: "#0B1030", bg: BackgroundStyle.Stars, grid: "#FFFFFF10", stars: "#FFFFFF",
                    finish: BallFinish.Planet, bloom: 0.7f, threshold: 0.95f, ballEm: 1.1f, shapeEm: 1.3f, trailEm: 1.3f,
                    pixelGrid: 0, vignette: 0.35f, backShade: 0.4f, font: "default", contrast: 1.08f, tint: "#F0F4FF"),
                // Blue-teal, soft highlights.
                Make("Ocean", new[] { "#00B4D8", "#90E0EF", "#48CAE4", "#FFBF69", "#0077B6", "#2EC4B6", "#CAF0F8", "#FF9F80" },
                    arena: "#CAF0F8", arenaAlt: "#48CAE4", danger: "#FF6B6B", accent: "#FFBF69", muted: "#1D5568", text: "#E0FBFC",
                    gold: "#FFD166", top: "#022B3A", bottom: "#1F7A8C", bg: BackgroundStyle.Gradient, grid: "#FFFFFF12", stars: "#E0FBFC",
                    finish: BallFinish.Gloss, bloom: 0.5f, threshold: 1f, ballEm: 1.1f, shapeEm: 1.1f, trailEm: 1.2f,
                    pixelGrid: 0, vignette: 0.2f, backShade: 0.55f, font: "rounded", saturation: 1.05f, tint: "#EAF8FF"),
                // Warm gradients.
                Make("Sunset", new[] { "#FFB347", "#FF6F61", "#FFD166", "#EF476F", "#F78C6B", "#C06C84", "#F8B195", "#7F5A83" },
                    arena: "#FFE3C2", arenaAlt: "#F8B195", danger: "#D7263D", accent: "#FFD166", muted: "#6C4A6B", text: "#FFF4E6",
                    gold: "#FFD166", top: "#2D1B4E", bottom: "#FF7E5F", bg: BackgroundStyle.Gradient, grid: "#FFFFFF14", stars: "#FFF4E6",
                    finish: BallFinish.None, bloom: 0.6f, threshold: 1f, ballEm: 1.15f, shapeEm: 1.1f, trailEm: 1.2f,
                    pixelGrid: 0, vignette: 0.2f, backShade: 0.55f, font: "default", saturation: 1.06f, contrast: 1.03f, tint: "#FFF0E0"),
                // Pixel look, 16-colour palette.
                Make("Retro", new[] { "#FF004D", "#FFA300", "#FFEC27", "#00E436", "#29ADFF", "#83769C", "#FF77A8", "#FFCCAA",
                        "#7E2553", "#008751", "#AB5236", "#C2C3C7", "#FFF1E8", "#5F574F", "#1D2B53", "#000000" },
                    arena: "#C2C3C7", arenaAlt: "#5F574F", danger: "#FF004D", accent: "#FFEC27", muted: "#5F574F", text: "#FFF1E8",
                    gold: "#FFA300", top: "#1D2B53", bottom: "#1D2B53", bg: BackgroundStyle.Grid, grid: "#29ADFF30", stars: "#FFF1E8",
                    finish: BallFinish.None, bloom: 0.3f, threshold: 1f, ballEm: 1f, shapeEm: 1f, trailEm: 1f,
                    pixelGrid: 320, vignette: 0f, backShade: 0.5f, font: "pixel", saturation: 1.05f),
                // Translucent glass balls with refraction-like rim.
                Make("Glass", new[] { "#7FDBFF", "#FF85C0", "#B9FBC0", "#FFD6A5", "#CDB4DB", "#A0C4FF", "#FDFFB6", "#9BF6FF" },
                    arena: "#DDE7FF", arenaAlt: "#8EA2C8", danger: "#FF6B8A", accent: "#FDFFB6", muted: "#39445E", text: "#F2F6FF",
                    gold: "#FFE08A", top: "#10141F", bottom: "#24304A", bg: BackgroundStyle.Blur, grid: "#FFFFFF10", stars: "#FFFFFF",
                    finish: BallFinish.Glass, bloom: 0.5f, threshold: 1f, ballEm: 1.1f, shapeEm: 1.1f, trailEm: 1.1f,
                    pixelGrid: 0, vignette: 0.2f, backShade: 0.5f, font: "default", saturation: 0.95f, tint: "#F2F6FF"),
                // High contrast, colour-blind safe (Okabe–Ito palette, spec §4.8).
                Make("Contrast", new[] { "#E69F00", "#56B4E9", "#009E73", "#F0E442", "#0072B2", "#D55E00", "#CC79A7", "#FFFFFF" },
                    arena: "#FFFFFF", arenaAlt: "#BFBFBF", danger: "#D55E00", accent: "#F0E442", muted: "#595959", text: "#FFFFFF",
                    gold: "#F0E442", top: "#000000", bottom: "#000000", bg: BackgroundStyle.Solid, grid: "#FFFFFF18", stars: "#FFFFFF",
                    finish: BallFinish.None, bloom: 0.2f, threshold: 1f, ballEm: 1f, shapeEm: 1f, trailEm: 1f,
                    pixelGrid: 0, vignette: 0f, backShade: 0.6f, font: "default", contrast: 1.1f),
            };
        }

        static RenderTheme Make(string id, string[] palette, string arena, string arenaAlt, string danger, string accent, string muted,
            string text, string gold, string top, string bottom, BackgroundStyle bg, string grid, string stars, BallFinish finish,
            float bloom, float threshold, float ballEm, float shapeEm, float trailEm, int pixelGrid, float vignette, float backShade, string font,
            float saturation = 1f, float contrast = 1f, string tint = "#FFFFFF")
        {
            var p = new uint[palette.Length];
            for (int i = 0; i < palette.Length; i++) p[i] = ColorMath.ParseHex(palette[i]);
            return new RenderTheme(id, p, ColorMath.ParseHex(arena), ColorMath.ParseHex(arenaAlt), ColorMath.ParseHex(danger),
                ColorMath.ParseHex(accent), ColorMath.ParseHex(muted), ColorMath.ParseHex(text), ColorMath.ParseHex(gold),
                ColorMath.ParseHex(top), ColorMath.ParseHex(bottom), bg, ColorMath.ParseHex(grid), ColorMath.ParseHex(stars), finish,
                bloom, threshold, ballEm, shapeEm, trailEm, pixelGrid, vignette, backShade, font,
                saturation, contrast, ColorMath.ParseHex(tint));
        }
    }
}
