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
        /// <summary>
        /// The mode does not ship in this release (<see cref="MonetizationConfig.ReleasedModes"/>; release 1: everything but
        /// M01, M02, M05). Not playable and not sold, whatever is free, grandfathered or owned; the gallery shows no card.
        /// </summary>
        NotReleased = 4,
    }

    /// <summary>
    /// Mode availability (spec §5.1): released in this build (owner decision, release 1) AND (free by config, OR
    /// grandfathered, OR entitled).
    /// The grandfathered set is persisted by the caller (local flag <c>grandfathered_modes</c>); these functions are pure.
    /// </summary>
    public static class ModeAvailability
    {
        /// <summary>
        /// Access of <paramref name="mode"/>, checked in order NotReleased, Free, Grandfathered, Entitled.
        /// </summary>
        /// <param name="grandfathered">Persisted grandfathered modes; null means none.</param>
        /// <param name="config">Release catalog (required; e.g. <see cref="MonetizationConfig.Release1"/>).</param>
        public static ModeAccess GetAccess(ModeCode mode, EntitlementSet entitlements,
            IEnumerable<ModeCode> grandfathered, MonetizationConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (!config.IsReleased(mode)) return ModeAccess.NotReleased;
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
            IEnumerable<ModeCode> grandfathered, MonetizationConfig config)
        {
            var access = GetAccess(mode, entitlements, grandfathered, config);
            return access != ModeAccess.Locked && access != ModeAccess.NotReleased;
        }

        /// <summary>
        /// Call when the user uses (starts) <paramref name="mode"/>. Returns the updated grandfathered set to persist:
        /// the input plus <paramref name="mode"/> if it is currently free by config and released (an unreleased mode cannot
        /// be used, release 1: M03/M04), otherwise the input unchanged.
        /// Result is sorted ascending and distinct; the input is never mutated.
        /// </summary>
        public static IReadOnlyList<ModeCode> RecordUse(IEnumerable<ModeCode> grandfathered, ModeCode mode,
            MonetizationConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var set = new SortedSet<ModeCode>();
            if (grandfathered != null)
            {
                foreach (var g in grandfathered) set.Add(g);
            }
            if (config.IsFree(mode) && config.IsReleased(mode)) set.Add(mode);
            var result = new ModeCode[set.Count];
            set.CopyTo(result);
            return result;
        }

        /// <summary>Every catalog mode currently available (released and open), in code order.</summary>
        public static IReadOnlyList<ModeCode> AvailableModes(EntitlementSet entitlements,
            IEnumerable<ModeCode> grandfathered, MonetizationConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
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
