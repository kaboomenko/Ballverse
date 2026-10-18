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
    /// Preview/export agreement (spec §3.3 acceptance, §3.10, §7.5): rules 2 and 3 are exact pixel tests, and rects rounded
    /// separately per canvas can differ by 1 px, so running them per canvas could pick a different corner, or slide past a
    /// second mask band on one canvas but not on another (hundreds of px apart), when a PiP window, text box or mask sits
    /// next to a watermark edge. The whole placement (corner and vertical mask push) is therefore decided once on the
    /// 1080x1920 reference canvas (<see cref="PlaceOnReference"/>, with masks and occupied rects in reference pixels) and
    /// each canvas only scales that result (<see cref="PlaceAt"/>): x from this canvas's safe margin (left) or safe right
    /// edge (right), size from <see cref="Size"/>, y = this canvas's safe top when the reference was not pushed, else
    /// round_half_up(referenceY * canvasHeight / 1920), clamped into the safe area. Consistency wins over per-canvas
    /// exactness: where the reference watermark just touches a mask edge, a scaled one may overlap it by a few pixel rows
    /// (edges are rounded independently: at most ⌊1.5 + 0.5·k⌋ rows for a canvas k = H / 1920 times the reference height, i.e.
    /// one row up to the reference size and two rows on larger previews such as tablets), reported in
    /// <see cref="WatermarkPlacement.MaskOverlap"/>. <see cref="FrameComposer"/> does
    /// exactly that; <see cref="Place"/> decides on the given canvas alone and is meant for single-canvas callers and the
    /// reference itself.
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
        /// The placement (corner and rect after mask avoidance) on the 1080x1920 reference canvas (see class remarks).
        /// <paramref name="referenceMasks"/> and <paramref name="referenceOccupied"/> are in reference pixels (e.g.
        /// normalized rects through <see cref="FloatRect.ToPixels"/> at 1080x1920, the PiP window computed at 1080x1920).
        /// Pass the result to <see cref="PlaceAt"/> for every canvas.
        /// </summary>
        public static WatermarkPlacement PlaceOnReference(float logoAspect, RectUnion referenceMasks, IReadOnlyList<IntRect> referenceOccupied)
        {
            return Place(PixelMath.ReferenceWidth, PixelMath.ReferenceHeight, logoAspect, referenceMasks, referenceOccupied);
        }

        /// <summary>Corner of <see cref="PlaceOnReference"/>.</summary>
        public static WatermarkCorner ChooseCorner(float logoAspect, RectUnion referenceMasks, IReadOnlyList<IntRect> referenceOccupied)
        {
            return PlaceOnReference(logoAspect, referenceMasks, referenceOccupied).Corner;
        }

        /// <summary>
        /// Scales a reference placement (from <see cref="PlaceOnReference"/>, same <paramref name="logoAspect"/>) to this
        /// canvas without re-running any pixel test (see class remarks), so every canvas shows the reference's corner and
        /// vertical position within 1 px of proportional scaling. <paramref name="masks"/> and <paramref name="occupied"/>
        /// (this canvas's pixels, occupied may be null) only feed the overlap diagnostics of the result. At 1080x1920 the
        /// result equals the reference placement.
        /// </summary>
        public static WatermarkPlacement PlaceAt(int canvasWidth, int canvasHeight, float logoAspect, RectUnion masks,
            IReadOnlyList<IntRect> occupied, WatermarkPlacement reference)
        {
            if (masks == null) throw new ArgumentNullException(nameof(masks));
            WatermarkCorner corner = reference.Corner;
            if (corner != WatermarkCorner.TopLeft && corner != WatermarkCorner.TopRight)
                throw new ArgumentOutOfRangeException(nameof(reference), corner, "unknown corner");
            IntVec2 size = Size(canvasWidth, canvasHeight, logoAspect);
            IntRect safe = SafeArea.Rect(canvasWidth, canvasHeight);
            IntRect refSafe = SafeArea.Rect(PixelMath.ReferenceWidth, PixelMath.ReferenceHeight);
            int x = corner == WatermarkCorner.TopLeft ? safe.X : safe.Right - size.X;
            int y = safe.Y;
            if (reference.Rect.Y != refSafe.Y)
            {
                y = PixelMath.RoundDiv((long)reference.Rect.Y * canvasHeight, PixelMath.ReferenceHeight);
                y = PixelMath.Clamp(y, safe.Y, Math.Max(safe.Y, safe.Bottom - size.Y));
            }
            var r = new IntRect(x, y, size.X, size.Y);
            var occ = new RectUnion(occupied ?? new IntRect[0]);
            return new WatermarkPlacement(corner, r, occ.IntersectionArea(r), masks.IntersectionArea(r));
        }

        static WatermarkPlacement Evaluate(int w, int h, float aspect, WatermarkCorner corner, RectUnion masks, RectUnion occupied)
        {
            IntRect r = CornerRect(w, h, aspect, corner, masks);
            return new WatermarkPlacement(corner, r, occupied.IntersectionArea(r), masks.IntersectionArea(r));
        }
    }
}
