using System;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Text layer geometry (spec §3.5): normalized position -&gt; pixel anchor, reference sizes -&gt; canvas sizes. Text is
    /// measured and laid out by the Unity compositor; Core only does the anchor and scale math so preview and export agree
    /// (spec §7.5). The anchor's meaning relative to the text box (center, alignment) is up to the renderer.
    /// </summary>
    public static class TextLayerGeometry
    {
        /// <summary>At most 5 text layers per video (spec §3.5).</summary>
        public const int MaxLayers = 5;

        /// <summary>Font size range at the reference width, px (spec §3.5).</summary>
        public const float MinSizePx = 12f;
        public const float MaxSizePx = 160f;

        /// <summary>Stroke range at the reference width, px (spec §3.5).</summary>
        public const float MaxStrokePx = 8f;

        /// <summary>
        /// Places a layer: position clamped to [0,1] then multiplied by the canvas size; font size clamped to 12–160 and
        /// stroke to 0–8 reference px, then scaled by canvasWidth / 1080.
        /// </summary>
        public static TextPlacement Place(int canvasWidth, int canvasHeight, FloatVec2 position, float sizeRefPx, float strokeRefPx = 0f)
        {
            PixelMath.RequireCanvas(canvasWidth, canvasHeight);
            if (!position.IsFinite) throw new ArgumentException("position must be finite", nameof(position));
            PixelMath.RequireFinite(sizeRefPx, nameof(sizeRefPx));
            PixelMath.RequireFinite(strokeRefPx, nameof(strokeRefPx));
            float nx = PixelMath.ClampF(position.X, 0f, 1f);
            float ny = PixelMath.ClampF(position.Y, 0f, 1f);
            double ax = (double)nx * canvasWidth;
            double ay = (double)ny * canvasHeight;
            var pixel = new IntVec2(
                PixelMath.Clamp(PixelMath.RoundHalfUp(ax), 0, canvasWidth - 1),
                PixelMath.Clamp(PixelMath.RoundHalfUp(ay), 0, canvasHeight - 1));
            float size = PixelMath.ScaleRefF(PixelMath.ClampF(sizeRefPx, MinSizePx, MaxSizePx), canvasWidth);
            float stroke = PixelMath.ScaleRefF(PixelMath.ClampF(strokeRefPx, 0f, MaxStrokePx), canvasWidth);
            float scale = (float)((double)canvasWidth / PixelMath.ReferenceWidth);
            return new TextPlacement(new FloatVec2((float)ax, (float)ay), pixel, size, stroke, scale);
        }

        /// <summary>Inverse for drag gestures: canvas pixel point -&gt; normalized position, clamped to [0,1].</summary>
        public static FloatVec2 ToNormalized(FloatVec2 pointPx, int canvasWidth, int canvasHeight)
        {
            PixelMath.RequireCanvas(canvasWidth, canvasHeight);
            if (!pointPx.IsFinite) throw new ArgumentException("point must be finite", nameof(pointPx));
            return new FloatVec2(
                PixelMath.ClampF((float)((double)pointPx.X / canvasWidth), 0f, 1f),
                PixelMath.ClampF((float)((double)pointPx.Y / canvasHeight), 0f, 1f));
        }

        /// <summary>Shifts a measured text box (canvas pixels) so it stays inside the canvas; see <see cref="IntRect.ClampInside"/>.</summary>
        public static IntRect KeepInside(IntRect measuredBounds, int canvasWidth, int canvasHeight)
        {
            PixelMath.RequireCanvas(canvasWidth, canvasHeight);
            return measuredBounds.ClampInside(canvasWidth, canvasHeight);
        }
    }
}
