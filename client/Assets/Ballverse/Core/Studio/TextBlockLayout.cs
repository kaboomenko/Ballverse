using System;
using System.Collections.Generic;
using System.Globalization;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;

namespace Ballverse.Core.Studio
{
    /// <summary>Metrics of one glyph in pixels at <see cref="IGlyphSource.Size"/>; y grows upward from the baseline (font convention).</summary>
    public readonly struct GlyphMetrics
    {
        public float Advance { get; }
        public float MinX { get; }
        public float MaxX { get; }
        public float MinY { get; }
        public float MaxY { get; }

        public GlyphMetrics(float advance, float minX, float maxX, float minY, float maxY)
        {
            Advance = advance;
            MinX = minX;
            MaxX = maxX;
            MinY = minY;
            MaxY = maxY;
        }
    }

    /// <summary>Font metrics for <see cref="TextBlockLayout"/> (the Unity font atlas in the app, a fixed table in tests).</summary>
    public interface IGlyphSource
    {
        /// <summary>Pixel size the metrics below are given at.</summary>
        float Size { get; }
        /// <summary>Distance from the top of a line to its baseline, px at <see cref="Size"/>.</summary>
        float Ascent { get; }
        /// <summary>Distance between baselines, px at <see cref="Size"/>.</summary>
        float LineHeight { get; }
        /// <summary>Metrics of a UTF-16 code unit; false when the font has no glyph (it is then skipped).</summary>
        bool TryGetGlyph(char c, out GlyphMetrics metrics);
    }

    /// <summary>A glyph quad of a laid-out text block, in 1080×1920 reference pixels with a top-left origin.</summary>
    public readonly struct PlacedGlyph
    {
        public char Character { get; }
        /// <summary>Index of the character in the displayed text (typewriter reveal order).</summary>
        public int CharIndex { get; }
        /// <summary>Quad of the glyph's ink box.</summary>
        public FloatRect Rect { get; }
        /// <summary>Cell of the bundled colour emoji (<see cref="EmojiSet"/>) drawn here instead of a font glyph, or −1.</summary>
        public int EmojiId { get; }

        public PlacedGlyph(char character, int charIndex, FloatRect rect)
            : this(character, charIndex, rect, -1)
        {
        }

        public PlacedGlyph(char character, int charIndex, FloatRect rect, int emojiId)
        {
            Character = character;
            CharIndex = charIndex;
            Rect = rect;
            EmojiId = emojiId;
        }

        public bool IsEmoji => EmojiId >= 0;
    }

