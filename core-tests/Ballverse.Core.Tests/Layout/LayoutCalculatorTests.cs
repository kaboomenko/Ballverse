using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Xunit;

namespace Ballverse.Core.Tests.Layout
{
    public class LayoutCalculatorTests
    {
        static readonly int[][] s_canvases =
        {
            new[] { 720, 1280 }, new[] { 1080, 1920 },
            new[] { 8, 8 }, new[] { 8, 15 }, new[] { 9, 16 }, new[] { 37, 1000 }, new[] { 101, 179 }, new[] { 333, 592 },
            new[] { 405, 720 }, new[] { 1001, 1777 }, new[] { 1179, 2096 }, new[] { 2000, 8 }, new[] { 8, 2000 }, new[] { 1920, 1080 },
        };

        static readonly int[] s_paddings = { 0, 1, 7, 24, 47, 48 };

        static IEnumerable<float> Ratios()
        {
            for (int p = 30; p <= 70; p++) yield return p / 100f;
            yield return 1f / 3f;
            yield return 2f / 3f;
            yield return 0.555f;
        }

        // ---- concrete numbers ---------------------------------------------------------------------------------

        [Fact]
        public void L1DefaultAt1080()
        {
            FrameLayout f = LayoutCalculator.ComputeSlots(1080, 1920, LayoutSpec.Default);
            Assert.Equal(LayoutType.L1, f.EffectiveType);
            Assert.Equal(new IntRect(0, 0, 1080, 957), f.Simulation.Rect);
            Assert.Equal(new IntRect(0, 957, 1080, 6), f.Divider);
            Assert.Equal(new IntRect(0, 963, 1080, 957), f.Video.Rect);
            Assert.Equal(24, f.Simulation.CornerRadius);
            Assert.Equal(24, f.Video.CornerRadius);
            Assert.Equal(SlotShape.RoundedRect, f.Simulation.Shape);
            Assert.True(f.HasVideo);
            Assert.True(f.HasDivider);
            Assert.False(f.HasWindow);
            Assert.Equal(new IntRect(0, 0, 1080, 1920), f.Content);
            Assert.Throws<InvalidOperationException>(() => f.Window);
        }

        [Fact]
        public void L1DefaultAt720()
        {
            FrameLayout f = LayoutCalculator.ComputeSlots(720, 1280, LayoutSpec.Default);
            Assert.Equal(new IntRect(0, 0, 720, 638), f.Simulation.Rect);
            Assert.Equal(new IntRect(0, 638, 720, 4), f.Divider);
            Assert.Equal(new IntRect(0, 642, 720, 638), f.Video.Rect);
            Assert.Equal(16, f.Simulation.CornerRadius);
        }

        [Fact]
        public void L1RatioAndSwap()
        {
            var spec = new LayoutSpec(LayoutType.L1, false, 0.55f, 6, 24, 0);
            FrameLayout f = LayoutCalculator.ComputeSlots(1080, 1920, spec);
            // S end = 1914 * 0.55 = 1052.7 -> 1053, divider end 1058.7 -> 1059
            Assert.Equal(new IntRect(0, 0, 1080, 1053), f.Simulation.Rect);
            Assert.Equal(new IntRect(0, 1053, 1080, 6), f.Divider);
            Assert.Equal(new IntRect(0, 1059, 1080, 861), f.Video.Rect);

            FrameLayout s = LayoutCalculator.ComputeSlots(1080, 1920, spec.WithSwap(true));
            Assert.Equal(new IntRect(0, 0, 1080, 861), s.Video.Rect);
            Assert.Equal(new IntRect(0, 861, 1080, 6), s.Divider);
            Assert.Equal(new IntRect(0, 867, 1080, 1053), s.Simulation.Rect);
        }

        [Fact]
        public void L2WithPaddingAndMagnetRatio()
        {
            var spec = new LayoutSpec(LayoutType.L2, false, 1f / 3f, 6, 24, 48);
            FrameLayout f = LayoutCalculator.ComputeSlots(1080, 1920, spec);
            Assert.Equal(LayoutType.L2, f.EffectiveType);
            // S end = 48 + 978 * 0.3333 = 373.97 -> 374; divider end 379.97 -> 380
            Assert.Equal(IntRect.FromEdges(48, 48, 374, 1872), f.Simulation.Rect);
            Assert.Equal(IntRect.FromEdges(374, 48, 380, 1872), f.Divider);
            Assert.Equal(IntRect.FromEdges(380, 48, 1032, 1872), f.Video.Rect);
            Assert.Equal(IntRect.FromEdges(48, 48, 1032, 1872), f.Content);

            FrameLayout s = LayoutCalculator.ComputeSlots(1080, 1920, spec.WithSwap(true));
            Assert.Equal(IntRect.FromEdges(48, 48, 700, 1872), s.Video.Rect);
            Assert.Equal(IntRect.FromEdges(706, 48, 1032, 1872), s.Simulation.Rect);
        }

