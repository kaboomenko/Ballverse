using System;
using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>
    /// A right granted by purchases (spec §5.1 "Даёт права"). Spec names are MODE_M06…MODE_M15, NO_EXPORT_ADS,
    /// NO_INTERSTITIALS, NO_WATERMARK (see <see cref="EntitlementInfo.SpecName"/>).
    /// Mode entitlements reuse the <see cref="ModeCode"/> number so future paid modes (M16…M39) fit without renumbering;
    /// non-mode rights live at 40+. Values are bit indices of <see cref="EntitlementSet"/> and are not persisted
    /// (the cache stores product ids, spec §5.4).
    /// </summary>
    public enum Entitlement
    {
        ModeM06 = 6,
        ModeM07 = 7,
        ModeM08 = 8,
        ModeM09 = 9,
        ModeM10 = 10,
        ModeM11 = 11,
        ModeM12 = 12,
        ModeM13 = 13,
        ModeM14 = 14,
        ModeM15 = 15,
        /// <summary>No rewarded ad before export (spec §5.3.1).</summary>
        NoExportAds = 40,
        /// <summary>No interstitials, not even loaded (spec §5.3.2 IN7).</summary>
        NoInterstitials = 41,
        /// <summary>Exports without the "Made with Ballverse" watermark (spec §3.10, §5.1, owner decision Р13).</summary>
        NoWatermark = 42,
    }

    /// <summary>Lookups between <see cref="Entitlement"/>, spec names and <see cref="ModeCode"/> (spec §5.1).</summary>
    public static class EntitlementInfo
    {
        static readonly Entitlement[] s_all =
        {
            Entitlement.ModeM06, Entitlement.ModeM07, Entitlement.ModeM08, Entitlement.ModeM09, Entitlement.ModeM10,
            Entitlement.ModeM11, Entitlement.ModeM12, Entitlement.ModeM13, Entitlement.ModeM14, Entitlement.ModeM15,
            Entitlement.NoExportAds, Entitlement.NoInterstitials, Entitlement.NoWatermark,
        };

        /// <summary>Every defined entitlement in ascending value order.</summary>
        public static IReadOnlyList<Entitlement> All => s_allView;

        static readonly IReadOnlyList<Entitlement> s_allView = Array.AsReadOnly(s_all);
        static readonly ulong s_definedMask = BuildDefinedMask();

        public static bool IsDefined(Entitlement e)
        {
            int v = (int)e;
            return v >= 0 && v < 64 && ((s_definedMask >> v) & 1UL) != 0UL;
        }

        static ulong BuildDefinedMask()
        {
            ulong mask = 0UL;
            foreach (var e in s_all) mask |= 1UL << (int)e;
            return mask;
        }

        /// <summary>The MODE_* entitlement that unlocks a paid mode; false for modes without one (M01–M05).</summary>
        public static bool TryGetModeEntitlement(ModeCode mode, out Entitlement entitlement)
        {
            entitlement = (Entitlement)(int)mode;
            if (IsMode(entitlement)) return true;
            entitlement = default;
            return false;
        }

        public static bool IsMode(Entitlement e)
        {
            return (int)e >= (int)Entitlement.ModeM06 && (int)e <= (int)Entitlement.ModeM15;
        }

        /// <summary>The mode a MODE_* entitlement unlocks.</summary>
        public static bool TryGetMode(Entitlement e, out ModeCode mode)
        {
            if (IsMode(e))
            {
                mode = (ModeCode)(int)e;
                return true;
            }
            mode = default;
            return false;
        }

        /// <summary>Spec spelling, e.g. "MODE_M06", "NO_EXPORT_ADS" (spec §5.1).</summary>
        public static string SpecName(Entitlement e)
        {
            if (TryGetMode(e, out ModeCode mode)) return "MODE_" + mode;
            switch (e)
            {
                case Entitlement.NoExportAds: return "NO_EXPORT_ADS";
                case Entitlement.NoInterstitials: return "NO_INTERSTITIALS";
                case Entitlement.NoWatermark: return "NO_WATERMARK";
                default: throw new ArgumentOutOfRangeException(nameof(e));
            }
        }

        /// <summary>Inverse of <see cref="SpecName"/>; ordinal, case-sensitive.</summary>
        public static bool TryParseSpecName(string name, out Entitlement entitlement)
        {
            foreach (var e in s_all)
            {
                if (string.Equals(SpecName(e), name, StringComparison.Ordinal))
                {
                    entitlement = e;
                    return true;
                }
            }
            entitlement = default;
            return false;
        }
    }
}