    /// <summary>An RGBA color with components 0..1 (straight alpha).</summary>
    public readonly struct StudioColor : IEquatable<StudioColor>
    {
        public float R { get; }
        public float G { get; }
        public float B { get; }
        public float A { get; }

        public StudioColor(float r, float g, float b, float a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public static readonly StudioColor White = new StudioColor(1f, 1f, 1f, 1f);
        public static readonly StudioColor Black = new StudioColor(0f, 0f, 0f, 1f);
        public static readonly StudioColor Transparent = new StudioColor(0f, 0f, 0f, 0f);

        public StudioColor WithAlpha(float a) => new StudioColor(R, G, B, a);

        /// <summary>Parses "#RRGGBB" or "#RRGGBBAA" (the project color format, spec §7.4).</summary>
        public static bool TryParse(string hex, out StudioColor color)
        {
            color = default(StudioColor);
            if (string.IsNullOrEmpty(hex) || hex[0] != '#' || (hex.Length != 7 && hex.Length != 9)) return false;
            if (!uint.TryParse(hex.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint v)) return false;
            if (hex.Length == 7) v = (v << 8) | 0xFF;
            color = new StudioColor(((v >> 24) & 0xFF) / 255f, ((v >> 16) & 0xFF) / 255f, ((v >> 8) & 0xFF) / 255f, (v & 0xFF) / 255f);
            return true;
        }

        /// <summary><see cref="TryParse"/> with a fallback.</summary>
        public static StudioColor Parse(string hex, StudioColor fallback)
        {
            return TryParse(hex, out StudioColor c) ? c : fallback;
        }

        public bool Equals(StudioColor o) => R.Equals(o.R) && G.Equals(o.G) && B.Equals(o.B) && A.Equals(o.A);
        public override bool Equals(object obj) => obj is StudioColor o && Equals(o);
        public override int GetHashCode() => unchecked((((R.GetHashCode() * 397) ^ G.GetHashCode()) * 397 ^ B.GetHashCode()) * 397 ^ A.GetHashCode());
    }

    /// <summary>A text layer laid out at the 1080×1920 reference (see <see cref="TextBlockLayout.Layout"/>).</summary>
    public sealed class TextBlock
    {
        public string Text { get; }
        public IReadOnlyList<PlacedGlyph> Glyphs { get; }
        /// <summary>Box of the lines (advance widths × line heights), reference px.</summary>
        public FloatRect TextRect { get; }
        /// <summary>Plate behind the text (empty when the layer has none).</summary>
        public FloatRect PlateRect { get; }
        public float PlateRadius { get; }
        public TextPlate Plate { get; }
        public float FontSize { get; }
        public float StrokePx { get; }
        public StudioColor Fill { get; }
        public StudioColor StrokeColor { get; }
        public bool Shadow { get; }
        /// <summary>Shadow offset, reference px (right, down).</summary>
        public FloatVec2 ShadowOffset { get; }
        /// <summary>Everything the layer can cover (text + stroke + shadow + plate): the watermark avoidance box (spec §3.10).</summary>
        public FloatRect Bounds { get; }
        /// <summary>Anchor of the block (its center), reference px; animations scale around it.</summary>
        public FloatVec2 Center { get; }

        internal TextBlock(string text, IReadOnlyList<PlacedGlyph> glyphs, FloatRect textRect, FloatRect plateRect, float plateRadius, TextPlate plate,
            float fontSize, float strokePx, StudioColor fill, StudioColor strokeColor, bool shadow, FloatVec2 shadowOffset, FloatRect bounds, FloatVec2 center)
        {
            Text = text;
            Glyphs = glyphs;
            TextRect = textRect;
            PlateRect = plateRect;
            PlateRadius = plateRadius;
            Plate = plate;
            FontSize = fontSize;
            StrokePx = strokePx;
            Fill = fill;
            StrokeColor = strokeColor;
            Shadow = shadow;
            ShadowOffset = shadowOffset;
            Bounds = bounds;
            Center = center;
        }

        /// <summary><see cref="Bounds"/> as a normalized rect for <see cref="FrameRequest.TextBounds"/>.</summary>
        public FloatRect NormalizedBounds => FloatRect.FromReferencePixels(Bounds);
    }

    /// <summary>
    /// Lays out a text layer (spec §3.5: size 12–160 px, stroke 0–8 px, shadow, plate, alignment) once at the 1080×1920 reference
    /// canvas, so preview and export draw the same block scaled to their canvas (<see cref="TextLayerGeometry"/>) and the
    /// watermark rule gets one stable box per layer (<see cref="FrameRequest.TextBounds"/>). The block is centered on the layer's
    /// position; lines are aligned inside it; lines wider than <see cref="MaxLineWidth"/> wrap at spaces (or anywhere for a single
    /// long word); the whole block is shifted to stay inside the canvas. Pure: font metrics come from an <see cref="IGlyphSource"/>.
    /// </summary>
    public static class TextBlockLayout
    {
        /// <summary>Side margin of wrapped text, reference px (5 % of the width).</summary>
        public const float SideMargin = 54f;
        /// <summary>Longest line before wrapping, reference px.</summary>
        public const float MaxLineWidth = PixelMath.ReferenceWidth - 2 * SideMargin;
        /// <summary>Plate padding as a fraction of the font size (horizontal, vertical) and corner radius.</summary>
        public const float PlatePadX = 0.35f;
        public const float PlatePadY = 0.18f;
        public const float PlateRadiusFactor = 0.3f;
        /// <summary>Shadow offset as a fraction of the font size.</summary>
        public const float ShadowOffsetFactor = 0.05f;
        /// <summary>Digits used to measure the live counter "{n}" (a wide value, so the box covers the real one).</summary>
        public const int CounterMeasureValue = 888;

        /// <summary>Lays out <paramref name="layer"/> with the displayed text <paramref name="text"/> (counter already resolved).</summary>
        /// <param name="emoji">Colour emoji drawn from the bundled atlas (spec §3.5), or null (emoji are then skipped like any glyph the font lacks).</param>
        public static TextBlock Layout(TextLayer layer, string text, IGlyphSource font, EmojiSet emoji = null)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            if (font == null) throw new ArgumentNullException(nameof(font));
            if (!(font.Size > 0f)) throw new ArgumentException("font size must be > 0", nameof(font));
            text = text ?? string.Empty;
            double[] pos = layer.Pos;
            var anchor = new FloatVec2(pos != null && pos.Length >= 2 ? Finite01(pos[0]) : 0.5f, pos != null && pos.Length >= 2 ? Finite01(pos[1]) : 0.5f);
            float strokeRef = layer.Stroke != null ? Math.Max(0, layer.Stroke.Px) : 0;
            TextPlacement placement = TextLayerGeometry.Place(PixelMath.ReferenceWidth, PixelMath.ReferenceHeight, anchor, layer.Size, strokeRef);
            float size = placement.FontSizePx;
            float stroke = placement.StrokePx;
            float k = size / font.Size;
            float lineHeight = Math.Max(1f, font.LineHeight * k);
            float ascent = font.Ascent * k;
            bool plate = layer.Plate == TextPlate.Solid || layer.Plate == TextPlate.Glass;
            float padX = plate ? size * PlatePadX : 0f;
            float padY = plate ? size * PlatePadY : 0f;
            float maxWidth = Math.Max(size, MaxLineWidth - 2 * padX - 2 * stroke);

            // 1. Break into lines (explicit newlines, then greedy wrapping at spaces).
            var lines = new List<Line>();
            int start = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                if (i == text.Length || text[i] == '\n')
                {
                    Wrap(text, start, i, font, emoji, k, size, maxWidth, lines);
                    start = i + 1;
                }
            }
            if (lines.Count == 0) lines.Add(new Line(0, 0, 0f));

            float blockWidth = 0f;
            foreach (Line l in lines) blockWidth = Math.Max(blockWidth, l.Width);
            float blockHeight = lines.Count * lineHeight;
            float left = placement.Anchor.X - blockWidth * 0.5f;
            float top = placement.Anchor.Y - blockHeight * 0.5f;

            // 2. Place glyphs.
            var glyphs = new List<PlacedGlyph>(text.Length);
            for (int li = 0; li < lines.Count; li++)
            {
                Line line = lines[li];
                float x = layer.Align == TextAlign.Left ? left : layer.Align == TextAlign.Right ? left + blockWidth - line.Width : left + (blockWidth - line.Width) * 0.5f;
                float baseline = top + li * lineHeight + ascent;
                for (int i = line.Start; i < line.End;)
                {
                    int n = Step(text, i, font, emoji, k, size, out bool has, out GlyphMetrics g, out int emojiId);
                    if (has)
                    {
                        if (g.MaxX > g.MinX && g.MaxY > g.MinY)
                        {
                            var rect = FloatRect.FromEdges(x + g.MinX, baseline - g.MaxY, x + g.MaxX, baseline - g.MinY);
                            glyphs.Add(new PlacedGlyph(text[i], i, rect, emojiId));
                        }
                        x += g.Advance;
                    }
                    i += n;
                }
            }

            var textRect = new FloatRect(left, top, blockWidth, blockHeight);
            FloatRect plateRect = plate ? FloatRect.FromEdges(left - padX, top - padY, left + blockWidth + padX, top + blockHeight + padY) : default(FloatRect);
            var shadowOffset = layer.Shadow ? new FloatVec2(size * ShadowOffsetFactor, size * ShadowOffsetFactor) : FloatVec2.Zero;
            FloatRect bounds = Union(Grow(textRect, stroke), plate ? plateRect : default(FloatRect));
            if (layer.Shadow) bounds = Union(bounds, Offset(Grow(textRect, stroke), shadowOffset.X, shadowOffset.Y));

            // 3. Keep the block inside the canvas (shift everything together).
            float dx = 0f, dy = 0f;
            if (bounds.Width <= PixelMath.ReferenceWidth)
            {
                if (bounds.X < 0) dx = -bounds.X;
                else if (bounds.Right > PixelMath.ReferenceWidth) dx = PixelMath.ReferenceWidth - bounds.Right;
            }
            if (bounds.Height <= PixelMath.ReferenceHeight)
            {
                if (bounds.Y < 0) dy = -bounds.Y;
                else if (bounds.Bottom > PixelMath.ReferenceHeight) dy = PixelMath.ReferenceHeight - bounds.Bottom;
            }
            if (dx != 0f || dy != 0f)
            {
                for (int i = 0; i < glyphs.Count; i++) glyphs[i] = new PlacedGlyph(glyphs[i].Character, glyphs[i].CharIndex, Offset(glyphs[i].Rect, dx, dy), glyphs[i].EmojiId);
                textRect = Offset(textRect, dx, dy);
                if (plate) plateRect = Offset(plateRect, dx, dy);
                bounds = Offset(bounds, dx, dy);
            }
            StudioColor fill = StudioColor.Parse(layer.Fill, StudioColor.White);
            StudioColor strokeColor = StudioColor.Parse(layer.Stroke?.Color, StudioColor.Black);
            var center = new FloatVec2(textRect.X + textRect.Width * 0.5f, textRect.Y + textRect.Height * 0.5f);
            return new TextBlock(text, glyphs.AsReadOnly(), textRect, plateRect, plate ? size * PlateRadiusFactor : 0f, plate ? layer.Plate : TextPlate.None,
                size, stroke, fill, strokeColor, layer.Shadow, shadowOffset, bounds, center);
        }

