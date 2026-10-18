using System;
using UnityEngine;

namespace Ballverse.UI.Glass
{
    /// <summary>Theme colours of the glass tint (spec §4.5 "Тонировка", "Контраст текста").</summary>
    public readonly struct GlassTint
    {
        /// <summary>Tint colour in linear space.</summary>
        public Color Linear { get; }
        /// <summary>Minimum tint opacity (10–40% by theme).</summary>
        public float MinAlpha { get; }
        /// <summary>Luminance of the text colour drawn on glass (linear).</summary>
        public float TextLuminance { get; }

        public GlassTint(Color linear, float minAlpha, float textLuminance)
        {
            Linear = linear;
            MinAlpha = minAlpha;
            TextLuminance = textLuminance;
        }

        /// <summary>Dark theme: near-black tint under white text.</summary>
        public static GlassTint Dark => new GlassTint(new Color(0.006f, 0.008f, 0.014f, 1f), 0.30f, 1f);
        /// <summary>Light theme: near-white tint under dark text (#14161A).</summary>
        public static GlassTint Light => new GlassTint(new Color(0.93f, 0.94f, 0.96f, 1f), 0.40f, 0.0075f);
    }

    /// <summary>One glass surface for the compose pass: panel-space rectangle and corner radius.</summary>
    public struct GlassRect
    {
        public Rect Bounds;
        public float Radius;
    }

    /// <summary>
    /// Renders the shared glass backdrop once per frame (spec §4.5): source → prefilter at 1/4 (1/8 on tier C) →
    /// dual-filter blur → compose (refraction, edge highlight, adaptive tint) into a persistent texture that the glass
    /// surfaces sample. Uses <see cref="Graphics.Blit(Texture, RenderTexture, Material, int)"/> only (render-pipeline
    /// independent, long-stable API).
    /// </summary>
    public sealed class GlassBackdropRenderer : IDisposable
    {
        public const int MaxRects = 8;

        const int PassPrefilter = 0;
        const int PassDown = 1;
        const int PassUp = 2;
        const int PassCompose = 3;

        static readonly int s_slotRect = Shader.PropertyToID("_SlotRect");
        static readonly int s_srcScaleOffset = Shader.PropertyToID("_SrcScaleOffset");
        static readonly int s_backdrop = Shader.PropertyToID("_Backdrop");
        static readonly int s_spread = Shader.PropertyToID("_Spread");
        static readonly int s_offset = Shader.PropertyToID("_Offset");
        static readonly int s_rects = Shader.PropertyToID("_Rects");
        static readonly int s_radii = Shader.PropertyToID("_Radii");
        static readonly int s_rectCount = Shader.PropertyToID("_RectCount");
        static readonly int s_panelSize = Shader.PropertyToID("_PanelSize");
        static readonly int s_refraction = Shader.PropertyToID("_Refraction");
        static readonly int s_band = Shader.PropertyToID("_Band");
        static readonly int s_tint = Shader.PropertyToID("_Tint");
        static readonly int s_textTarget = Shader.PropertyToID("_TextTarget");
        static readonly int s_tintLum = Shader.PropertyToID("_TintLum");
        static readonly int s_lightText = Shader.PropertyToID("_LightText");
        static readonly int s_highlight = Shader.PropertyToID("_Highlight");
        static readonly int s_lightDir = Shader.PropertyToID("_LightDir");

        readonly Material _material;
        readonly Vector4[] _rects = new Vector4[MaxRects];
        readonly Vector4[] _radii = new Vector4[MaxRects];
        readonly RenderTexture[] _chain = new RenderTexture[8];
        RenderTexture _output;

        /// <summary>False when the shader is missing or unsupported: surfaces fall back to frosted colours.</summary>
        public bool Supported { get; }
        /// <summary>The composed backdrop (recreated when the size changes; check <see cref="OutputVersion"/>).</summary>
        public RenderTexture Output => _output;
        /// <summary>Incremented whenever <see cref="Output"/> is recreated.</summary>
        public int OutputVersion { get; private set; }

        public GlassBackdropRenderer()
        {
            var shader = Resources.Load<Shader>(UiAssets.GlassShaderPath);
            if (shader == null || !shader.isSupported)
            {
                Supported = false;
                Debug.LogWarning("[Ballverse.UI] Glass shader unavailable; glass surfaces use frosted colours.");
                return;
            }
            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            Supported = true;
        }

