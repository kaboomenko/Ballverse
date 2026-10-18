using System;
using System.IO;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using Xunit;
using static Ballverse.Core.Tests.RenderGeometry.GeometryTestUtil;

namespace Ballverse.Core.Tests.RenderGeometry
{
    public class VfxTests
    {
        static readonly System.Collections.Generic.List<ShapeDefinition> S = Shapes(ShapeDefinition.Circle,
            ShapeDefinition.Ring(Q.QuantizeScale(2f), Q.QuantizeScale(0.12f), new[] { RingGap.FromRadians(0f, 0.6f) }));

        static VfxSettings Settings(ImpactFx impact = ImpactFx.Sparks, float factor = 1f)
        {
            var p = new uint[256];
            new LookSettings().ResolvePalette(p);
            return new VfxSettings { Impact = impact, CountFactor = factor, Palette = p };
        }

        static RenderEvent[] SampleEvents()
        {
            return new[]
            {
                RenderEvent.Hit(10, 0, 1f, 2f, 0f, 1f, 3, 0.8f, 1, 0.3f, 7, 2, 0.5f),
                RenderEvent.Break(12, 0, 7, 1, 0f, 0f, 0.3f, 1f, PaletteSlots.Arena, 1f),
                RenderEvent.Create(12, RenderEventType.Spawn, 1, 0.5f, 0.5f, 2, 1f),
                RenderEvent.Create(20, RenderEventType.Finish, 0, 0f, 3f, 4, 1f),
            };
        }

        [Fact]
        public void EvaluationIsDeterministicAndHistoryFree()
        {
            RenderEvent[] events = SampleEvents();
            var a = new Particle[600];
            var b = new Particle[600];
            var vfx = new VfxSystem();
            int na = vfx.Evaluate(events, 30, Settings(), S, Q, a, 600);
            // Evaluate other ticks in between: no state carries over.
            for (int t = 0; t < 30; t++) vfx.Evaluate(events, t, Settings(), S, Q, b, 600);
            int nb = new VfxSystem().Evaluate(events, 30, Settings(), S, Q, b, 600);
            Assert.True(na > 0);
            Assert.Equal(na, nb);
            for (int i = 0; i < na; i++) Assert.Equal(a[i], b[i]);
        }

        [Fact]
        public void ParticlesMoveContinuouslyAndExpire()
        {
            var ev = new[] { RenderEvent.Hit(0, 0, 0f, 0f, 0f, 1f, 1, 1f, 1, 0.3f, -1, -1, 0f) };
            var p1 = new Particle[64];
            var p2 = new Particle[64];
            var vfx = new VfxSystem();
            int n1 = vfx.Evaluate(ev, 5, Settings(), S, Q, p1, 64);
            int n2 = vfx.Evaluate(ev, 6, Settings(), S, Q, p2, 64);
            Assert.Equal(n1, n2);
            for (int i = 0; i < n1; i++)
            {
                if (p1[i].Shape != ParticleShape.Streak) continue;
                float d = MathF.Sqrt((p1[i].X - p2[i].X) * (p1[i].X - p2[i].X) + (p1[i].Y - p2[i].Y) * (p1[i].Y - p2[i].Y));
                Assert.True(d < 0.25f, "jump of " + d + " m in one tick");
                Assert.True(p2[i].Y >= 0f || p2[i].VY < 0f);
            }
            Assert.Equal(0, vfx.Evaluate(ev, 60, Settings(), S, Q, p1, 64));   // sparks live ≤ 0.5 s
        }

        [Fact]
        public void LowerQualityShowsASubsetOfTheSameParticles()
        {
            var ev = new[] { RenderEvent.Hit(0, 3, 0f, 0f, 1f, 0f, 1, 1f, 1, 0.1f, -1, -1, 0f) };
            var full = new Particle[64];
            var half = new Particle[64];
            var vfx = new VfxSystem();
            int nf = vfx.Evaluate(ev, 3, Settings(ImpactFx.Sparks, 1f), S, Q, full, 64);
            int nh = vfx.Evaluate(ev, 3, Settings(ImpactFx.Sparks, 0.5f), S, Q, half, 64);
            Assert.True(nh < nf);
            for (int i = 0; i < nh; i++) Assert.Equal(full[i], half[i]);
        }

