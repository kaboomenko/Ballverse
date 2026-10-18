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
    }
}
