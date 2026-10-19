using System;
using Ballverse.Core.Studio;
using UnityEngine;

namespace Ballverse.Studio
{
    /// <summary>
    /// The bundled colour-emoji atlas (spec §3.5: Noto Color Emoji, the same on iOS and Android): a PNG stored as
    /// <c>Resources/BallverseStudio/emoji_atlas.bytes</c> (a TextAsset, so no texture import settings are involved) decoded once
    /// with <see cref="ImageConversion.LoadImage(Texture2D, byte[], bool)"/> into an sRGB RGBA32 texture. The cell layout is Core
    /// <see cref="EmojiCatalog"/> (generated together with the PNG by <c>Studio/Tools~/make_emoji_atlas.py</c>). Main thread.
    /// </summary>
    public sealed class StudioEmojiAtlas : IDisposable
    {
        public const string ResourcePath = "BallverseStudio/emoji_atlas";

        Texture2D _texture;

        StudioEmojiAtlas(Texture2D texture)
        {
            _texture = texture;
        }

        public Texture2D Texture => _texture;

        /// <summary>The emoji the atlas holds (for <see cref="TextBlockLayout"/>).</summary>
        public EmojiSet Set => EmojiSet.Bundled;

        /// <summary>Loads the atlas, or returns null (emoji are then skipped) when the resource is missing or does not match the catalog.</summary>
        public static StudioEmojiAtlas Load()
        {
            var asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null)
            {
                Debug.LogWarning("[Studio] Emoji atlas missing (" + ResourcePath + "): colour emoji are not drawn.");
                return null;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false, false)
            {
                name = "StudioEmojiAtlas",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            bool ok = ImageConversion.LoadImage(tex, asset.bytes, true);
            Resources.UnloadAsset(asset);
            if (!ok || tex.width != EmojiCatalog.Columns * EmojiCatalog.CellSize || tex.height != EmojiCatalog.Rows * EmojiCatalog.CellSize)
            {
                Debug.LogWarning("[Studio] Emoji atlas does not match the catalog: colour emoji are not drawn.");
                StudioUnity.Destroy(tex);
                return null;
            }
            return new StudioEmojiAtlas(tex);
        }

        /// <summary>UV corners (top-left origin rows mapped to Unity's bottom-up UVs) of emoji <paramref name="id"/>.</summary>
        public void GetUv(int id, out Vector2 topLeft, out Vector2 topRight, out Vector2 bottomRight, out Vector2 bottomLeft)
        {
            EmojiSet.AtlasGlyph(id, out int x, out int y, out int size);
            float w = _texture.width, h = _texture.height;
            float u0 = x / w, u1 = (x + size) / w;
            // LoadImage puts the PNG's first row at the top (v = 1).
            float v0 = 1f - y / h, v1 = 1f - (y + size) / h;
            topLeft = new Vector2(u0, v0);
            topRight = new Vector2(u1, v0);
            bottomRight = new Vector2(u1, v1);
            bottomLeft = new Vector2(u0, v1);
        }

        public void Dispose()
        {
            StudioUnity.Destroy(_texture);
            _texture = null;
        }
    }
}
