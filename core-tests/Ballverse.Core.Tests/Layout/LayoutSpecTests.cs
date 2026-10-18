using System;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Xunit;

namespace Ballverse.Core.Tests.Layout
{
    public class LayoutSpecTests
    {
        [Fact]
        public void DefaultsMatchSpec()
        {
            LayoutSpec d = LayoutSpec.Default;
            Assert.Equal(LayoutType.L1, d.Type);
            Assert.False(d.Swap);
            Assert.Equal(0.5f, d.Ratio);
            Assert.Equal(6, d.DividerPx);
            Assert.Equal(24, d.CornerRadiusPx);
            Assert.Equal(0, d.PaddingPx);
            Assert.Same(PipSpec.Default, d.Pip);
        }

        [Fact]
        public void ConstructorDefaultsMatchDefaultSpec()
        {
            var s = new LayoutSpec(LayoutType.L1);
            Assert.Equal(LayoutSpec.Default.DividerPx, s.DividerPx);
            Assert.Equal(LayoutSpec.Default.CornerRadiusPx, s.CornerRadiusPx);
            Assert.Equal(LayoutSpec.Default.PaddingPx, s.PaddingPx);
            Assert.Equal(LayoutSpec.Default.Ratio, s.Ratio);
            Assert.Equal(6, s.DividerPx);
            Assert.Equal(24, s.CornerRadiusPx);
        }

        [Fact]
        public void ValuesAreClampedToRanges()
        {
            var s = new LayoutSpec(LayoutType.L2, true, 0.9f, 99, -3, 100);
            Assert.Equal(0.70f, s.Ratio);
            Assert.Equal(16, s.DividerPx);
            Assert.Equal(0, s.CornerRadiusPx);
            Assert.Equal(48, s.PaddingPx);
            Assert.Equal(0.30f, s.WithRatio(0.1f).Ratio);
            Assert.Equal(48, s.WithCornerRadius(60).CornerRadiusPx);
            Assert.Equal(0, s.WithPadding(-1).PaddingPx);
            Assert.Equal(0, s.WithDivider(-1).DividerPx);
        }

        [Fact]
        public void InvalidValuesThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new LayoutSpec((LayoutType)0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new LayoutSpec((LayoutType)6));
            Assert.Throws<ArgumentException>(() => new LayoutSpec(LayoutType.L1, false, float.NaN));
            Assert.Throws<ArgumentException>(() => new PipSpec(SlotShape.Circle, float.PositiveInfinity, FloatVec2.Zero));
            Assert.Throws<ArgumentException>(() => new PipSpec(SlotShape.Circle, 0.3f, new FloatVec2(float.NaN, 0f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PipSpec((SlotShape)7, 0.3f, FloatVec2.Zero));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PipSpec(SlotShape.Rect, 0.3f, FloatVec2.Zero));
        }

        [Fact]
        public void PipSpecClamps()
        {
            var p = new PipSpec(SlotShape.Circle, 0.9f, new FloatVec2(-1f, 2f));
            Assert.Equal(0.60f, p.SizeFraction);
            Assert.Equal(new FloatVec2(0f, 1f), p.Center);
            Assert.Equal(0.20f, p.WithSizeFraction(0.01f).SizeFraction);
            Assert.Equal(SlotShape.RoundedRect, p.WithShape(SlotShape.RoundedRect).Shape);
        }

        [Theory]
        [InlineData(LayoutType.L1, false, LayoutType.L1)]
        [InlineData(LayoutType.L1, true, LayoutType.L1)]
        [InlineData(LayoutType.L2, true, LayoutType.L2)]
        [InlineData(LayoutType.L3, false, LayoutType.L3)]
        [InlineData(LayoutType.L3, true, LayoutType.L5)]
        [InlineData(LayoutType.L4, true, LayoutType.L4)]
        [InlineData(LayoutType.L5, false, LayoutType.L5)]
        [InlineData(LayoutType.L5, true, LayoutType.L3)]
        public void EffectiveTypeResolvesSwap(LayoutType type, bool swap, LayoutType expected)
        {
            Assert.Equal(expected, new LayoutSpec(type, swap).EffectiveType);
        }

