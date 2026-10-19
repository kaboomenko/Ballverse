#!/usr/bin/env python3
"""Builds the studio's bundled colour-emoji atlas (spec §3.5: built-in Noto Color Emoji set, same look on iOS and Android).

Renders a curated set of single-code-point emoji from Noto Color Emoji (SIL OFL 1.1, Google) into a PNG grid and writes:
  - Studio/Resources/BallverseStudio/emoji_atlas.bytes   (PNG bytes, loaded with ImageConversion.LoadImage: no import settings)
  - Core/Studio/EmojiCatalog.cs                           (code points in atlas order + grid constants, used by text layout)
Usage: python3 make_emoji_atlas.py [path/to/NotoColorEmoji.ttf]   (Pillow >= 9; Ubuntu: fonts-noto-color-emoji)
Skin-tone modifiers and variation selectors are matched and drawn with the base emoji; ZWJ sequences draw their parts.
"""
import io, os, sys
from PIL import Image, ImageDraw, ImageFont

FONT = sys.argv[1] if len(sys.argv) > 1 else '/usr/share/fonts/truetype/noto/NotoColorEmoji.ttf'
CELL = 64        # atlas cell, px
GLYPH = 56       # emoji size inside the cell (4 px transparent border against bleeding)
COLUMNS = 20

def r(a, b):
    return list(range(a, b + 1))

SET = []
SET += r(0x1F600, 0x1F64F)                      # smileys, gestures (🙏 🙌 …)
SET += r(0x1F910, 0x1F92F) + r(0x1F970, 0x1F97A) + [0x1F9D0, 0x1FAE0, 0x1FAE1, 0x1FAE2, 0x1FAE3]
SET += [0x1F44A, 0x1F44B, 0x1F44C, 0x1F44D, 0x1F44E, 0x1F44F, 0x1F450, 0x1F446, 0x1F447, 0x1F448, 0x1F449, 0x261D, 0x270A,
        0x270B, 0x270C, 0x270D, 0x1F4AA, 0x1F590, 0x1F595, 0x1F596, 0x1F918, 0x1F919, 0x1F91A, 0x1F91B, 0x1F91C, 0x1F91D,
        0x1F91E, 0x1F91F, 0x1F90C, 0x1F90F, 0x1FAF0, 0x1FAF5, 0x1FAF6, 0x1F937, 0x1F926, 0x1F64B, 0x1F646, 0x1F645, 0x1F481,
        0x1F440, 0x1F441, 0x1F445, 0x1F444, 0x1F9E0, 0x1F480, 0x2620, 0x1F47B, 0x1F47D, 0x1F47E, 0x1F916, 0x1F4A9, 0x1F921,
        0x1F479, 0x1F47A, 0x1F47F, 0x1F608]
SET += [0x2764, 0x1F9E1, 0x1F49B, 0x1F49A, 0x1F499, 0x1F49C, 0x1F5A4, 0x1F90D, 0x1F90E, 0x1F494, 0x2763, 0x1F495, 0x1F49E,
        0x1F493, 0x1F497, 0x1F496, 0x1F498, 0x1F49D, 0x1F49F, 0x1F48B, 0x1F4AF, 0x1F4A2, 0x1F4A5, 0x1F4AB, 0x1F4A6, 0x1F4A8,
        0x1F4A3, 0x1F4AC, 0x1F4AD, 0x1F4A4]
SET += [0x1F525, 0x2B50, 0x1F31F, 0x2728, 0x26A1, 0x1F308, 0x2600, 0x1F324, 0x2601, 0x1F327, 0x2744, 0x2603, 0x1F30A,
        0x1F319, 0x1F31E, 0x1F30D, 0x1FA90, 0x1F4AB, 0x2604, 0x1F300, 0x1F340, 0x1F339, 0x1F33B, 0x1F338, 0x1F335, 0x1F332]
SET += [0x1F389, 0x1F38A, 0x1F388, 0x1F381, 0x1F3C6, 0x1F947, 0x1F948, 0x1F949, 0x1F3C5, 0x1F396, 0x1F3AF, 0x1F3B2, 0x1F3AE,
        0x1F579, 0x1F3B0, 0x1F9E9, 0x26BD, 0x1F3C0, 0x1F3C8, 0x26BE, 0x1F3BE, 0x1F3D0, 0x1F3B1, 0x1F3D3, 0x1F94E, 0x1F3B3,
        0x1F3B5, 0x1F3B6, 0x1F3A4, 0x1F3A7, 0x1F3B8, 0x1F941, 0x1F3B9, 0x1F4F8, 0x1F3AC, 0x1F3A5, 0x1F4F1, 0x1F4BB, 0x1F680,
        0x1F6F8, 0x1F48E, 0x1F451, 0x1F4A1, 0x1F50B, 0x1F9F2, 0x23F0, 0x231B, 0x23F3, 0x23F1, 0x1F514, 0x1F515, 0x1F3C1,
        0x1F6A9, 0x1F6A8, 0x1F6A7, 0x1F512, 0x1F513, 0x1F511, 0x1F4B0, 0x1F4B8, 0x1FA99, 0x1F4C8, 0x1F4C9, 0x1F4CC, 0x1F4CD,
        0x1F9F1, 0x1F9CA, 0x1F36D, 0x1F36C, 0x1F369, 0x1F36A, 0x1F382, 0x1F354, 0x1F355, 0x1F37F, 0x1F34E, 0x1F34A, 0x1F34B,
        0x1F349, 0x1F347, 0x1F353, 0x1F352, 0x1F951, 0x1F336, 0x1F37E, 0x1F964, 0x2615]