        [Fact]
        public void ZeroDividerHasNoDividerRect()
        {
            FrameLayout f = LayoutCalculator.ComputeSlots(1080, 1920, new LayoutSpec(LayoutType.L1, false, 0.5f, 0, 0, 0));
            Assert.False(f.HasDivider);
            Assert.Equal(IntRect.Empty, f.Divider);
            Assert.Equal(new IntRect(0, 0, 1080, 960), f.Simulation.Rect);
            Assert.Equal(new IntRect(0, 960, 1080, 960), f.Video.Rect);
            Assert.Equal(0, f.Simulation.CornerRadius);
        }

        [Fact]
        public void L4IsSimulationOnly()
        {
            FrameLayout f = LayoutCalculator.ComputeSlots(720, 1280, new LayoutSpec(LayoutType.L4, true, 0.5f, 16, 48, 48));
            Assert.Equal(LayoutType.L4, f.EffectiveType);
            Assert.False(f.HasVideo);
            Assert.False(f.HasDivider);
            Assert.False(f.HasWindow);
            Assert.Equal(new SlotGeometry(SlotKind.Simulation, new IntRect(0, 0, 720, 1280), SlotShape.Rect, 0, 0), f.Simulation);
            Assert.Single(f.SlotsBackToFront);
        }

        [Fact]
        public void L3PutsVideoInWindow()
        {
            var pip = new PipSpec(SlotShape.RoundedRect, 0.35f, new FloatVec2(0.75f, 0.25f));
            FrameLayout f = LayoutCalculator.ComputeSlots(1080, 1920, new LayoutSpec(LayoutType.L3, false, 0.5f, 6, 24, 0, pip));
            Assert.Equal(LayoutType.L3, f.EffectiveType);
            Assert.True(f.HasWindow);
            Assert.Equal(new SlotGeometry(SlotKind.Simulation, new IntRect(0, 0, 1080, 1920), SlotShape.Rect, 0, 0), f.Simulation);
            // 378 x 672 centered at (810, 480)
            Assert.Equal(new SlotGeometry(SlotKind.Video, new IntRect(621, 144, 378, 672), SlotShape.RoundedRect, 24, 1), f.Video);
            Assert.Equal(f.Video, f.Window);
            Assert.Equal(SlotKind.Simulation, f.SlotsBackToFront[0].Kind);
            Assert.Equal(SlotKind.Video, f.SlotsBackToFront[1].Kind);
        }

        [Fact]
        public void L5AndSwappedL3PutSimulationInWindow()
        {
            var pip = new PipSpec(SlotShape.Circle, 0.4f, new FloatVec2(0.5f, 0.5f));
            FrameLayout l5 = LayoutCalculator.ComputeSlots(1080, 1920, new LayoutSpec(LayoutType.L5, false, 0.5f, 0, 24, 0, pip));
            FrameLayout l3s = LayoutCalculator.ComputeSlots(1080, 1920, new LayoutSpec(LayoutType.L3, true, 0.5f, 0, 24, 0, pip));
            foreach (FrameLayout f in new[] { l5, l3s })
            {
                Assert.Equal(LayoutType.L5, f.EffectiveType);
                Assert.Equal(new IntRect(0, 0, 1080, 1920), f.Video.Rect);
                Assert.Equal(0, f.Video.ZIndex);
                Assert.Equal(new SlotGeometry(SlotKind.Simulation, new IntRect(324, 744, 432, 432), SlotShape.Circle, 216, 1), f.Simulation);
                Assert.Equal(SlotKind.Video, f.SlotsBackToFront[0].Kind);
                Assert.Equal(SlotKind.Simulation, f.Window.Kind);
            }
            FrameLayout l5s = LayoutCalculator.ComputeSlots(1080, 1920, new LayoutSpec(LayoutType.L5, true, 0.5f, 0, 24, 0, pip));
            Assert.Equal(LayoutType.L3, l5s.EffectiveType);
            Assert.Equal(SlotKind.Video, l5s.Window.Kind);
        }

