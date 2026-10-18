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
                    f.AddBody(q.QuantizeBody(i, BodyKind.Circle, x, y, angle, 0.15f, (byte)(i % 12), BodyFlags.Visible));
                }
                f.AddHud(0, t);
                f.AddHud(1, t / 90);
                frames.Add(f);
            }
            return frames;
        }

        /// <summary>
        /// Heavy churn: spawns/despawns every tick, id reuse (including in the same tick with a kind change), extreme
        /// ids and values, teleports, colour/flag/scale changes and HUD add/remove/change. Bodies are added in shuffled
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
                        b.Kind = (BodyKind)rng.NextInt(0, 4); // possible kind change -> despawn + spawn in the stream
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
                        (BodyKind)rng.NextInt(0, 4),
                        (short)rng.NextInt(short.MinValue, short.MaxValue + 1),
                        (short)rng.NextInt(short.MinValue, short.MaxValue + 1),
                        (ushort)rng.NextInt(0, 65536),
                        (ushort)rng.NextInt(0, 65536),
                        (byte)rng.NextUInt(),
                        (BodyFlags)(byte)rng.NextUInt()));
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

        public static byte[] Write(List<RenderFrame> frames, RenderStreamHeader header = null)
        {
            var ms = new MemoryStream();
            using (var w = new RenderStreamWriter(ms, header ?? new RenderStreamHeader("test_mode"), leaveOpen: true))
            {
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
            return "hud differs";
        }
    }
}