        [Fact]
        public void SeedsDifferPerEventAndImpactFxSelectsTheRecipe()
        {
            var a = new[] { RenderEvent.Hit(5, 0, 0f, 0f, 0f, 1f, 1, 1f, 1, 0.3f, -1, -1, 0f) };
            var b = new[] { RenderEvent.Hit(5, 1, 0f, 0f, 0f, 1f, 1, 1f, 1, 0.3f, -1, -1, 0f) };
            var pa = new Particle[64];
            var pb = new Particle[64];
            var vfx = new VfxSystem();
            vfx.Evaluate(a, 8, Settings(), S, Q, pa, 64);
            vfx.Evaluate(b, 8, Settings(), S, Q, pb, 64);
            Assert.NotEqual(pa[1].X, pb[1].X);

            Assert.Equal(0, vfx.Evaluate(a, 8, Settings(ImpactFx.Off), S, Q, pa, 64));
            int n = vfx.Evaluate(a, 8, Settings(ImpactFx.Confetti), S, Q, pa, 64);
            Assert.Contains(ParticleShape.Rect, ShapesOf(pa, n));
            n = vfx.Evaluate(a, 8, Settings(ImpactFx.Shards), S, Q, pa, 64);
            Assert.Contains(ParticleShape.Triangle, ShapesOf(pa, n));
        }

        static ParticleShape[] ShapesOf(Particle[] p, int n)
        {
            var r = new ParticleShape[n];
            for (int i = 0; i < n; i++) r[i] = p[i].Shape;
            return r;
        }

        [Fact]
        public void RingBreakShardsFollowTheRingAndAvoidTheGap()
        {
            var ev = new[] { RenderEvent.Break(0, 0, 7, 1, 0f, 0f, 0f, 1f, PaletteSlots.Arena, 1f) };
            var p = new Particle[64];
            int n = new VfxSystem().Evaluate(ev, 0, Settings(), S, Q, p, 64);
            int shards = 0;
            for (int i = 0; i < n; i++)
            {
                if (p[i].Shape != ParticleShape.Rect) continue;
                shards++;
                float r = MathF.Sqrt(p[i].X * p[i].X + p[i].Y * p[i].Y);
                Assert.Equal(2f, r, 2);
                float a = MathF.Atan2(p[i].Y, p[i].X);
                if (a < 0) a += 2 * MathF.PI;
                Assert.False(a > 0.02f && a < 0.58f, "shard in the gap at " + a);
            }
            Assert.Equal(24, shards);
        }

        [Fact]
        public void HorizontalBreaksHaveDepthForTheHelixView()
        {
            var ev = new[] { RenderEvent.Break(0, 0, 7, 1, 0f, -3f, 0.5f, 1f, PaletteSlots.Arena, 1f) };
            var p = new Particle[64];
            VfxSettings s = Settings();
            s.HorizontalBreaks = true;
            int n = new VfxSystem().Evaluate(ev, 10, s, S, Q, p, 64);
            bool anyDepth = false;
            for (int i = 0; i < n; i++) if (!float.IsNaN(p[i].Z) && Math.Abs(p[i].Z) > 0.1f) anyDepth = true;
            Assert.True(anyDepth);
        }

        [Fact]
        public void CapKeepsTheNewestEvents()
        {
            var events = new RenderEvent[40];
            for (int i = 0; i < events.Length; i++) events[i] = RenderEvent.Hit(i, 0, i, 0f, 0f, 1f, 1, 1f, 1, 0.3f, -1, -1, 0f);
            var p = new Particle[100];
            var vfx = new VfxSystem();
            int n = vfx.Evaluate(events, 40, Settings(), S, Q, p, 30);
            Assert.Equal(30, n);
            Assert.True(vfx.Capped);
            Assert.True(p[0].X > 35f);   // newest event first
        }

