using System;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Xunit;

namespace Ballverse.Core.Tests.Layout
{
    public class TextAndCanvasTests
    {
        [Fact]
        public void TextAnchorAndSizeAt1080()
        {
            TextPlacement t = TextLayerGeometry.Place(1080, 1920, new FloatVec2(0.5f, 0.12f), 72f, 4f);
            Assert.Equal(540f, t.Anchor.X, 3);
            Assert.Equal(230.4f, t.Anchor.Y, 3);
            Assert.Equal(new IntVec2(540, 230), t.AnchorPixel);
            Assert.Equal(72f, t.FontSizePx, 4);
            Assert.Equal(4f, t.StrokePx, 4);
            Assert.Equal(1f, t.Scale, 6);
        }

        [Fact]
        public void TextScalesTo720()
        {
            TextPlacement t = TextLayerGeometry.Place(720, 1280, new FloatVec2(0.5f, 0.12f), 72f, 4f);
            Assert.Equal(new IntVec2(360, 154), t.AnchorPixel); // 153.6
            Assert.Equal(48f, t.FontSizePx, 4);
            Assert.Equal(8f / 3f, t.StrokePx, 4);
            Assert.Equal(2f / 3f, t.Scale, 6);
        }

        [Fact]
        public void TextClampsPositionAndSizes()
        {
            TextPlacement t = TextLayerGeometry.Place(1080, 1920, new FloatVec2(1.5f, -1f), 500f, 20f);
            Assert.Equal(new FloatVec2(1080f, 0f), t.Anchor);
            Assert.Equal(new IntVec2(1079, 0), t.AnchorPixel);
            Assert.Equal(160f, t.FontSizePx, 4);
            Assert.Equal(8f, t.StrokePx, 4);
            TextPlacement small = TextLayerGeometry.Place(1080, 1920, new FloatVec2(0f, 1f), 1f, -3f);
            Assert.Equal(12f, small.FontSizePx, 4);
            Assert.Equal(0f, small.StrokePx);
            Assert.Equal(new IntVec2(0, 1919), small.AnchorPixel);
        }

        [Fact]
        public void TextRejectsNonFinite()
        {
            Assert.Throws<ArgumentException>(() => TextLayerGeometry.Place(1080, 1920, new FloatVec2(float.NaN, 0f), 72f));
            Assert.Throws<ArgumentException>(() => TextLayerGeometry.Place(1080, 1920, FloatVec2.Zero, float.PositiveInfinity));
            Assert.Throws<ArgumentOutOfRangeException>(() => TextLayerGeometry.Place(0, 1920, FloatVec2.Zero, 72f));
        }

        [Fact]
        public void TextNormalizeAndKeepInside()
        {
            Assert.Equal(new FloatVec2(0.5f, 0.25f), TextLayerGeometry.ToNormalized(new FloatVec2(540f, 480f), 1080, 1920));
            Assert.Equal(new FloatVec2(1f, 0f), TextLayerGeometry.ToNormalized(new FloatVec2(5000f, -3f), 1080, 1920));
            Assert.Equal(new IntRect(880, 0, 200, 50), TextLayerGeometry.KeepInside(new IntRect(950, -10, 200, 50), 1080, 1920));
            Assert.Equal(5, TextLayerGeometry.MaxLayers);
        }

        [Fact]
        public void ExportCanvasSizes()
        {
            Assert.Equal(new IntVec2(720, 1280), OutputCanvas.ForExport(ExportResolution.P720));
            Assert.Equal(new IntVec2(1080, 1920), OutputCanvas.ForExport(ExportResolution.P1080));
            Assert.Throws<ArgumentOutOfRangeException>(() => OutputCanvas.ForExport((ExportResolution)480));
        }

        [Fact]
        public void PreviewCanvasFitsNineBySixteen()
        {
            Assert.Equal(new IntVec2(562, 999), OutputCanvas.FitPreview(1000, 1000));
            Assert.Equal(new IntVec2(400, 711), OutputCanvas.FitPreview(400, 2000));
            Assert.Equal(new IntVec2(1080, 1920), OutputCanvas.FitPreview(1080, 1920));
            Assert.Equal(new IntVec2(1080, 1920), OutputCanvas.FitPreview(1500, 1920));
            Assert.Throws<ArgumentOutOfRangeException>(() => OutputCanvas.FitPreview(5, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => OutputCanvas.FitPreview(1000, 10));
        }

        [Fact]
        public void SafeAreaInsetsByScaledMargin()
        {
            Assert.Equal(IntRect.FromEdges(32, 32, 1048, 1888), SafeArea.Rect(1080, 1920));
            Assert.Equal(IntRect.FromEdges(21, 21, 699, 1259), SafeArea.Rect(720, 1280));
            Assert.False(SafeArea.Rect(8, 8).IsEmpty);
        }
    }
}
