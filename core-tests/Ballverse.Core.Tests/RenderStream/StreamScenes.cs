using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.Tests.RenderStream
{
    /// <summary>Deterministic frame generators for render stream tests.</summary>
    internal static class StreamScenes
    {
        /// <summary>One shape of every kind and variant (shape ids 0..n-1 in this order; 0 is the unit circle).</summary>
        public static readonly ShapeDefinition[] StandardShapes =
        {
            ShapeDefinition.Circle,
            ShapeDefinition.Polygon(new[] { new ShapePoint(-500, -500), new ShapePoint(500, -500), new ShapePoint(500, 500), new ShapePoint(-500, 500) }),
            ShapeDefinition.Polygon(Star(5, 900, 350)),
            ShapeDefinition.Polygon(new[] { new ShapePoint(short.MinValue, short.MinValue), new ShapePoint(short.MaxValue, short.MinValue), new ShapePoint(0, short.MaxValue) }),
            ShapeDefinition.Polyline(new[] { new ShapePoint(-3000, 0), new ShapePoint(-1000, 800), new ShapePoint(1000, -800), new ShapePoint(3000, 0) }, false, 40),
            ShapeDefinition.Polyline(Star(8, 2000, 1500), true, 120, fixedThickness: true),
            ShapeDefinition.Ring(16000, 300, new[] { new RingGap(1000, 6000) }),
            ShapeDefinition.Ring(24000, 250, new[] { new RingGap(100, 200), new RingGap(30000, 34000), new RingGap(60000, 50) }, fixedThickness: true),
            ShapeDefinition.Ring(8000, 500, new RingGap[0]),
            ShapeDefinition.Capsule(6000, 400),
            ShapeDefinition.Capsule(65535, 65535, fixedThickness: true),
        };

        /// <summary>Label strings (label ids 1..n in this order).</summary>
        public static readonly string[] StandardLabels = { "Анна", "Bob", "#12", "×2", "+10", "Zoë 🙂", new string('W', RenderStreamLimits.MaxLabelLength) };

        public static ShapePoint[] Star(int arms, short outer, short inner)
        {
            var points = new ShapePoint[arms * 2];
            for (int i = 0; i < points.Length; i++)
            {
                double a = Math.PI * i / arms;
                double r = i % 2 == 0 ? outer : inner;
                points[i] = new ShapePoint((short)Math.Round(r * Math.Cos(a)), (short)Math.Round(r * Math.Sin(a)));
            }
            return points;
        }

        /// <summary>Defines <see cref="StandardShapes"/> and <see cref="StandardLabels"/>, checking the assigned ids.</summary>
        public static void DefineStandardTables(RenderStreamWriter writer)
        {
            for (int i = 0; i < StandardShapes.Length; i++)
            {
                if (writer.DefineShape(StandardShapes[i]) != i) throw new InvalidOperationException("unexpected shape id");
            }
            for (int i = 0; i < StandardLabels.Length; i++)
            {
                if (writer.DefineLabel(StandardLabels[i]) != i + 1) throw new InvalidOperationException("unexpected label id");
            }
        }
        /// <summary>
        /// Bodies moving smoothly on Lissajous paths with constant spin (deterministic Rng parameters), plus a HUD timer
        /// and score. Represents the spec's "300 bodies" budget case.
        /// </summary>
        public static List<RenderFrame> Smooth(int bodyCount, int ticks, ulong seed)
        {
            var q = RenderQuantization.Default;
            var rng = Rng.FromSeed(seed, 10);
            var p = new double[bodyCount, 8];
            for (int i = 0; i < bodyCount; i++)
            {
                p[i, 0] = rng.NextFloat(-3f, 3f);      // centre x
                p[i, 1] = rng.NextFloat(-5f, 5f);      // centre y
                p[i, 2] = rng.NextFloat(0.3f, 1.5f);   // amplitude x
                p[i, 3] = rng.NextFloat(0.3f, 2.5f);   // amplitude y
                p[i, 4] = rng.NextFloat(0.3f, 2.0f);   // omega x
                p[i, 5] = rng.NextFloat(0.3f, 2.0f);   // omega y
                p[i, 6] = rng.NextFloat(0f, 6.28f);    // phase
                p[i, 7] = rng.NextFloat(-4f, 4f);      // spin rad/s
            }

            var frames = new List<RenderFrame>(ticks);
            for (int t = 0; t < ticks; t++)
            {
                double time = t / 60.0;
                var f = new RenderFrame(bodyCount, 2);
                for (int i = 0; i < bodyCount; i++)
                {
                    float x = (float)(p[i, 0] + p[i, 2] * Math.Cos(p[i, 4] * time + p[i, 6]));
                    float y = (float)(p[i, 1] + p[i, 3] * Math.Sin(p[i, 5] * time + p[i, 6]));
                    float angle = (float)(p[i, 6] + p[i, 7] * time);
                    f.AddBody(q.QuantizeBody(i, 0, x, y, angle, 0.15f, (byte)(i % 12), BodyFlags.Visible));
                }
                f.AddHud(0, t);
                f.AddHud(1, t / 90);
                frames.Add(f);
            }
            return frames;
        }

        /// <summary>
        /// Heavy churn: spawns/despawns every tick, id reuse (including in the same tick with a shape or label change),
        /// extreme ids and values, teleports, colour/flag/scale changes and HUD add/remove/change. Bodies use all
        /// <see cref="StandardShapes"/> and <see cref="StandardLabels"/>. Bodies are added in shuffled
        /// order so the writer has to canonicalize; use <see cref="Canonical"/> for the expected frames.
        /// </summary>
        public static List<RenderFrame> Churn(int ticks, ulong seed)
        {
            var rng = Rng.FromSeed(seed, 20);
            var live = new List<BodyState>();
            var velocity = new Dictionary<int, int>();
            var dead = new List<int>();
            var hud = new SortedDictionary<int, int>();
            int nextId = 0;
            var frames = new List<RenderFrame>(ticks);
            var extremeIds = new[] { int.MinValue, int.MinValue + 1, -1, 0, int.MaxValue - 1, int.MaxValue };

            for (int t = 0; t < ticks; t++)
            {
                // Despawn.
                for (int i = live.Count - 1; i >= 0; i--)
                {
                    if (rng.NextUInt(100) < 4)
                    {
                        dead.Add(live[i].Id);
                        live.RemoveAt(i);
                    }
                }

                // Mutate survivors.
                for (int i = 0; i < live.Count; i++)
                {
                    BodyState b = live[i];
                    int v = velocity.TryGetValue(b.Id, out int vv) ? vv : 0;
                    uint r = rng.NextUInt(100);
                    if (r < 3)
                    {
                        b.X = (short)rng.NextInt(short.MinValue, short.MaxValue + 1); // teleport
                        b.Y = (short)rng.NextInt(short.MinValue, short.MaxValue + 1);
                    }
                    else
                    {
                        b.X = unchecked((short)(b.X + v));
                        b.Y = unchecked((short)(b.Y - v / 2 + rng.NextInt(-3, 4)));
                    }
                    b.Angle = unchecked((ushort)(b.Angle + v * 7));
                    if (r >= 3 && r < 6) b.Scale = (ushort)rng.NextInt(0, 65536);
                    if (r >= 6 && r < 9) b.Color = (byte)rng.NextUInt();
                    if (r >= 9 && r < 12) b.Flags = (BodyFlags)(byte)rng.NextUInt();
                    if (r == 12)
                    {
                        b.ShapeId = rng.NextInt(0, StandardShapes.Length); // possible shape change -> despawn + spawn in the stream
                    }
                    if (r == 13)
                    {
                        b.LabelId = rng.NextInt(0, StandardLabels.Length + 1); // possible label change -> despawn + spawn
                    }
                    live[i] = b;
                }

                // Spawn, sometimes reusing a dead id or an extreme id.
                int spawns = rng.NextInt(0, t == 0 ? 40 : 4);
                for (int s = 0; s < spawns; s++)
                {
                    int id;
                    uint mode = rng.NextUInt(10);
                    if (mode < 3 && dead.Count > 0)
                    {
                        int k = rng.NextInt(0, dead.Count);
                        id = dead[k];
                        dead.RemoveAt(k);
                    }
                    else if (mode == 3)
                    {
                        id = extremeIds[rng.NextInt(0, extremeIds.Length)];
                    }
                    else
                    {
                        id = nextId;
                        nextId += rng.NextInt(1, 1000);
                    }
                    if (live.Exists(b => b.Id == id)) continue;
                    live.Add(new BodyState(
                        id,
                        rng.NextInt(0, StandardShapes.Length),
                        (short)rng.NextInt(short.MinValue, short.MaxValue + 1),
                        (short)rng.NextInt(short.MinValue, short.MaxValue + 1),
                        (ushort)rng.NextInt(0, 65536),
                        (ushort)rng.NextInt(0, 65536),
                        (byte)rng.NextUInt(),
                        (BodyFlags)(byte)rng.NextUInt(),
                        rng.NextBool() ? BodyState.NoLabel : rng.NextInt(1, StandardLabels.Length + 1)));
                    velocity[id] = rng.NextInt(-400, 401);
                }

                // HUD.
                for (int k = 0; k < 3; k++)
                {
                    int key = rng.NextInt(0, 12) == 11 ? HudEntry.MaxKey : rng.NextInt(0, 11);
                    uint op = rng.NextUInt(10);
                    if (op < 2) hud.Remove(key);
                    else if (op < 4) hud[key] = rng.NextBool() ? int.MinValue : int.MaxValue;
                    else hud[key] = (hud.TryGetValue(key, out int old) ? old : 0) + rng.NextInt(-5, 6);
                }

                // Build the frame in shuffled order.
                var order = new List<BodyState>(live);
                for (int i = order.Count - 1; i > 0; i--)
                {
                    int j = rng.NextInt(0, i + 1);
                    var tmp = order[i];
                    order[i] = order[j];
                    order[j] = tmp;
                }
                var f = new RenderFrame();
                foreach (var b in order) f.AddBody(b);
                var hudKeys = new List<int>(hud.Keys);
                hudKeys.Reverse();
                foreach (int key in hudKeys) f.AddHud(key, hud[key]);
                frames.Add(f);
            }
            return frames;
        }

        public static List<RenderFrame> Canonical(List<RenderFrame> frames)
        {
            var result = new List<RenderFrame>(frames.Count);
            foreach (var f in frames)
            {
                var c = new RenderFrame();
                c.CopyFrom(f);
                c.Sort();
                result.Add(c);
            }
            return result;
        }

        /// <summary>
        /// Gives every frame a <paramref name="width"/> × <paramref name="height"/> grid painted by a few random walkers
        /// (small per-tick changes), with occasional whole-region fills and a full reset (large changes) so both grid
        /// delta encodings are exercised. Returns the same list.
        /// </summary>
        public static List<RenderFrame> AddGrid(List<RenderFrame> frames, int width, int height, ulong seed)
        {
            var rng = Rng.FromSeed(seed, 30);
            var cells = new byte[width * height];
            var walkers = new int[6];
            for (int i = 0; i < walkers.Length; i++) walkers[i] = rng.NextInt(0, cells.Length);
            for (int t = 0; t < frames.Count; t++)
            {
                if (t % 41 == 40)
                {
                    int x0 = rng.NextInt(0, width), y0 = rng.NextInt(0, height);
                    int x1 = rng.NextInt(x0, width), y1 = rng.NextInt(y0, height);
                    byte v = (byte)rng.NextUInt();
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int x = x0; x <= x1; x++) cells[y * width + x] = v;
                    }
                }
                else if (t % 97 == 96)
                {
                    for (int i = 0; i < cells.Length; i++) cells[i] = (byte)rng.NextUInt(4);
                }
                for (int i = 0; i < walkers.Length; i++)
                {
                    int step = rng.NextInt(0, 4);
                    int p = walkers[i];
                    int x = p % width, y = p / width;
                    if (step == 0 && x > 0) x--;
                    if (step == 1 && x < width - 1) x++;
                    if (step == 2 && y > 0) y--;
                    if (step == 3 && y < height - 1) y++;
                    walkers[i] = y * width + x;
                    cells[walkers[i]] = (byte)(i + 1);
                }
                frames[t].SetGridSize(width, height);
                cells.CopyTo(frames[t].Grid);
            }
            return frames;
        }

        /// <summary>A header declaring a <paramref name="width"/> × <paramref name="height"/> grid over the 9 × 16 m arena.</summary>
        public static RenderStreamHeader GridHeader(string modeId, int width, int height, int blockTicks = 60)
        {
            var q = RenderQuantization.Default;
            return new RenderStreamHeader(modeId, 60, blockTicks, q, RenderGridSpec.FromWorld(q, width, height, GridTopology.Square, -4.5f, -8f, 4.5f, 8f));
        }

        /// <summary>
        /// Writes the frames; <paramref name="define"/> registers shapes and labels first (default:
        /// <see cref="DefineStandardTables"/>).
        /// </summary>
        public static byte[] Write(List<RenderFrame> frames, RenderStreamHeader header = null, Action<RenderStreamWriter> define = null)
        {
            var ms = new MemoryStream();
            using (var w = new RenderStreamWriter(ms, header ?? new RenderStreamHeader("test_mode"), leaveOpen: true))
            {
                (define ?? DefineStandardTables)(w);
                for (int t = 0; t < frames.Count; t++) w.AppendFrame(t, frames[t]);
                w.Finish();
            }
            return ms.ToArray();
        }

        public static RenderStreamReader Open(byte[] data)
        {
            return new RenderStreamReader(new MemoryStream(data, writable: false));
        }

        public static string Describe(RenderFrame expected, RenderFrame actual)
        {
            if (expected.BodyCount != actual.BodyCount) return "body count " + expected.BodyCount + " != " + actual.BodyCount;
            for (int i = 0; i < expected.BodyCount; i++)
            {
                if (expected.GetBody(i) != actual.GetBody(i)) return "body " + i + ": " + expected.GetBody(i) + " != " + actual.GetBody(i);
            }
            if (!expected.Hud.SequenceEqual(actual.Hud)) return "hud differs";
            return "grid differs";
        }
    }
}
