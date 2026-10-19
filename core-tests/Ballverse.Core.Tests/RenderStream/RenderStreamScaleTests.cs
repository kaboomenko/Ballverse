using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.RenderStream;
using Xunit;
using Xunit.Abstractions;

namespace Ballverse.Core.Tests.RenderStream
{
    /// <summary>
    /// Heavy-mode scale (spec §7.4 "до ~30 МБ/мин для M09/M15", §2.9 M09 body budget 2500): 2500 bouncing balls with
    /// gravity, random collision kicks and continuous respawns for 10 s, extrapolated to one minute; random seek stays fast.
    /// </summary>
    public class RenderStreamScaleTests : IClassFixture<RenderStreamScaleTests.HeavyStream>
    {
        public const int Bodies = 2500;
        public const int Ticks = 600;
        public const long BudgetBytesPerMinute = 30L * 1024 * 1024;

        public sealed class HeavyStream
        {
            public readonly byte[] Data;
            /// <summary>Expected frames of a few probe ticks (the full clip is not kept in memory).</summary>
            public readonly Dictionary<int, RenderFrame> Probes = new Dictionary<int, RenderFrame>();
            public readonly int[] ProbeTicks = { 0, 1, 59, 60, 61, 299, 300, 333, 451, 538, 599 };

            public HeavyStream()
            {
                var q = RenderQuantization.Default;
                var rng = Rng.FromSeed(2500, 40);
                var x = new double[Bodies];
                var y = new double[Bodies];
                var vx = new double[Bodies];
                var vy = new double[Bodies];
                var a = new double[Bodies];
                var ids = new int[Bodies];
                int nextId = 0;
                for (int i = 0; i < Bodies; i++) Respawn(i);

                void Respawn(int i)
                {
                    ids[i] = nextId++;
                    x[i] = rng.NextFloat(-4.3f, 4.3f);
                    y[i] = rng.NextFloat(-7f, 7.5f);
                    vx[i] = rng.NextFloat(-2f, 2f);
                    vy[i] = rng.NextFloat(-3f, 1f);
                    a[i] = rng.NextFloat(0f, 6.28f);
                }

                const double dt = 1.0 / 60.0;
                var ms = new MemoryStream();
                var frame = new RenderFrame(Bodies, 2);
                var probeSet = new HashSet<int>(ProbeTicks);
                using (var w = new RenderStreamWriter(ms, new RenderStreamHeader("multiply"), leaveOpen: true))
                {
                    int ball = w.DefineShape(ShapeDefinition.Circle);
                    for (int t = 0; t < Ticks; t++)
                    {
                        frame.Clear();
                        for (int i = 0; i < Bodies; i++)
                        {
                            if (rng.NextUInt(1000) < 3)
                            {
                                Respawn(i); // multiplied / bowled balls: continuous despawn + spawn churn
                            }
                            else
                            {
                                if (rng.NextUInt(100) < 3)
                                {
                                    vx[i] += rng.NextFloat(-1f, 1f); // collision kick
                                    vy[i] += rng.NextFloat(-1f, 1f);
                                }
                                vy[i] -= 9.8 * dt;
                                x[i] += vx[i] * dt;
                                y[i] += vy[i] * dt;
                                if (y[i] < -7.8) { y[i] = -7.8; vy[i] = -vy[i] * 0.9; }
                                if (x[i] < -4.4) { x[i] = -4.4; vx[i] = -vx[i]; }
                                if (x[i] > 4.4) { x[i] = 4.4; vx[i] = -vx[i]; }
                                a[i] += vx[i] * dt * 8;
                            }
                            frame.AddBody(q.QuantizeBody(ids[i], ball, (float)x[i], (float)y[i], (float)a[i], 0.06f, (byte)(ids[i] % 4), BodyFlags.Visible));
                        }
                        frame.AddHud(0, nextId);
                        frame.AddHud(1, t);
                        w.AppendFrame(t, frame);
                        if (probeSet.Contains(t))
                        {
                            var copy = new RenderFrame();
                            copy.CopyFrom(frame);
                            copy.Sort();
                            Probes[t] = copy;
                        }
                    }
                    w.Finish();
                }
                Data = ms.ToArray();
            }
        }

        readonly HeavyStream _stream;
        readonly ITestOutputHelper _output;

        public RenderStreamScaleTests(HeavyStream stream, ITestOutputHelper output)
        {
            _stream = stream;
            _output = output;
        }

        [Fact]
        public void TwentyFiveHundredBodiesStayWithinThirtyMegabytesPerMinute()
        {
            long size = _stream.Data.Length;
            double perMinute = size * (3600.0 / Ticks);
            _output.WriteLine("2500 bodies x 10 s: .bvs = " + size + " bytes; extrapolated " + (perMinute / 1048576.0).ToString("F2")
                + " MB/min; " + (size * 8.0 / Ticks / Bodies).ToString("F2") + " bits/body/frame");
            Assert.True(perMinute <= BudgetBytesPerMinute, "extrapolated " + perMinute + " bytes/min, budget " + BudgetBytesPerMinute);
        }

        [Fact]
        public void RandomSeekIsFastAndExact()
        {
            var dest = new RenderFrame();
            double worstMs = 0;
            using (var warm = StreamScenes.Open(_stream.Data)) warm.ReadFrame(1, dest); // JIT warm-up
            foreach (int tick in _stream.ProbeTicks)
            {
                using (var reader = StreamScenes.Open(_stream.Data))
                {
                    var sw = Stopwatch.StartNew();
                    reader.ReadFrame(tick, dest);
                    sw.Stop();
                    worstMs = Math.Max(worstMs, sw.Elapsed.TotalMilliseconds);
                }
                Assert.True(_stream.Probes[tick].ContentEquals(dest), "tick " + tick + ": " + StreamScenes.Describe(_stream.Probes[tick], dest));
            }
            _output.WriteLine("worst cold random seek at 2500 bodies: " + worstMs.ToString("F3") + " ms");
            Assert.True(worstMs < 250, "seek took " + worstMs + " ms");
        }

