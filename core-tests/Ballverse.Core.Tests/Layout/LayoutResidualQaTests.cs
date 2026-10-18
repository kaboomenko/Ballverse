using System;
using Ballverse.Core.Layout;
using Xunit;

namespace Ballverse.Core.Tests.Layout
{
    /// <summary>Residual QA findings of the merged Layout module (watermark scaling).</summary>
    public class LayoutResidualQaTests
    {
        [Theory]
        [InlineData(187, 125)] // 124.67 → round half up 125 (floor would give 124)
        [InlineData(190, 127)] // 126.67 → 127
        [InlineData(188, 125)] // 125.33 → 125
        [InlineData(186, 124)] // exact
        public void PushedYIsRoundedHalfUp(int referenceY, int expectedY)
        {
            var reference = new WatermarkPlacement(WatermarkCorner.TopLeft, new IntRect(32, referenceY, 216, 48), 0, 0);
            var none = new RectUnion(new IntRect[0]);
            WatermarkPlacement p = WatermarkLayout.PlaceAt(720, 1280, WatermarkLayout.DefaultLogoAspect, none, null, reference);
            Assert.Equal(expectedY, p.Rect.Y);
        }

        [Fact]
        public void MaskOverlapOnLargeCanvasesStaysWithinTheDocumentedBound()
        {
            // QA: on canvases taller than 1920 the scaled watermark can overlap a mask edge by two rows (docs said one).
            int worstRows = 0;
            for (int w = 1081; w <= 1600; w += 7)
            {
                var canvas = OutputCanvas.FitPreview(w, w * 2);
                double k = canvas.Y / 1920.0;
                int bound = (int)Math.Floor(1.5 + 0.5 * k);
                for (int i = 0; i <= 60; i++)
                {
                    float top2 = 0.118f + i * 0.0001f;
                    var masks = new[] { new FloatRect(0f, 0f, 1f, 0.08f), new FloatRect(0f, top2, 0.3f, 0.1f) };
                    FramePlan hi = FrameComposer.Compose(new FrameRequest(1080, 1920, LayoutSpec.Default, masks: masks));
                    if (hi.Watermark.MaskOverlap != 0) continue;
                    FramePlan lo = FrameComposer.Compose(new FrameRequest(canvas.X, canvas.Y, LayoutSpec.Default, masks: masks));
                    int rows = (int)((lo.Watermark.MaskOverlap + lo.Watermark.Rect.Width - 1) / lo.Watermark.Rect.Width);
                    Assert.True(rows <= bound, canvas.X + "x" + canvas.Y + " rows=" + rows + " bound=" + bound);
                    worstRows = Math.Max(worstRows, rows);
                }
            }
            Assert.True(worstRows >= 1, "the sweep must reach touching cases");
        }
    }
}
