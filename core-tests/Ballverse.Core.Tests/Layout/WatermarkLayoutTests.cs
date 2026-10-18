using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Xunit;

namespace Ballverse.Core.Tests.Layout
{
    public class WatermarkLayoutTests
    {
        const float Aspect = 4.5f;

        [Theory]
        [InlineData(1920, 48)]
        [InlineData(1280, 32)]
        [InlineData(1000, 25)]
        [InlineData(20, 1)] // 0.5 rounds up
        [InlineData(8, 1)]
        public void HeightIsTwoAndAHalfPercent(int canvasHeight, int expected)
        {
            Assert.Equal(expected, WatermarkLayout.HeightPx(canvasHeight));
        }

        [Fact]
        public void SizeFollowsLogoAspect()
        {
            Assert.Equal(new IntVec2(216, 48), WatermarkLayout.Size(1080, 1920, Aspect));
            Assert.Equal(new IntVec2(144, 32), WatermarkLayout.Size(720, 1280, Aspect));
            Assert.Equal(new IntVec2(48, 48), WatermarkLayout.Size(1080, 1920, 1f));
            // Never wider than the safe area.
            Assert.Equal(1016, WatermarkLayout.Size(1080, 1920, 100f).X);
            Assert.Throws<ArgumentOutOfRangeException>(() => WatermarkLayout.Size(1080, 1920, 0f));
            Assert.Throws<ArgumentException>(() => WatermarkLayout.Size(1080, 1920, float.NaN));
        }

        [Fact]
        public void DefaultIsTopLeftOfSafeArea()
        {
            WatermarkPlacement p = WatermarkLayout.Place(1080, 1920, Aspect, RectUnion.Empty, null);
            Assert.Equal(WatermarkCorner.TopLeft, p.Corner);
            Assert.Equal(new IntRect(32, 32, 216, 48), p.Rect);
            Assert.True(p.IsClear);
            WatermarkPlacement p720 = WatermarkLayout.Place(720, 1280, Aspect, RectUnion.Empty, new IntRect[0]);
            Assert.Equal(new IntRect(21, 21, 144, 32), p720.Rect);
        }

        [Fact]
        public void MovesBelowPlatformMasks()
        {
            WatermarkPlacement p = WatermarkLayout.Place(1080, 1920, Aspect, PlatformUiMasks.Default.ToPixels(1080, 1920), null);
            Assert.Equal(WatermarkCorner.TopLeft, p.Corner);
            Assert.Equal(new IntRect(32, 186, 216, 48), p.Rect); // TikTok top bar 154 + 32
            Assert.True(p.IsClear);
            WatermarkPlacement p720 = WatermarkLayout.Place(720, 1280, Aspect, PlatformUiMasks.Default.ToPixels(720, 1280), null);
            Assert.Equal(new IntRect(21, 123, 144, 32), p720.Rect); // TikTok top bar 102 + 21
        }

        [Fact]
        public void PipWindowAtTopLeftMovesWatermarkToTopRight()
        {
            RectUnion masks = PlatformUiMasks.Default.ToPixels(1080, 1920);
            var pip = new IntRect(32, 186, 378, 672);
            WatermarkPlacement p = WatermarkLayout.Place(1080, 1920, Aspect, masks, new[] { pip });
            Assert.Equal(WatermarkCorner.TopRight, p.Corner);
            Assert.Equal(new IntRect(832, 186, 216, 48), p.Rect);
            Assert.True(p.IsClear);
        }

        [Fact]
        public void TextLayerAtTopLeftMovesWatermarkToTopRight()
        {
            var text = new IntRect(0, 150, 300, 80);
            WatermarkPlacement p = WatermarkLayout.Place(1080, 1920, Aspect, PlatformUiMasks.Default.ToPixels(1080, 1920), new[] { text });
            Assert.Equal(WatermarkCorner.TopRight, p.Corner);
        }

