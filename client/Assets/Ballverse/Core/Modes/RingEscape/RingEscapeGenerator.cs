using System;
using Ballverse.Core.Params;

namespace Ballverse.Core.Modes.RingEscape
{
    /// <summary>
    /// Candidate parameters of 🎲 for M01 (spec §2.9 M01 «Генератор», §2.4 step 2). The style sets the curated ranges; the ring
    /// count is the target duration divided by the expected time per ring T̄ of the drawn parameters
    /// (<see cref="EstimateSecondsPerRing"/>), so candidates cluster around the target and the headless runs pick the closest.
    /// Generated runs always use «Сжатие» (the time per ring is stationary there), the physics defaults of the card
    /// (1 g, e = 1, friction 0, 20 m/s), alternating directions and a finale after the escape. Look and sound parameters the
    /// style does not name are kept from the user's set. Pure function of the request.
    /// </summary>
    public static class RingEscapeGenerator
    {
        /// <summary>Rng stream of candidate parameters ("M01_GEN_").</summary>
        public const ulong GeneratorStream = 0x4D30315F47454E5FUL;
        /// <summary>
        /// Candidates aim slightly below the target: escape times are right-skewed (a ball resting on a slowly turning ring waits
        /// for the gap) and runs cut at target × 1.3 are rejected, so a centre at 0.93 × target keeps more candidates in the
        /// ±30 % scoring window.
        /// </summary>
        public const float AimFactor = 0.93f;
        /// <summary>Ring count basis when the target is "unlimited": a 45 s escape.</summary>
        public const float UnlimitedTargetSeconds = 45f;

