using System;
using System.Collections.Generic;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Watermark appearance constants and placement (spec §3.10, owner decision Р13). Whether the watermark is drawn at all
    /// (NO_WATERMARK entitlement, user toggle, Remote Config kill switch) is decided elsewhere; this is pure geometry.
    /// <para>
    /// Placement rule, a pure function of the canvas, masks and occupied rects:
    /// <list type="number">
    /// <item>Size: height = round_half_up(2.5% of canvas height) (48 px at 1920, 32 px at 1280), width =
    /// round_half_up(height * logoAspect), limited to the safe-area width.</item>
    /// <item>Each candidate corner (top-left, top-right) starts at the safe-area corner (<see cref="SafeArea"/>) and slides
    /// down past intersecting platform UI masks with a safe-margin gap; if no mask-free spot exists inside the safe area it
    /// stays at the corner.</item>
    /// <item>Top-left is used if it is clear (no occupied overlap, no mask overlap); else top-right if clear; else the corner
    /// with the least occupied overlap, then least mask overlap, then top-left.</item>
    /// </list>
    /// The user cannot move, scale or cover the watermark; the compositor draws it last.
    /// </para>
    /// <para>
    /// Preview/export agreement (spec §3.3 acceptance, §3.10, §7.5): rule 3 is an exact pixel test, and rects rounded
    /// separately per canvas can differ by 1 px, so deciding it per canvas could pick different corners for preview and
    /// export when a PiP window or text box sits next to the watermark edge. The corner is therefore decided once on the
    /// 1080x1920 reference canvas (<see cref="ChooseCorner"/>, with masks and occupied rects in reference pixels) and only
    /// the rect is computed per canvas (<see cref="PlaceAt"/>). <see cref="FrameComposer"/> does exactly that;
    /// <see cref="Place"/> decides on the given canvas alone and is meant for single-canvas callers and the reference itself.
    /// </para>
    /// </summary>
    public static class WatermarkLayout
    {
        /// <summary>Watermark height as a fraction of the canvas height (spec §3.10).</summary>
        public const float HeightFraction = 0.025f;

        /// <summary>Opacity of the white logo and wordmark (spec §3.10).</summary>
        public const float Opacity = 0.7f;

        /// <summary>
        /// Placeholder logo aspect (width / height of glowing ball + "Ballverse") used when the real asset aspect is not given.
        /// </summary>
        public const float DefaultLogoAspect = 4.5f;

        /// <summary>round_half_up(canvasHeight * 0.025), at least 1: 1920 -&gt; 48, 1280 -&gt; 32.</summary>
        public static int HeightPx(int canvasHeight)
        {
            if (canvasHeight <= 0) throw new ArgumentOutOfRangeException(nameof(canvasHeight), "canvas height must be > 0");
            return Math.Max(1, PixelMath.RoundDiv(canvasHeight * 25L, 1000));
        }

        /// <summary>Watermark size for a canvas and logo aspect (width / height, finite, &gt; 0).</summary>
        public static IntVec2 Size(int canvasWidth, int canvasHeight, float logoAspect)
        {
            PixelMath.RequireCanvas(canvasWidth, canvasHeight);
            PixelMath.RequireFinite(logoAspect, nameof(logoAspect));
            if (logoAspect <= 0f) throw new ArgumentOutOfRangeException(nameof(logoAspect), "logo aspect must be > 0");
            IntRect safe = SafeArea.Rect(canvasWidth, canvasHeight);
            int h = Math.Min(HeightPx(canvasHeight), safe.Height);
            double wd = Math.Min((double)h * logoAspect, safe.Width);
            int w = PixelMath.Clamp(PixelMath.RoundHalfUp(wd), 1, safe.Width);
            return new IntVec2(w, h);
        }

        /// <summary>Candidate rect for one corner after mask avoidance (rule 2).</summary>
        public static IntRect CornerRect(int canvasWidth, int canvasHeight, float logoAspect, WatermarkCorner corner, RectUnion masks)
        {
            if (masks == null) throw new ArgumentNullException(nameof(masks));
            IntVec2 size = Size(canvasWidth, canvasHeight, logoAspect);
            IntRect safe = SafeArea.Rect(canvasWidth, canvasHeight);
            int x = corner == WatermarkCorner.TopLeft ? safe.X : safe.Right - size.X;
            var start = new IntRect(x, safe.Y, size.X, size.Y);
            int gap = safe.X;
            return MaskAvoidance.PushVertical(start, masks, true, gap, safe.Y, safe.Bottom, out bool _);
        }

        /// <summary>
        /// Places the watermark (see class remarks). <paramref name="occupied"/>: PiP window rect and measured text layer
        /// rects in canvas pixels (may be null or empty).
        /// </summary>
        public static WatermarkPlacement Place(int canvasWidth, int canvasHeight, float logoAspect, RectUnion masks,
            IReadOnlyList<IntRect> occupied)
        {
            if (masks == null) throw new ArgumentNullException(nameof(masks));
            var occ = new RectUnion(occupied ?? new IntRect[0]);
            WatermarkPlacement tl = Evaluate(canvasWidth, canvasHeight, logoAspect, WatermarkCorner.TopLeft, masks, occ);
            if (tl.IsClear) return tl;
            WatermarkPlacement tr = Evaluate(canvasWidth, canvasHeight, logoAspect, WatermarkCorner.TopRight, masks, occ);
            if (tr.IsClear) return tr;
            if (tr.OccupiedOverlap < tl.OccupiedOverlap) return tr;
            if (tr.OccupiedOverlap == tl.OccupiedOverlap && tr.MaskOverlap < tl.MaskOverlap) return tr;
            return tl;
        }

        /// <summary>
        /// Decides the corner on the 1080x1920 reference canvas (see class remarks). <paramref name="referenceMasks"/> and
        /// <paramref name="referenceOccupied"/> are in reference pixels (e.g. normalized rects through
        /// <see cref="FloatRect.ToPixels"/> at 1080x1920, the PiP window computed at 1080x1920).
        /// </summary>
        public static WatermarkCorner ChooseCorner(float logoAspect, RectUnion referenceMasks, IReadOnlyList<IntRect> referenceOccupied)
        {
            return Place(PixelMath.ReferenceWidth, PixelMath.ReferenceHeight, logoAspect, referenceMasks, referenceOccupied).Corner;
        }

        /// <summary>
        /// Watermark at a fixed <paramref name="corner"/> (usually from <see cref="ChooseCorner"/>) on this canvas, with the
        /// occupied and mask overlaps measured here for diagnostics. <paramref name="occupied"/> may be null.
        /// </summary>
        public static WatermarkPlacement PlaceAt(int canvasWidth, int canvasHeight, float logoAspect, RectUnion masks,
            IReadOnlyList<IntRect> occupied, WatermarkCorner corner)
        {
            if (masks == null) throw new ArgumentNullException(nameof(masks));
            if (corner != WatermarkCorner.TopLeft && corner != WatermarkCorner.TopRight)
                throw new ArgumentOutOfRangeException(nameof(corner), corner, "unknown corner");
            var occ = new RectUnion(occupied ?? new IntRect[0]);
            return Evaluate(canvasWidth, canvasHeight, logoAspect, corner, masks, occ);
        }

        static WatermarkPlacement Evaluate(int w, int h, float aspect, WatermarkCorner corner, RectUnion masks, RectUnion occupied)
        {
            IntRect r = CornerRect(w, h, aspect, corner, masks);
            return new WatermarkPlacement(corner, r, occupied.IntersectionArea(r), masks.IntersectionArea(r));
        }
    }
}
