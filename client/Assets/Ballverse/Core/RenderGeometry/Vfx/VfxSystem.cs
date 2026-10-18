using System;
using System.Collections.Generic;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>Particle silhouettes drawn by the particle shader.</summary>
    public enum ParticleShape : byte
    {
        /// <summary>Soft round dot.</summary>
        Dot = 0,
        /// <summary>Rectangle (confetti, arc shards), rotated by <see cref="Particle.Rotation"/>.</summary>
        Rect = 1,
        /// <summary>Spark streak stretched along the velocity.</summary>
        Streak = 2,
        /// <summary>Expanding ring (ripple, shockwave): SizeX = outer diameter, SizeY = ring width as a fraction of the radius.</summary>
        Ring = 3,
        /// <summary>Triangle shard.</summary>
        Triangle = 4,
    }

    /// <summary>
    /// One evaluated particle in effect space: x, y in stream metres; z = depth for Helix horizontal effects (0 for planar
    /// effects, NaN for "a point in the stream plane" that the Helix view projects like a body).
    /// </summary>
    public struct Particle
    {
        public float X;
        public float Y;
        public float Z;
        public float VX;
        public float VY;
        public float VZ;
        /// <summary>Full width (diameter / length), metres.</summary>
        public float SizeX;
        /// <summary>Full height, metres (Ring: width fraction).</summary>
        public float SizeY;
        public float Rotation;
        /// <summary>Colour with the fade baked into alpha (0xRRGGBBAA).</summary>
        public uint Color;
        public float Emission;
        public ParticleShape Shape;
    }

    /// <summary>Per-frame VFX configuration (from the look, theme, quality profile and view).</summary>
    public sealed class VfxSettings
    {
        public ImpactFx Impact { get; set; } = ImpactFx.Sparks;
        public BreakFx Break { get; set; } = BreakFx.Shards;
        /// <summary>Fraction of the nominal particle counts (quality profile).</summary>
        public float CountFactor { get; set; } = 1f;
        /// <summary>Resolved 256-entry palette (see <see cref="PaletteSlots"/>).</summary>
        public uint[] Palette { get; set; }
        /// <summary>Number of theme palette colours used for confetti (palette indices 224 + i).</summary>
        public int ConfettiColors { get; set; } = 8;
        /// <summary>Base emission of effects (theme glow).</summary>
        public float Emission { get; set; } = 1.5f;
        /// <summary>Helix view: ring breaks happen in the horizontal plane of the platform (3D shards).</summary>
        public bool HorizontalBreaks { get; set; }
        /// <summary>Helix view: sine of the view tilt (orients horizontal shards on screen).</summary>
        public float TiltSin { get; set; } = 0.47f;
        /// <summary>"Reduce motion" (spec §4.8): no camera shake.</summary>
        public bool ReduceMotion { get; set; }
    }

    /// <summary>
    /// Deterministic particle system of spec §7.7 / §7.4 rule 7. Every effect is a pure function of its
    /// <see cref="RenderEvent"/> and its age: the random stream is <see cref="Rng"/> seeded from (tick, event id), each
    /// particle consumes a fixed number of draws (so lower quality shows a subset of the same particles), and motion is
    /// evaluated in closed form (linear drag k and gravity g: p = p0 + v0·(1 − e^{−kt})/k + g·(t − (1 − e^{−kt})/k)/k).
    /// No state is kept between frames, so any tick — live, scrubbed or exported — shows the same particles.
    /// Allocation-free.
    /// </summary>
    public sealed class VfxSystem
    {
        /// <summary>Stream id of the VFX random stream (spec §7.4 rule 2: one stream per subsystem).</summary>
        public const ulong VfxStreamId = 0x5646_5853UL;   // "VFXS"
        /// <summary>Longest particle lifetime, ticks (finish confetti 2.6 s).</summary>
        public const int MaxParticleLifetimeTicks = 160;
        /// <summary>Longest body effect (splat 3 s), ticks: the event window the renderer must supply.</summary>
        public const int MaxEffectTicks = 180;
        /// <summary>Duration of the hit flash on the target, seconds (M02 "грань … на 0,25 с вспыхивает").</summary>
        public const float FlashSeconds = 0.25f;
        /// <summary>Duration of a splat, seconds (M05 "гаснет за 3 с").</summary>
        public const float SplatSeconds = 3f;
        /// <summary>Duration of the ball squash after a hit, seconds.</summary>
        public const float SquashSeconds = 0.14f;
        /// <summary>Duration of the pierce camera shake, seconds.</summary>
        public const float ShakeSeconds = 0.3f;

        const int DrawsPerParticle = 8;
        const float Dt = 1f / SimConstants.TickRate;

        Particle[] _out;
        int _count;
        int _max;

        /// <summary>Particles written by the last <see cref="Evaluate"/>.</summary>
        public int Count => _count;

        /// <summary>True when the last evaluation hit the cap and dropped older particles.</summary>
        public bool Capped { get; private set; }

        public static ulong Seed(int tick, int id)
        {
            return ((ulong)(uint)tick << 32) | (uint)id;
        }

        /// <summary>
        /// Evaluates all particles alive at <paramref name="tick"/> from <paramref name="events"/> (tick-ordered, e.g. a
        /// <see cref="RenderEventLog.GetWindow"/> of <see cref="MaxParticleLifetimeTicks"/>) into <paramref name="output"/>,
        /// newest events first, at most min(output length, <paramref name="maxParticles"/>). Returns the count.
        /// </summary>
        public int Evaluate(ReadOnlySpan<RenderEvent> events, int tick, VfxSettings settings, IReadOnlyList<ShapeDefinition> shapes,
            RenderQuantization quantization, Particle[] output, int maxParticles)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (settings.Palette == null || settings.Palette.Length < 256) throw new ArgumentException("settings.Palette needs 256 entries", nameof(settings));
            if (output == null) throw new ArgumentNullException(nameof(output));
            _out = output;
            _count = 0;
            _max = Math.Min(output.Length, Math.Max(0, maxParticles));
            Capped = false;
            for (int i = events.Length - 1; i >= 0 && _count < _max; i--)
            {
                RenderEvent e = events[i];
                if (e.Tick > tick) continue;
                int ageTicks = tick - e.Tick;
                if (ageTicks > MaxParticleLifetimeTicks) continue;
                float t = ageTicks * Dt;
                var rng = Rng.FromSeed(Seed(e.Tick, e.Id), VfxStreamId);
                switch (e.Type)
                {
                    case RenderEventType.Hit: EmitHit(e, t, ref rng, settings); break;
                    case RenderEventType.Break: EmitBreak(e, t, ref rng, settings, shapes, quantization); break;
                    case RenderEventType.Spawn: EmitSpawn(e, t, ref rng, settings); break;
                    case RenderEventType.Eliminate: EmitEliminate(e, t, ref rng, settings); break;
                    case RenderEventType.Finish: EmitFinish(e, t, ref rng, settings, 3f, 70); break;
                    case RenderEventType.Escape: EmitFinish(e, t, ref rng, settings, 6f, 50); break;
                    case RenderEventType.Pierce: EmitPierce(e, t, ref rng, settings); break;
                }
            }
            if (_count >= _max && _max > 0) Capped = true;
            _out = null;
            return _count;
        }

        static int Nominal(float min, float max, float intensity, VfxSettings s)
        {
            float n = (min + (max - min) * Clamp01(intensity)) * Clamp01(s.CountFactor);
            return Math.Max(0, (int)Math.Round(n));
        }

        // -------------------------------------------------------------------------------------------- recipes

        void EmitHit(in RenderEvent e, float t, ref Rng rng, VfxSettings s)
        {
            if (s.Impact == ImpactFx.Off) return;
            uint color = s.Palette[e.ColorIndex];
            float I = Clamp01(e.Intensity);
            float nx = e.NormalX, ny = e.NormalY;
            bool directional = nx * nx + ny * ny > 1e-6f;
            float baseAngle = directional ? MathF.Atan2(ny, nx) : 0f;
            float spread = directional ? 1.25f : MathF.PI;
            float z = s.HorizontalBreaks ? float.NaN : 0f;

            if (I > 0.25f)
            {
                // Ripple ring at the contact (M02 "кольцевая рябь").
                Ring(e.X, e.Y, z, t, 0.35f, Math.Max(0.05f, e.Size), Math.Max(0.05f, e.Size) + 0.45f + 0.5f * I, 0.18f, color, 0.55f, s.Emission * 1.2f);
            }
            switch (s.Impact)
            {
                case ImpactFx.Sparks:
                {
                    int n = Nominal(4, 12, I, s);
                    for (int i = 0; i < n; i++)
                    {
                        Draws(ref rng, out float r0, out float r1, out float r2, out float r3, out _, out _, out _, out _);
                        float a = baseAngle + (r0 * 2f - 1f) * spread;
                        float speed = (2f + 4f * r1) * (0.5f + I);
                        float life = 0.25f + 0.25f * r2;
                        uint c = ColorMath.Lerp(color, ColorMath.White, 0.25f + 0.3f * r3);
                        Ballistic(e.X, e.Y, z, MathF.Cos(a) * speed, MathF.Sin(a) * speed, 0f, 5f, -4f, t, life, ParticleShape.Streak,
                            0.03f, 0.03f, 0f, 0f, c, s.Emission * 1.5f, true);
                    }
                    break;
                }
                case ImpactFx.Confetti:
                {
                    int n = Nominal(6, 14, I, s);
                    for (int i = 0; i < n; i++)
                    {
                        Draws(ref rng, out float r0, out float r1, out float r2, out float r3, out float r4, out float r5, out _, out _);
                        float a = baseAngle + (r0 * 2f - 1f) * Math.Min(MathF.PI, spread + 0.15f);
                        float speed = 2f + 2.5f * r1;
                        float life = 0.9f + 0.4f * r2;
                        uint c = ConfettiColor(s, r3);
                        float spin = (4f + 6f * r4) * (r5 < 0.5f ? -1f : 1f);
                        Ballistic(e.X, e.Y, z, MathF.Cos(a) * speed, MathF.Sin(a) * speed, 0f, 2.5f, -6f, t, life, ParticleShape.Rect,
                            0.075f, 0.04f, r0 * 6.283f, spin, c, 1f, false);
                    }
                    break;
                }
                case ImpactFx.Shards:
                {
                    int n = Nominal(4, 9, I, s);
                    for (int i = 0; i < n; i++)
                    {
                        Draws(ref rng, out float r0, out float r1, out float r2, out float r3, out float r4, out _, out _, out _);
                        float a = baseAngle + (r0 * 2f - 1f) * spread;
                        float speed = 2f + 3f * r1;
                        float size = 0.05f + 0.05f * r2;
                        float spin = (6f + 8f * r3) * (r4 < 0.5f ? -1f : 1f);
                        Ballistic(e.X, e.Y, z, MathF.Cos(a) * speed, MathF.Sin(a) * speed, 0f, 2f, -8f, t, 0.5f + 0.3f * r2, ParticleShape.Triangle,
                            size, size, r1 * 6.283f, spin, color, s.Emission, false);
                    }
                    break;
                }
            }
        }

        void EmitBreak(in RenderEvent e, float t, ref Rng rng, VfxSettings s, IReadOnlyList<ShapeDefinition> shapes, RenderQuantization q)
        {
            uint color = s.Palette[e.ColorIndex];
            float I = e.Intensity > 0f ? Clamp01(e.Intensity) : 1f;
            ShapeDefinition shape = shapes != null && q != null && (uint)e.ShapeId < (uint)shapes.Count ? shapes[e.ShapeId] : null;
            bool ring = shape != null && shape.Kind == ShapeKind.Ring;
            float radius = 0.5f, thickness = 0.12f;
            if (ring) HelixTowerBuilderRadii(shape, q, e.Scale, out radius, out thickness);
            bool horizontal = ring && s.HorizontalBreaks;

            // Light wave along the broken ring (M01) — planar views only.
            if (ring && !horizontal) Ring(e.X, e.Y, 0f, t, 0.4f, radius, radius * 1.15f + 0.2f, Math.Min(0.9f, thickness / Math.Max(0.05f, radius)), color, 0.8f, s.Emission * 1.6f);

            switch (s.Break)
            {
                case BreakFx.Flash:
                {
                    float d = 2f * (radius + 0.4f);
                    float z = horizontal ? float.NaN : 0f;
                    Static(e.X, e.Y, z, t, 0.3f, ParticleShape.Dot, d, d * 1.3f, color, 0.85f, s.Emission * 2f);
                    break;
                }
                case BreakFx.Dissolve:
                {
                    int n = Nominal(24, 40, I, s);
                    for (int i = 0; i < n; i++)
                    {
                        Draws(ref rng, out float r0, out float r1, out float r2, out float r3, out _, out _, out _, out _);
                        PointOnShape(e, ring, radius, r0, r3, s, out float px, out float py, out float pz, out float ox, out float oz);
                        float life = 0.8f + 0.6f * r2;
                        Ballistic(px, py, pz, ox * 0.2f, 0.3f + 0.5f * r1, oz * 0.2f, 1.2f, 0.6f, t, life, ParticleShape.Dot,
                            0.07f, 0.07f, 0f, 0f, color, s.Emission, false);
                    }
                    break;
                }
                case BreakFx.Burst:
                {
                    int n = Nominal(20, 32, I, s);
                    for (int i = 0; i < n; i++)
                    {
                        Draws(ref rng, out float r0, out float r1, out float r2, out float r3, out _, out _, out _, out _);
                        PointOnShape(e, ring, radius, r0, r3, s, out float px, out float py, out float pz, out float ox, out float oz);
                        float speed = 3f + 4f * r1;
                        float oy = horizontal ? 0.3f : 0f;
                        if (!horizontal) { float a = r0 * 6.2831853f; ox = MathF.Cos(a); oz = 0f; oy = MathF.Sin(a); }
                        Ballistic(px, py, pz, ox * speed, oy * speed, oz * speed, 4f, -3f, t, 0.5f + 0.3f * r2, ParticleShape.Streak,
                            0.035f, 0.035f, 0f, 0f, ColorMath.Lerp(color, ColorMath.White, 0.3f), s.Emission * 1.6f, true);
                    }
                    break;
                }
                default:
                {
                    if (ring) RingShards(e, t, ref rng, s, shape, radius, thickness, color, I, horizontal);
                    else
                    {
                        int n = Nominal(10, 16, I, s);
                        for (int i = 0; i < n; i++)
                        {
                            Draws(ref rng, out float r0, out float r1, out float r2, out float r3, out float r4, out _, out _, out _);
                            float a = r0 * 6.2831853f;
                            float speed = 1.5f + 3f * r1;
                            float size = 0.08f + 0.1f * r2;
                            float spin = (3f + 6f * r3) * (r4 < 0.5f ? -1f : 1f);
                            Ballistic(e.X, e.Y, 0f, MathF.Cos(a) * speed, MathF.Sin(a) * speed, 0f, 1f, -3f, t, 1.0f + 0.2f * r2,
                                ParticleShape.Triangle, size, size, a, spin, color, s.Emission, false);
                        }
                    }
                    break;
                }
            }
        }

        void RingShards(in RenderEvent e, float t, ref Rng rng, VfxSettings s, ShapeDefinition shape, float radius, float thickness,
            uint color, float I, bool horizontal)
        {
            // 12–24 arc shards spread over the solid arcs (M01: "12–24 дуговых осколка … гаснут за 1,2 с"; M05 sectors, 1 s).
            int n = Nominal(12, 24, I, s);
            ReadOnlySpan<RingGap> gaps = shape.Gaps;
            const float unit = 2f * MathF.PI / 65536f;
            int gapSum = 0;
            for (int g = 0; g < gaps.Length; g++) gapSum += gaps[g].Span;
            float solid = (65536 - gapSum) * unit;
            float shardLen = Math.Min(0.7f, Math.Max(0.06f, solid * radius / Math.Max(1, n)));
            float life = horizontal ? 1.0f : 1.2f;
            for (int i = 0; i < n; i++)
            {
                Draws(ref rng, out float r0, out float r1, out float r2, out float r3, out float r4, out float r5, out _, out _);
                // Even spacing over the solid length with jitter, mapped through the gaps.
                float along = ((i + 0.2f + 0.6f * r0) / n) * solid;
                float local = SolidToLocalAngle(gaps, along);
                float speed = 1.5f + 2f * r1;
                float spin = (2f + 6f * r2) * (r3 < 0.5f ? -1f : 1f);
                float l = life * (0.85f + 0.3f * r4);
                if (horizontal)
                {
                    float psi = local + e.Angle;
                    float sx = MathF.Sin(psi), cz = -MathF.Cos(psi);
                    float px = e.X + radius * sx, pz = radius * cz;
                    float rot = MathF.Atan2(MathF.Sin(psi) * s.TiltSin, MathF.Cos(psi));
                    Ballistic(px, e.Y, pz, sx * speed, 1f + 1.5f * r5, cz * speed, 1f, -7f, t, l, ParticleShape.Rect,
                        shardLen, thickness * 0.6f, rot, spin * 0.5f, color, s.Emission, false);
                }
                else
                {
                    float a = local + e.Angle;
                    float cx = MathF.Cos(a), sy = MathF.Sin(a);
                    Ballistic(e.X + radius * cx, e.Y + radius * sy, 0f, cx * speed, sy * speed, 0f, 1f, -2.5f, t, l, ParticleShape.Rect,
                        shardLen, thickness, a + MathF.PI * 0.5f, spin, color, s.Emission, false);
                }
            }
        }

        /// <summary>Maps a distance along the solid arcs (radians of solid ring, from the first gap's end) to a local angle.</summary>
        static float SolidToLocalAngle(ReadOnlySpan<RingGap> gaps, float along)
        {
            const float unit = 2f * MathF.PI / 65536f;
            if (gaps.Length == 0) return along;
            float remaining = along;
            for (int g = 0; g < gaps.Length; g++)
            {
                int start = gaps[g].End;
                int end = gaps[(g + 1) % gaps.Length].Start;
                int span = (end - start) & 0xFFFF;
                if (span == 0 && gaps.Length == 1) span = 65536 - gaps[0].Span;
                float len = span * unit;
                if (remaining <= len || g == gaps.Length - 1) return start * unit + Math.Min(remaining, len);
                remaining -= len;
            }
            return along;
        }

        void PointOnShape(in RenderEvent e, bool ring, float radius, float r0, float r1, VfxSettings s,
            out float px, out float py, out float pz, out float outX, out float outZ)
        {
            float a = r0 * 6.2831853f;
            if (ring && s.HorizontalBreaks)
            {
                float psi = a + e.Angle;
                outX = MathF.Sin(psi);
                outZ = -MathF.Cos(psi);
                px = e.X + radius * outX;
                py = e.Y;
                pz = radius * outZ;
                return;
            }
            float rr = ring ? radius : radius * MathF.Sqrt(r1);
            outX = MathF.Cos(a);
            outZ = 0f;
            px = e.X + rr * outX;
            py = e.Y + rr * MathF.Sin(a);
            pz = 0f;
        }

        void EmitSpawn(in RenderEvent e, float t, ref Rng rng, VfxSettings s)
        {
            uint color = s.Palette[e.ColorIndex];
            float r = Math.Max(0.05f, e.Size);
            float z = s.HorizontalBreaks ? float.NaN : 0f;
            Ring(e.X, e.Y, z, t, 0.3f, r, r * 2.2f, 0.2f, color, 0.8f, s.Emission * 1.3f);
            int n = Nominal(6, 6, 1f, s);
            for (int i = 0; i < n; i++)
            {
                Draws(ref rng, out float r0, out float r1, out _, out _, out _, out _, out _, out _);
                float a = (i + r0 * 0.5f) / Math.Max(1, n) * 6.2831853f;
                float speed = 1f + r1;
                Ballistic(e.X + MathF.Cos(a) * r, e.Y + MathF.Sin(a) * r, z, MathF.Cos(a) * speed, MathF.Sin(a) * speed, 0f, 3f, 0f, t, 0.4f,
                    ParticleShape.Dot, 0.06f, 0.06f, 0f, 0f, color, s.Emission, false);
            }
        }

        void EmitEliminate(in RenderEvent e, float t, ref Rng rng, VfxSettings s)
        {
            uint color = s.Palette[e.ColorIndex];
            float z = s.HorizontalBreaks ? float.NaN : 0f;
            float nx = e.NormalX, ny = e.NormalY;
            if (nx * nx + ny * ny > 1e-6f)
            {
                // Streak along the exit direction (M02 «полоса-шлейф при вылете через проём»): a fading band left behind the
                // ball plus fast sparks flying on along the normal.
                float len = MathF.Sqrt(nx * nx + ny * ny);
                nx /= len;
                ny /= len;
                float width = 2f * Math.Max(0.05f, e.Size > 0f ? e.Size : 0.2f);
                float streak = 1.2f + 0.8f * Clamp01(e.Intensity > 0f ? e.Intensity : 1f);
                Band(e.X - nx * streak * 0.5f, e.Y - ny * streak * 0.5f, z, t, 0.45f, streak, width, MathF.Atan2(ny, nx), color, 0.8f, s.Emission * 1.5f);
                int sparks = Nominal(8, 8, 1f, s);
                for (int i = 0; i < sparks; i++)
                {
                    Draws(ref rng, out float r0, out float r1, out float r2, out _, out _, out _, out _, out _);
                    float a = MathF.Atan2(ny, nx) + (r0 * 2f - 1f) * 0.35f;
                    float speed = 4f + 4f * r1;
                    Ballistic(e.X, e.Y, z, MathF.Cos(a) * speed, MathF.Sin(a) * speed, 0f, 3f, 0f, t, 0.35f + 0.15f * r2, ParticleShape.Streak,
                        0.04f, 0.04f, 0f, 0f, ColorMath.Lerp(color, ColorMath.White, 0.3f), s.Emission * 1.5f, true);
                }
            }
            int n = Nominal(16, 16, 1f, s);
            for (int i = 0; i < n; i++)
            {
                Draws(ref rng, out float r0, out float r1, out float r2, out _, out _, out _, out _, out _);
                float a = r0 * 6.2831853f;
                float speed = 1f + 2f * r1;
                Ballistic(e.X, e.Y, z, MathF.Cos(a) * speed, MathF.Sin(a) * speed, 0f, 2.5f, -2f, t, 0.5f + 0.2f * r2, ParticleShape.Dot,
                    0.09f, 0.09f, 0f, 0f, ColorMath.Lerp(color, s.Palette[PaletteSlots.Muted], 0.3f), s.Emission, false);
            }
        }

        void EmitFinish(in RenderEvent e, float t, ref Rng rng, VfxSettings s, float waveRadius, int confetti)
        {
            uint color = s.Palette[e.ColorIndex];
            float z = s.HorizontalBreaks ? float.NaN : 0f;
            float size = e.Size > 0f ? e.Size : waveRadius;
            Ring(e.X, e.Y, z, t, 0.65f, 0.1f, size, 0.12f, color, 0.9f, s.Emission * 2f);
            int n = Nominal(confetti, confetti, 1f, s);
            for (int i = 0; i < n; i++)
            {
                Draws(ref rng, out float r0, out float r1, out float r2, out float r3, out float r4, out float r5, out _, out _);
                float a = MathF.PI * 0.5f + (r0 * 2f - 1f) * 1.1f;
                float speed = 5f + 5f * r1;
                float spin = (4f + 8f * r4) * (r5 < 0.5f ? -1f : 1f);
                Ballistic(e.X, e.Y, z, MathF.Cos(a) * speed, MathF.Sin(a) * speed, 0f, 1.5f, -9f, t, 2f + 0.6f * r2, ParticleShape.Rect,
                    0.09f, 0.05f, r0 * 6.283f, spin, ConfettiColor(s, r3), 1f, false);
            }
        }

        void EmitPierce(in RenderEvent e, float t, ref Rng rng, VfxSettings s)
        {
            float z = s.HorizontalBreaks ? float.NaN : 0f;
            uint accent = s.Palette[PaletteSlots.Accent];
            uint danger = s.Palette[PaletteSlots.Danger];
            Static(e.X, e.Y, z, t, 0.22f, ParticleShape.Dot, 1.2f, 2.2f, accent, 0.9f, s.Emission * 2.5f);
            int n = Nominal(18, 26, Clamp01(e.Intensity > 0f ? e.Intensity : 1f), s);
            for (int i = 0; i < n; i++)
            {
                Draws(ref rng, out float r0, out float r1, out float r2, out float r3, out _, out _, out _, out _);
                float a = r0 * 6.2831853f;
                float speed = 3f + 5f * r1;
                uint c = ColorMath.Lerp(danger, accent, r3);
                Ballistic(e.X, e.Y, z, MathF.Cos(a) * speed, MathF.Sin(a) * speed, 0f, 4f, -2f, t, 0.4f + 0.3f * r2, ParticleShape.Streak,
                    0.04f, 0.04f, 0f, 0f, c, s.Emission * 2.5f, true);
            }
        }

        // -------------------------------------------------------------------------------------------- primitives

        static void Draws(ref Rng rng, out float a, out float b, out float c, out float d, out float e, out float f, out float g, out float h)
        {
            a = rng.NextFloat(); b = rng.NextFloat(); c = rng.NextFloat(); d = rng.NextFloat();
            e = rng.NextFloat(); f = rng.NextFloat(); g = rng.NextFloat(); h = rng.NextFloat();
        }

        static uint ConfettiColor(VfxSettings s, float r)
        {
            int n = Math.Max(1, Math.Min(16, s.ConfettiColors));
            int i = Math.Min(n - 1, (int)(r * n));
            return s.Palette[PaletteSlots.ThemeDecorFirst + i];
        }

        void Ballistic(float x0, float y0, float z0, float vx, float vy, float vz, float drag, float gravity, float t, float life,
            ParticleShape shape, float sizeX, float sizeY, float rotation0, float spin, uint color, float emission, bool stretch)
        {
            if (t >= life || _count >= _max) return;
            float d, g, decay;
            if (drag > 1e-4f)
            {
                decay = MathF.Exp(-drag * t);
                d = (1f - decay) / drag;
                g = (t - d) / drag;
            }
            else
            {
                decay = 1f;
                d = t;
                g = 0.5f * t * t;
            }
            float u = t / life;
            float alpha = 1f - u * u;
            ref Particle p = ref _out[_count++];
            p.X = x0 + vx * d;
            p.Y = y0 + vy * d + gravity * g;
            p.Z = float.IsNaN(z0) ? float.NaN : z0 + vz * d;
            float gv = drag > 1e-4f ? (1f - decay) / drag : t;
            p.VX = vx * decay;
            p.VY = vy * decay + gravity * gv;
            p.VZ = vz * decay;
            float shrink = shape == ParticleShape.Dot || shape == ParticleShape.Streak ? 1f - 0.5f * u : 1f;
            p.SizeX = sizeX * shrink;
            p.SizeY = sizeY * shrink;
            if (stretch)
            {
                float speed = MathF.Sqrt(p.VX * p.VX + p.VY * p.VY);
                p.SizeX = sizeX * shrink + speed * 0.035f;
                p.Rotation = MathF.Atan2(p.VY, p.VX);
            }
            else
            {
                p.Rotation = rotation0 + spin * t;
                if (shape == ParticleShape.Rect && spin != 0f)
                {
                    // Confetti flip: the short side breathes with the spin.
                    p.SizeY = sizeY * (0.35f + 0.65f * MathF.Abs(MathF.Cos(spin * 0.7f * t + rotation0)));
                }
            }
            p.Color = ColorMath.WithAlpha(color, alpha);
            p.Emission = emission;
            p.Shape = shape;
        }

        void Ring(float x, float y, float z, float t, float life, float r0, float r1, float widthFraction, uint color, float alpha0, float emission)
        {
            if (t >= life || _count >= _max) return;
            float u = t / life;
            float ease = 1f - (1f - u) * (1f - u);
            float r = r0 + (r1 - r0) * ease;
            ref Particle p = ref _out[_count++];
            p.X = x;
            p.Y = y;
            p.Z = z;
            p.VX = p.VY = p.VZ = 0f;
            p.SizeX = 2f * r;
            p.SizeY = Math.Max(0.02f, Math.Min(1f, widthFraction * (1f - 0.5f * u)));
            p.Rotation = 0f;
            p.Color = ColorMath.WithAlpha(color, alpha0 * (1f - u));
            p.Emission = emission;
            p.Shape = ParticleShape.Ring;
        }

        void Static(float x, float y, float z, float t, float life, ParticleShape shape, float size0, float size1, uint color, float alpha0, float emission)
        {
            if (t >= life || _count >= _max) return;
            float u = t / life;
            float size = size0 + (size1 - size0) * u;
            ref Particle p = ref _out[_count++];
            p.X = x;
            p.Y = y;
            p.Z = z;
            p.VX = p.VY = p.VZ = 0f;
            p.SizeX = size;
            p.SizeY = size;
            p.Rotation = 0f;
            p.Color = ColorMath.WithAlpha(color, alpha0 * (1f - u) * (1f - u));
            p.Emission = emission;
            p.Shape = shape;
        }

        /// <summary>A rotated rectangle (streak band) that thins and fades over its life.</summary>
        void Band(float x, float y, float z, float t, float life, float length, float width, float rotation, uint color, float alpha0, float emission)
        {
            if (t >= life || _count >= _max) return;
            float u = t / life;
            ref Particle p = ref _out[_count++];
            p.X = x;
            p.Y = y;
            p.Z = z;
            p.VX = p.VY = p.VZ = 0f;
            p.SizeX = length;
            p.SizeY = width * (1f - 0.7f * u);
            p.Rotation = rotation;
            p.Color = ColorMath.WithAlpha(color, alpha0 * (1f - u) * (1f - u));
            p.Emission = emission;
            p.Shape = ParticleShape.Rect;
        }

        static void HelixTowerBuilderRadii(ShapeDefinition ring, RenderQuantization q, float scale, out float radius, out float thickness)
        {
            float s = scale > 0f ? scale : 1f;
            radius = q.DequantizeScale(ring.Radius) * s;
            thickness = ring.FixedThickness ? q.DequantizeScale(ring.Thickness) : q.DequantizeScale(ring.Thickness) * s;
        }

        static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : (float.IsNaN(v) ? 0f : v);

        // -------------------------------------------------------------------------------------------- body effects

        /// <summary>
        /// Squash of ball <paramref name="bodyId"/> at <paramref name="tick"/> from recent Hit events (M05 "сжатие-растяжение
        /// шара при отскоке", M02 pulse): amount in [0, 0.35] along the contact normal (nx, ny). False when none.
        /// </summary>
        public static bool TryGetSquash(ReadOnlySpan<RenderEvent> events, int tick, int bodyId, out float amount, out float nx, out float ny)
        {
            amount = 0f;
            nx = 0f;
            ny = 1f;
            int window = (int)Math.Ceiling(SquashSeconds * SimConstants.TickRate);
            for (int i = events.Length - 1; i >= 0; i--)
            {
                RenderEvent e = events[i];
                if (e.Tick > tick) continue;
                if (tick - e.Tick > window) break;
                if (e.Type != RenderEventType.Hit || e.BodyId != bodyId) continue;
                float u = (tick - e.Tick) * Dt / SquashSeconds;
                if (u >= 1f) continue;
                float a = 0.35f * Clamp01(e.Intensity) * (1f - u) * (1f - u);
                if (a > amount)
                {
                    amount = a;
                    float len = MathF.Sqrt(e.NormalX * e.NormalX + e.NormalY * e.NormalY);
                    if (len > 1e-6f)
                    {
                        nx = e.NormalX / len;
                        ny = e.NormalY / len;
                    }
                }
            }
            return amount > 0f;
        }

        /// <summary>Duration of the growth / birth pulse of a ball (Spawn event with its BodyId), seconds.</summary>
        public const float PulseSeconds = 0.3f;

        /// <summary>
        /// Radius pulse of ball <paramref name="bodyId"/> at <paramref name="tick"/> from recent Spawn events naming it
        /// (M02 «пульсация шара при росте»; a newborn ball pops in the same way): a scale factor ≥ 1 (up to 1.22) that
        /// overshoots and settles within <see cref="PulseSeconds"/>. 1 when none. Pure function of the events.
        /// </summary>
        public static float GetPulse(ReadOnlySpan<RenderEvent> events, int tick, int bodyId)
        {
            int window = (int)Math.Ceiling(PulseSeconds * SimConstants.TickRate);
            float best = 0f;
            for (int i = events.Length - 1; i >= 0; i--)
            {
                RenderEvent e = events[i];
                if (e.Tick > tick) continue;
                if (tick - e.Tick > window) break;
                if (e.Type != RenderEventType.Spawn || e.BodyId != bodyId) continue;
                float u = (tick - e.Tick) * Dt / PulseSeconds;
                if (u >= 1f) continue;
                float a = 0.22f * MathF.Sin(MathF.PI * u) * (1f - u) * (0.5f + 0.5f * Clamp01(e.Intensity > 0f ? e.Intensity : 1f));
                if (a > best) best = a;
            }
            return 1f + best;
        }

        /// <summary>
        /// Camera shake offset at <paramref name="tick"/> from recent Pierce events (M05 "лёгкая тряска камеры при пробивном
        /// ударе"); zero with reduce motion. Deterministic: the jitter is seeded from (event, tick).
        /// </summary>
        public static bool TryGetShake(ReadOnlySpan<RenderEvent> events, int tick, bool reduceMotion, out float dx, out float dy)
        {
            dx = 0f;
            dy = 0f;
            if (reduceMotion) return false;
            int window = (int)Math.Ceiling(ShakeSeconds * SimConstants.TickRate);
            for (int i = events.Length - 1; i >= 0; i--)
            {
                RenderEvent e = events[i];
                if (e.Tick > tick) continue;
                if (tick - e.Tick > window) break;
                if (e.Type != RenderEventType.Pierce) continue;
                float u = (tick - e.Tick) * Dt / ShakeSeconds;
                if (u >= 1f) continue;
                float amp = 0.12f * (1f - u) * (1f - u);
                var rng = Rng.FromSeed(Seed(e.Tick, e.Id) ^ ((ulong)(uint)tick << 16), VfxStreamId + 1);
                dx += (rng.NextFloat() * 2f - 1f) * amp;
                dy += (rng.NextFloat() * 2f - 1f) * amp;
            }
            return dx != 0f || dy != 0f;
        }

        /// <summary>Fade of a flash / splat started <paramref name="ageTicks"/> ago lasting <paramref name="seconds"/>: 1 → 0.</summary>
        public static float Fade(int ageTicks, float seconds)
        {
            if (ageTicks < 0) return 0f;
            float u = ageTicks * Dt / seconds;
            if (u >= 1f) return 0f;
            return 1f - u * u;
        }
    }
}
