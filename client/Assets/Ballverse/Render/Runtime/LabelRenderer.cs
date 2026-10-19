using System;
using Ballverse.Core.RenderGeometry;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ballverse.Render
{
    /// <summary>
    /// Draws ball labels (names / numbers, spec §2.2 <c>labels</c>; the letter skin) as glyph quads from a dynamic font
    /// atlas (<see cref="Font.RequestCharactersInTexture(string, int, FontStyle)"/>), all labels of a frame in one mesh and
    /// one draw. Glyphs are requested at a pixel size close to the label's on-screen size (in steps), so text stays sharp
    /// in a 1080p export and cheap on a phone. Strings come from the stream's label table (no per-frame string building).
    /// </summary>
    sealed class LabelRenderer : IDisposable
    {
        const FontStyle Style = FontStyle.Bold;
        const int MinFontSize = 12;
        const int MaxFontSize = 128;

        readonly Mesh _mesh;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        Vector3[] _positions = new Vector3[256];
        Color32[] _colors = new Color32[256];
        Vector2[] _uvs = new Vector2[256];
        int[] _indices = new int[384];
        int _vertexCount;
        int _indexCount;
        Font _font;
        bool _atlasRebuilt;
        static Font s_builtinFont;
        static bool s_builtinLoaded;

        public LabelRenderer()
        {
            _mesh = new Mesh { name = "BallverseLabels", hideFlags = HideFlags.HideAndDontSave };
            _mesh.MarkDynamic();
            Font.textureRebuilt += OnFontTextureRebuilt;
        }

        void OnFontTextureRebuilt(Font font)
        {
            if (ReferenceEquals(font, _font)) _atlasRebuilt = true;
        }

        /// <summary>Unity's built-in runtime font (LegacyRuntime.ttf, Unity 2022.2+), or null when unavailable.</summary>
        static Font BuiltinFont()
        {
            if (s_builtinLoaded) return s_builtinFont;
            s_builtinLoaded = true;
            try
            {
                s_builtinFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            catch (ArgumentException)
            {
                s_builtinFont = null;
            }
            if (s_builtinFont == null) Debug.LogWarning("[Ballverse.Render] No label font: assign RenderContext.LabelFont or a font set.");
            return s_builtinFont;
        }

        public static Font ResolveFont(RenderContext ctx, RenderTheme theme)
        {
            if (ctx.LabelFont != null) return ctx.LabelFont;
            if (ctx.Fonts != null)
            {
                Font f = ctx.Fonts.Get(theme.FontKey);
                if (f != null) return f;
            }
            return BuiltinFont();
        }

        /// <summary>
        /// Builds the label mesh for this frame and records its draw. <paramref name="pixelsPerMetre"/> is the output scale,
        /// used to pick the glyph size.
        /// </summary>
        public void Draw(CommandBuffer cmd, RenderResources res, ReadOnlySpan<LabelInstance> labels, RenderContext ctx, RenderTheme theme, float pixelsPerMetre)
        {
            if (labels.Length == 0 || res.TextMaterial == null || ctx.Labels == null) return;
            Font font = ResolveFont(ctx, theme);
            if (font == null) return;
            _font = font;

            // Request every glyph first: a request may rebuild the atlas and invalidate earlier UVs.
            for (int attempt = 0; attempt < 2; attempt++)
            {
                _atlasRebuilt = false;
                for (int i = 0; i < labels.Length; i++)
                {
                    string text = ctx.Labels(labels[i].LabelId);
                    if (string.IsNullOrEmpty(text)) continue;
                    font.RequestCharactersInTexture(text, FontSize(labels[i], pixelsPerMetre), Style);
                }
                if (!_atlasRebuilt) break;
            }

            _vertexCount = 0;
            _indexCount = 0;
            for (int i = 0; i < labels.Length; i++)
            {
                string text = ctx.Labels(labels[i].LabelId);
                if (string.IsNullOrEmpty(text)) continue;
                AppendLabel(font, text, labels[i], FontSize(labels[i], pixelsPerMetre));
            }
            if (_indexCount == 0) return;

            _mesh.Clear(true);
            _mesh.SetVertices(_positions, 0, _vertexCount);
            _mesh.SetColors(_colors, 0, _vertexCount);
            _mesh.SetUVs(0, _uvs, 0, _vertexCount);
            _mesh.SetIndices(_indices, 0, _indexCount, MeshTopology.Triangles, 0, false);
            _mesh.bounds = new Bounds(Vector3.zero, new Vector3(100000f, 100000f, 100f));

            Texture atlas = font.material != null ? font.material.mainTexture : null;
            if (atlas == null) return;
            _block.SetTexture(ShaderIds.MainTex, atlas);
            cmd.DrawMesh(_mesh, Matrix4x4.identity, res.TextMaterial, 0, 0, _block);
        }

        static int FontSize(in LabelInstance label, float pixelsPerMetre)
        {
            // Font size ≈ 1.4 × cap height; quantized to steps of 8 px to share atlas entries between frames.
            float px = label.Height * pixelsPerMetre * 1.4f;
            int size = Mathf.Clamp(Mathf.CeilToInt(px / 8f) * 8, MinFontSize, MaxFontSize);
            return size;
        }

        void AppendLabel(Font font, string text, in LabelInstance label, int size)
        {
            // Measure.
            float advance = 0f;
            for (int c = 0; c < text.Length; c++)
            {
                if (font.GetCharacterInfo(text[c], out CharacterInfo info, size, Style)) advance += info.advance;
            }
            if (advance <= 0f) return;
            float metresPerPixel = label.Height * 1.4f / size;
            if (advance * metresPerPixel > label.MaxWidth) metresPerPixel = label.MaxWidth / advance;
            float x = label.X - advance * metresPerPixel * 0.5f;
            // Centre the cap height (≈ 0.7 em) on the ball centre.
            float baseline = label.Y - 0.35f * size * metresPerPixel;
            var color = new Color32((byte)(label.Color >> 24), (byte)(label.Color >> 16), (byte)(label.Color >> 8), (byte)label.Color);

            EnsureCapacity(_vertexCount + text.Length * 4, _indexCount + text.Length * 6);
            for (int c = 0; c < text.Length; c++)
            {
                if (!font.GetCharacterInfo(text[c], out CharacterInfo info, size, Style)) continue;
                float x0 = x + info.minX * metresPerPixel;
                float x1 = x + info.maxX * metresPerPixel;
                float y0 = baseline + info.minY * metresPerPixel;
                float y1 = baseline + info.maxY * metresPerPixel;
                int v = _vertexCount;
                _positions[v] = new Vector3(x0, y0, 0f);
                _positions[v + 1] = new Vector3(x1, y0, 0f);
                _positions[v + 2] = new Vector3(x1, y1, 0f);
                _positions[v + 3] = new Vector3(x0, y1, 0f);
                _uvs[v] = info.uvBottomLeft;
                _uvs[v + 1] = info.uvBottomRight;
                _uvs[v + 2] = info.uvTopRight;
                _uvs[v + 3] = info.uvTopLeft;
                _colors[v] = color;
                _colors[v + 1] = color;
                _colors[v + 2] = color;
                _colors[v + 3] = color;
                _indices[_indexCount++] = v;
                _indices[_indexCount++] = v + 1;
                _indices[_indexCount++] = v + 2;
                _indices[_indexCount++] = v;
                _indices[_indexCount++] = v + 2;
                _indices[_indexCount++] = v + 3;
                _vertexCount += 4;
                x += info.advance * metresPerPixel;
            }
        }

        void EnsureCapacity(int vertices, int indices)
        {
            if (vertices > _positions.Length)
            {
                int n = Mathf.NextPowerOfTwo(vertices);
                Array.Resize(ref _positions, n);
                Array.Resize(ref _colors, n);
                Array.Resize(ref _uvs, n);
            }
            if (indices > _indices.Length) Array.Resize(ref _indices, Mathf.NextPowerOfTwo(indices));
        }

        public void Dispose()
        {
            Font.textureRebuilt -= OnFontTextureRebuilt;
            RenderResources.Destroy(_mesh);
        }
    }
}
