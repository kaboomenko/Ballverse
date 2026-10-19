using System;
using Ballverse.Core.Sim;

namespace Ballverse.Core.Modes.RingEscape
{
    /// <summary>
    /// The formulas of the M01 card (spec §2.9 "Модель", "Валидация геометрии" rules 1–7), shared by validation, the scene and
    /// the generator so all three agree. Float math with <see cref="SimMath"/> only (spec §7.4 rule 5).
    /// </summary>
    public static class RingEscapeGeometry
    {
        /// <summary>Clearance added to every fit rule (rules 1, 3, 4, 7), m.</summary>
        public const float Clearance = 0.05f;
        /// <summary>Solid arc kept between gaps (rule 2), degrees.</summary>
        public const float MinSolidArcDeg = 20f;
        /// <summary>Radius of the content safe zone circle: «Статично» fits every ring inside it, m.</summary>
        public const float SafeZoneRadius = 4f;
        /// <summary>Farthest ring radius in the solver and the render stream (spec §2.9 «Поток рендера»), m.</summary>
        public const float MaxRingRadius = 12f;
        /// <summary>Solver envelope (spec §6.5): ring surface speed ≤ 25 m/s, |ω| ≤ 360 °/s.</summary>
        public const float MaxWallSpeed = 25f;
        public const float MaxOmegaDeg = 360f;
        /// <summary>Rule 6b: every ring turns at least this fast (°/s), so a gap always comes round to a resting ball.</summary>
        public const float MinRotationDeg = 10f;
        /// <summary>Rule 6: gravity ≥ 0.2 g, or launch ≥ 2 m/s with restitution ≥ 0.95.</summary>
        public const float MotionMinGravity = 0.2f;
        public const float MotionMinLaunch = 2f;
        public const float MotionMinRestitution = 0.95f;
        /// <summary>«Сжатие»: one spacing step takes 0.4 s (radial wall speed ≤ 3 m/s).</summary>
        public const float ShiftSeconds = 0.4f;
        /// <summary>Speed multiplier floor of the additive per-ring step (card: «не меньше 0,2»).</summary>
        public const float MinSpeedMultiplier = 0.2f;
        /// <summary>Pulse law: ±40 % with a 4 s period.</summary>
        public const float PulseAmplitude = 0.4f;
        public const float PulsePeriod = 4f;
        const float AngleTolDeg = 1e-3f;
        const float LengthTol = 1e-5f;

        /// <summary>Gap width of ring <paramref name="i"/>: gapSize · (1 − gapShrink · i / (ringCount − 1)), degrees.</summary>
        public static float GapDeg(float gapSizeDeg, float shrink, int i, int ringCount)
        {
            if (ringCount <= 1) return gapSizeDeg;
            return gapSizeDeg * (1f - shrink * i / (ringCount - 1));
        }

        public static float GapDeg(RingEscapeConfig c, int i)
        {
            return GapDeg(c.GapSizeDeg, c.GapShrink, i, c.RingCount);
        }

        /// <summary>Radius of ring i's centre line at the start (all progressions), m.</summary>
        public static float RingRadius(RingEscapeConfig c, int i)
        {
            return c.InnerRadius + i * c.RingSpacing;
        }

        /// <summary>Radius at which ring i is passed (rule 3): innerRadius in «Сжатие», else its own radius.</summary>
        public static float PassRadius(RingEscapeConfig c, int i)
        {
            return c.Progression == RingProgression.Compress ? c.InnerRadius : RingRadius(c, i);
        }

        /// <summary>Free width of a gap at radius r: 2 · r · sin(gap / 2) − t (round caps of radius t / 2), m.</summary>
        public static float FreeWidth(float radius, float gapDeg, float thickness)
        {
            return 2f * radius * SimMath.Sin(gapDeg * 0.5f * SimMath.DegToRad) - thickness;
        }

        /// <summary>Rule 3 bound gapMin(r) = 2 · asin((d + t + 0.05) / (2r)), degrees; 360 when no gap can pass.</summary>
        public static float GapMinDeg(float radius, float ballSize, float thickness)
        {
            float a = (ballSize + thickness + Clearance) / (2f * radius);
            if (!(a < 1f)) return 360f;
            return 2f * SimMath.Asin(a) / SimMath.DegToRad;
        }

