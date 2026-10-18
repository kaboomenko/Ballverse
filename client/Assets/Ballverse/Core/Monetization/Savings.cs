using System;
using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>Bundle savings vs the sum of parts (spec §5.1 "Экономия в наборах", owner decision Р6). Pure functions.</summary>
    public static class Savings
    {
        static readonly string[] s_singleModes = BuildSingleModes();
        static readonly string[] s_noAds = { ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds };
        static readonly IReadOnlyList<string> s_allModesParts = Array.AsReadOnly(s_singleModes);
        static readonly IReadOnlyList<string> s_noAdsParts = Array.AsReadOnly(s_noAds);
        static readonly IReadOnlyList<string> s_ultimateParts = Array.AsReadOnly(Concat(s_singleModes, s_noAds));
        static readonly IReadOnlyList<string> s_ultimateCheapestParts =
            Array.AsReadOnly(new[] { ProductIds.AllModes, ProductIds.NoAds });

        /// <summary>
        /// Every separately sold item a bundle replaces: All Modes → 10 modes; No Ads → Remove Export + Remove Interstitial;
        /// Ultimate → 10 modes + both ad removals. Empty for non-bundles and unknown ids.
        /// </summary>
        public static IReadOnlyList<string> SeparatePartsOf(string bundleId)
        {
            switch (bundleId)
            {
                case ProductIds.AllModes: return s_allModesParts;
                case ProductIds.NoAds: return s_noAdsParts;
                case ProductIds.Ultimate: return s_ultimateParts;
                default: return Array.Empty<string>();
            }
        }

        /// <summary>Ultimate's cheapest parts: All Modes + No Ads (spec §5.1: $13.98 "выгоднейшие части").</summary>
        public static IReadOnlyList<string> UltimateCheapestParts => s_ultimateCheapestParts;

        /// <summary>
        /// Saving of <paramref name="bundleId"/> against buying every item separately (e.g. All Modes "вместо $19,90", 75%).
        /// Null when the id is not a bundle, a price is missing or not positive, or the bundle is not cheaper.
        /// </summary>
        /// <param name="prices">productId → price in one currency (the store's localized prices).</param>
        public static BundleSavings ForBundle(string bundleId, IReadOnlyDictionary<string, decimal> prices)
        {
            var parts = SeparatePartsOf(bundleId);
            if (parts.Count == 0) return null;
            return Compute(bundleId, parts, prices);
        }

        /// <summary>Ultimate against its cheapest parts, All Modes + No Ads ($11.99 vs $13.98, 14% at base prices). Null as in <see cref="ForBundle"/>.</summary>
        public static BundleSavings UltimateVsCheapestParts(IReadOnlyDictionary<string, decimal> prices)
        {
            return Compute(ProductIds.Ultimate, s_ultimateCheapestParts, prices);
        }

        /// <summary>
        /// Saving of <paramref name="bundleId"/> against an explicit list of parts (duplicates count each time).
        /// Null when any price is missing or ≤ 0, the part list is empty, or the saving is not positive.
        /// </summary>
        public static BundleSavings Compute(string bundleId, IReadOnlyList<string> partIds, IReadOnlyDictionary<string, decimal> prices)
        {
            if (bundleId == null || partIds == null || prices == null || partIds.Count == 0) return null;
            if (!TryPrice(prices, bundleId, out decimal bundlePrice)) return null;
            decimal total = 0m;
            var ids = new string[partIds.Count];
            try
            {
                for (int i = 0; i < partIds.Count; i++)
                {
                    if (!TryPrice(prices, partIds[i], out decimal price)) return null;
                    total += price;
                    ids[i] = partIds[i];
                }
                decimal amount = total - bundlePrice;
                if (amount <= 0m) return null;
                int percent = (int)Math.Round(amount / total * 100m, 0, MidpointRounding.AwayFromZero);
                // 0 < amount < total: never claim "0%" for a real saving nor "100%" for a paid bundle.
                percent = Math.Min(99, Math.Max(1, percent));
                return new BundleSavings(bundleId, ids, bundlePrice, total, amount, percent);
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        static bool TryPrice(IReadOnlyDictionary<string, decimal> prices, string id, out decimal price)
        {
            if (id == null || !prices.TryGetValue(id, out price))
            {
                price = 0m;
                return false;
            }
            return price > 0m;
        }

        static string[] BuildSingleModes()
        {
            var list = new List<string>();
            foreach (var p in ProductCatalog.All)
            {
                if (p.Kind == ProductKind.SingleMode) list.Add(p.Id);
            }
            return list.ToArray();
        }

        static string[] Concat(string[] a, string[] b)
        {
            var r = new string[a.Length + b.Length];
            a.CopyTo(r, 0);
            b.CopyTo(r, a.Length);
            return r;
        }
    }
}