        public static ParamSet Generate(in ModeCandidateRequest request)
        {
            if (request.BaseValues == null) throw new ArgumentException("BaseValues are required", nameof(request));
            ParamSchema schema = RingEscapeParams.Schema;
            Rng rng = Rng.FromSeed(request.Seed, GeneratorStream);
            GenerationStyle style = request.Style;
            if (style == GenerationStyle.Any) style = (GenerationStyle)(1 + rng.NextInt(0, 5));
            ParamSet v = request.BaseValues.Sanitize(schema, request.Tier).Values;
            RingEscapeConfig baseCfg = RingEscapeConfig.From(v);
            bool keepParticipants = request.FixedParticipants > 0;
            float targetSeconds = (baseCfg.TargetDurationTicks.HasValue ? baseCfg.TargetDurationTicks.Value / (float)SimConstants.TickRate : UnlimitedTargetSeconds)
                * AimFactor;

            v = v.With(CommonParams.Gravity, ParamValue.Float(1.0))
                .With(CommonParams.GravityAngle, ParamValue.Int(270))
                .With(CommonParams.Restitution, ParamValue.Float(RingEscapeParams.DefaultRestitution))
                .With(CommonParams.Friction, ParamValue.Float(RingEscapeParams.DefaultFriction))
                .With(CommonParams.MaxSpeed, ParamValue.Float(RingEscapeParams.DefaultMaxSpeed))
                .With(CommonParams.BallSize, ParamValue.Float(RingEscapeParams.DefaultBallSize))
                .With(RingEscapeParams.Progression, ParamValue.String(RingEscapeParams.ProgressionCompress))
                .With(RingEscapeParams.InnerRadius, ParamValue.Float(1.2))
                .With(RingEscapeParams.RingSpacing, ParamValue.Float(0.6))
                .With(RingEscapeParams.RingThickness, ParamValue.Float(0.12))
                .With(RingEscapeParams.RotationDirection, ParamValue.String(RingEscapeParams.DirectionAlternate))
                .With(RingEscapeParams.SpeedLaw, ParamValue.String(RingEscapeParams.LawConstant))
                .With(RingEscapeParams.SpeedStep, ParamValue.Int(0))
                .With(RingEscapeParams.GapCount, ParamValue.Int(1))
                .With(RingEscapeParams.GapPlacement, ParamValue.String(RingEscapeParams.PlacementRandom))
                .With(RingEscapeParams.GapShrink, ParamValue.Int(0))
                .With(RingEscapeParams.GrowOnHit, ParamValue.Int(0))
                .With(RingEscapeParams.DestroyPassed, ParamValue.Bool(true))
                .With(RingEscapeParams.LaunchAngleRandom, ParamValue.Bool(true))
                .With(RingEscapeParams.AfterEscape, ParamValue.String(RingEscapeParams.AfterFinale));

            int balls = 1;
            bool competition = false;
            bool reverse = false;
            float rotation, launch, perRing;
            int ringCount = -1;
            switch (style)
            {
                case GenerationStyle.Chaos:
                    balls = rng.NextInt(4, 13);
                    rotation = rng.NextFloat(120f, 240f);
                    launch = rng.NextFloat(8f, 12f);
                    perRing = rng.NextFloat(1f, 2f);
                    reverse = true;
                    v = v.With(RingEscapeParams.SpeedLaw, ParamValue.String(RingEscapeParams.LawReverse))
                        .With(RingEscapeParams.ReversePeriod, ParamValue.Float(rng.NextInt(6, 11) * 0.5));
                    break;
                case GenerationStyle.Meditation:
                    rotation = rng.NextFloat(25f, 50f);
                    launch = rng.NextFloat(3f, 6f);
                    perRing = rng.NextFloat(3f, 5f);
                    v = v.With(CommonParams.Theme, ParamValue.String("Pastel"));
                    break;
                case GenerationStyle.TenseFinale:
                    rotation = rng.NextFloat(60f, 120f);
                    launch = rng.NextFloat(5f, 9f);
                    perRing = rng.NextFloat(1.5f, 3f);
                    break;
                case GenerationStyle.ManyBalls:
                    balls = rng.NextInt(10, RingEscapeParams.MaxBalls + 1);
                    competition = true;
                    rotation = rng.NextFloat(60f, 150f);
                    launch = rng.NextFloat(6f, 10f);
                    perRing = rng.NextFloat(1f, 2f);
                    break;
                default: // Minimalism: 6–10 rings, the time per ring follows from the target.
                    rotation = rng.NextFloat(40f, 90f);
                    launch = rng.NextFloat(4f, 8f);
                    ringCount = rng.NextInt(MinimalismMinRings, MinimalismMaxRings + 1);
                    perRing = targetSeconds / ringCount;
                    v = v.With(CommonParams.Theme, ParamValue.String("Mono"));
                    break;
            }
            if (keepParticipants)
            {
                balls = baseCfg.BallCount;
                competition = baseCfg.Competition;
            }
            else if (balls > 1)
            {
                // Many balls pass rings fast (≥ 0.4 s each, at most 100 rings): fewer balls until the target is reachable.
                int minBalls = competition ? 2 : 1;
                while (balls > minBalls && MaxRingsSeconds(balls, rotation, launch, reverse) < 0.8f * targetSeconds) balls--;
            }
            v = v.With(CommonParams.BallCount, ParamValue.Int(balls))
                .With(RingEscapeParams.Competition, ParamValue.Bool(competition))
                .With(RingEscapeParams.RotationSpeed, ParamValue.Float(Math.Round(rotation)))
                .With(RingEscapeParams.LaunchSpeed, ParamValue.Float(Math.Round(launch * 2f) * 0.5));

            // The gap width that gives the style's time per ring (inverse of the model), then the ring count for the target.
            RingEscapeConfig cfg = RingEscapeConfig.From(Prepare(v, request.Tier));
            float gapMin = RingEscapeGeometry.GapMinDeg(cfg.InnerRadius, cfg.BallSize, cfg.RingThickness);
            float slack = SlackFor(perRing, cfg.RotationSpeedDeg, cfg.BallCount, cfg.LaunchSpeed, reverse);
            float gap = MathF.Min(RingEscapeGeometry.MaxGapForCount(1), MathF.Max(MathF.Ceiling(gapMin) + 1f, MathF.Round(gapMin + slack)));
            v = v.With(RingEscapeParams.GapSize, ParamValue.Float(gap));
            cfg = RingEscapeConfig.From(Prepare(v, request.Tier));
            float estimate = EstimateSecondsPerRing(cfg);
            if (ringCount < 0) ringCount = (int)MathF.Round(targetSeconds / estimate);
            if (style == GenerationStyle.Minimalism)
            {
                // Card: «Минимализм» always has 6–10 rings. At long targets (≳ 150 s for one ball) even the narrowest passable
                // gap cannot stretch 10 rings to the target: the run ends earlier and the session prefers better-fitting
                // candidates of other styles when the style is not forced.
                ringCount = Math.Max(MinimalismMinRings, Math.Min(MinimalismMaxRings, ringCount));
            }
            else
            {
                // Even the narrowest passable gap cannot make a ring slower than the estimate at this gap: never fewer rings
                // than the target needs.
                if (gap <= MathF.Ceiling(gapMin) + 1f) ringCount = Math.Max(ringCount, (int)MathF.Ceiling(targetSeconds / estimate * 0.9f));
                ringCount = Math.Max(2, Math.Min(100, ringCount));
            }
            v = v.With(RingEscapeParams.RingCount, ParamValue.Int(ringCount));

            if (style == GenerationStyle.TenseFinale && ringCount > 1)
            {
                // Narrowest allowed last gap: 1.1–1.3 × gapMin, reached by the shrink (card «Напряжённый финал»).
                cfg = RingEscapeConfig.From(Prepare(v, request.Tier));
                float last = gapMin * rng.NextFloat(1.1f, 1.3f);
                int shrink = (int)MathF.Floor((1f - last / cfg.GapSizeDeg) * 100f);
                v = v.With(RingEscapeParams.GapShrink, ParamValue.Int(Math.Max(0, Math.Min(50, shrink))));
                // The narrower gaps take longer: shorten the run by the extra expected time.
                cfg = RingEscapeConfig.From(Prepare(v, request.Tier));
                float total = 0f;
                for (int i = 0; i < cfg.RingCount; i++) total += EstimateSecondsPerRing(cfg, RingEscapeGeometry.GapDeg(cfg, i));
                if (total > 0f)
                {
                    int scaled = (int)MathF.Round(cfg.RingCount * targetSeconds / total);
                    v = v.With(RingEscapeParams.RingCount, ParamValue.Int(Math.Max(2, Math.Min(100, scaled))));
                }
            }
            return Prepare(v, request.Tier);
        }

