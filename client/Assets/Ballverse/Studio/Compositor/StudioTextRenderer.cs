using System;
using System.Collections.Generic;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ballverse.Studio
{
    /// <summary>A text layer to draw on one frame: its reference layout, the font and the animation state.</summary>
    public readonly struct TextDraw
    {
        public TextDraw(TextBlock block, Font font, TextLayerState state)
        {
            Block = block;
            Font = font;
            State = state;
        }

        public TextBlock Block { get; }
        public Font Font { get; }
        public TextLayerState State { get; }
    }

    /// <summary>
    /// Text layers of the compositor (spec §3.5): lays each layer out once at the 1080×1920 reference with Core
    /// <see cref="TextBlockLayout"/> (the same boxes feed the watermark rule) and draws it on any canvas as glyph quads from the
    /// font's dynamic atlas, scaled by canvasWidth / 1080 — so preview and export show the same text, only sharper in the export
    /// (glyphs are rasterized at the drawn size). Stroke = the glyphs drawn around the outline radius, shadow = an offset copy,
    /// plate = rounded rect (solid dark or glass). Colour emoji are not drawn (the dynamic font atlas has no colour glyphs; see
    /// the Studio README). Main thread.
    /// </summary>
    public sealed class StudioTextRenderer : IDisposable
    {
        /// <summary>Plate colours (the project stores only the plate kind).</summary>
        public static readonly StudioColor SolidPlateColor = new StudioColor(0f, 0f, 0f, 0.6f);
        public static readonly StudioColor GlassPlateColor = new StudioColor(1f, 1f, 1f, 0.18f);
        public static readonly StudioColor ShadowColor = new StudioColor(0f, 0f, 0f, 0.5f);

        readonly IStudioFontProvider _fonts;
        readonly GlyphSourceCache _sources = new GlyphSourceCache();
        readonly Dictionary<TextLayer, CachedBlock> _blocks = new Dictionary<TextLayer, CachedBlock>();
        readonly List<Mesh> _meshes = new List<Mesh>();
        Vector3[] _positions = new Vector3[256];
        Vector2[] _uvs = new Vector2[256];
        int[] _indices = new int[384];
        int _meshCursor;
        bool _atlasRebuilt;

        sealed class CachedBlock
        {
            public string Text;
            public TextBlock Block;
        }

        public StudioTextRenderer(IStudioFontProvider fonts)
        {
            _fonts = fonts ?? new BuiltinFontProvider();
            Font.textureRebuilt += OnFontTextureRebuilt;
        }

        void OnFontTextureRebuilt(Font font)
        {
            _atlasRebuilt = true;
        }

        public Font FontOf(TextLayer layer) => _fonts.GetFont(layer?.Font);

        /// <summary>Forget cached layouts (the project changed).</summary>
        public void ClearCache()
        {
            _blocks.Clear();
        }

        /// <summary>Reference layout of <paramref name="layer"/> showing <paramref name="text"/> (cached per layer object and text).</summary>
        public TextBlock Layout(TextLayer layer, string text)
        {
            if (layer == null) throw new ArgumentNullException(nameof(layer));
            text = text ?? string.Empty;
            if (_blocks.TryGetValue(layer, out CachedBlock cached) && cached.Text == text) return cached.Block;
            Font font = FontOf(layer);
            TextBlock block;
            if (font == null)
            {
                block = TextBlockLayout.Layout(layer, string.Empty, new EmptyGlyphs());
            }
            else
            {
                FontGlyphSource source = _sources.Get(font, Mathf.Clamp(layer.Size, ProjectLimits.MinTextSize, ProjectLimits.MaxTextSize));
                source.Prepare(text);
                block = TextBlockLayout.Layout(layer, text, source);
            }
            _blocks[layer] = new CachedBlock { Text = text, Block = block };
            return block;
        }

        /// <summary>
        /// Normalized boxes of all text layers for <see cref="FrameRequest.TextBounds"/> (spec §3.10): every layer, whatever its show
        /// time, measured with the live counter at a wide value (<see cref="TextBlockLayout.MeasureText"/>).
        /// </summary>
        public IReadOnlyList<FloatRect> MeasureBounds(IReadOnlyList<TextLayer> layers)
        {
            var list = new List<FloatRect>();
            if (layers == null) return list;
            foreach (TextLayer layer in layers)
            {
                if (layer == null || string.IsNullOrEmpty(layer.Text)) continue;
                TextBlock block = Layout(layer, TextBlockLayout.MeasureText(layer));
                if (!block.Bounds.IsEmpty) list.Add(block.NormalizedBounds);
            }
            return list;
        }

        /// <summary>Starts a frame: meshes of the previous frame are reused from now on.</summary>
        public void BeginFrame()
        {
            _meshCursor = 0;
        }

        /// <summary>
        /// Rasterizes every glyph the frame needs before any mesh is built: a request may rebuild the atlas and move earlier
        /// glyphs, so all requests come first (repeated once if the atlas was rebuilt).
        /// </summary>
        /// <param name="draws">Text layers of the frame.</param>
        /// <param name="canvasScale">canvasWidth / 1080.</param>
        /// <param name="lineFont">Font of an extra single line drawn with <see cref="DrawLine"/> this frame (the watermark), or null.</param>
        /// <param name="lineText">Its text.</param>
        /// <param name="lineRect">Its rect in canvas px (sets the glyph size, <see cref="LineSize"/>).</param>
        public void PrepareGlyphs(IReadOnlyList<TextDraw> draws, float canvasScale, Font lineFont = null, string lineText = null, Rect lineRect = default(Rect))
        {
            for (int attempt = 0; attempt < 2; attempt++)
            {
                _atlasRebuilt = false;
                if (draws != null)
                {
                    for (int i = 0; i < draws.Count; i++)
                    {
                        TextDraw d = draws[i];
                        if (d.Font == null || !d.State.Visible || string.IsNullOrEmpty(d.Block.Text)) continue;
                        d.Font.RequestCharactersInTexture(d.Block.Text, RenderSize(d.Block.FontSize, canvasScale, d.State.Scale), FontGlyphSource.Style);
                    }
                }
                if (lineFont != null && !string.IsNullOrEmpty(lineText)) lineFont.RequestCharactersInTexture(lineText, LineSize(lineRect), FontStyle.Bold);
                if (!_atlasRebuilt) break;
            }
        }

        /// <summary>Glyph pixel size of a <see cref="DrawLine"/> line in <paramref name="rect"/>.</summary>
        public static int LineSize(Rect rect)
        {
            return Mathf.Clamp(Mathf.RoundToInt(rect.height), 4, 512);
        }

        /// <summary>Records one text layer into <paramref name="cmd"/> (call <see cref="PrepareGlyphs"/> for the frame first).</summary>
        public void Draw(CommandBuffer cmd, StudioDrawContext ctx, in TextDraw draw)
        {
            TextLayerState state = draw.State;
            if (!state.Visible || state.Alpha <= 0f) return;
            TextBlock block = draw.Block;
            float scale = ctx.CanvasScale;
            Vector2 center = new Vector2(block.Center.X, block.Center.Y);
            float anim = state.Scale;
            Vector2 offset = new Vector2(0f, state.OffsetYRef);

            if (block.Plate != TextPlate.None && !block.PlateRect.IsEmpty)
            {
                Rect plate = Transform(block.PlateRect, center, anim, offset, scale);
                StudioColor pc = block.Plate == TextPlate.Glass ? GlassPlateColor : SolidPlateColor;
                ctx.DrawShape(cmd, plate, block.PlateRadius * anim * scale, pc.WithAlpha(pc.A * state.Alpha), 0f, block.Plate == TextPlate.Glass);
            }
            if (draw.Font == null || block.Glyphs.Count == 0) return;
            Texture atlas = draw.Font.material != null ? draw.Font.material.mainTexture : null;
            if (atlas == null) return;
            int size = RenderSize(block.FontSize, scale, anim);
            Mesh mesh = BuildMesh(block, draw.Font, size, center, anim, offset, scale, state.VisibleCharacters);
            if (mesh == null) return;
            if (block.Shadow)
            {
                Vector2 so = new Vector2(block.ShadowOffset.X, block.ShadowOffset.Y) * (anim * scale);
                ctx.DrawGlyphs(cmd, mesh, atlas, so, ShadowColor.WithAlpha(ShadowColor.A * state.Alpha));
            }
            float stroke = block.StrokePx * anim * scale;
            if (stroke > 0.25f)
            {
                StudioColor sc = block.StrokeColor.WithAlpha(block.StrokeColor.A * state.Alpha);
                int dirs = stroke <= 2f ? 8 : 16;
                for (int ring = stroke > 3f ? 0 : 1; ring < 2; ring++)
                {
                    float r = ring == 0 ? stroke * 0.5f : stroke;
                    for (int k = 0; k < dirs; k++)
                    {
                        float a = k * (2f * Mathf.PI / dirs);
                        ctx.DrawGlyphs(cmd, mesh, atlas, new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r), sc);
                    }
                }
            }
            ctx.DrawGlyphs(cmd, mesh, atlas, Vector2.zero, block.Fill.WithAlpha(block.Fill.A * state.Alpha));
        }

        /// <summary>
        /// Draws one line of text fitted into <paramref name="rect"/> (canvas px), vertically centred, left-aligned: the watermark's
        /// "Ballverse" word. Its glyphs must have been requested by <see cref="PrepareGlyphs"/> of the same frame.
        /// </summary>
        public void DrawLine(CommandBuffer cmd, StudioDrawContext ctx, Font font, string text, Rect rect, StudioColor color, StudioColor shadow)
        {
            if (font == null || string.IsNullOrEmpty(text) || rect.width <= 0f || rect.height <= 0f) return;
            int size = LineSize(rect);
            float advance = 0f, top = float.MinValue, bottom = float.MaxValue;
            for (int i = 0; i < text.Length; i++)
            {
                if (!font.GetCharacterInfo(text[i], out CharacterInfo ci, size, FontStyle.Bold)) continue;
                advance += ci.advance;
                top = Mathf.Max(top, ci.maxY);
                bottom = Mathf.Min(bottom, ci.minY);
            }
            if (advance <= 0f || top <= bottom) return;
            float k = Mathf.Min(rect.width / advance, rect.height / (top - bottom));
            float baseline = rect.y + rect.height * 0.5f + (top + bottom) * 0.5f * k;
            float x = rect.x;
            int n = 0;
            EnsureCapacity(text.Length * 4, text.Length * 6);
            for (int i = 0; i < text.Length; i++)
            {
                if (!font.GetCharacterInfo(text[i], out CharacterInfo ci, size, FontStyle.Bold)) continue;
                AddQuad(ref n, new Rect(x + ci.minX * k, baseline - ci.maxY * k, (ci.maxX - ci.minX) * k, (ci.maxY - ci.minY) * k), ci);
                x += ci.advance * k;
            }
            Texture atlas = font.material != null ? font.material.mainTexture : null;
            Mesh mesh = Upload(n);
            if (mesh == null || atlas == null) return;
            float so = Mathf.Max(1f, rect.height * 0.06f);
            ctx.DrawGlyphs(cmd, mesh, atlas, new Vector2(so, so), shadow);
            ctx.DrawGlyphs(cmd, mesh, atlas, Vector2.zero, color);
        }

        static int RenderSize(float fontSizeRef, float canvasScale, float animScale)
        {
            return Mathf.Clamp(Mathf.RoundToInt(fontSizeRef * canvasScale * Mathf.Max(0.05f, animScale)), 4, 512);
        }

        static Rect Transform(FloatRect r, Vector2 center, float anim, Vector2 offset, float scale)
        {
            Vector2 a = TransformPoint(new Vector2(r.X, r.Y), center, anim, offset, scale);
            Vector2 b = TransformPoint(new Vector2(r.Right, r.Bottom), center, anim, offset, scale);
            return Rect.MinMaxRect(a.x, a.y, b.x, b.y);
        }

        static Vector2 TransformPoint(Vector2 p, Vector2 center, float anim, Vector2 offset, float scale)
        {
            return ((p - center) * anim + center + offset) * scale;
        }

        Mesh BuildMesh(TextBlock block, Font font, int size, Vector2 center, float anim, Vector2 offset, float scale, int visibleChars)
        {
            EnsureCapacity(block.Glyphs.Count * 4, block.Glyphs.Count * 6);
            int n = 0;
            for (int i = 0; i < block.Glyphs.Count; i++)
            {
                PlacedGlyph g = block.Glyphs[i];
                if (visibleChars >= 0 && g.CharIndex >= visibleChars) break;
                if (!font.GetCharacterInfo(g.Character, out CharacterInfo ci, size, FontGlyphSource.Style)) continue;
                Rect r = Transform(g.Rect, center, anim, offset, scale);
                AddQuad(ref n, r, ci);
            }
            return Upload(n);
        }

        void AddQuad(ref int n, Rect r, CharacterInfo ci)
        {
            int v = n * 4;
            _positions[v] = new Vector3(r.xMin, r.yMin, 0f);
            _positions[v + 1] = new Vector3(r.xMax, r.yMin, 0f);
            _positions[v + 2] = new Vector3(r.xMax, r.yMax, 0f);
            _positions[v + 3] = new Vector3(r.xMin, r.yMax, 0f);
            // Canvas y grows downward: the glyph's top edge is yMin.
            _uvs[v] = ci.uvTopLeft;
            _uvs[v + 1] = ci.uvTopRight;
            _uvs[v + 2] = ci.uvBottomRight;
            _uvs[v + 3] = ci.uvBottomLeft;
            int t = n * 6;
            _indices[t] = v;
            _indices[t + 1] = v + 1;
            _indices[t + 2] = v + 2;
            _indices[t + 3] = v;
            _indices[t + 4] = v + 2;
            _indices[t + 5] = v + 3;
            n++;
        }

        Mesh Upload(int quads)
        {
            if (quads == 0) return null;
            if (_meshCursor >= _meshes.Count)
            {
                var m = new Mesh { name = "StudioText", hideFlags = HideFlags.HideAndDontSave };
                m.MarkDynamic();
                _meshes.Add(m);
            }
            Mesh mesh = _meshes[_meshCursor++];
            mesh.Clear(true);
            mesh.SetVertices(_positions, 0, quads * 4);
            mesh.SetUVs(0, _uvs, 0, quads * 4);
            mesh.SetIndices(_indices, 0, quads * 6, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 10f));
            return mesh;
        }

        void EnsureCapacity(int vertices, int indices)
        {
            if (vertices > _positions.Length)
            {
                int n = Mathf.NextPowerOfTwo(vertices);
                Array.Resize(ref _positions, n);
                Array.Resize(ref _uvs, n);
            }
            if (indices > _indices.Length) Array.Resize(ref _indices, Mathf.NextPowerOfTwo(indices));
        }

        public void Dispose()
        {
            Font.textureRebuilt -= OnFontTextureRebuilt;
            foreach (Mesh m in _meshes)
            {
                StudioUnity.Destroy(m);
            }
            _meshes.Clear();
        }

        sealed class EmptyGlyphs : IGlyphSource
        {
            public float Size => 100f;
            public float Ascent => 80f;
            public float LineHeight => 120f;

            public bool TryGetGlyph(char c, out GlyphMetrics metrics)
            {
                metrics = default(GlyphMetrics);
                return false;
            }
        }
    }
}
