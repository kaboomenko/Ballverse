using System;
using System.Collections.Generic;
using Ballverse.Core.Config;
using Ballverse.Core.Monetization;

namespace Ballverse.Services
{
    /// <summary>
    /// The release catalog in force (spec §5.0.4, §9.3): Core <see cref="MonetizationConfig.Release1"/> (or the build's preset)
    /// with the typed Remote Config <c>free_modes</c> and <c>modes_disabled</c> applied. Parsing and validation of those keys
    /// belong to Core Config (<see cref="RemoteConfigValues"/>); Monetization only widens the free set and applies the kill
    /// switch, so one payload never yields two different free sets. Pure.
    /// </summary>
    public static class MonetizationConfigMapper
    {
        /// <summary><c>release.WithFreeModes(values.FreeModes).WithDisabledModes(values.ModesDisabled)</c>.</summary>
        /// <param name="release">The build's preset (required): <see cref="MonetizationConfig.Release1"/> in release 1.</param>
        public static MonetizationConfig From(MonetizationConfig release, RemoteConfigValues values)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            if (values == null) throw new ArgumentNullException(nameof(values));
            return release.WithFreeModes(values.FreeModes).WithDisabledModes(values.ModesDisabled);
        }

        /// <summary>
        /// True when both configs show and sell the same: same free, released and disabled modes and the same sellable
        /// products (rights never depend on the config).
        /// </summary>
        public static bool SameCatalog(MonetizationConfig a, MonetizationConfig b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            return Same(a.FreeModes, b.FreeModes) && Same(a.ReleasedModes, b.ReleasedModes)
                   && Same(a.DisabledModes, b.DisabledModes) && Same(a.SellableProductIds, b.SellableProductIds);
        }

        static bool Same<T>(IReadOnlyList<T> a, IReadOnlyList<T> b)
        {
            if (a.Count != b.Count) return false;
            var comparer = EqualityComparer<T>.Default;
            for (int i = 0; i < a.Count; i++)
            {
                if (!comparer.Equals(a[i], b[i])) return false;
            }
            return true;
        }
    }
}
