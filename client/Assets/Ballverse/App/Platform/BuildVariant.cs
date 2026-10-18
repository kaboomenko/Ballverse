using System;

namespace Ballverse.Platform
{
    /// <summary>
    /// Build variant (spec §7.9), chosen by Ballverse.Editor.BuildScript (-ballverseVariant) and compiled into the
    /// player as one scripting symbol: <see cref="BuildVariants.DevSymbol"/>, <see cref="BuildVariants.OwnerTestSymbol"/>
    /// or <see cref="BuildVariants.StoreSymbol"/>.
    /// </summary>
    public enum BuildVariant
    {
        /// <summary>Debug-optimised development player (Unity "Development Build"): test ad units, fake store, developer menu.</summary>
        Dev = 0,

        /// <summary>Release-optimised player for the owner's phones: test ad units, fake store or sandbox, hidden developer menu.</summary>
        OwnerTest = 1,

        /// <summary>Store release: real AdMob units and store billing; no fakes, test ids or developer menu.</summary>
        Store = 2,
    }

    /// <summary>
    /// The variant symbols and the variant this code was compiled as. Feature code branches on the symbols
    /// (<c>#if BALLVERSE_STORE</c>) where fakes, test ad ids or the developer menu must be physically absent from Store
    /// builds (spec §8.9), and may read <see cref="Current"/> for everything else.
    /// </summary>
    public static class BuildVariants
    {
        public const string DevSymbol = "BALLVERSE_DEV";
        public const string OwnerTestSymbol = "BALLVERSE_OWNERTEST";
        public const string StoreSymbol = "BALLVERSE_STORE";

        /// <summary>
        /// The variant of this player. In the Editor and in players built without Ballverse.Editor.BuildScript no symbol
        /// is defined, which counts as <see cref="BuildVariant.Dev"/>.
        /// </summary>
        public static BuildVariant Current
        {
            get
            {
#if BALLVERSE_STORE
                return BuildVariant.Store;
#elif BALLVERSE_OWNERTEST
                return BuildVariant.OwnerTest;
#else
                return BuildVariant.Dev;
#endif
            }
        }

        /// <summary>Scripting symbol of <paramref name="variant"/>.</summary>
        public static string SymbolOf(BuildVariant variant)
        {
            switch (variant)
            {
                case BuildVariant.Dev: return DevSymbol;
                case BuildVariant.OwnerTest: return OwnerTestSymbol;
                case BuildVariant.Store: return StoreSymbol;
                default: throw new ArgumentOutOfRangeException(nameof(variant), variant, "Unknown build variant.");
            }
        }

        /// <summary>True for the variants that may contain fakes and Google's test ad ids (all but Store).</summary>
        public static bool AllowsTestServices(BuildVariant variant) => variant != BuildVariant.Store;

        /// <summary>Parses "Dev", "OwnerTest" or "Store" (case-insensitive, surrounding spaces ignored).</summary>
        public static bool TryParse(string value, out BuildVariant variant)
        {
            variant = BuildVariant.Dev;
            if (value == null) return false;
            string v = value.Trim();
            if (string.Equals(v, "Dev", StringComparison.OrdinalIgnoreCase)) { variant = BuildVariant.Dev; return true; }
            if (string.Equals(v, "OwnerTest", StringComparison.OrdinalIgnoreCase)) { variant = BuildVariant.OwnerTest; return true; }
            if (string.Equals(v, "Store", StringComparison.OrdinalIgnoreCase)) { variant = BuildVariant.Store; return true; }
            return false;
        }
    }
}