        [Fact]
        public void WarmScrubAt2500BodiesIsFast()
        {
            // Studio scrubbing (spec §3.7 "кадр обновляется ≤ 100 мс"): one reader, random seeks to the worst case,
            // the last tick of a block (decompress + decode the whole block).
            var dest = new RenderFrame();
            var rng = Rng.FromSeed(2501, 3);
            using (var reader = StreamScenes.Open(_stream.Data))
            {
                int blockTicks = reader.Header.BlockTicks;
                reader.ReadFrame(blockTicks - 1, dest); // JIT and buffer warm-up
                double total = 0, worst = 0;
                const int seeks = 100;
                var times = new double[seeks];
                int previous = 0;
                for (int i = 0; i < seeks; i++)
                {
                    int block = rng.NextInt(0, reader.BlockCount);
                    if (block == previous) block = (block + 1) % reader.BlockCount; // force a block load every time
                    previous = block;
                    int tick = Math.Min(reader.TickCount, (block + 1) * blockTicks) - 1;
                    var sw = Stopwatch.StartNew();
                    reader.ReadFrame(tick, dest);
                    sw.Stop();
                    times[i] = sw.Elapsed.TotalMilliseconds;
                    total += sw.Elapsed.TotalMilliseconds;
                    worst = Math.Max(worst, sw.Elapsed.TotalMilliseconds);
                    if (_stream.Probes.TryGetValue(tick, out var expected)) Assert.True(expected.ContentEquals(dest), "tick " + tick);
                }
                Array.Sort(times);
                double median = times[seeks / 2];
                _output.WriteLine("warm scrub at 2500 bodies, block-end ticks: avg " + (total / seeks).ToString("F3") + " ms, median " + median.ToString("F3") + " ms, worst " + worst.ToString("F3") + " ms");
                // Median, not mean: a loaded CI runner preempts the test thread now and then, and a few such spikes
                // must not fail it; a real decode regression moves the median.
                Assert.True(median < 50, "median warm seek " + median + " ms");
            }
        }

        /// <summary>
        /// M15 Liquid &amp; Sand at its tier-A budget (spec §2.9 M15 "Бюджет тел. 3000", §7.4 "до ~30 МБ/мин для M09/M15"):
        /// 3000 non-rotating metaball particles under gravity that pile up in a container, with per-tick jitter of
        /// ±1–3 position units (fluid solver noise that defeats linear prediction), 10 s extrapolated to a minute.
        /// </summary>
        [Fact]
        public void ThreeThousandParticlesStayWithinThirtyMegabytesPerMinute()
        {
            const int particles = 3000;
            var q = RenderQuantization.Default;
            var rng = Rng.FromSeed(3000, 41);
            var x = new double[particles];
            var y = new double[particles];
            var vy = new double[particles];
            for (int i = 0; i < particles; i++)
            {
                x[i] = rng.NextFloat(-4.3f, 4.3f);
                y[i] = rng.NextFloat(-7.5f, 7.5f);
            }
            const double dt = 1.0 / 60.0;
            var ms = new MemoryStream();
            var frame = new RenderFrame(particles, 1);
            RenderFrame last = null;
            using (var w = new RenderStreamWriter(ms, new RenderStreamHeader("liquid_sand"), leaveOpen: true))
            {
                int particle = w.DefineShape(ShapeDefinition.Circle);
                for (int t = 0; t < Ticks; t++)
                {
                    frame.Clear();
                    for (int i = 0; i < particles; i++)
                    {
                        vy[i] -= 9.8 * dt;
                        y[i] += vy[i] * dt;
                        double floor = -7.9 + (i % 40) * 0.05; // particles pile up in layers
                        if (y[i] < floor) { y[i] = floor; vy[i] = 0; }
                        BodyState b = q.QuantizeBody(i, particle, (float)x[i], (float)y[i], 0f, 0.04f, (byte)(i * 7 / particles), BodyFlags.Visible | BodyFlags.Metaball);
                        int jitter = rng.NextInt(1, 4);
                        b.X = (short)(b.X + (rng.NextBool() ? jitter : -jitter));
                        jitter = rng.NextInt(1, 4);
                        b.Y = (short)(b.Y + (rng.NextBool() ? jitter : -jitter));
                        frame.AddBody(b);
                    }
                    frame.AddHud(0, t);
                    w.AppendFrame(t, frame);
                    if (t == Ticks - 1)
                    {
                        last = new RenderFrame();
                        last.CopyFrom(frame);
                    }
                }
                w.Finish();
            }
            byte[] data = ms.ToArray();
            double perMinute = data.Length * (3600.0 / Ticks);
            _output.WriteLine("3000 jittery particles x 10 s: .bvs = " + data.Length + " bytes; extrapolated " + (perMinute / 1048576.0).ToString("F2")
                + " MB/min; " + (data.Length * 8.0 / Ticks / particles).ToString("F2") + " bits/body/frame");
            using (var r = StreamScenes.Open(data)) Assert.True(last.ContentEquals(r.ReadFrame(Ticks - 1)));
            Assert.True(perMinute <= BudgetBytesPerMinute, "extrapolated " + perMinute + " bytes/min, budget " + BudgetBytesPerMinute);
        }
    }
}
