using System;
using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>Why a mode is open or closed for the user (spec §5.1 "Права и бесплатные режимы").</summary>
    public enum ModeAccess
    {
        Locked = 0,
        /// <summary>In the configured free set (<see cref="MonetizationConfig.FreeModes"/>).</summary>
        Free = 1,
        /// <summary>Used while it was free; stays open forever after the free set narrows (local flag <c>grandfathered_modes</c>).</summary>
        Grandfathered = 2,
        /// <summary>The user owns its MODE_* entitlement.</summary>
        Entitled = 3,
    }

    /// <summary>
    /// Mode availability (spec §5.1): free by config, OR grandfathered, OR entitled.
    /// The grandfathered set is persisted by the caller (local flag <c>grandfathered_modes</c>); these functions are pure.
    /// </summary>
    public static class ModeAvailability
    {
        /// <summary>
        /// Access of <paramref name="mode"/>, checked in order Free, Grandfathered, Entitled.
        /// </summary>
        /// <param name="grandfathered">Persisted grandfathered modes; null means none.</param>
        /// <param name="config">Null means <see cref="MonetizationConfig.Default"/>.</param>
        public static ModeAccess GetAccess(ModeCode mode, EntitlementSet entitlements,
            IEnumerable<ModeCode> grandfathered, MonetizationConfig config = null)
        {
            config = config ?? MonetizationConfig.Default;
            if (config.IsFree(mode)) return ModeAccess.Free;
            if (grandfathered != null)
            {
                foreach (var g in grandfathered)
                {
                    if (g == mode) return ModeAccess.Grandfathered;
                }
            }
            if (entitlements.ContainsMode(mode)) return ModeAccess.Entitled;
            return ModeAccess.Locked;
        }

        public static bool IsAvailable(ModeCode mode, EntitlementSet entitlements,
            IEnumerable<ModeCode> grandfathered, MonetizationConfig config = null)
        {
            return GetAccess(mode, entitlements, grandfathered, config) != ModeAccess.Locked;
        }

        /// <summary>
        /// Call when the user uses (starts) <paramref name="mode"/>. Returns the updated grandfathered set to persist:
        /// the input plus <paramref name="mode"/> if it is currently free by config, otherwise the input unchanged.
        /// Result is sorted ascending and distinct; the input is never mutated.
        /// </summary>
        public static IReadOnlyList<ModeCode> RecordUse(IEnumerable<ModeCode> grandfathered, ModeCode mode,
            MonetizationConfig config = null)
        {
            config = config ?? MonetizationConfig.Default;
            var set = new SortedSet<ModeCode>();
            if (grandfathered != null)
            {
                foreach (var g in grandfathered) set.Add(g);
            }
            if (config.IsFree(mode)) set.Add(mode);
            var result = new ModeCode[set.Count];
            set.CopyTo(result);
            return result;
        }

        /// <summary>Every catalog mode currently available, in code order.</summary>
        public static IReadOnlyList<ModeCode> AvailableModes(EntitlementSet entitlements,
            IEnumerable<ModeCode> grandfathered, MonetizationConfig config = null)
        {
            var g = grandfathered == null ? Array.Empty<ModeCode>() : new List<ModeCode>(grandfathered).ToArray();
            var list = new List<ModeCode>();
            foreach (var m in ModeCatalog.All)
            {
                if (IsAvailable(m.Code, entitlements, g, config)) list.Add(m.Code);
            }
            return list;
        }
    }
}
