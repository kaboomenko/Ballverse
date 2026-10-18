using System;
using System.Collections.Generic;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace Ballverse.Studio
{
    /// <summary>What a slot shows on one frame.</summary>
    public enum SlotPictureKind
    {
        /// <summary>Nothing loaded yet (placeholder).</summary>
        Empty = 0,
        /// <summary>A texture mapped into the slot.</summary>
        Picture = 1,
        /// <summary>Black slot (<c>VideoAfterEnd.Black</c>).</summary>
        Black = 2,
    }

    /// <summary>Picture of one slot: texture, its mapping into the slot (Core <see cref="ContentMapper"/>) and the Fit margin fill.</summary>
    public struct SlotPicture
    {
        public SlotPictureKind Kind;
        public Texture Texture;
        /// <summary>True for render textures (row 0 = bottom); false for frames uploaded top-down (decoded video).</summary>
        public bool BottomUp;
        /// <summary>Mapping of the texture into the slot (crop, rotation, mirror, letterbox).</summary>
        public ContentMapping Mapping;
        /// <summary>Slot content settings (rotation and mirror are needed for the blurred margin fill).</summary>
        public SlotContent Content;
        public FitBackgroundKind Background;
        public StudioColor BackgroundColor;

        public static SlotPicture Empty => new SlotPicture { Kind = SlotPictureKind.Empty };
        public static SlotPicture Black => new SlotPicture { Kind = SlotPictureKind.Black };
    }

    /// <summary>Look of the frame that is not geometry: divider, PiP frame and shadow, theme colour, watermark artwork.</summary>
    public sealed class CompositeStyle
    {
        /// <summary>Spec §3.3 divider colour (default "#FFFFFF33").</summary>
        public StudioColor DividerColor { get; set; } = new StudioColor(1f, 1f, 1f, 0.2f);
        /// <summary>Spec §3.3 / §4.5 glass divider.</summary>
        public bool DividerGlass { get; set; } = true;
        /// <summary>PiP frame width in 1080-reference px (0 = none).</summary>
        public int PipBorderRefPx { get; set; }
        public StudioColor PipBorderColor { get; set; } = StudioColor.White;
        public bool PipShadow { get; set; } = true;
        /// <summary>Background colour of the theme (Fit margins of kind "theme", placeholders).</summary>
        public StudioColor ThemeBackground { get; set; } = new StudioColor(0.06f, 0.06f, 0.09f, 1f);
        /// <summary>Optional watermark artwork (logo + word, white); null = the procedural glowing ball + "Ballverse".</summary>
        public Texture WatermarkTexture { get; set; }

        /// <summary>Style of a project (spec §3.3 divider and PiP settings).</summary>
        public static CompositeStyle FromProject(ProjectDocument doc, StudioColor themeBackground, Texture watermarkTexture = null)
        {
            var s = new CompositeStyle { ThemeBackground = themeBackground, WatermarkTexture = watermarkTexture };
            LayoutSettings l = doc?.Layout;
            if (l?.Divider != null)
            {
                s.DividerColor = StudioColor.Parse(l.Divider.Color, s.DividerColor);
                s.DividerGlass = l.Divider.Glass;
            }
            if (l?.Pip != null)
            {
                s.PipShadow = l.Pip.Shadow;
                if (l.Pip.Border != null)
                {
                    s.PipBorderRefPx = Math.Max(0, Math.Min(ProjectLimits.MaxPipBorderPx, l.Pip.Border.Px));
                    s.PipBorderColor = StudioColor.Parse(l.Pip.Border.Color, StudioColor.White);
                }
            }
            return s;
        }
    }

    /// <summary>
    /// Draw helpers over one command buffer and the compositor material (pixel canvas with a top-left origin). Used by
    /// <see cref="StudioCompositor"/> and <see cref="StudioTextRenderer"/>.
    /// </summary>
    public sealed class StudioDrawContext
    {
        internal const int PassImage = 0;
        internal const int PassShape = 1;
        internal const int PassGlyph = 2;
        internal const int PassReadback = 3;

        static readonly int s_rect = Shader.PropertyToID("_StRect");
        static readonly int s_dest = Shader.PropertyToID("_StDest");
        static readonly int s_uv0 = Shader.PropertyToID("_StUv0");
        static readonly int s_uv1 = Shader.PropertyToID("_StUv1");
        static readonly int s_bgUv0 = Shader.PropertyToID("_StBgUv0");
        static readonly int s_bgUv1 = Shader.PropertyToID("_StBgUv1");
        static readonly int s_shape = Shader.PropertyToID("_StShape");
        static readonly int s_params = Shader.PropertyToID("_StParams");
        static readonly int s_offset = Shader.PropertyToID("_StOffset");
        static readonly int s_color = Shader.PropertyToID("_StColor");
        static readonly int s_bgColor = Shader.PropertyToID("_StBgColor");
        static readonly int s_borderColor = Shader.PropertyToID("_StBorderColor");
        static readonly int s_tint = Shader.PropertyToID("_StTint");
        static readonly int s_mainTex = Shader.PropertyToID("_MainTex");
        static readonly int s_bgTex = Shader.PropertyToID("_StBgTex");

        readonly Material _material;
        readonly Mesh _quad;
        readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();

        internal StudioDrawContext(Material material, Mesh quad)
        {
            _material = material;
            _quad = quad;
        }

        public int CanvasWidth { get; private set; }
        public int CanvasHeight { get; private set; }
        /// <summary>canvasWidth / 1080 (reference sizes → canvas pixels).</summary>
        public float CanvasScale { get; private set; }

        internal void SetCanvas(int width, int height)
        {
            CanvasWidth = width;
            CanvasHeight = height;
            CanvasScale = width / (float)PixelMath.ReferenceWidth;
        }

        internal Material Material => _material;

        /// <summary>Rounded rect (or soft shadow / glow when <paramref name="softness"/> &gt; 0) in canvas px.</summary>
        public void DrawShape(CommandBuffer cmd, Rect rect, float radius, StudioColor color, float softness, bool glass)
        {
            if (rect.width <= 0f || rect.height <= 0f || color.A <= 0f) return;
            Rect quad = softness > 0f ? new Rect(rect.x - softness, rect.y - softness, rect.width + 2 * softness, rect.height + 2 * softness) : rect;
            _block.Clear();
            _block.SetVector(s_rect, new Vector4(quad.x, quad.y, quad.width, quad.height));
            _block.SetVector(s_shape, new Vector4(rect.width, rect.height, Mathf.Max(0f, radius), 0f));
            _block.SetVector(s_params, new Vector4(softness, glass ? 1f : 0f, 0f, 0f));
            _block.SetVector(s_color, ToShader(color));
            cmd.DrawMesh(_quad, Matrix4x4.identity, _material, 0, PassShape, _block);
        }

        /// <summary>Glyph mesh (positions in canvas px) offset by <paramref name="offset"/>, sampling the font atlas alpha.</summary>
        public void DrawGlyphs(CommandBuffer cmd, Mesh mesh, Texture atlas, Vector2 offset, StudioColor color)
        {
            if (mesh == null || atlas == null || color.A <= 0f) return;
            _block.Clear();
            _block.SetTexture(s_mainTex, atlas);
            _block.SetVector(s_offset, new Vector4(offset.x, offset.y, 0f, 0f));
            _block.SetVector(s_color, ToShader(color));
            cmd.DrawMesh(mesh, Matrix4x4.identity, _material, 0, PassGlyph, _block);
        }

        /// <summary>
        /// Textured slot: <paramref name="texture"/> through <paramref name="uv"/> inside <paramref name="destination"/> (slot-normalized
        /// x0 y0 x1 y1), margin fill outside it, rounded/circle mask, optional frame.
        /// </summary>
        public void DrawImage(CommandBuffer cmd, Rect slot, float radius, float borderPx, StudioColor borderColor, Texture texture,
            UvTransform uv, bool bottomUp, Vector4 destination, StudioColor marginColor, Texture marginTexture, UvTransform marginUv,
            bool marginBottomUp, bool keepAlpha, float opacity)
        {
            if (slot.width <= 0f || slot.height <= 0f || texture == null) return;
            _block.Clear();
            _block.SetVector(s_rect, new Vector4(slot.x, slot.y, slot.width, slot.height));
            _block.SetVector(s_shape, new Vector4(slot.width, slot.height, Mathf.Max(0f, radius), Mathf.Max(0f, borderPx)));
            _block.SetVector(s_dest, destination);
            _block.SetVector(s_uv0, new Vector4(uv.M00, uv.M01, uv.Tx, bottomUp ? 1f : 0f));
            _block.SetVector(s_uv1, new Vector4(uv.M10, uv.M11, uv.Ty, 0f));
            _block.SetTexture(s_mainTex, texture);
            bool marginTex = marginTexture != null;
            _block.SetTexture(s_bgTex, marginTex ? marginTexture : Texture2D.blackTexture);
            _block.SetVector(s_bgUv0, new Vector4(marginUv.M00, marginUv.M01, marginUv.Tx, marginBottomUp ? 1f : 0f));
            _block.SetVector(s_bgUv1, new Vector4(marginUv.M10, marginUv.M11, marginUv.Ty, 0f));
            _block.SetVector(s_params, new Vector4(0f, 0f, keepAlpha ? 1f : 0f, marginTex ? 1f : 0f));
            _block.SetVector(s_bgColor, ToShader(marginColor));
            _block.SetVector(s_borderColor, ToShader(borderColor));
            _block.SetVector(s_tint, new Vector4(1f, 1f, 1f, Mathf.Clamp01(opacity)));
            cmd.DrawMesh(_quad, Matrix4x4.identity, _material, 0, PassImage, _block);
        }

        /// <summary>Full-screen readback conversion of <paramref name="source"/> into the current target (flip + sRGB encode).</summary>
        internal void DrawReadback(CommandBuffer cmd, Texture source, bool encodeSrgb)
        {
            _block.Clear();
            _block.SetTexture(s_mainTex, source);
            _block.SetVector(s_params, new Vector4(encodeSrgb ? 1f : 0f, 0f, 0f, 0f));
            cmd.DrawProcedural(Matrix4x4.identity, _material, PassReadback, MeshTopology.Triangles, 3, 1, _block);
        }

        /// <summary>
        /// Project colours are sRGB. In linear colour space the shader works in linear light, so the colour is converted here and
        /// passed with <see cref="MaterialPropertyBlock.SetVector(int, Vector4)"/> (which never converts), independent of how a Unity
        /// version treats <c>SetColor</c> for properties not declared in the shader's Properties block. Alpha stays linear.
        /// </summary>
        internal static Vector4 ToShader(StudioColor c)
        {
            if (QualitySettings.activeColorSpace != ColorSpace.Linear) return new Vector4(c.R, c.G, c.B, c.A);
            return new Vector4(Mathf.GammaToLinearSpace(c.R), Mathf.GammaToLinearSpace(c.G), Mathf.GammaToLinearSpace(c.B), c.A);
        }
    }

    /// <summary>
    /// The studio frame compositor (spec §7.5 step c, §3 principle "предпросмотр и экспорт рисуются одним и тем же компоновщиком"):
    /// draws a Core <see cref="FramePlan"/> — slots back to front (simulation picture, user video, Fit margins, rounded corners,
    /// PiP circle/rounded window with frame and shadow), the divider (plain or glass), text layers and the watermark — into an
    /// output render texture. Preview and export call the same <see cref="Compose"/>; only the canvas size differs. Also converts
    /// a composed frame for the encoder readback (<see cref="ConvertForReadback"/>). Main thread; owns GPU resources (dispose).
    /// </summary>
    public sealed class StudioCompositor : IDisposable
    {
        /// <summary>Resources path of the compositor shader.</summary>
        public const string ShaderResource = "BallverseStudio/StudioComposite";
        /// <summary>Text of the procedural watermark (spec §3.10 "Made with Ballverse": logo + word "Ballverse").</summary>
        public const string WatermarkWord = "Ballverse";

        static readonly int s_viewProj = Shader.PropertyToID("_StViewProj");
        static readonly StudioColor s_placeholder = new StudioColor(0.11f, 0.11f, 0.14f, 1f);
        static readonly StudioColor s_maskOverlay = new StudioColor(1f, 0.25f, 0.3f, 0.28f);

        readonly CommandBuffer _cmd = new CommandBuffer { name = "Ballverse.StudioCompositor" };
        readonly Material _material;
        readonly Mesh _quad;
        readonly StudioDrawContext _ctx;
        readonly StudioTextRenderer _text;
        readonly IStudioFontProvider _fonts;
        readonly List<RenderTexture> _temporaries = new List<RenderTexture>();
        bool _disposed;

        public StudioCompositor(IStudioFontProvider fonts = null)
        {
            Shader shader = Resources.Load<Shader>(ShaderResource);
            if (shader == null) throw new InvalidOperationException("Studio compositor shader missing: Resources/" + ShaderResource);
            _material = new Material(shader) { name = "StudioComposite", hideFlags = HideFlags.HideAndDontSave };
            _quad = CreateQuad();
            _ctx = new StudioDrawContext(_material, _quad);
            _fonts = new BuiltinFontProvider(fonts);
            _text = new StudioTextRenderer(_fonts);
        }

        /// <summary>Text layout and drawing (shared so the reference measurements match what is drawn).</summary>
        public StudioTextRenderer Text => _text;

        /// <summary>Width / height of the watermark artwork: the texture's, else the procedural one (Core default 4.5).</summary>
        public static float LogoAspect(CompositeStyle style)
        {
            Texture t = style?.WatermarkTexture;
            return t != null && t.height > 0 ? t.width / (float)t.height : WatermarkLayout.DefaultLogoAspect;
        }

        /// <summary>
        /// Composes one frame into <paramref name="target"/> (its size is the canvas the plan was composed for).
        /// </summary>
        /// <param name="plan">Geometry from <see cref="FrameComposer"/> / the export planner at this canvas size.</param>
        /// <param name="sim">Simulation slot picture.</param>
        /// <param name="video">Video slot picture (ignored when the layout has no video slot).</param>
        /// <param name="style">Divider, PiP, theme and watermark look.</param>
        /// <param name="texts">Text layers of this frame (any order: drawn in list order, i.e. the project's layer order).</param>
        /// <param name="maskOverlay">Platform UI masks to tint in canvas px (preview "Зоны интерфейса"), or null (export).</param>
        public void Compose(RenderTexture target, FramePlan plan, SlotPicture sim, SlotPicture video, CompositeStyle style,
            IReadOnlyList<TextDraw> texts, IReadOnlyList<IntRect> maskOverlay = null)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(StudioCompositor));
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            style = style ?? new CompositeStyle();
            int w = target.width, h = target.height;
            _ctx.SetCanvas(w, h);
            CommandBuffer cmd = _cmd;
            cmd.Clear();

            FrameLayout layout = plan.Layout;
            Texture simMargin = PrepareMargin(cmd, sim);
            Texture videoMargin = layout.HasVideo ? PrepareMargin(cmd, video) : null;

            cmd.SetRenderTarget(target, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            cmd.ClearRenderTarget(false, true, Color.black);
            cmd.SetGlobalMatrix(s_viewProj, GL.GetGPUProjectionMatrix(Matrix4x4.Ortho(0f, w, h, 0f, -1f, 1f), true));

            IReadOnlyList<SlotGeometry> slots = layout.SlotsBackToFront;
            for (int i = 0; i < slots.Count; i++)
            {
                SlotGeometry slot = slots[i];
                bool isVideo = slot.Kind == SlotKind.Video;
                bool isWindow = layout.HasWindow && slot.Equals(layout.Window);
                DrawSlot(cmd, slot, isVideo ? video : sim, isVideo ? videoMargin : simMargin, style, isWindow);
            }
            if (layout.HasDivider)
            {
                IntRect d = layout.Divider;
                _ctx.DrawShape(cmd, new Rect(d.X, d.Y, d.Width, d.Height), 0f, style.DividerColor, 0f, style.DividerGlass);
            }

            Font wmFont = plan.HasWatermark && style.WatermarkTexture == null ? _fonts.GetFont(null) : null;
            Rect wmTextRect = plan.HasWatermark ? WatermarkTextRect(plan.Watermark.Rect) : default(Rect);
            _text.BeginFrame();
            _text.PrepareGlyphs(texts, _ctx.CanvasScale, wmFont, WatermarkWord, wmTextRect);
            if (texts != null)
            {
                for (int i = 0; i < texts.Count; i++) _text.Draw(cmd, _ctx, texts[i]);
            }
            if (plan.HasWatermark) DrawWatermark(cmd, plan.Watermark.Rect, style, wmFont, wmTextRect);
            if (maskOverlay != null)
            {
                for (int i = 0; i < maskOverlay.Count; i++)
                {
                    IntRect m = maskOverlay[i];
                    _ctx.DrawShape(cmd, new Rect(m.X, m.Y, m.Width, m.Height), 0f, s_maskOverlay, 0f, false);
                }
            }
            Graphics.ExecuteCommandBuffer(cmd);
            ReleaseTemporaries();
        }

        /// <summary>
        /// Copies a composed frame into <paramref name="readbackTarget"/> (an RGBA8 UNorm texture of the same size) flipped so rows
        /// are top-down in the readback, and encoded to sRGB when the project renders in linear colour space — the bytes the
        /// encoder expects (RGBA32, top-down, sRGB/BT.709 transfer).
        /// </summary>
        public void ConvertForReadback(RenderTexture composed, RenderTexture readbackTarget)
        {
            if (composed == null) throw new ArgumentNullException(nameof(composed));
            if (readbackTarget == null) throw new ArgumentNullException(nameof(readbackTarget));
            CommandBuffer cmd = _cmd;
            cmd.Clear();
            cmd.SetRenderTarget(readbackTarget, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
            _ctx.DrawReadback(cmd, composed, QualitySettings.activeColorSpace == ColorSpace.Linear);
            Graphics.ExecuteCommandBuffer(cmd);
        }

        /// <summary>RGBA8 sRGB render texture for composed frames (preview canvas, export canvas).</summary>
        public static RenderTexture CreateCanvas(int width, int height, string name)
        {
            var desc = new RenderTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_SRGB, 0)
            {
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = true,
            };
            var rt = new RenderTexture(desc) { name = name, filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            rt.Create();
            return rt;
        }

        /// <summary>RGBA8 UNorm render texture for the encoder readback (bytes are already sRGB-encoded by the shader).</summary>
        public static RenderTexture CreateReadbackTarget(int width, int height, string name)
        {
            var desc = new RenderTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_UNorm, 0)
            {
                msaaSamples = 1,
                useMipMap = false,
                autoGenerateMips = false,
                sRGB = false,
            };
            var rt = new RenderTexture(desc) { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave };
            rt.Create();
            return rt;
        }

        void DrawSlot(CommandBuffer cmd, SlotGeometry slot, SlotPicture pic, Texture margin, CompositeStyle style, bool isWindow)
        {
            IntRect r = slot.Rect;
            var rect = new Rect(r.X, r.Y, r.Width, r.Height);
            float radius = slot.Shape == SlotShape.Circle ? Mathf.Min(r.Width, r.Height) * 0.5f : slot.Shape == SlotShape.RoundedRect ? slot.CornerRadius : 0f;
            float border = isWindow ? style.PipBorderRefPx * _ctx.CanvasScale : 0f;
            if (isWindow && style.PipShadow)
            {
                float soft = Mathf.Max(2f, 0.022f * _ctx.CanvasWidth);
                var shadowRect = new Rect(rect.x, rect.y + soft * 0.35f, rect.width, rect.height);
                _ctx.DrawShape(cmd, shadowRect, radius, new StudioColor(0f, 0f, 0f, 0.45f), soft, false);
            }
            if (pic.Kind != SlotPictureKind.Picture || pic.Texture == null || pic.Mapping == null)
            {
                StudioColor fill = pic.Kind == SlotPictureKind.Black ? StudioColor.Black : s_placeholder;
                _ctx.DrawShape(cmd, rect, radius, fill, 0f, false);
                if (border > 0f) DrawFrameOnly(cmd, rect, radius, border, style.PipBorderColor);
                return;
            }
            ContentMapping m = pic.Mapping;
            float sw = Mathf.Max(1, m.SlotWidth), sh = Mathf.Max(1, m.SlotHeight);
            IntRect d = m.Destination;
            var dest = new Vector4(d.X / sw, d.Y / sh, (d.X + d.Width) / sw, (d.Y + d.Height) / sh);
            StudioColor marginColor = pic.Background == FitBackgroundKind.Theme ? style.ThemeBackground : pic.BackgroundColor;
            UvTransform marginUv = UvTransform.Identity;
            if (margin != null) marginUv = CoverMapping(m, pic.Content).Uv;
            _ctx.DrawImage(cmd, rect, radius, border, style.PipBorderColor, pic.Texture, m.Uv, pic.BottomUp, dest, marginColor,
                margin, marginUv, pic.BottomUp, false, 1f);
        }

        void DrawFrameOnly(CommandBuffer cmd, Rect rect, float radius, float border, StudioColor color)
        {
            // A frame around a placeholder: outer rounded rect in the frame colour, inner placeholder on top.
            _ctx.DrawShape(cmd, rect, radius, color, 0f, false);
            var inner = new Rect(rect.x + border, rect.y + border, rect.width - 2 * border, rect.height - 2 * border);
            _ctx.DrawShape(cmd, inner, Mathf.Max(0f, radius - border), s_placeholder, 0f, false);
        }

        /// <summary>Blurred low-resolution copy of the picture for the Fit margin fill of kind "blur" (spec §3.3 "размытое это же видео").</summary>
        Texture PrepareMargin(CommandBuffer cmd, SlotPicture pic)
        {
            if (pic.Kind != SlotPictureKind.Picture || pic.Texture == null || pic.Mapping == null) return null;
            if (pic.Background != FitBackgroundKind.Blur || !pic.Mapping.HasLetterbox) return null;
            int w = Mathf.Max(8, pic.Texture.width / 4), h = Mathf.Max(8, pic.Texture.height / 4);
            RenderTexture a = Temporary(w, h);
            RenderTexture b = Temporary(Mathf.Max(4, w / 4), Mathf.Max(4, h / 4));
            RenderTexture c = Temporary(Mathf.Max(2, w / 8), Mathf.Max(2, h / 8));
            cmd.Blit(pic.Texture, a);
            cmd.Blit(a, b);
            cmd.Blit(b, c);
            return c;
        }

        RenderTexture Temporary(int w, int h)
        {
            RenderTexture rt = RenderTexture.GetTemporary(w, h, 0, GraphicsFormat.R8G8B8A8_SRGB);
            rt.filterMode = FilterMode.Bilinear;
            rt.wrapMode = TextureWrapMode.Clamp;
            _temporaries.Add(rt);
            return rt;
        }

        void ReleaseTemporaries()
        {
            for (int i = 0; i < _temporaries.Count; i++) RenderTexture.ReleaseTemporary(_temporaries[i]);
            _temporaries.Clear();
        }

        static ContentMapping CoverMapping(ContentMapping m, SlotContent content)
        {
            SlotContent c = (content ?? SlotContent.Default).WithFill(Ballverse.Core.SlotFill.Cover).WithZoom(1f).WithPan(FloatVec2.Zero);
            return ContentMapper.Map(m.SourceWidth, m.SourceHeight, Math.Max(1, m.SlotWidth), Math.Max(1, m.SlotHeight), c);
        }

        static Rect WatermarkTextRect(IntRect wm)
        {
            float hgt = wm.Height;
            float x = wm.X + hgt * 1.08f;
            return new Rect(x, wm.Y + hgt * 0.12f, Mathf.Max(1f, wm.X + wm.Width - x), hgt * 0.76f);
        }

        void DrawWatermark(CommandBuffer cmd, IntRect wm, CompositeStyle style, Font font, Rect textRect)
        {
            if (wm.IsEmpty) return;
            float opacity = WatermarkLayout.Opacity;
            var rect = new Rect(wm.X, wm.Y, wm.Width, wm.Height);
            if (style.WatermarkTexture != null)
            {
                var full = new Vector4(0f, 0f, 1f, 1f);
                // A texture asset stores row 0 at the bottom: identity map with the vertical flip.
                _ctx.DrawImage(cmd, rect, 0f, 0f, StudioColor.Transparent, style.WatermarkTexture, UvTransform.Identity, true,
                    full, StudioColor.Transparent, null, UvTransform.Identity, false, true, opacity);
                return;
            }
            // Procedural logo: a glowing ball and the word, white at 70 %, with a light shadow for bright backgrounds (spec §3.10).
            float hgt = wm.Height;
            float d = hgt * 0.82f;
            var ball = new Rect(wm.X + (hgt - d) * 0.5f, wm.Y + (hgt - d) * 0.5f, d, d);
            float shadow = Mathf.Max(1f, hgt * 0.06f);
            _ctx.DrawShape(cmd, new Rect(ball.x + shadow, ball.y + shadow, d, d), d * 0.5f, new StudioColor(0f, 0f, 0f, 0.3f * opacity), shadow, false);
            _ctx.DrawShape(cmd, ball, d * 0.5f, new StudioColor(1f, 1f, 1f, 0.35f * opacity), d * 0.25f, false);
            _ctx.DrawShape(cmd, ball, d * 0.5f, new StudioColor(1f, 1f, 1f, opacity), 0f, false);
            _text.DrawLine(cmd, _ctx, font, WatermarkWord, textRect, new StudioColor(1f, 1f, 1f, opacity), new StudioColor(0f, 0f, 0f, 0.35f * opacity));
        }

        static Mesh CreateQuad()
        {
            var mesh = new Mesh { name = "StudioQuad", hideFlags = HideFlags.HideAndDontSave };
            mesh.SetVertices(new List<Vector3> { new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f) });
            mesh.SetUVs(0, new List<Vector2> { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
            mesh.SetIndices(new[] { 0, 1, 2, 0, 2, 3 }, MeshTopology.Triangles, 0, false);
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(1e6f, 1e6f, 10f));
            mesh.UploadMeshData(true);
            return mesh;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleaseTemporaries();
            _cmd.Release();
            _text.Dispose();
            StudioUnity.Destroy(_quad);
            StudioUnity.Destroy(_material);
        }
    }
}