        [Theory]
        [InlineData(0.50f, 0.5f)]
        [InlineData(0.504f, 0.5f)]
        [InlineData(0.496f, 0.5f)]
        [InlineData(0.51f, 0.51f)]
        [InlineData(0.49f, 0.49f)]
        [InlineData(0.52f, 0.52f)]
        [InlineData(0.48f, 0.48f)]
        [InlineData(0.53f, 0.53f)]
        [InlineData(0.33f, 1f / 3f)]
        [InlineData(0.3283f, 1f / 3f)]
        [InlineData(0.334f, 1f / 3f)]
        [InlineData(0.32f, 0.32f)]
        [InlineData(0.34f, 0.34f)]
        [InlineData(0.35f, 0.35f)]
        [InlineData(0.31f, 0.31f)]
        [InlineData(0.30f, 0.30f)]
        [InlineData(0.65f, 0.65f)]
        [InlineData(0.66f, 0.66f)]
        [InlineData(0.67f, 2f / 3f)]
        [InlineData(0.6666f, 2f / 3f)]
        [InlineData(0.68f, 0.68f)]
        [InlineData(0.69f, 0.69f)]
        [InlineData(0.70f, 0.70f)]
        [InlineData(0.95f, 0.70f)]
        [InlineData(-3f, 0.30f)]
        [InlineData(0.444f, 0.44f)]
        [InlineData(0.4451f, 0.45f)]
        public void RatioMagnetsSnap(float raw, float expected)
        {
            Assert.Equal(expected, RatioMagnets.Snap(raw), 6);
        }

        [Fact]
        public void EveryOnePercentStepIsReachable()
        {
            // Spec §3.3: 30-70% in 1% steps; 33/50/67 are represented by the exact magnets 1/3, 1/2, 2/3.
            var reached = new bool[101];
            for (int i = 0; i <= 100000; i++)
            {
                float s = RatioMagnets.Snap(i / 100000f);
                reached[(int)Math.Round(s * 100.0)] = true;
            }
            for (int p = 30; p <= 70; p++) Assert.True(reached[p], "unreachable percent " + p);
            for (int p = 30; p <= 70; p++)
            {
                float s = RatioMagnets.Snap(p / 100f);
                float expected = p == 33 ? 1f / 3f : p == 50 ? 0.5f : p == 67 ? 2f / 3f : p / 100f;
                Assert.Equal(expected, s);
            }
        }

        [Fact]
        public void RatioMagnetsCustomThresholdAndValidation()
        {
            Assert.Equal(0.55f, RatioMagnets.Snap(0.55f, 0.02f), 5);
            Assert.Equal(0.5f, RatioMagnets.Snap(0.55f, 0.05f), 5);
            Assert.Equal(0.5f, RatioMagnets.Snap(0.52f, 0.02f), 5); // a stickier drag window is the caller's choice
            Assert.Equal(0.51f, RatioMagnets.Snap(0.51f, 0f), 5);
            Assert.Equal(0.5f, RatioMagnets.Snap(0.5f, 0f), 5);
            Assert.Equal(1f / 3f, RatioMagnets.Snap(0.33f, 0f), 6);
            Assert.Throws<ArgumentOutOfRangeException>(() => RatioMagnets.Snap(0.5f, -0.1f));
            Assert.Throws<ArgumentException>(() => RatioMagnets.Snap(float.NaN));
            Assert.Throws<ArgumentException>(() => RatioMagnets.Snap(0.5f, float.NaN));
            Assert.True(RatioMagnets.IsMagnet(RatioMagnets.Snap(0.33f)));
            Assert.False(RatioMagnets.IsMagnet(0.55f));
            Assert.Equal(3, RatioMagnets.Magnets.Length);
        }

        [Fact]
        public void SnappedValuesStayOnStepOrMagnet()
        {
            for (int i = 0; i <= 2000; i++)
            {
                float raw = 0.2f + i * 0.0003f;
                float s = RatioMagnets.Snap(raw);
                Assert.InRange(s, LayoutSpec.MinRatio, LayoutSpec.MaxRatio);
                bool onStep = Math.Abs(s * 100f - (float)Math.Round(s * 100f)) < 1e-3f;
                Assert.True(onStep || RatioMagnets.IsMagnet(s), "value " + s);
            }
        }
    }
}