        [Fact]
        public void EvaluationDoesNotAllocate()
        {
            RenderEvent[] events = SampleEvents();
            var p = new Particle[600];
            var vfx = new VfxSystem();
            VfxSettings s = Settings(ImpactFx.Confetti);
            for (int t = 0; t < 50; t++) vfx.Evaluate(events, t, s, S, Q, p, 600);
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 0; t < 200; t++) vfx.Evaluate(events, t, s, S, Q, p, 600);
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        [Fact]
        public void SquashShakeAndFade()
        {
            var ev = new[]
            {
                RenderEvent.Hit(10, 0, 0f, 0f, 0f, 1f, 1, 1f, 5, 0.3f, -1, -1, 0f),
                RenderEvent.Create(10, RenderEventType.Pierce, 1, 0f, 0f, 1, 1f),
            };
            Assert.True(VfxSystem.TryGetSquash(ev, 11, 5, out float amount, out float nx, out float ny));
            Assert.InRange(amount, 0.1f, 0.35f);
            Assert.Equal(0f, nx, 4);
            Assert.Equal(1f, ny, 4);
            Assert.False(VfxSystem.TryGetSquash(ev, 30, 5, out _, out _, out _));
            Assert.False(VfxSystem.TryGetSquash(ev, 11, 6, out _, out _, out _));

            Assert.True(VfxSystem.TryGetShake(ev, 12, false, out float dx1, out float dy1));
            Assert.True(VfxSystem.TryGetShake(ev, 12, false, out float dx2, out float dy2));
            Assert.Equal(dx1, dx2);
            Assert.Equal(dy1, dy2);
            Assert.False(VfxSystem.TryGetShake(ev, 12, true, out _, out _));
            Assert.False(VfxSystem.TryGetShake(ev, 40, false, out _, out _));

            Assert.Equal(1f, VfxSystem.Fade(0, 1f));
            Assert.Equal(0f, VfxSystem.Fade(60, 1f));
            Assert.InRange(VfxSystem.Fade(30, 1f), 0.7f, 0.8f);
        }

        // ------------------------------------------------------------------ event log

        [Fact]
        public void LogWindowUsesBinarySearchAndValidatesOrder()
        {
            var log = new RenderEventLog(2);
            for (int t = 0; t < 100; t += 2) log.Add(RenderEvent.Create(t, RenderEventType.Hit, 0, 0, 0, 1, 1f));
            log.Add(RenderEvent.Create(98, RenderEventType.Hit, 1, 0, 0, 1, 1f));
            ReadOnlySpan<RenderEvent> w = log.GetWindow(50, 10);
            Assert.Equal(6, w.Length);   // 40, 42, …, 50
            Assert.Equal(40, w[0].Tick);
            Assert.Equal(50, w[w.Length - 1].Tick);
            Assert.Equal(0, log.GetWindow(-5, 2).Length);
            Assert.Throws<ArgumentException>(() => log.Add(RenderEvent.Create(10, RenderEventType.Hit, 0, 0, 0, 1, 1f)));
            Assert.Throws<ArgumentException>(() => log.Add(RenderEvent.Create(98, RenderEventType.Hit, 1, 0, 0, 1, 1f)));
            log.TruncateFrom(50);
            Assert.Equal(25, log.Count);
        }

        [Fact]
        public void LogRoundTripsAndDetectsCorruption()
        {
            var log = new RenderEventLog();
            foreach (RenderEvent e in SampleEvents()) log.Add(e);
            var ms = new MemoryStream();
            log.WriteTo(ms);
            Assert.Equal(10 + log.Count * RenderEventLog.RecordSize + 8, ms.Length);
            ms.Position = 0;
            RenderEventLog back = RenderEventLog.ReadFrom(ms);
            Assert.Equal(log.Events.ToArray(), back.Events.ToArray());

            byte[] bytes = ms.ToArray();
            bytes[20] ^= 0x40;
            Assert.Throws<InvalidDataException>(() => RenderEventLog.ReadFrom(new MemoryStream(bytes)));
            Assert.Throws<InvalidDataException>(() => RenderEventLog.ReadFrom(new MemoryStream(bytes, 0, 30)));
            bytes = ms.ToArray();
            bytes[0] = (byte)'X';
            Assert.Throws<InvalidDataException>(() => RenderEventLog.ReadFrom(new MemoryStream(bytes)));
        }
    }
}
