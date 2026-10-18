using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>
    /// The 15 store product ids of the 1.0 catalog (spec §5.1, owner decision Р5). The single source of truth for ids:
    /// they cannot be reused after deletion in App Store Connect, so never rename or recycle one.
    /// The "ballverse." prefix depends on the final bundle id (open question В13).
    /// </summary>
    public static class ProductIds
    {
        public const string ModeBallBattle = "ballverse.mode.ball_battle";
        public const string ModeLastStanding = "ballverse.mode.last_standing";
        public const string ModeColorWar = "ballverse.mode.color_war";
        public const string ModeMultiply = "ballverse.mode.multiply";
        public const string ModeMazeEscape = "ballverse.mode.maze_escape";
        public const string ModeSmash = "ballverse.mode.smash";
        public const string ModeChainReaction = "ballverse.mode.chain_reaction";
        public const string ModePendulumWave = "ballverse.mode.pendulum_wave";
        public const string ModeOrbit = "ballverse.mode.orbit";
        public const string ModeLiquidSand = "ballverse.mode.liquid_sand";

        public const string AllModes = "ballverse.bundle.all_modes";
        public const string RemoveExportAds = "ballverse.ads.remove_export";
        public const string RemoveInterstitialAds = "ballverse.ads.remove_interstitial";
        public const string NoAds = "ballverse.bundle.no_ads";
        public const string Ultimate = "ballverse.bundle.ultimate";

        /// <summary>Prefix of single-mode products; the suffix is <see cref="ModeInfo.Id"/>.</summary>
        public const string ModePrefix = "ballverse.mode.";

        static readonly string[] s_all =
        {
            ModeBallBattle, ModeLastStanding, ModeColorWar, ModeMultiply, ModeMazeEscape,
            ModeSmash, ModeChainReaction, ModePendulumWave, ModeOrbit, ModeLiquidSand,
            AllModes, RemoveExportAds, RemoveInterstitialAds, NoAds, Ultimate,
        };

        /// <summary>All ids in spec §5.1 table order (what to register with the store).</summary>
        public static IReadOnlyList<string> All => s_all;
    }
}