        /// <summary>The text a layer is measured with for the watermark box: the live counter replaced by a wide value.</summary>
        public static string MeasureText(TextLayer layer)
        {
            return TextLayerTiming.ResolveText(layer?.Text, CounterMeasureValue);
        }

        /// <summary>Normalized boxes of all layers (any show time) for <see cref="FrameRequest.TextBounds"/>; empty texts are skipped.</summary>
        public static IReadOnlyList<FloatRect> MeasureAll(IReadOnlyList<TextLayer> layers, Func<TextLayer, IGlyphSource> fontOf, EmojiSet emoji = null)
        {
            if (fontOf == null) throw new ArgumentNullException(nameof(fontOf));
            var list = new List<FloatRect>();
            if (layers == null) return list;
            foreach (TextLayer layer in layers)
            {
                if (layer == null || string.IsNullOrEmpty(layer.Text)) continue;
                IGlyphSource font = fontOf(layer);
                if (font == null) continue;
                TextBlock block = Layout(layer, MeasureText(layer), font, emoji);
                if (!block.Bounds.IsEmpty) list.Add(block.NormalizedBounds);
            }
            return list;
        }

        readonly struct Line
        {
            public readonly int Start;
            public readonly int End;
            public readonly float Width;

            public Line(int start, int end, float width)
            {
                Start = start;
                End = end;
                Width = width;
            }
        }

