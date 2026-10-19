using System;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Xunit;
using static Ballverse.Core.Tests.RenderGeometry.GeometryTestUtil;

namespace Ballverse.Core.Tests.RenderGeometry
{
    public class ShapeGeometryCacheTests
    {
        static readonly ShapeDefinition Ring = ShapeDefinition.Ring(Q.QuantizeScale(2f), Q.QuantizeScale(0.12f), new[] { RingGap.FromRadians(0f, 0.5f) });
        static readonly ShapeDefinition FixedRing = ShapeDefinition.Ring(Q.QuantizeScale(2f), Q.QuantizeScale(0.12f), new[] { RingGap.FromRadians(0f, 0.5f) }, fixedThickness: true);
        static readonly ShapeDefinition Square = ShapeDefinition.Polygon(Pts(0f, 0f, 1f, 0f, 1f, 1f, 0f, 1f));

        static ShapeGeometryCache NewCache(int capacity = 64)
        {
            var c = new ShapeGeometryCache(capacity);
            c.Reset(Shapes(ShapeDefinition.Circle, Ring, FixedRing, Square), Q);
            return c;
        }

        [Fact]
        public void SharedEntriesAreReusedAcrossScalesInTheSameBucket()
        {
            var cache = NewCache();
            var s = TessellationSettings.Create(120f);
            cache.BeginFrame();
            LocalGeometry a = cache.Get(1, Q.UnitScale, s, out float drawA, out _);
            LocalGeometry b = cache.Get(1, Q.QuantizeScale(0.9f), s, out float drawB, out _);
            Assert.Same(a, b);
            Assert.Equal(1, cache.BuildCount);
            Assert.Equal(Q.DequantizeScale(Q.UnitScale), drawA);
            Assert.Equal(Q.DequantizeScale(Q.QuantizeScale(0.9f)), drawB);
            // A much larger scale needs a finer tessellation: another bucket.
            LocalGeometry c = cache.Get(1, Q.QuantizeScale(4f), s, out _, out _);
            Assert.NotSame(a, c);
            Assert.True(c.VertexCount > a.VertexCount);
        }

        [Fact]
        public void SharedTessellationMeetsToleranceAtTheDrawnScale()
        {
            var cache = NewCache();
            float ppm = 120f;
            var s = TessellationSettings.Create(ppm, 0.25f);
            cache.BeginFrame();
            float scale = 1.7f;
            LocalGeometry g = cache.Get(1, Q.QuantizeScale(scale), s, out float drawScale, out _);
            // Outer vertices at unit scale, drawn ×drawScale: chord error in pixels ≤ tolerance.
            ReadOnlySpan<Float2> p = g.Positions;
            double outer = 2.06 * drawScale;
            double worst = 0;
            for (int i = 1; i + 2 < p.Length && i < 200; i += 2)
            {
                double mid = ((p[i] + p[i + 2]) * 0.5f).Length * drawScale;
                if (Math.Abs(p[i].Length * drawScale - outer) > 1e-3 || Math.Abs(p[i + 2].Length * drawScale - outer) > 1e-3) continue;
                worst = Math.Max(worst, (outer - mid) * ppm);
            }
            Assert.True(worst <= 0.25 + 1e-3, "chord error " + worst + " px");
        }

        [Fact]
        public void FixedThicknessEntriesAreKeyedByExactScaleAndDrawnAtScaleOne()
        {
            var cache = NewCache();
            var s = TessellationSettings.Create(120f);
            cache.BeginFrame();
            LocalGeometry a = cache.Get(2, Q.QuantizeScale(0.5f), s, out float drawScale, out _);
            Assert.Equal(1f, drawScale);
            a.TryGetBounds(out _, out _, out float maxX, out _);
            Assert.Equal(1.06f, maxX, 2);   // radius 1 + 0.06 (thickness kept)
            LocalGeometry b = cache.Get(2, Q.QuantizeScale(0.5f), s, out _, out _);
            Assert.Same(a, b);
            cache.Get(2, Q.QuantizeScale(0.49f), s, out _, out _);
            Assert.Equal(2, cache.BuildCount);
        }

