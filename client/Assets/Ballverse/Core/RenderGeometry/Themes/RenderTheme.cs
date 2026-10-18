using System;
using System.Collections.Generic;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>Procedural background of the simulation slot (spec §2.2 <c>background</c>, §2.10).</summary>
    public enum BackgroundStyle : byte
    {
        Solid = 0,
        /// <summary>Vertical gradient top → bottom.</summary>
        Gradient = 1,
        /// <summary>Gradient plus a 1 m world-space grid that moves with the camera.</summary>
        Grid = 2,
        /// <summary>Gradient plus a deterministic twinkling star field.</summary>
        Stars = 3,
        /// <summary>
        /// Soft out-of-focus colour blobs over the gradient. The simulation renderer has no access to the user video, so
        /// "blur" is a bokeh look; the compositor may still place a blurred video behind a transparent slot.
        /// </summary>
        Blur = 4,
    }

    /// <summary>Surface finish applied to "solid" balls by the theme (spec §2.10: Candy gloss, Glass, Space planets).</summary>
    public enum BallFinish : byte
    {
        None = 0,
        Gloss = 1,
        /// <summary>Translucent glass with rim and highlight (simplified to translucency on tier C, spec §2.10).</summary>
        Glass = 2,
        /// <summary>Banded planet shading (Space theme: "planets instead of balls").</summary>
        Planet = 3,
    }

    /// <summary>
    /// A visual theme (spec §2.10): ball palette, background, arena colour, glow and finish. Immutable. Colours are packed
    /// 0xRRGGBBAA sRGB (<see cref="ColorMath"/>). Built-in themes come from <see cref="RenderThemeCatalog"/>; new themes
    /// arrive as JSON (<see cref="RenderThemeJson"/>) without an app update.
    /// </summary>
    public sealed class RenderTheme
    {
        public const int MaxPaletteColors = 32;

        readonly uint[] _palette;

        public RenderTheme(string id, IReadOnlyList<uint> palette, uint arena, uint arenaAlt, uint danger, uint accent, uint muted,
            uint text, uint gold, uint backgroundTop, uint backgroundBottom, BackgroundStyle background, uint gridColor, uint starColor,
            BallFinish ballFinish, float bloomIntensity, float bloomThreshold, float ballEmission, float shapeEmission,
            float trailEmission, int pixelGrid, float vignette, float helixBackShade, string fontKey,
            float saturation = 1f, float contrast = 1f, uint tint = ColorMath.White)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Theme id is required", nameof(id));
            if (palette == null || palette.Count == 0 || palette.Count > MaxPaletteColors)
                throw new ArgumentException("Theme palette needs 1.." + MaxPaletteColors + " colours", nameof(palette));
            Id = id;
            _palette = new uint[palette.Count];
            for (int i = 0; i < palette.Count; i++) _palette[i] = palette[i];
            Arena = arena;
            ArenaAlt = arenaAlt;
            Danger = danger;
            Accent = accent;
            Muted = muted;
            Text = text;
            Gold = gold;
            BackgroundTop = backgroundTop;
            BackgroundBottom = backgroundBottom;
            Background = background;
            GridColor = gridColor;
            StarColor = starColor;
            BallFinish = ballFinish;
            BloomIntensity = Check(bloomIntensity, 0f, 4f, nameof(bloomIntensity));
            BloomThreshold = Check(bloomThreshold, 0f, 8f, nameof(bloomThreshold));
            BallEmission = Check(ballEmission, 0f, 8f, nameof(ballEmission));
            ShapeEmission = Check(shapeEmission, 0f, 8f, nameof(shapeEmission));
            TrailEmission = Check(trailEmission, 0f, 8f, nameof(trailEmission));
            if (pixelGrid != 0 && (pixelGrid < 64 || pixelGrid > 2160)) throw new ArgumentOutOfRangeException(nameof(pixelGrid));
            PixelGrid = pixelGrid;
            Vignette = Check(vignette, 0f, 1f, nameof(vignette));
            HelixBackShade = Check(helixBackShade, 0f, 1f, nameof(helixBackShade));
            FontKey = string.IsNullOrEmpty(fontKey) ? "default" : fontKey;
            Saturation = Check(saturation, MinSaturation, MaxSaturation, nameof(saturation));
            Contrast = Check(contrast, 0.5f, 2f, nameof(contrast));
            Tint = tint | 0xFFu;
        }

        public string Id { get; }

        /// <summary>Participant colours (1..32), indexed by palette indices 0…223 cycling (see <see cref="PaletteSlots"/>).</summary>
        public ReadOnlySpan<uint> Palette => _palette;

        public int PaletteCount => _palette.Length;
        public uint Arena { get; }
        public uint ArenaAlt { get; }
        public uint Danger { get; }
        public uint Accent { get; }
        public uint Muted { get; }
        public uint Text { get; }
        public uint Gold { get; }
        public uint BackgroundTop { get; }
        public uint BackgroundBottom { get; }
        /// <summary>Default background (used when the <c>background</c> parameter is "theme").</summary>
        public BackgroundStyle Background { get; }
        public uint GridColor { get; }
        public uint StarColor { get; }
        public BallFinish BallFinish { get; }
        /// <summary>Bloom strength at <c>glow</c> = 1 (the parameter scales it; 0 disables bloom for the theme).</summary>
        public float BloomIntensity { get; }
        /// <summary>HDR brightness above which pixels bloom (1 = full-white sRGB at emission 1).</summary>
        public float BloomThreshold { get; }
        /// <summary>Emission multiplier of balls (values above 1 push them over the bloom threshold).</summary>
        public float BallEmission { get; }
        /// <summary>Emission multiplier of shapes (walls, rings, platforms).</summary>
        public float ShapeEmission { get; }
        public float TrailEmission { get; }
        /// <summary>Retro pixelation: the scene is rendered with this many pixel rows (point upscaled); 0 = off.</summary>
        public int PixelGrid { get; }
        public float Vignette { get; }
        /// <summary>Brightness of the back of Helix platforms relative to the front (spec M05 "задние секторы темнее").</summary>
        public float HelixBackShade { get; }
        /// <summary>Font key for labels and HUD; the renderer maps it to a font asset ("default", "pixel", "rounded").</summary>
        public string FontKey { get; }
        /// <summary>
        /// Colour grade (spec §7.7 «цветокоррекция по теме»): saturation, 1 = unchanged, limited to
        /// [<see cref="MinSaturation"/>, <see cref="MaxSaturation"/>]. The grade runs on the whole composited frame, including
        /// colours the user chose explicitly (§2.2 <c>ballColors</c>, <c>background</c> = colour), so it may only nuance them:
        /// a theme that wants to look desaturated (Mono) does it with its palette, not with the grade.
        /// </summary>
        public float Saturation { get; }
        /// <summary>Lowest allowed <see cref="Saturation"/> (user colours keep at least 75 % of their chroma).</summary>
        public const float MinSaturation = 0.75f;
        /// <summary>Highest allowed <see cref="Saturation"/>.</summary>
        public const float MaxSaturation = 1.25f;
        /// <summary>Colour grade: contrast around mid-grey (linear 0.18), 0.5–2, 1 = unchanged.</summary>
        public float Contrast { get; }
        /// <summary>Colour grade: multiplicative tint / white balance (sRGB 0xRRGGBBFF; white = unchanged).</summary>
        public uint Tint { get; }

        /// <summary>True when the grade changes the image (the composite can skip it otherwise).</summary>
        public bool HasGrade => Saturation != 1f || Contrast != 1f || (Tint | 0xFFu) != ColorMath.White;

        public uint PaletteColor(int index)
        {
            int n = _palette.Length;
            int i = index % n;
            return _palette[i < 0 ? i + n : i];
        }

        static float Check(float v, float min, float max, string name)
        {
            if (float.IsNaN(v) || v < min || v > max) throw new ArgumentOutOfRangeException(name, name + " must be in [" + min + ", " + max + "]");
            return v;
        }
    }
}
