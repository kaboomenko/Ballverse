using System;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// Dynamic body budgets per mode and tier A / B / C (spec §2.9 "Бюджет тел"): the most dynamic bodies a mode keeps
    /// at 60 FPS in real time; exports use the same cap so replays stay reproducible. Composite budgets keep their parts
    /// per <see cref="BodyKind"/> (M03 16 balls + 60 obstacles, M07 32 balls + 200 / 150 / 100 tiles…), so a mode caps
    /// each count parameter with the matching part (<see cref="AsTierCap"/>, <see cref="BallCap"/>), never with the sum.
    /// </summary>
    /// <remarks>
    /// Release-1 modes follow the release-1 mode cards (owner decisions Р18/Р19, spec §2.9 "Физика и бюджет"): kinematic
    /// walls (rings, polygons, platforms) and cosmetic VFX particles are not solver bodies and are not counted, so M01
    /// Ring Escape is 20 / 12 / 8 balls, M02 Geometry Bounce 200 / 120 / 60 balls (all variants, incl. "Размножение") and
    /// M05 Helix Drop one ball. Roadmap modes keep the original card numbers: single-number budgets are
    /// <see cref="BodyKind.Balls"/> when the spec counts balls (M04, M08, M09, M10, M13 simple pendulums; double pendulums
    /// 20 / 15 / 10 are a mode rule) and <see cref="BodyKind.Bodies"/> when it counts every dynamic body together (M12, M14,
    /// M15). M06 weapons are sensors on the fighters and are not counted. Targets are verified by the Core.Sim benchmark.
    /// </remarks>
    public static class BodyBudgets
    {
        sealed class Part
        {
            public readonly BodyKind Kind;
            public readonly int A;
            public readonly int B;
            public readonly int C;

            public Part(BodyKind kind, int a, int b, int c)
            {
                Kind = kind;
                A = a;
                B = b;
                C = c;
            }

            public int For(DeviceTier tier)
            {
                switch (tier)
                {
                    case DeviceTier.A: return A;
                    case DeviceTier.B: return B;
                    case DeviceTier.C: return C;
                    default: throw new ArgumentOutOfRangeException(nameof(tier));
                }
            }
        }

        static Part P(BodyKind kind, int a, int b, int c)
        {
            return new Part(kind, a, b, c);
        }

        static Part P(BodyKind kind, int all)
        {
            return new Part(kind, all, all, all);
        }

        static readonly Part[][] s_budgets =
        {
            new[] { P(BodyKind.Balls, 20, 12, 8) },                                                       // M01 ring_escape (release 1)
            new[] { P(BodyKind.Balls, 200, 120, 60) },                                                    // M02 geometry_bounce (release 1)
            new[] { P(BodyKind.Balls, 16), P(BodyKind.Obstacles, 60) },                                   // M03 marble_race
            new[] { P(BodyKind.Balls, 2000, 1000, 400) },                                                 // M04 peg_drop ("Ливень")
            new[] { P(BodyKind.Balls, 1) },                                                               // M05 helix_drop (release 1)
            new[] { P(BodyKind.Balls, 8), P(BodyKind.Projectiles, 40) },                                  // M06 ball_battle
            new[] { P(BodyKind.Balls, 32), P(BodyKind.Tiles, 200, 150, 100) },                            // M07 last_standing
            new[] { P(BodyKind.Balls, 120, 80, 50) },                                                     // M08 color_war
            new[] { P(BodyKind.Balls, 2500, 1200, 500) },                                                 // M09 multiply
            new[] { P(BodyKind.Balls, 20) },                                                              // M10 maze_escape
            new[] { P(BodyKind.Shards, 600, 350, 200) },                                                  // M11 smash (fragments)
            new[] { P(BodyKind.Bodies, 400, 250, 150) },                                                  // M12 chain_reaction
            new[] { P(BodyKind.Balls, 40, 40, 30) },                                                      // M13 pendulum_wave
            new[] { P(BodyKind.Bodies, 1000, 600, 300) },                                                 // M14 orbit
            new[] { P(BodyKind.Bodies, 3000, 1500, 700) },                                                // M15 liquid_sand
        };

        /// <summary>All dynamic bodies of <paramref name="mode"/> on <paramref name="tier"/>: the sum of its parts.</summary>
        public static int Total(ModeCode mode, DeviceTier tier)
        {
            int sum = 0;
            foreach (var p in Parts(mode)) sum += p.For(tier);
            return sum;
        }

        /// <summary>Budget of one kind of body, or null when the spec gives <paramref name="mode"/> no such part.</summary>
        public static int? Get(ModeCode mode, DeviceTier tier, BodyKind kind)
        {
            CheckTier(tier);
            foreach (var p in Parts(mode))
                if (p.Kind == kind) return p.For(tier);
            return null;
        }

        /// <summary>Whether the spec gives <paramref name="mode"/> a budget part of <paramref name="kind"/>.</summary>
        public static bool Has(ModeCode mode, BodyKind kind)
        {
            return Get(mode, DeviceTier.A, kind).HasValue;
        }

        /// <summary>
        /// The <paramref name="kind"/> part as a tier cap for the matching count parameter (e.g. M07 tiles). Throws
        /// <see cref="ArgumentException"/> when the mode has no such part. A cap above the parameter's max is a no-op.
        /// </summary>
        public static TierCap AsTierCap(ModeCode mode, BodyKind kind)
        {
            foreach (var p in Parts(mode))
                if (p.Kind == kind) return new TierCap(p.A, p.B, p.C);
            throw new ArgumentException("Mode " + mode + " has no " + kind + " budget", nameof(kind));
        }

        /// <summary>
        /// Tier cap of a ball-count parameter (<c>ballCount</c>): the <see cref="BodyKind.Balls"/> part, or the shared
        /// <see cref="BodyKind.Bodies"/> budget when balls are counted together with other bodies. Throws
        /// <see cref="ArgumentException"/> for M11, whose budget covers fragments only.
        /// </summary>
        public static TierCap BallCap(ModeCode mode)
        {
            return AsTierCap(mode, Has(mode, BodyKind.Balls) ? BodyKind.Balls : BodyKind.Bodies);
        }

        static Part[] Parts(ModeCode mode)
        {
            int m = (int)mode - 1;
            if (m < 0 || m >= s_budgets.Length) throw new ArgumentOutOfRangeException(nameof(mode));
            return s_budgets[m];
        }

        static void CheckTier(DeviceTier tier)
        {
            if (tier != DeviceTier.A && tier != DeviceTier.B && tier != DeviceTier.C) throw new ArgumentOutOfRangeException(nameof(tier));
        }
    }
}