        [Fact]
        public void TouchingRectIsNotAConflict()
        {
            var below = new IntRect(0, 80, 1080, 100); // starts exactly at the watermark bottom (32 + 48)
            WatermarkPlacement p = WatermarkLayout.Place(1080, 1920, Aspect, RectUnion.Empty, new[] { below });
            Assert.Equal(WatermarkCorner.TopLeft, p.Corner);
            Assert.True(p.IsClear);
        }

        [Fact]
        public void BothCornersConflictPicksLeastOverlap()
        {
            // TL (32..248 x 32..80) overlapped by a 100 x 48 text, TR (832..1048) by a 50 x 48 one -> TR.
            var occupied = new[] { new IntRect(32, 32, 100, 48), new IntRect(998, 32, 50, 48) };
            WatermarkPlacement p = WatermarkLayout.Place(1080, 1920, Aspect, RectUnion.Empty, occupied);
            Assert.Equal(WatermarkCorner.TopRight, p.Corner);
            Assert.Equal(50L * 48, p.OccupiedOverlap);

            var reversed = new[] { new IntRect(32, 32, 50, 48), new IntRect(948, 32, 100, 48) };
            WatermarkPlacement q = WatermarkLayout.Place(1080, 1920, Aspect, RectUnion.Empty, reversed);
            Assert.Equal(WatermarkCorner.TopLeft, q.Corner);
            Assert.Equal(50L * 48, q.OccupiedOverlap);
        }

        [Fact]
        public void EqualOverlapTieGoesToTopLeft()
        {
            var wide = new[] { new IntRect(0, 0, 1080, 300) };
            WatermarkPlacement p = WatermarkLayout.Place(1080, 1920, Aspect, RectUnion.Empty, wide);
            Assert.Equal(WatermarkCorner.TopLeft, p.Corner);
            Assert.Equal(216L * 48, p.OccupiedOverlap);
        }

        [Fact]
        public void OverlappingOccupiedRectsAreCountedOnce()
        {
            var occupied = new[] { new IntRect(32, 32, 100, 48), new IntRect(32, 32, 100, 48), new IntRect(82, 32, 100, 48) };
            WatermarkPlacement p = WatermarkLayout.Place(1080, 1920, Aspect, RectUnion.Empty,
                new List<IntRect>(occupied) { new IntRect(832, 32, 216, 48) });
            Assert.Equal(WatermarkCorner.TopLeft, p.Corner);
            Assert.Equal(150L * 48, p.OccupiedOverlap);
        }

        [Fact]
        public void UnavoidableMaskOnLeftPrefersClearRightCorner()
        {
            // A left column covering the full height cannot be escaped by sliding down.
            var masks = new RectUnion(new[] { new IntRect(0, 0, 100, 1920) });
            WatermarkPlacement p = WatermarkLayout.Place(1080, 1920, Aspect, masks, null);
            Assert.Equal(WatermarkCorner.TopRight, p.Corner);
            Assert.Equal(new IntRect(832, 32, 216, 48), p.Rect);
            IntRect tl = WatermarkLayout.CornerRect(1080, 1920, Aspect, WatermarkCorner.TopLeft, masks);
            Assert.Equal(new IntRect(32, 32, 216, 48), tl); // stays in its corner
        }

        [Fact]
        public void ConflictWithMasksEverywhereFallsBackToLeastMaskOverlap()
        {
            var masks = new RectUnion(new[] { new IntRect(0, 0, 100, 1920), new IntRect(1000, 0, 80, 1920) });
            WatermarkPlacement p = WatermarkLayout.Place(1080, 1920, Aspect, masks, null);
            Assert.Equal(WatermarkCorner.TopRight, p.Corner); // 48 px column overlap vs 68 px
            Assert.Equal(48L * 48, p.MaskOverlap);
        }