        /// <summary>Rule 1: ringSpacing ≥ ballSize + ringThickness + 0.05.</summary>
        public static bool SpacingOk(float spacing, float ballSize, float thickness)
        {
            return spacing + LengthTol >= ballSize + thickness + Clearance;
        }

        /// <summary>Rule 2: gapCount · (gap_0 + 20°) ≤ 360° (gap_0 = gapSize is the widest).</summary>
        public static bool SolidArcOk(int gapCount, float gapSizeDeg)
        {
            return gapCount * (gapSizeDeg + MinSolidArcDeg) <= 360f + AngleTolDeg;
        }

        /// <summary>Widest gap rule 2 allows for a gap count, degrees.</summary>
        public static float MaxGapForCount(int gapCount)
        {
            return 360f / gapCount - MinSolidArcDeg;
        }

        /// <summary>Rule 3 for every ring.</summary>
        public static bool PassabilityOk(RingEscapeConfig c)
        {
            for (int i = 0; i < c.RingCount; i++)
            {
                if (GapDeg(c, i) + AngleTolDeg < GapMinDeg(PassRadius(c, i), c.BallSize, c.RingThickness)) return false;
            }
            return true;
        }

        /// <summary>Largest gapShrink fraction keeping rule 3 on every ring (≥ 0; 0.5 when shrink cannot break it).</summary>
        public static float MaxGapShrink(RingEscapeConfig c)
        {
            if (c.RingCount <= 1) return 0.5f;
            float best = 0.5f;
            for (int i = 1; i < c.RingCount; i++)
            {
                float gm = GapMinDeg(PassRadius(c, i), c.BallSize, c.RingThickness);
                float s = (1f - gm / c.GapSizeDeg) * (c.RingCount - 1) / i;
                if (s < best) best = s;
            }
            return best < 0f ? 0f : best;
        }

        /// <summary>
        /// Rule 4: largest ball diameter once ring <paramref name="current"/> is the current one:
        /// min(ringSpacing − t − 0.05; min over j ≥ current of w(ř_j, gap_j) − 0.05), m.
        /// </summary>
        public static float GrowLimit(RingEscapeConfig c, int current)
        {
            float limit = c.RingSpacing - c.RingThickness - Clearance;
            for (int j = Math.Max(0, current); j < c.RingCount; j++)
            {
                float w = FreeWidth(PassRadius(c, j), GapDeg(c, j), c.RingThickness) - Clearance;
                if (w < limit) limit = w;
            }
            return limit;
        }

        /// <summary>Speed multiplier of ring i: max(0.2, 1 + speedStep · i).</summary>
        public static float SpeedMultiplier(float speedStep, int i)
        {
            float m = 1f + speedStep * i;
            return m < MinSpeedMultiplier ? MinSpeedMultiplier : m;
        }

        /// <summary>Smallest multiplier over the rings of a run.</summary>
        public static float MinSpeedMultiplierOf(float speedStep, int ringCount)
        {
            return speedStep >= 0f ? 1f : SpeedMultiplier(speedStep, ringCount - 1);
        }

        /// <summary>
        /// Rule 6b: the smallest rotationSpeed (°/s) such that every ring turns at ≥ 10 °/s and, with the reverse law, sweeps a
        /// full turn between reversals (|ω_min| · T ≥ 360°), so a gap always reaches a ball resting anywhere on the ring.
        /// </summary>
        public static float MinRotationSpeedDeg(RingEscapeConfig c)
        {
            float m = MinSpeedMultiplierOf(c.SpeedStep, c.RingCount);
            float min = MinRotationDeg / m;
            if (c.SpeedLaw == RingSpeedLaw.Reverse)
            {
                float sweep = 360f / (m * c.ReversePeriod);
                if (sweep > min) min = sweep;
            }
            return min;
        }

        /// <summary>Rule 6: the ball can never stop for good.</summary>
        public static bool MotionOk(float gravityG, float launchSpeed, float restitution)
        {
            return gravityG + 1e-4f >= MotionMinGravity || (launchSpeed + 1e-4f >= MotionMinLaunch && restitution + 1e-4f >= MotionMinRestitution);
        }