        [Fact]
        public void WindowIsClampedInsideCanvas()
        {
            foreach (var c in s_canvases)
            {
                foreach (SlotShape shape in new[] { SlotShape.RoundedRect, SlotShape.Circle })
                {
                    foreach (float size in new[] { 0.2f, 0.37f, 0.6f })
                    {
                        foreach (var center in new[] { new FloatVec2(0f, 0f), new FloatVec2(1f, 1f), new FloatVec2(0f, 1f), new FloatVec2(0.5f, 0.5f) })
                        {
                            var pip = new PipSpec(shape, size, center);
                            IntRect r = LayoutCalculator.PipWindowRect(c[0], c[1], pip);
                            Assert.True(new IntRect(0, 0, c[0], c[1]).Contains(r), r + " in " + c[0] + "x" + c[1]);
                            Assert.False(r.IsEmpty);
                            if (shape == SlotShape.Circle) Assert.Equal(r.Width, r.Height);
                        }
                    }
                }
            }
        }

        [Fact]
        public void WindowKeepsCanvasAspect()
        {
            IntVec2 s1080 = LayoutCalculator.PipWindowSize(1080, 1920, new PipSpec(SlotShape.RoundedRect, 0.5f, FloatVec2.Zero));
            Assert.Equal(new IntVec2(540, 960), s1080);
            IntVec2 s720 = LayoutCalculator.PipWindowSize(720, 1280, new PipSpec(SlotShape.RoundedRect, 0.3f, FloatVec2.Zero));
            Assert.Equal(new IntVec2(216, 384), s720);
            IntVec2 wide = LayoutCalculator.PipWindowSize(2000, 100, new PipSpec(SlotShape.Circle, 0.6f, FloatVec2.Zero));
            Assert.Equal(new IntVec2(100, 100), wide);
        }

        [Fact]
        public void RoundedWindowRadiusIsClamped()
        {
            var pip = new PipSpec(SlotShape.RoundedRect, 0.2f, FloatVec2.Zero);
            FrameLayout f = LayoutCalculator.ComputeSlots(40, 71, new LayoutSpec(LayoutType.L3, false, 0.5f, 0, 48, 0, pip));
            Assert.Equal(new IntRect(0, 0, 8, 14), f.Window.Rect);
            Assert.Equal(2, f.Window.CornerRadius); // 48 * 40/1080 = 1.78 -> 2, <= 8/2
            FrameLayout g = LayoutCalculator.ComputeSlots(8, 2000, new LayoutSpec(LayoutType.L1, false, 0.5f, 0, 48, 0));
            Assert.True(g.Simulation.CornerRadius <= g.Simulation.Rect.Width / 2);
        }

        [Fact]
        public void PipCenterRoundTrips()
        {
            var rng = Rng.FromSeed(77, 3);
            for (int i = 0; i < 500; i++)
            {
                int w = rng.NextInt(8, 3000), h = rng.NextInt(8, 3000);
                var pip = new PipSpec(rng.NextBool() ? SlotShape.Circle : SlotShape.RoundedRect, rng.NextFloat(0.2f, 0.6f),
                    new FloatVec2(rng.NextFloat(-0.2f, 1.2f), rng.NextFloat(-0.2f, 1.2f)));
                IntRect r = LayoutCalculator.PipWindowRect(w, h, pip);
                IntRect again = LayoutCalculator.PipWindowRect(w, h, pip.WithCenter(LayoutCalculator.PipCenterOf(r, w, h)));
                Assert.Equal(r, again);
            }
        }

