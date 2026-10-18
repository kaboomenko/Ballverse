using System;
using System.Collections.Generic;
using Ballverse.Core.Studio;
using UnityEngine;

namespace Ballverse.Studio
{
    /// <summary>
    /// Fonts of the text layers by project font id (spec §3.5: 8 OFL fonts incl. Cyrillic, e.g. "manrope_black"). The App supplies
    /// the bundled font assets; ids it does not know fall back to <see cref="BuiltinFontProvider"/>.
    /// </summary>
    public interface IStudioFontProvider
    {
        /// <summary>Dynamic font for <paramref name="fontId"/>, or null when unknown.</summary>
        Font GetFont(string fontId);
    }

    /// <summary>
    /// Fallback fonts: Unity's built-in runtime font (<c>LegacyRuntime.ttf</c>, Latin + Cyrillic) for every id, optionally after
    /// an App provider. Never returns null unless the engine has no built-in font.
    /// </summary>
    public sealed class BuiltinFontProvider : IStudioFontProvider
    {
        readonly IStudioFontProvider _primary;
        static Font s_builtin;
        static bool s_loaded;

        public BuiltinFontProvider(IStudioFontProvider primary = null)
        {
            _primary = primary;
        }

        public Font GetFont(string fontId)
        {
            Font f = _primary?.GetFont(fontId);
            return f != null ? f : Builtin();
        }

        /// <summary>The engine's built-in font, or null.</summary>
        public static Font Builtin()
        {
            if (s_loaded) return s_builtin;
            s_loaded = true;
            try
            {
                s_builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (ArgumentException)
            {
                s_builtin = null;
            }
            if (s_builtin == null) Debug.LogWarning("[Studio] No built-in font: text layers and the watermark text are not drawn.");
            return s_builtin;
        }
    }

    /// <summary>
    /// <see cref="IGlyphSource"/> over a dynamic <see cref="Font"/> at one pixel size (Core text layout measures with it). Call
    /// <see cref="Prepare"/> with the text before the layout so the glyphs are rasterized and their metrics known.
    /// Main thread.
    /// </summary>
    public sealed class FontGlyphSource : IGlyphSource
    {
        public const FontStyle Style = FontStyle.Normal;
        readonly Font _font;
        readonly int _size;

        public FontGlyphSource(Font font, int size)
        {
            _font = font ? font : throw new ArgumentNullException(nameof(font));
            _size = Mathf.Clamp(size, 4, 512);
            float scale = font.fontSize > 0 ? (float)_size / font.fontSize : 0f;
            if (scale > 0f && font.lineHeight > 0)
            {
                Ascent = font.ascent * scale;
                LineHeight = font.lineHeight * scale;
            }
            else
            {
                Ascent = 0.8f * _size;
                LineHeight = 1.2f * _size;
            }
            if (Ascent <= 0f || Ascent > LineHeight) Ascent = 0.8f * LineHeight;
        }

        public Font Font => _font;
        public float Size => _size;
        public float Ascent { get; }
        public float LineHeight { get; }

        /// <summary>Rasterizes the characters of <paramref name="text"/> at this size.</summary>
        public void Prepare(string text)
        {
            if (!string.IsNullOrEmpty(text)) _font.RequestCharactersInTexture(text, _size, Style);
        }

        public bool TryGetGlyph(char c, out GlyphMetrics metrics)
        {
            if (c == '\n' || char.IsSurrogate(c) || !_font.GetCharacterInfo(c, out CharacterInfo info, _size, Style))
            {
                metrics = default(GlyphMetrics);
                return false;
            }
            metrics = new GlyphMetrics(info.advance, info.minX, info.maxX, info.minY, info.maxY);
            return true;
        }
    }

    /// <summary>Caches one <see cref="FontGlyphSource"/> per (font, size).</summary>
    internal sealed class GlyphSourceCache
    {
        readonly Dictionary<long, FontGlyphSource> _cache = new Dictionary<long, FontGlyphSource>();

        public FontGlyphSource Get(Font font, int size)
        {
            long key = ((long)font.GetInstanceID() << 16) ^ (uint)size;
            if (!_cache.TryGetValue(key, out FontGlyphSource src))
            {
                src = new FontGlyphSource(font, size);
                _cache[key] = src;
            }
            return src;
        }

        public void Clear() => _cache.Clear();
    }
}
