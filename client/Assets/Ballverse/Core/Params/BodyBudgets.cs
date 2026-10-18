using System;

namespace Ballverse.Core.Params
{
    /// <summary>
    /// Dynamic body budgets per mode and tier A / B / C (spec §2.9 "Бюджет тел"): the most dynamic bodies a mode keeps
    /// at 60 FPS in real time; exports use the same cap so replays stay reproducible. Modes derive the
    /// <see cref="TierCap"/> of their body-count parameters from these values.
    /// </summary>
    /// <remarks>
    /// Composite budgets are summed: M03 16 balls + 60 obstacles; M05 1 ball + 30 platforms + 150/100/60 shards;
    /// M06 8 fighters + 40 arrows (weapons are sensors on the fighters); M07 32 balls + 200/150/100 tiles.
    /// M02 uses the "new ball" variant, M13 simple pendulums (double: 20/15/10). Targets are verified in spike S1.
    /// </remarks>
    public static class BodyBudgets
    {
        static readonly int[,] s_budgets =
        {
            { 300, 200, 120 },   // M01 ring_escape (incl. shards)
            { 600, 400, 200 },   // M02 grow_bounce ("new ball" variant)
            { 76, 76, 76 },      // M03 marble_race
            { 2000, 1000, 400 }, // M04 peg_drop ("Ливень")
            { 181, 131, 91 },    // M05 spiral_drop
            { 48, 48, 48 },      // M06 ball_battle
            { 232, 182, 132 },   // M07 last_standing
            { 120, 80, 50 },     // M08 color_war
            { 2500, 1200, 500 }, // M09 multiply
            { 20, 20, 20 },      // M10 maze_escape
            { 600, 350, 200 },   // M11 smash (fragments)
            { 400, 250, 150 },   // M12 chain_reaction
            { 40, 40, 30 },      // M13 pendulum_wave
            { 1000, 600, 300 },  // M14 orbit
            { 3000, 1500, 700 }, // M15 liquid_sand
        };

        public static int Get(ModeCode mode, DeviceTier tier)
        {
            int m = (int)mode - 1;
            if (m < 0 || m >= s_budgets.GetLength(0)) throw new ArgumentOutOfRangeException(nameof(mode));
            int t = (int)tier;
            if (t < 0 || t > 2) throw new ArgumentOutOfRangeException(nameof(tier));
            return s_budgets[m, t];
        }

        /// <summary>The budget as a tier cap.</summary>
        public static TierCap AsTierCap(ModeCode mode)
        {
            return new TierCap(Get(mode, DeviceTier.A), Get(mode, DeviceTier.B), Get(mode, DeviceTier.C));
        }
    }
}