        [Fact]
        public void ResultDoesNotDependOnOccupiedOrder()
        {
            var rng = Rng.FromSeed(11, 1);
            RectUnion masks = PlatformUiMasks.Default.ToPixels(1080, 1920);
            for (int i = 0; i < 200; i++)
            {
                var list = new List<IntRect>();
                int n = rng.NextInt(0, 6);
                for (int k = 0; k < n; k++) list.Add(new IntRect(rng.NextInt(0, 1000), rng.NextInt(0, 400), rng.NextInt(1, 500), rng.NextInt(1, 200)));
                WatermarkPlacement a = WatermarkLayout.Place(1080, 1920, Aspect, masks, list);
                list.Reverse();
                WatermarkPlacement b = WatermarkLayout.Place(1080, 1920, Aspect, masks, list);
                Assert.Equal(a.Corner, b.Corner);
                Assert.Equal(a.Rect, b.Rect);
                Assert.True(SafeArea.Rect(1080, 1920).Contains(a.Rect));
            }
        }

        [Fact]
        public void ScalesConsistentlyBetween720And1080()
        {
            WatermarkPlacement hi = WatermarkLayout.Place(1080, 1920, Aspect, PlatformUiMasks.Default.ToPixels(1080, 1920), null);
            WatermarkPlacement lo = WatermarkLayout.Place(720, 1280, Aspect, PlatformUiMasks.Default.ToPixels(720, 1280), null);
            const double k = 720.0 / 1080.0;
            Assert.True(Math.Abs(hi.Rect.X * k - lo.Rect.X) <= 1.0);
            Assert.True(Math.Abs(hi.Rect.Y * k - lo.Rect.Y) <= 1.0);
            Assert.True(Math.Abs(hi.Rect.Width * k - lo.Rect.Width) <= 1.0);
            Assert.True(Math.Abs(hi.Rect.Height * k - lo.Rect.Height) <= 1.0);
        }

        [Fact]
        public void ChooseCornerDecidesOnTheReferenceCanvas()
        {
            RectUnion refMasks = PlatformUiMasks.Default.ToPixels(1080, 1920);
            Assert.Equal(WatermarkCorner.TopLeft, WatermarkLayout.ChooseCorner(Aspect, refMasks, null));
            // Touching the reference watermark bottom (186 + 48) is no conflict; one pixel of overlap is.
            Assert.Equal(WatermarkCorner.TopLeft, WatermarkLayout.ChooseCorner(Aspect, refMasks, new[] { new IntRect(0, 234, 400, 300) }));
            Assert.Equal(WatermarkCorner.TopRight, WatermarkLayout.ChooseCorner(Aspect, refMasks, new[] { new IntRect(0, 233, 400, 300) }));
            Assert.Equal(WatermarkLayout.Place(1080, 1920, Aspect, refMasks, new[] { new IntRect(0, 233, 400, 300) }).Corner,
                WatermarkLayout.ChooseCorner(Aspect, refMasks, new[] { new IntRect(0, 233, 400, 300) }));
        }

        [Fact]
        public void PlaceOnReferenceIsPlaceAt1080x1920()
        {
            RectUnion refMasks = PlatformUiMasks.Default.ToPixels(1080, 1920);
            var occ = new[] { new IntRect(0, 200, 400, 300) };
            WatermarkPlacement r = WatermarkLayout.PlaceOnReference(Aspect, refMasks, occ);
            WatermarkPlacement p = WatermarkLayout.Place(1080, 1920, Aspect, refMasks, occ);
            Assert.Equal(p.Corner, r.Corner);
            Assert.Equal(p.Rect, r.Rect);
            Assert.Equal(new IntRect(832, 186, 216, 48), r.Rect);
            // Scaling the reference onto the reference canvas changes nothing.
            WatermarkPlacement same = WatermarkLayout.PlaceAt(1080, 1920, Aspect, refMasks, occ, r);
            Assert.Equal(r.Rect, same.Rect);
            Assert.Equal(r.Corner, same.Corner);
            Assert.Equal(r.OccupiedOverlap, same.OccupiedOverlap);
            Assert.Equal(r.MaskOverlap, same.MaskOverlap);
        }