        /// <summary>Most rings a progression allows (card «Прогрессия»): «Отдаление камеры» ≤ 12 m, «Статично» ≤ 4 m.</summary>
        public static int MaxRingCount(RingProgression p, float innerRadius, float spacing)
        {
            if (p == RingProgression.Compress) return 100;
            float outer = p == RingProgression.Static ? SafeZoneRadius : MaxRingRadius;
            int n = (int)MathF.Floor((outer - innerRadius) / spacing + 1e-4f) + 1;
            return Math.Max(1, Math.Min(100, n));
        }

        /// <summary>Envelope |ω| limit of a ring whose outer face is at <paramref name="outerRadius"/>: min(360 °/s, 25 m/s ÷ r̂), rad/s.</summary>
        public static float OmegaLimitRad(float outerRadius)
        {
            float byWall = MaxWallSpeed / outerRadius;
            float max = MaxOmegaDeg * SimMath.DegToRad;
            return byWall < max ? byWall : max;
        }

        // ------------------------------------------------------------------ rule 7: start layout in ring 0

        /// <summary>Radius available to ball centres plus their radius inside ring 0: innerRadius − t/2 − 0.05, m.</summary>
        public static float FreeRadius(float innerRadius, float thickness)
        {
            return innerRadius - thickness * 0.5f - Clearance;
        }

        /// <summary>
        /// Rule 7: how many balls of diameter <paramref name="ballSize"/> the deterministic start layout places inside ring 0
        /// with 0.05 m clearance to each other and to the ring (see <see cref="Layout"/>). Always ≥ 1 when rule 3 holds.
        /// </summary>
        public static int StartCapacity(float ballSize, float innerRadius, float thickness)
        {
            return LayoutCore(ballSize, innerRadius, thickness, int.MaxValue, null);
        }

        /// <summary>
        /// Start positions of <paramref name="count"/> balls (spec §2.9 M01 rule 7): one ball sits at the centre; more balls fill
        /// concentric layers anchored at the ring: layer k at radius ρ_k = R − d/2 − k·p (R = <see cref="FreeRadius"/>, pitch
        /// p = d + 0.05) holds ⌊π / asin(p / 2ρ_k)⌋ balls; a centre slot exists when the innermost layer is at least p from
        /// the centre. Balls fill the centre first, then layers from the inside out, evenly spread on a partly filled layer,
        /// starting at the top. Returns the number placed (≤ count).
        /// </summary>
        public static int Layout(float ballSize, float innerRadius, float thickness, int count, Vec2[] positions)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (count > positions.Length) throw new ArgumentException("positions is too short", nameof(positions));
            return LayoutCore(ballSize, innerRadius, thickness, count, positions);
        }

        const int MaxLayers = 64;

        static int LayoutCore(float d, float innerRadius, float t, int count, Vec2[] positions)
        {
            float free = FreeRadius(innerRadius, t);
            float r = d * 0.5f;
            if (free + LengthTol < r) return 0;
            if (count == 1)
            {
                if (positions != null) positions[0] = Vec2.Zero;
                return 1;
            }
            float p = d + Clearance;
            Span<float> radii = stackalloc float[MaxLayers];
            Span<int> caps = stackalloc int[MaxLayers];
            int layers = 0;
            float rho = free - r;
            while (layers < MaxLayers && rho + LengthTol >= p * 0.5f)
            {
                float s = p / (2f * rho);
                int cap = s >= 1f ? 2 : (int)MathF.Floor(SimMath.Pi / SimMath.Asin(s) + 1e-4f);
                if (cap < 2) break;
                radii[layers] = rho;
                caps[layers] = cap;
                layers++;
                rho -= p;
            }
            bool centre = layers == 0 ? true : radii[layers - 1] + LengthTol >= p;
            int capacity = centre ? 1 : 0;
            for (int k = 0; k < layers; k++) capacity += caps[k];
            if (positions == null) return capacity;

            int placed = 0;
            if (centre && placed < count) positions[placed++] = Vec2.Zero;
            for (int k = layers - 1; k >= 0 && placed < count; k--)
            {
                int m = Math.Min(caps[k], count - placed);
                float offset = SimMath.HalfPi + (k & 1) * (SimMath.Pi / caps[k]);
                for (int j = 0; j < m; j++)
                {
                    float a = offset + SimMath.TwoPi * j / m;
                    positions[placed++] = Vec2.FromAngle(a) * radii[k];
                }
            }
            return placed;
        }
    }
}