        /// <summary>Ring count range of the «Минимализм» style (card «Генератор»).</summary>
        public const int MinimalismMinRings = 6;
        public const int MinimalismMaxRings = 10;

        /// <summary>Longest expected escape (100 rings at the narrowest passable gap) for a ball count, seconds.</summary>
        static float MaxRingsSeconds(int balls, float rotationDeg, float launch, bool reverse)
        {
            Model m = ModelFor(balls);
            float t = m.Factor(rotationDeg, balls, launch, reverse) * MathF.Pow(1.5f, m.Slack);
            return 100f * MathF.Max(MinSecondsPerRing, t);
        }

        /// <summary>Gap slack beyond gapMin (degrees) for which the model expects <paramref name="secondsPerRing"/>.</summary>
        static float SlackFor(float secondsPerRing, float rotationDeg, int balls, float launch, bool reverse)
        {
            Model m = ModelFor(balls);
            float k = m.Factor(rotationDeg, balls, launch, reverse);
            float slack = MathF.Pow(MathF.Max(MinSecondsPerRing, secondsPerRing) / k, 1f / m.Slack);
            return MathF.Max(1f, MathF.Min(110f, slack));
        }

        static ParamSet Prepare(ParamSet v, DeviceTier tier)
        {
            return ModeParams.Prepare(RingEscapeMode.Instance, v, tier).Values;
        }

        /// <summary>Expected seconds per ring of a configuration (empirical model, see <see cref="EstimateSecondsPerRing(RingEscapeConfig, float)"/>).</summary>
        public static float EstimateSecondsPerRing(RingEscapeConfig c)
        {
            return EstimateSecondsPerRing(c, c.GapSizeDeg);
        }

        /// <summary>
        /// Expected seconds to pass one ring in «Сжатие» with the generator's physics (1 g, e = 1, friction 0), fitted on headless
        /// probes of this solver: it falls with the gap's slack beyond gapMin, with the rotation speed, the launch speed and the
        /// number of balls (any ball may pass), and never drops below <see cref="MinSecondsPerRing"/>. Used only to size
        /// candidates; the headless run decides.
        /// </summary>
        public static float EstimateSecondsPerRing(RingEscapeConfig c, float gapDeg)
        {
            float gapMin = RingEscapeGeometry.GapMinDeg(c.InnerRadius, c.BallSize, c.RingThickness);
            float slack = MathF.Max(0.5f, (gapDeg - gapMin) * c.GapCount);
            int balls = Math.Max(1, c.BallCount);
            Model m = ModelFor(balls);
            float t = m.Factor(c.RotationSpeedDeg, balls, c.LaunchSpeed, c.SpeedLaw == RingSpeedLaw.Reverse) * MathF.Pow(slack, m.Slack);
            return MathF.Max(MinSecondsPerRing, t);
        }

        /// <summary>
        /// Floor of the time per ring: in «Сжатие» the next ring needs 0.4 s to shift in, so even many balls cannot sustain a
        /// faster rhythm.
        /// </summary>
        public const float MinSecondsPerRing = 0.4f;

        /// <summary>ln T = ln A + a·ln slack° + b·ln rotation°/s + c·ln balls + d·ln launch m/s (+ ln reverse factor).</summary>
        readonly struct Model
        {
            public readonly float A;
            public readonly float Slack;
            public readonly float Rot;
            public readonly float Balls;
            public readonly float Launch;
            public readonly float Reverse;

            public Model(float a, float slack, float rot, float balls, float launch, float reverse)
            {
                A = a;
                Slack = slack;
                Rot = rot;
                Balls = balls;
                Launch = launch;
                Reverse = reverse;
            }

            /// <summary>Everything but the slack term.</summary>
            public float Factor(float rotationDeg, int balls, float launch, bool reverse)
            {
                return A * MathF.Pow(MathF.Max(5f, rotationDeg), Rot) * MathF.Pow(Math.Max(1, balls), Balls)
                    * MathF.Pow(MathF.Max(1f, launch), Launch) * (reverse ? Reverse : 1f);
            }
        }

        // Least-squares fits on 400 generator candidates (all styles, gaps widened at random by 0–30°, 20 rings, 2 seeds each):
        // one ball (rms error 0.29 in ln T) and four or more balls (rms 0.10). Two or three balls (only with a fixed participant
        // count) use the one-ball fit with the balls^−0.6 trend of an all-configurations fit.
        static readonly Model s_single = new Model(20.197f, -0.714f, -0.025f, -0.6f, -0.106f, 1f);
        static readonly Model s_many = new Model(2.174f, -0.208f, -0.066f, -0.402f, -0.072f, 1.019f);

        static Model ModelFor(int balls)
        {
            return balls >= 4 ? s_many : s_single;
        }
    }
}