        [Fact]
        public void PlaceAtScalesTheReferencePlacement()
        {
            RectUnion masks720 = PlatformUiMasks.Default.ToPixels(720, 1280);
            WatermarkPlacement refTr = new WatermarkPlacement(WatermarkCorner.TopRight, new IntRect(832, 186, 216, 48), 0, 0);
            // The corner is forced even though top-left would be clear here; y = round(186 * 1280 / 1920) = 124.
            WatermarkPlacement p = WatermarkLayout.PlaceAt(720, 1280, Aspect, masks720, null, refTr);
            Assert.Equal(WatermarkCorner.TopRight, p.Corner);
            Assert.Equal(new IntRect(555, 124, 144, 32), p.Rect);
            Assert.True(p.IsClear);
            // An unpushed reference stays at this canvas's safe top (21 at 720 width), whatever the masks here.
            WatermarkPlacement refTl = new WatermarkPlacement(WatermarkCorner.TopLeft, new IntRect(32, 32, 216, 48), 0, 0);
            WatermarkPlacement q = WatermarkLayout.PlaceAt(720, 1280, Aspect, masks720, new[] { new IntRect(0, 30, 50, 10) }, refTl);
            Assert.Equal(new IntRect(21, 21, 144, 32), q.Rect);
            Assert.Equal(29L * 10, q.OccupiedOverlap); // diagnostics are measured on this canvas
            Assert.True(q.MaskOverlap > 0);
            // A reference pushed to the bottom of the safe area is clamped into this canvas's safe area.
            WatermarkPlacement low = new WatermarkPlacement(WatermarkCorner.TopLeft, new IntRect(32, 1840, 216, 48), 0, 0);
            IntRect lowRect = WatermarkLayout.PlaceAt(393, 699, Aspect, RectUnion.Empty, null, low).Rect;
            Assert.True(SafeArea.Rect(393, 699).Contains(lowRect));
            Assert.Throws<ArgumentOutOfRangeException>(() => WatermarkLayout.PlaceAt(720, 1280, Aspect, masks720, null,
                new WatermarkPlacement((WatermarkCorner)5, new IntRect(32, 32, 216, 48), 0, 0)));
            Assert.Throws<ArgumentNullException>(() => WatermarkLayout.PlaceAt(720, 1280, Aspect, null, null, refTl));
        }

        [Fact]
        public void PlaceAtDoesNotRerunMaskAvoidancePerCanvas()
        {
            // QA round 2: top bar + a second band whose top sits 1 px under the reference watermark bottom. At 1080 the
            // watermark fits between them; re-running the push at 720 would slide it past the band (hundreds of px).
            var norm = new[] { new FloatRect(0f, 0f, 1f, 0.08f), new FloatRect(0f, 0.1215f, 0.3f, 0.1f) };
            WatermarkPlacement r = WatermarkLayout.PlaceOnReference(Aspect, RectUnion.FromNormalized(norm, 1080, 1920), null);
            RectUnion masks720 = RectUnion.FromNormalized(norm, 720, 1280);
            WatermarkPlacement perCanvas = WatermarkLayout.Place(720, 1280, Aspect, masks720, null);
            WatermarkPlacement scaled = WatermarkLayout.PlaceAt(720, 1280, Aspect, masks720, null, r);
            Assert.True(Math.Abs(r.Rect.Y * 2.0 / 3.0 - perCanvas.Rect.Y) > 100, "probe no longer reaches the flip: " + r.Rect + " vs " + perCanvas.Rect);
            Assert.True(Math.Abs(r.Rect.Y * 2.0 / 3.0 - scaled.Rect.Y) <= 1.0, r.Rect + " vs " + scaled.Rect);
            Assert.Equal(r.Corner, scaled.Corner);
        }

        [Fact]
        public void NullMasksThrow()
        {
            Assert.Throws<ArgumentNullException>(() => WatermarkLayout.Place(1080, 1920, Aspect, null, null));
            Assert.Throws<ArgumentNullException>(() => WatermarkLayout.ChooseCorner(Aspect, null, null));
            Assert.Throws<ArgumentNullException>(() => WatermarkLayout.PlaceOnReference(Aspect, null, null));
        }
    }
}
