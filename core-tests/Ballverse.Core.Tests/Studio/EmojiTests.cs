using System;
using System.Linq;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Xunit;

namespace Ballverse.Core.Tests.Studio
{
    /// <summary>Colour emoji in text layers (spec §3.5; QA round 1: emoji were dropped, including the template "👇").</summary>
    public class EmojiTests
    {
        static TextLayer Layer(string text, int size = 100) => new TextLayer { Id = "t1", Text = text, Size = size, Pos = new[] { 0.5, 0.5 } };

        [Fact]
        public void BundledSetHasTheTemplateEmojiAndMatchesSequences()
        {
            EmojiSet set = EmojiSet.Bundled;
            Assert.Equal(EmojiCatalog.CodePoints.Length, set.Count);
            Assert.True(set.Count <= EmojiCatalog.Columns * EmojiCatalog.Rows);
            Assert.True(set.Contains(0x1F447)); // 👇 (template "Пиши свой цвет в комментариях 👇")
            Assert.True(set.Contains(0x1F525));

            Assert.Equal(2, set.Match("\U0001F447", 0, out int id));
            Assert.Equal(Array.IndexOf(EmojiCatalog.CodePoints, 0x1F447), id);
            Assert.Equal(3, set.Match("\U0001F447️!", 0, out _));            // variation selector belongs to it
            Assert.Equal(4, set.Match("\U0001F44D\U0001F3FD", 0, out int thumb)); // skin tone drawn with the base
            Assert.Equal(Array.IndexOf(EmojiCatalog.CodePoints, 0x1F44D), thumb);
            Assert.Equal(0, set.Match("A\U0001F447", 0, out _));
            Assert.Equal(0, set.Match("\uD83D", 0, out _)); // lone surrogate
            // Text-default code points are emoji only with U+FE0F (© in plain text stays a letter).
            Assert.Equal(0, set.Match("❤", 0, out _));
            Assert.Equal(2, set.Match("❤️", 0, out _));
            Assert.Equal(1, set.Match("✅", 0, out _)); // ✅ is emoji by default
        }

        [Fact]
        public void LayoutPlacesEmojiAsSquareGlyphs()
        {
            const string text = "AB \U0001F447";
            TextBlock b = TextBlockLayout.Layout(Layer(text), text, new MonoFont(), EmojiSet.Bundled);
            Assert.Equal(3, b.Glyphs.Count); // A, B, 👇 (space has no ink)
            PlacedGlyph e = b.Glyphs[2];
            Assert.True(e.IsEmoji);
            Assert.Equal(3, e.CharIndex);
            Assert.Equal(e.Rect.Width, e.Rect.Height, 3);
            Assert.Equal(100f * EmojiSet.InkSizeEm, e.Rect.Width, 3);
            // Width: 3 × 50 + 1.15 × 100.
            Assert.Equal(150f + 100f * EmojiSet.AdvanceEm, b.TextRect.Width, 3);
            // Sits on the baseline like the letters: bottom = baseline + 12 % of the size.
            float baseline = b.Glyphs[0].Rect.Bottom;
            Assert.Equal(baseline + 100f * EmojiSet.DescentEm, e.Rect.Bottom, 3);
            Assert.False(b.Glyphs[0].IsEmoji);
        }

        [Fact]
        public void WithoutTheSetEmojiAreLeftToTheFont()
        {
            const string text = "\U0001F447";
            TextBlock b = TextBlockLayout.Layout(Layer(text), text, new MonoFont());
            Assert.All(b.Glyphs, g => Assert.False(g.IsEmoji));
        }

        [Fact]
        public void EmojiWrapAsOneUnitAndCountInTheBounds()
        {
            string text = string.Concat(Enumerable.Repeat("\U0001F525", 12)); // 12 × 115 px > 972 px: wraps, never splits a pair
            TextBlock b = TextBlockLayout.Layout(Layer(text), text, new MonoFont(), EmojiSet.Bundled);
            Assert.Equal(12, b.Glyphs.Count);
            Assert.All(b.Glyphs, g => Assert.True(g.IsEmoji && g.CharIndex % 2 == 0));
            Assert.Equal(240f, b.TextRect.Height);
            var measured = TextBlockLayout.MeasureAll(new[] { Layer(text) }, _ => new MonoFont(), EmojiSet.Bundled);
            Assert.Equal(b.NormalizedBounds, measured.Single());
        }

        [Fact]
        public void AtlasCellsAreInsideTheGrid()
        {
            EmojiSet.AtlasGlyph(0, out int x0, out int y0, out int size);
            Assert.Equal((EmojiCatalog.CellSize - EmojiCatalog.GlyphSize) / 2, x0);
            Assert.Equal(x0, y0);
            Assert.Equal(EmojiCatalog.GlyphSize, size);
            int last = EmojiCatalog.CodePoints.Length - 1;
            EmojiSet.AtlasGlyph(last, out int x, out int y, out _);
            Assert.True(x + size <= EmojiCatalog.Columns * EmojiCatalog.CellSize);
            Assert.True(y + size <= EmojiCatalog.Rows * EmojiCatalog.CellSize);
            Assert.Throws<ArgumentOutOfRangeException>(() => EmojiSet.AtlasGlyph(-1, out _, out _, out _));
            Assert.Equal(EmojiCatalog.CodePoints.Length, EmojiCatalog.CodePoints.Distinct().Count());
        }
    }
}
