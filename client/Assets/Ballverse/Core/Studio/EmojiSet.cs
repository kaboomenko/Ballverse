using System;
using System.Collections.Generic;

namespace Ballverse.Core.Studio
{
    /// <summary>
    /// The colour emoji the studio draws itself (spec §3.5: a built-in set, so iOS and Android look the same and the export
    /// matches the preview) — matching in text and glyph geometry for <see cref="TextBlockLayout"/>. An emoji is one code point
    /// of the set, optionally followed by a variation selector (U+FE0F) and a skin-tone modifier (U+1F3FB…U+1F3FF), which are
    /// drawn with the base emoji. Code points whose default presentation is text (©, ™, ❤, ☝, arrows…) count as emoji only
    /// with U+FE0F, as phone keyboards send them. Zero-width joiners and stray selectors have no glyph and are skipped, so a
    /// ZWJ sequence shows its parts. Pure.
    /// </summary>
    public sealed class EmojiSet
    {
        /// <summary>Advance and ink box of an emoji as fractions of the font size (square, baseline at 12 % from the bottom).</summary>
        public const float AdvanceEm = 1.15f;
        public const float InkLeftEm = 0.075f;
        public const float InkSizeEm = 1.0f;
        public const float DescentEm = 0.12f;

        const int VariationSelector16 = 0xFE0F;
        const int SkinToneFirst = 0x1F3FB;
        const int SkinToneLast = 0x1F3FF;

        readonly Dictionary<int, int> _ids;

        /// <param name="codePoints">Code points in atlas order (index = emoji id).</param>
        public EmojiSet(IReadOnlyList<int> codePoints)
        {
            if (codePoints == null) throw new ArgumentNullException(nameof(codePoints));
            _ids = new Dictionary<int, int>(codePoints.Count);
            for (int i = 0; i < codePoints.Count; i++)
            {
                if (!_ids.ContainsKey(codePoints[i])) _ids.Add(codePoints[i], i);
            }
            Count = codePoints.Count;
        }

        /// <summary>The bundled Noto Color Emoji set (<see cref="EmojiCatalog"/>).</summary>
        public static EmojiSet Bundled { get; } = new EmojiSet(EmojiCatalog.CodePoints);

        /// <summary>Number of emoji (atlas cells).</summary>
        public int Count { get; }

        public bool Contains(int codePoint) => _ids.ContainsKey(codePoint);

        /// <summary>
        /// Length in UTF-16 units of the emoji starting at <paramref name="text"/>[<paramref name="index"/>] (base + optional
        /// U+FE0F + optional skin tone), or 0 when none starts there.
        /// </summary>
        public int Match(string text, int index, out int emojiId)
        {
            emojiId = -1;
            if (text == null || index < 0 || index >= text.Length) return 0;
            int cp = CodePointAt(text, index, out int len);
            if (cp < 0 || !_ids.TryGetValue(cp, out int id)) return 0;
            int end = index + len;
            bool selector = end < text.Length && text[end] == (char)VariationSelector16;
            if (selector) end++;
            if (!selector && !IsDefaultEmojiPresentation(cp)) return 0;
            if (end < text.Length)
            {
                int mod = CodePointAt(text, end, out int modLen);
                if (mod >= SkinToneFirst && mod <= SkinToneLast) end += modLen;
            }
            emojiId = id;
            return end - index;
        }

        /// <summary>Metrics of an emoji at <paramref name="fontSizePx"/>, px, y up from the baseline (<see cref="GlyphMetrics"/>).</summary>
        public static GlyphMetrics Metrics(float fontSizePx)
        {
            float s = Math.Max(0f, fontSizePx);
            return new GlyphMetrics(AdvanceEm * s, InkLeftEm * s, (InkLeftEm + InkSizeEm) * s, -DescentEm * s, (InkSizeEm - DescentEm) * s);
        }

        /// <summary>Pixel rect (x, y, size, size; top-left origin) of emoji <paramref name="id"/>'s glyph in the atlas.</summary>
        public static void AtlasGlyph(int id, out int x, out int y, out int size)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            int border = (EmojiCatalog.CellSize - EmojiCatalog.GlyphSize) / 2;
            x = (id % EmojiCatalog.Columns) * EmojiCatalog.CellSize + border;
            y = (id / EmojiCatalog.Columns) * EmojiCatalog.CellSize + border;
            size = EmojiCatalog.GlyphSize;
        }

        /// <summary>
        /// Unicode Emoji_Presentation=Yes: shown as emoji without U+FE0F. Everything from U+1F000 except the regional indicators
        /// and the few text-default pictographs listed, plus the BMP emoji below.
        /// </summary>
        public static bool IsDefaultEmojiPresentation(int cp)
        {
            if (cp >= 0x1F000)
            {
                if (cp >= 0x1F1E6 && cp <= 0x1F1FF) return false; // regional indicators (flags are not in the set)
                switch (cp)
                {
                    case 0x1F321: case 0x1F324: case 0x1F325: case 0x1F326: case 0x1F327: case 0x1F328: case 0x1F329: case 0x1F32A:
                    case 0x1F32B: case 0x1F32C: case 0x1F336: case 0x1F37D: case 0x1F396: case 0x1F397: case 0x1F399: case 0x1F39A:
                    case 0x1F39B: case 0x1F39E: case 0x1F39F: case 0x1F441: case 0x1F579: case 0x1F590: case 0x1F587:
                        return false;
                    default:
                        return true;
                }
            }
            switch (cp)
            {
                case 0x231A: case 0x231B: case 0x23E9: case 0x23EA: case 0x23EB: case 0x23EC: case 0x23F0: case 0x23F3: case 0x25FD:
                case 0x25FE: case 0x2614: case 0x2615: case 0x267F: case 0x2693: case 0x26A1: case 0x26AA: case 0x26AB: case 0x26BD:
                case 0x26BE: case 0x26C4: case 0x26C5: case 0x26CE: case 0x26D4: case 0x26EA: case 0x26F2: case 0x26F3: case 0x26F5:
                case 0x26FA: case 0x26FD: case 0x2705: case 0x270A: case 0x270B: case 0x2728: case 0x274C: case 0x274E: case 0x2753:
                case 0x2754: case 0x2755: case 0x2757: case 0x2795: case 0x2796: case 0x2797: case 0x27B0: case 0x27BF: case 0x2B1B:
                case 0x2B1C: case 0x2B50: case 0x2B55:
                    return true;
                default:
                    return cp >= 0x2648 && cp <= 0x2653; // zodiac
            }
        }

        static int CodePointAt(string s, int i, out int length)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                length = 2;
                return char.ConvertToUtf32(c, s[i + 1]);
            }
            length = 1;
            return char.IsSurrogate(c) ? -1 : c;
        }
    }
}