        /// <param name="source">Texture behind the UI (simulation render target or studio preview).</param>
        /// <param name="slotUv">Where the source is shown on screen: min and size in screen UV (origin bottom-left).</param>
        /// <param name="srcScaleOffset">Slot UV → source UV ("cover" crop), see <c>AspectFit.CoverUv</c>.</param>
        /// <param name="screenWidth">Screen size in pixels.</param>
        /// <param name="panelSize">Panel size in panel units (rects are in panel units).</param>
        /// <param name="lightDir">Direction towards the highlight light in panel space (y down).</param>
        public RenderTexture Render(Texture source, Rect slotUv, Vector4 srcScaleOffset, int screenWidth, int screenHeight,
            Vector2 panelSize, GlassQuality quality, GlassTint tint, Color backdrop, GlassRect[] rects, int rectCount,
            Vector2 lightDir)
        {
            if (!Supported || source == null || quality == null || !quality.UsesBackdrop) return null;
            int w = Mathf.Max(16, screenWidth / Mathf.Max(1, quality.Downsample));
            int h = Mathf.Max(16, screenHeight / Mathf.Max(1, quality.Downsample));
            EnsureOutput(w, h);

            var m = _material;
            m.SetVector(s_slotRect, new Vector4(slotUv.x, slotUv.y, slotUv.width, slotUv.height));
            m.SetVector(s_srcScaleOffset, srcScaleOffset);
            m.SetColor(s_backdrop, backdrop);
            float srcPixelsPerDst = source.width / (float)Mathf.Max(1, w) * Mathf.Max(srcScaleOffset.x, 1e-3f);
            m.SetFloat(s_spread, Mathf.Clamp(srcPixelsPerDst * 0.25f, 0.5f, 4f));
            m.SetFloat(s_offset, quality.Offset);

            int iterations = Mathf.Clamp(quality.Iterations, 1, _chain.Length - 1);
            var baseRt = Temp(w, h);
            Graphics.Blit(source, baseRt, m, PassPrefilter);
            _chain[0] = baseRt;
            int cw = w;
            int ch = h;
            int levels = 0;
            for (int i = 1; i <= iterations; i++)
            {
                cw = Mathf.Max(2, cw / 2);
                ch = Mathf.Max(2, ch / 2);
                _chain[i] = Temp(cw, ch);
                Graphics.Blit(_chain[i - 1], _chain[i], m, PassDown);
                levels = i;
            }
            var blurred = Temp(w, h);
            for (int i = levels; i >= 1; i--)
            {
                var dst = i == 1 ? blurred : _chain[i - 1];
                Graphics.Blit(_chain[i], dst, m, PassUp);
            }

            int count = Mathf.Clamp(rectCount, 0, MaxRects);
            for (int i = 0; i < MaxRects; i++)
            {
                if (i < count)
                {
                    var b = rects[i].Bounds;
                    _rects[i] = new Vector4(b.xMin, b.yMin, b.xMax, b.yMax);
                    _radii[i] = new Vector4(rects[i].Radius, 0, 0, 0);
                }
                else
                {
                    _rects[i] = Vector4.zero;
                    _radii[i] = Vector4.zero;
                }
            }
            m.SetVectorArray(s_rects, _rects);
            m.SetVectorArray(s_radii, _radii);
            m.SetFloat(s_rectCount, count);
            m.SetVector(s_panelSize, new Vector4(panelSize.x, panelSize.y, 0, 0));
            float panelPerPixel = screenWidth > 0 ? panelSize.x / screenWidth : 1f;
            m.SetFloat(s_refraction, quality.RefractionPx * panelPerPixel);
            m.SetFloat(s_band, 14f);
            var t = tint.Linear;
            m.SetVector(s_tint, new Vector4(t.r, t.g, t.b, tint.MinAlpha));
            float tintLum = (float)GlassContrast.LinearLuminance(t.r, t.g, t.b);
            bool lightText = tint.TextLuminance > tintLum;
            float target = lightText
                ? (tint.TextLuminance + 0.05f) / (float)GlassContrast.MinTextContrast - 0.05f
                : (float)GlassContrast.MinTextContrast * (tint.TextLuminance + 0.05f) - 0.05f;
            m.SetFloat(s_textTarget, target);
            m.SetFloat(s_tintLum, tintLum);
            m.SetFloat(s_lightText, lightText ? 1f : 0f);
            m.SetFloat(s_highlight, quality.Highlight);
            var ld = lightDir.sqrMagnitude > 1e-6f ? lightDir.normalized : new Vector2(-0.7071f, -0.7071f);
            m.SetVector(s_lightDir, new Vector4(ld.x, ld.y, 0, 0));
            Graphics.Blit(blurred, _output, m, PassCompose);

            for (int i = 0; i <= levels; i++)
            {
                if (_chain[i] != null) RenderTexture.ReleaseTemporary(_chain[i]);
                _chain[i] = null;
            }
            RenderTexture.ReleaseTemporary(blurred);
            return _output;
        }

        static RenderTexture Temp(int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Clamp;
            return rt;
        }

        void EnsureOutput(int w, int h)
        {
            if (_output != null && _output.width == w && _output.height == h && _output.IsCreated()) return;
            if (_output != null)
            {
                _output.Release();
                UnityEngine.Object.Destroy(_output);
            }
            _output = new RenderTexture(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default)
            {
                name = "BallverseGlassBackdrop",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                hideFlags = HideFlags.HideAndDontSave,
            };
            _output.Create();
            OutputVersion++;
        }

        public void Dispose()
        {
            if (_output != null)
            {
                _output.Release();
                UnityEngine.Object.Destroy(_output);
                _output = null;
            }
            if (_material != null) UnityEngine.Object.Destroy(_material);
        }
    }
}
