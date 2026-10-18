using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>
    /// JSON form of a <see cref="RenderTheme"/> for themes delivered as data (spec §2.10 "новые темы поставляются данными",
    /// §7.6). Uses <see cref="CoreJson"/> (camelCase keys, enums as strings); colours are <c>#RRGGBB</c> / <c>#RRGGBBAA</c>.
    /// Missing optional fields take the values of <see cref="RenderThemeCatalog.Default"/>; invalid values throw
    /// <see cref="FormatException"/> with the reason.
    /// </summary>
    public static class RenderThemeJson
    {
        sealed class ThemeDto
        {
            public string Id { get; set; }
            public List<string> Palette { get; set; }
            public string Arena { get; set; }
            public string ArenaAlt { get; set; }
            public string Danger { get; set; }
            public string Accent { get; set; }
            public string Muted { get; set; }
            public string Text { get; set; }
            public string Gold { get; set; }
            public string BackgroundTop { get; set; }
            public string BackgroundBottom { get; set; }
            public BackgroundStyle? Background { get; set; }
            public string GridColor { get; set; }
            public string StarColor { get; set; }
            public BallFinish? BallFinish { get; set; }
            public float? BloomIntensity { get; set; }
            public float? BloomThreshold { get; set; }
            public float? BallEmission { get; set; }
            public float? ShapeEmission { get; set; }
            public float? TrailEmission { get; set; }
            public int? PixelGrid { get; set; }
            public float? Vignette { get; set; }
            public float? HelixBackShade { get; set; }
            public string FontKey { get; set; }
            public float? Saturation { get; set; }
            public float? Contrast { get; set; }
            public string Tint { get; set; }
        }

        public static RenderTheme Parse(string json)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            ThemeDto dto;
            try
            {
                dto = CoreJson.Deserialize<ThemeDto>(json);
            }
            catch (JsonException e)
            {
                throw new FormatException("Theme JSON is malformed: " + e.Message, e);
            }
            if (dto == null) throw new FormatException("Theme JSON is empty");
            if (string.IsNullOrEmpty(dto.Id)) throw new FormatException("Theme JSON needs an id");
            if (dto.Palette == null || dto.Palette.Count == 0 || dto.Palette.Count > RenderTheme.MaxPaletteColors)
                throw new FormatException("Theme '" + dto.Id + "' needs 1.." + RenderTheme.MaxPaletteColors + " palette colours");
            RenderTheme d = RenderThemeCatalog.Default;
            var palette = new uint[dto.Palette.Count];
            for (int i = 0; i < palette.Length; i++) palette[i] = Color(dto.Palette[i], "palette[" + i + "]", 0);
            try
            {
                return new RenderTheme(dto.Id, palette,
                    Color(dto.Arena, "arena", d.Arena), Color(dto.ArenaAlt, "arenaAlt", d.ArenaAlt), Color(dto.Danger, "danger", d.Danger),
                    Color(dto.Accent, "accent", d.Accent), Color(dto.Muted, "muted", d.Muted), Color(dto.Text, "text", d.Text),
                    Color(dto.Gold, "gold", d.Gold), Color(dto.BackgroundTop, "backgroundTop", d.BackgroundTop),
                    Color(dto.BackgroundBottom, "backgroundBottom", d.BackgroundBottom), dto.Background ?? d.Background,
                    Color(dto.GridColor, "gridColor", d.GridColor), Color(dto.StarColor, "starColor", d.StarColor),
                    dto.BallFinish ?? BallFinish.None, dto.BloomIntensity ?? d.BloomIntensity, dto.BloomThreshold ?? d.BloomThreshold,
                    dto.BallEmission ?? 1f, dto.ShapeEmission ?? 1f, dto.TrailEmission ?? 1f, dto.PixelGrid ?? 0,
                    dto.Vignette ?? 0f, dto.HelixBackShade ?? d.HelixBackShade, dto.FontKey,
                    dto.Saturation ?? 1f, dto.Contrast ?? 1f, Color(dto.Tint, "tint", ColorMath.White));
            }
            catch (ArgumentException e)
            {
                throw new FormatException("Theme '" + dto.Id + "' is invalid: " + e.Message, e);
            }
        }

        public static string ToJson(RenderTheme theme, bool indented = true)
        {
            if (theme == null) throw new ArgumentNullException(nameof(theme));
            var palette = new List<string>(theme.PaletteCount);
            foreach (uint c in theme.Palette) palette.Add(ColorMath.ToHex(c));
            var dto = new ThemeDto
            {
                Id = theme.Id,
                Palette = palette,
                Arena = ColorMath.ToHex(theme.Arena),
                ArenaAlt = ColorMath.ToHex(theme.ArenaAlt),
                Danger = ColorMath.ToHex(theme.Danger),
                Accent = ColorMath.ToHex(theme.Accent),
                Muted = ColorMath.ToHex(theme.Muted),
                Text = ColorMath.ToHex(theme.Text),
                Gold = ColorMath.ToHex(theme.Gold),
                BackgroundTop = ColorMath.ToHex(theme.BackgroundTop),
                BackgroundBottom = ColorMath.ToHex(theme.BackgroundBottom),
                Background = theme.Background,
                GridColor = ColorMath.ToHex(theme.GridColor),
                StarColor = ColorMath.ToHex(theme.StarColor),
                BallFinish = theme.BallFinish,
                BloomIntensity = theme.BloomIntensity,
                BloomThreshold = theme.BloomThreshold,
                BallEmission = theme.BallEmission,
                ShapeEmission = theme.ShapeEmission,
                TrailEmission = theme.TrailEmission,
                PixelGrid = theme.PixelGrid,
                Vignette = theme.Vignette,
                HelixBackShade = theme.HelixBackShade,
                FontKey = theme.FontKey,
                Saturation = theme.Saturation,
                Contrast = theme.Contrast,
                Tint = ColorMath.ToHex(theme.Tint),
            };
            return CoreJson.Serialize(dto, indented);
        }

        static uint Color(string text, string field, uint fallback)
        {
            if (text == null) return fallback;
            if (!ColorMath.TryParseHex(text, out uint c)) throw new FormatException("Theme colour '" + field + "' must be #RRGGBB or #RRGGBBAA, got '" + text + "'");
            return c;
        }
    }
}