        [Fact]
        public void InvalidArgumentsThrow()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => LayoutCalculator.ComputeSlots(7, 100, LayoutSpec.Default));
            Assert.Throws<ArgumentOutOfRangeException>(() => LayoutCalculator.ComputeSlots(100, 0, LayoutSpec.Default));
            Assert.Throws<ArgumentOutOfRangeException>(() => LayoutCalculator.ComputeSlots(100, 16385, LayoutSpec.Default));
            Assert.Throws<ArgumentNullException>(() => LayoutCalculator.ComputeSlots(100, 100, null));
            Assert.Throws<ArgumentNullException>(() => LayoutCalculator.PipWindowSize(100, 100, null));
        }

        [Fact]
        public void QuantizeRatio()
        {
            Assert.Equal(5000, LayoutCalculator.QuantizeRatio(0.5f));
            Assert.Equal(3333, LayoutCalculator.QuantizeRatio(1f / 3f));
            Assert.Equal(6667, LayoutCalculator.QuantizeRatio(2f / 3f));
            Assert.Equal(7000, LayoutCalculator.QuantizeRatio(0.9f));
            Assert.Equal(3000, LayoutCalculator.QuantizeRatio(0f));
        }

        // ---- exhaustive tiling ----------------------------------------------------------------------------------

        [Fact]
        public void SplitLayoutsTileCanvasExactly()
        {
            int checkedCount = 0;
            foreach (var c in s_canvases)
            {
                foreach (LayoutType type in new[] { LayoutType.L1, LayoutType.L2 })
                {
                    foreach (bool swap in new[] { false, true })
                    {
                        foreach (float ratio in Ratios())
                        {
                            for (int div = 0; div <= LayoutSpec.MaxDividerPx; div++)
                            {
                                foreach (int pad in s_paddings)
                                {
                                    var spec = new LayoutSpec(type, swap, ratio, div, 24, pad);
                                    AssertTiles(c[0], c[1], spec, LayoutCalculator.ComputeSlots(c[0], c[1], spec));
                                    checkedCount++;
                                }
                            }
                        }
                    }
                }
            }
            Assert.True(checkedCount > 100000);
        }

        [Fact]
        public void SplitLayoutsTileRandomCanvases()
        {
            var rng = Rng.FromSeed(2024, 7);
            for (int i = 0; i < 3000; i++)
            {
                int w = rng.NextInt(8, 4000), h = rng.NextInt(8, 4000);
                var spec = new LayoutSpec(rng.NextBool() ? LayoutType.L1 : LayoutType.L2, rng.NextBool(), rng.NextFloat(0.3f, 0.7f),
                    rng.NextInt(0, 17), rng.NextInt(0, 49), rng.NextInt(0, 49));
                AssertTiles(w, h, spec, LayoutCalculator.ComputeSlots(w, h, spec));
            }
        }

        static void AssertTiles(int w, int h, LayoutSpec spec, FrameLayout f)
        {
            var canvas = new IntRect(0, 0, w, h);
            IntRect s = f.Simulation.Rect, v = f.Video.Rect, d = f.Divider, content = f.Content;
            string ctx = w + "x" + h + " " + spec.Type + " swap=" + spec.Swap + " r=" + spec.Ratio + " d=" + spec.DividerPx + " p=" + spec.PaddingPx;

            Assert.True(f.HasVideo, ctx);
            Assert.True(s.Width >= 1 && s.Height >= 1, ctx);
            Assert.True(v.Width >= 1 && v.Height >= 1, ctx);
            // Padding frame is symmetric and the content is canvas minus padding.
            int pad = content.X;
            Assert.Equal(IntRect.FromEdges(pad, pad, w - pad, h - pad), content);
            Assert.True(canvas.Contains(content), ctx);
            // Slots and divider are inside the content, pairwise disjoint and their areas sum to it: exact tiling.
            Assert.True(content.Contains(s) && content.Contains(v) && (d.IsEmpty || content.Contains(d)), ctx);
            Assert.False(s.Intersects(v), ctx);
            Assert.False(s.Intersects(d), ctx);
            Assert.False(v.Intersects(d), ctx);
            Assert.Equal(content.Area, s.Area + v.Area + d.Area);
            bool stacked = spec.Type == LayoutType.L1;
            if (stacked)
            {
                Assert.Equal(s.Width, w - 2 * pad);
                Assert.Equal(v.Width, w - 2 * pad);
                IntRect first = spec.Swap ? v : s;
                IntRect second = spec.Swap ? s : v;
                Assert.Equal(pad, first.Y);
                Assert.Equal(first.Bottom + d.Height, second.Y);
                Assert.Equal(h - pad, second.Bottom);
            }
            else
            {
                Assert.Equal(s.Height, h - 2 * pad);
                IntRect first = spec.Swap ? v : s;
                IntRect second = spec.Swap ? s : v;
                Assert.Equal(pad, first.X);
                Assert.Equal(first.Right + d.Width, second.X);
                Assert.Equal(w - pad, second.Right);
            }
        }

        [Fact]
        public void SwapMirrorsAlongSplitAxis()
        {
            foreach (var c in s_canvases)
            {
                foreach (LayoutType type in new[] { LayoutType.L1, LayoutType.L2 })
                {
                    foreach (float ratio in Ratios())
                    {
                        var spec = new LayoutSpec(type, false, ratio, 5, 10, 7);
                        FrameLayout a = LayoutCalculator.ComputeSlots(c[0], c[1], spec);
                        FrameLayout b = LayoutCalculator.ComputeSlots(c[0], c[1], spec.WithSwap(true));
                        if (type == LayoutType.L1)
                        {
                            Assert.Equal(a.Simulation.Rect.MirrorY(c[1]), b.Simulation.Rect);
                            Assert.Equal(a.Video.Rect.MirrorY(c[1]), b.Video.Rect);
                            Assert.Equal(a.HasDivider ? a.Divider.MirrorY(c[1]) : IntRect.Empty, b.Divider);
                        }
                        else
                        {
                            Assert.Equal(a.Simulation.Rect.MirrorX(c[0]), b.Simulation.Rect);
                            Assert.Equal(a.Video.Rect.MirrorX(c[0]), b.Video.Rect);
                            Assert.Equal(a.HasDivider ? a.Divider.MirrorX(c[0]) : IntRect.Empty, b.Divider);
                        }
                    }
                }
            }
        }

        [Fact]
        public void SimulationShareFollowsRatio()
        {
            foreach (float ratio in Ratios())
            {
                var spec = new LayoutSpec(LayoutType.L1, false, ratio, 6, 0, 0);
                FrameLayout f = LayoutCalculator.ComputeSlots(1080, 1920, spec);
                double exact = (1920 - 6) * (double)LayoutCalculator.QuantizeRatio(ratio) / 10000.0;
                Assert.True(Math.Abs(f.Simulation.Rect.Height - exact) <= 0.5 + 1e-9, "ratio " + ratio);
            }
        }

        // ---- 720 vs 1080 proportional consistency ---------------------------------------------------------------

        [Fact]
        public void SplitLayoutsAgreeBetween720And1080WithinOnePixel()
        {
            foreach (LayoutType type in new[] { LayoutType.L1, LayoutType.L2 })
            {
                foreach (bool swap in new[] { false, true })
                {
                    foreach (float ratio in Ratios())
                    {
                        for (int div = 0; div <= LayoutSpec.MaxDividerPx; div++)
                        {
                            for (int pad = 0; pad <= LayoutSpec.MaxPaddingPx; pad++)
                            {
                                var spec = new LayoutSpec(type, swap, ratio, div, 24, pad);
                                FrameLayout hi = LayoutCalculator.ComputeSlots(1080, 1920, spec);
                                FrameLayout lo = LayoutCalculator.ComputeSlots(720, 1280, spec);
                                AssertProportional(hi.Simulation.Rect, lo.Simulation.Rect);
                                AssertProportional(hi.Video.Rect, lo.Video.Rect);
                                if (lo.HasDivider && hi.HasDivider) AssertProportional(hi.Divider, lo.Divider);
                                else Assert.True(div * 2.0 / 3.0 <= 1.0, "divider vanished at 720: " + div);
                                Assert.True(Math.Abs(hi.Simulation.CornerRadius * 2.0 / 3.0 - lo.Simulation.CornerRadius) <= 1.0);
                            }
                        }
                    }
                }
            }
        }

        [Fact]
        public void WindowLayoutsAgreeBetween720And1080WithinOnePixel()
        {
            var rng = Rng.FromSeed(99, 2);
            for (int i = 0; i < 2000; i++)
            {
                var pip = new PipSpec(rng.NextBool() ? SlotShape.Circle : SlotShape.RoundedRect, rng.NextFloat(0.2f, 0.6f),
                    new FloatVec2(rng.NextFloat(0f, 1f), rng.NextFloat(0f, 1f)));
                var spec = new LayoutSpec(rng.NextBool() ? LayoutType.L3 : LayoutType.L5, rng.NextBool(), 0.5f, 0, rng.NextInt(0, 49), 0, pip);
                FrameLayout hi = LayoutCalculator.ComputeSlots(1080, 1920, spec);
                FrameLayout lo = LayoutCalculator.ComputeSlots(720, 1280, spec);
                AssertProportional(hi.Window.Rect, lo.Window.Rect);
                Assert.Equal(hi.EffectiveType, lo.EffectiveType);
            }
        }

        static void AssertProportional(IntRect hi, IntRect lo)
        {
            const double k = 720.0 / 1080.0;
            Assert.True(Math.Abs(hi.X * k - lo.X) <= 1.0, hi + " vs " + lo);
            Assert.True(Math.Abs(hi.Y * k - lo.Y) <= 1.0, hi + " vs " + lo);
            Assert.True(Math.Abs(hi.Right * k - lo.Right) <= 1.0, hi + " vs " + lo);
            Assert.True(Math.Abs(hi.Bottom * k - lo.Bottom) <= 1.0, hi + " vs " + lo);
        }

        [Fact]
        public void ComputeSlotsIsDeterministic()
        {
            var spec = new LayoutSpec(LayoutType.L2, true, 0.61f, 9, 33, 17);
            FrameLayout a = LayoutCalculator.ComputeSlots(1001, 1777, spec);
            FrameLayout b = LayoutCalculator.ComputeSlots(1001, 1777, spec);
            Assert.Equal(a.Simulation, b.Simulation);
            Assert.Equal(a.Video, b.Video);
            Assert.Equal(a.Divider, b.Divider);
        }
    }
}