        [Fact]
        public void UnusedEntriesAreEvictedAfterTheirKeepTime()
        {
            var cache = NewCache();
            var s = TessellationSettings.Create(120f);
            cache.BeginFrame();
            cache.Get(2, Q.QuantizeScale(0.5f), s, out _, out _);   // fixed: short-lived
            cache.Get(3, Q.UnitScale, s, out _, out _);             // polygon: long-lived
            Assert.Equal(2, cache.Count);
            for (int i = 0; i < ShapeGeometryCache.FixedScaleKeepFrames + 1; i++) cache.BeginFrame();
            Assert.Equal(1, cache.Count);
            for (int i = 0; i < ShapeGeometryCache.SharedKeepFrames; i++) cache.BeginFrame();
            Assert.Equal(0, cache.Count);
        }

        [Fact]
        public void FullCacheEvictsLeastRecentlyUsedOrFallsBackToScratch()
        {
            var cache = NewCache(capacity: 2);
            var s = TessellationSettings.Create(120f);
            cache.BeginFrame();
            cache.Get(2, Q.QuantizeScale(0.5f), s, out _, out _);
            cache.Get(2, Q.QuantizeScale(0.6f), s, out _, out _);
            // Same frame, both in use: a third shape is still returned (uncached scratch), nothing is corrupted.
            LocalGeometry third = cache.Get(2, Q.QuantizeScale(0.7f), s, out _, out _);
            Assert.True(third.IndexCount > 0);
            Assert.Equal(2, cache.Count);
            cache.BeginFrame();
            LocalGeometry fourth = cache.Get(3, Q.UnitScale, s, out _, out _);
            Assert.Equal(6, fourth.IndexCount);
            Assert.Equal(2, cache.Count);
        }

        [Fact]
        public void StyleChangeFlushesAndZoomChangeOnlyRebucketsCurves()
        {
            var cache = NewCache();
            cache.BeginFrame();
            cache.Get(3, Q.UnitScale, TessellationSettings.Create(120f), out _, out _);
            cache.Get(3, Q.UnitScale, TessellationSettings.Create(1000f), out _, out _);   // polygon ignores zoom
            Assert.Equal(1, cache.BuildCount);
            cache.Get(3, Q.UnitScale, TessellationSettings.Create(120f, 0.5f), out _, out _); // new tolerance: flush
            Assert.Equal(2, cache.BuildCount);
            Assert.Equal(1, cache.Count);
        }

        [Fact]
        public void SteadyStateDoesNotAllocate()
        {
            var cache = NewCache(256);
            var s = TessellationSettings.Create(120f);
            for (int f = 0; f < 10; f++)
            {
                cache.BeginFrame();
                for (int i = 0; i < 20; i++) cache.Get(2, (ushort)(Q.QuantizeScale(0.5f) + f * 20 + i), s, out _, out _);
                cache.Get(1, Q.UnitScale, s, out _, out _);
            }
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int f = 10; f < 200; f++)
            {
                cache.BeginFrame();
                // A shrinking fixed-thickness ring: a new scale every frame (cache miss + eviction each frame).
                for (int i = 0; i < 20; i++) cache.Get(2, (ushort)(Q.QuantizeScale(0.5f) + (f % 10) * 20 + i), s, out _, out _);
                cache.Get(1, Q.UnitScale, s, out _, out _);
                cache.Get(3, Q.UnitScale, s, out _, out _);
            }
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Fact]
        public void PixelScaleBucketIsCeilLog2()
        {
            Assert.Equal(7, ShapeGeometryCache.PixelScaleBucket(120f));
            Assert.Equal(7, ShapeGeometryCache.PixelScaleBucket(128f));
            Assert.Equal(8, ShapeGeometryCache.PixelScaleBucket(129f));
            Assert.Equal(-8, ShapeGeometryCache.PixelScaleBucket(0f));
            Assert.Equal(20, ShapeGeometryCache.PixelScaleBucket(1e9f));
        }

        [Fact]
        public void GetBeforeResetOrWithBadIdThrows()
        {
            var cache = new ShapeGeometryCache(4);
            Assert.Throws<InvalidOperationException>(() => cache.Get(0, 1, TessellationSettings.Create(1f), out _, out _));
            cache.Reset(Shapes(ShapeDefinition.Circle), Q);
            Assert.Throws<ArgumentOutOfRangeException>(() => cache.Get(5, 1, TessellationSettings.Create(1f), out _, out _));
        }
    }
}