SET += [0x1F431, 0x1F436, 0x1F98A, 0x1F43B, 0x1F43C, 0x1F428, 0x1F42F, 0x1F981, 0x1F438, 0x1F435, 0x1F648, 0x1F649, 0x1F64A,
        0x1F414, 0x1F427, 0x1F426, 0x1F985, 0x1F989, 0x1F984, 0x1F41D, 0x1F98B, 0x1F40C, 0x1F419, 0x1F420, 0x1F42C, 0x1F433,
        0x1F988, 0x1F40D, 0x1F422, 0x1F996, 0x1F409]
SET += [0x2B06, 0x2B07, 0x2B05, 0x27A1, 0x2197, 0x2198, 0x2199, 0x2196, 0x2195, 0x2194, 0x21A9, 0x21AA, 0x1F504, 0x1F503,
        0x1F53A, 0x1F53B, 0x1F51D, 0x1F51C, 0x1F519, 0x25B6, 0x23E9, 0x23EA, 0x23F8, 0x23F9, 0x23FA, 0x1F500, 0x1F501, 0x1F502]
SET += [0x2705, 0x274C, 0x2714, 0x2716, 0x274E, 0x2795, 0x2796, 0x2797, 0x2753, 0x2754, 0x2757, 0x2755, 0x203C, 0x2049,
        0x26A0, 0x26D4, 0x1F6AB, 0x1F4DB, 0x2B55, 0x1F198, 0x1F195, 0x1F199, 0x1F192, 0x1F193, 0x1F197, 0x1F51E, 0x2122,
        0x00A9, 0x00AE, 0x1F4E2, 0x1F4E3, 0x1F50A, 0x1F507, 0x1F440]
SET += r(0x1F534, 0x1F535) + r(0x1F7E0, 0x1F7EB) + [0x26AA, 0x26AB, 0x2B1B, 0x2B1C, 0x25FC, 0x25FB, 0x1F536, 0x1F537,
        0x1F538, 0x1F539, 0x1F4A0, 0x1F518, 0x1F532, 0x1F533]
SET += [0x1F3B2, 0x1F0CF, 0x1F004, 0x265F, 0x2660, 0x2665, 0x2666, 0x2663, 0x1F52E, 0x1F9FF, 0x1FA84]

seen = set()
ORDER = []
for cp in SET:
    if cp not in seen:
        seen.add(cp)
        ORDER.append(cp)

font = ImageFont.truetype(FONT, 109)   # Noto Color Emoji is a bitmap font with one strike at 109 px
cells = []
for cp in ORDER:
    ch = chr(cp)
    canvas = Image.new('RGBA', (160, 160), (0, 0, 0, 0))
    ImageDraw.Draw(canvas).text((10, 10), ch, font=font, embedded_color=True)
    box = canvas.getbbox()
    if box is None:
        raise SystemExit('no glyph for U+%04X' % cp)
    cells.append((cp, canvas.crop(box)))

rows = (len(cells) + COLUMNS - 1) // COLUMNS
atlas = Image.new('RGBA', (COLUMNS * CELL, rows * CELL), (0, 0, 0, 0))
for i, (cp, img) in enumerate(cells):
    w, h = img.size
    s = GLYPH / max(w, h)
    img = img.resize((max(1, round(w * s)), max(1, round(h * s))), Image.LANCZOS)
    x = (i % COLUMNS) * CELL + (CELL - img.size[0]) // 2
    y = (i // COLUMNS) * CELL + (CELL - img.size[1]) // 2
    atlas.alpha_composite(img, (x, y))

here = os.path.dirname(os.path.abspath(__file__))
studio = os.path.dirname(here)
core = os.path.join(os.path.dirname(studio), 'Core', 'Studio')
buf = io.BytesIO()
atlas.save(buf, 'PNG', optimize=True)
with open(os.path.join(studio, 'Resources', 'BallverseStudio', 'emoji_atlas.bytes'), 'wb') as f:
    f.write(buf.getvalue())

lines = []
for i in range(0, len(ORDER), 12):
    lines.append('            ' + ', '.join('0x%X' % cp for cp in ORDER[i:i + 12]) + ',')
cs = '''// <auto-generated> by Studio/Tools~/make_emoji_atlas.py — do not edit; rerun the script. </auto-generated>
namespace Ballverse.Core.Studio
{
    /// <summary>
    /// The bundled colour-emoji set (spec §3.5): code points in atlas order (cell i = column i %% <see cref="Columns"/>, row
    /// i / <see cref="Columns"/>, rows top-down) of <c>Resources/BallverseStudio/emoji_atlas.bytes</c>, rendered from Noto Color
    /// Emoji (SIL OFL 1.1, see <c>emoji_atlas_license.txt</c>).
    /// </summary>
    public static class EmojiCatalog
    {
        /// <summary>Atlas cell size, px.</summary>
        public const int CellSize = %d;
        /// <summary>Emoji size inside a cell, px (the rest is a transparent border).</summary>
        public const int GlyphSize = %d;
        public const int Columns = %d;
        public const int Rows = %d;

        /// <summary>Code points, atlas order.</summary>
        public static readonly int[] CodePoints =
        {
%s
        };
    }
}
''' % (CELL, GLYPH, COLUMNS, rows, '\n'.join(lines))
with open(os.path.join(core, 'EmojiCatalog.cs'), 'w', encoding='utf-8') as f:
    f.write(cs)
print('%d emoji, atlas %dx%d, %d bytes' % (len(ORDER), atlas.size[0], atlas.size[1], len(buf.getvalue())))