        /// <summary>
        /// One layout step at <paramref name="i"/>: a bundled colour emoji (with its variation selector / skin tone) or one UTF-16
        /// unit of the font. Returns the units consumed (≥ 1); <paramref name="metrics"/> are in reference px.
        /// </summary>
        static int Step(string text, int i, IGlyphSource font, EmojiSet emoji, float k, float size, out bool has, out GlyphMetrics metrics, out int emojiId)
        {
            if (emoji != null)
            {
                int n = emoji.Match(text, i, out emojiId);
                if (n > 0)
                {
                    has = true;
                    metrics = EmojiSet.Metrics(size);
                    return n;
                }
            }
            emojiId = -1;
            if (font.TryGetGlyph(text[i], out GlyphMetrics g))
            {
                has = true;
                metrics = new GlyphMetrics(g.Advance * k, g.MinX * k, g.MaxX * k, g.MinY * k, g.MaxY * k);
            }
            else
            {
                has = false;
                metrics = default(GlyphMetrics);
            }
            return 1;
        }

        static void Wrap(string text, int start, int end, IGlyphSource font, EmojiSet emoji, float k, float size, float maxWidth, List<Line> lines)
        {
            int lineStart = start;
            float width = 0f;
            int lastSpace = -1;
            float widthAtSpace = 0f;
            for (int i = start; i < end;)
            {
                int n = Step(text, i, font, emoji, k, size, out bool has, out GlyphMetrics g, out _);
                if (i + n > end) n = end - i;
                float adv = has ? g.Advance : 0f;
                if (text[i] == ' ')
                {
                    lastSpace = i;
                    widthAtSpace = width;
                }
                if (width + adv > maxWidth && i > lineStart)
                {
                    if (lastSpace > lineStart)
                    {
                        lines.Add(new Line(lineStart, lastSpace, widthAtSpace));
                        lineStart = lastSpace + 1;
                        width = Measure(text, lineStart, i, font, emoji, k, size);
                    }
                    else
                    {
                        lines.Add(new Line(lineStart, i, width));
                        lineStart = i;
                        width = 0f;
                    }
                    lastSpace = -1;
                }
                width += adv;
                i += n;
            }
            lines.Add(new Line(lineStart, end, Measure(text, lineStart, end, font, emoji, k, size)));
        }

        static float Measure(string text, int start, int end, IGlyphSource font, EmojiSet emoji, float k, float size)
        {
            float w = 0f;
            for (int i = start; i < end;)
            {
                i += Step(text, i, font, emoji, k, size, out bool has, out GlyphMetrics g, out _);
                if (has) w += g.Advance;
            }
            return w;
        }

        static float Finite01(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return 0.5f;
            return (float)Math.Max(0.0, Math.Min(1.0, v));
        }

        static FloatRect Grow(FloatRect r, float by)
        {
            return by <= 0f ? r : FloatRect.FromEdges(r.X - by, r.Y - by, r.Right + by, r.Bottom + by);
        }

        static FloatRect Offset(FloatRect r, float dx, float dy)
        {
            return new FloatRect(r.X + dx, r.Y + dy, r.Width, r.Height);
        }

        static FloatRect Union(FloatRect a, FloatRect b)
        {
            if (b.IsEmpty) return a;
            if (a.IsEmpty) return b;
            return FloatRect.FromEdges(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));
        }
    }
}
