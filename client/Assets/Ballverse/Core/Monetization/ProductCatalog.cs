using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Ballverse.Core.Monetization
{
    /// <summary>The 1.0 product catalog with base prices and grants (spec §5.1, owner decisions Р5, Р13).</summary>
    public static class ProductCatalog
    {
        public const int SingleModePriceCents = 199;
        public const int AllModesPriceCents = 499;
        public const int RemoveExportAdsPriceCents = 699;
        public const int RemoveInterstitialAdsPriceCents = 599;
        public const int NoAdsPriceCents = 899;
        public const int UltimatePriceCents = 1199;

        static readonly ProductInfo[] s_all = Build();
        static readonly Dictionary<string, ProductInfo> s_byId = BuildIndex();
        static readonly IReadOnlyList<ProductInfo> s_allView = Array.AsReadOnly(s_all);
        static readonly IReadOnlyDictionary<string, decimal> s_basePricesUsd =
            new ReadOnlyDictionary<string, decimal>(BuildPrices());

        /// <summary>All 15 products in spec §5.1 table order (same order as <see cref="ProductIds.All"/>).</summary>
        public static IReadOnlyList<ProductInfo> All => s_allView;

        public static ProductInfo Ultimate => s_byId[ProductIds.Ultimate];
        public static ProductInfo AllModes => s_byId[ProductIds.AllModes];
        public static ProductInfo NoAds => s_byId[ProductIds.NoAds];
        public static ProductInfo RemoveExportAds => s_byId[ProductIds.RemoveExportAds];
        public static ProductInfo RemoveInterstitialAds => s_byId[ProductIds.RemoveInterstitialAds];

        /// <summary>Base USD prices as productId → dollars; a fallback price map for <see cref="Savings"/>.</summary>
        public static IReadOnlyDictionary<string, decimal> BasePricesUsd => s_basePricesUsd;

        /// <summary>Ordinal, case-sensitive lookup; false for null and unknown ids.</summary>
        public static bool TryGet(string productId, out ProductInfo product)
        {
            if (productId == null)
            {
                product = null;
                return false;
            }
            return s_byId.TryGetValue(productId, out product);
        }

        public static ProductInfo Get(string productId)
        {
            if (!TryGet(productId, out var p)) throw new ArgumentException("Unknown product id: " + productId, nameof(productId));
            return p;
        }

        /// <summary>The single-mode product unlocking <paramref name="mode"/>; null for modes that are not sold (M01–M05).</summary>
        public static ProductInfo ForMode(ModeCode mode)
        {
            foreach (var p in s_all)
            {
                if (p.Kind == ProductKind.SingleMode && p.Mode == mode) return p;
            }
            return null;
        }

        static ProductInfo[] Build()
        {
            string[] modeIds =
            {
                ProductIds.ModeBallBattle, ProductIds.ModeLastStanding, ProductIds.ModeColorWar, ProductIds.ModeMultiply,
                ProductIds.ModeMazeEscape, ProductIds.ModeSmash, ProductIds.ModeChainReaction, ProductIds.ModePendulumWave,
                ProductIds.ModeOrbit, ProductIds.ModeLiquidSand,
            };
            var list = new List<ProductInfo>();
            for (int i = 0; i < modeIds.Length; i++)
            {
                var mode = (ModeCode)((int)ModeCode.M06 + i);
                EntitlementInfo.TryGetModeEntitlement(mode, out var e);
                list.Add(new ProductInfo(modeIds[i], ProductKind.SingleMode, SingleModePriceCents, EntitlementSet.Of(e), mode));
            }
            list.Add(new ProductInfo(ProductIds.AllModes, ProductKind.Bundle, AllModesPriceCents, EntitlementSet.PaidModes10, null));
            list.Add(new ProductInfo(ProductIds.RemoveExportAds, ProductKind.AdRemoval, RemoveExportAdsPriceCents,
                EntitlementSet.Of(Entitlement.NoExportAds), null));
            list.Add(new ProductInfo(ProductIds.RemoveInterstitialAds, ProductKind.AdRemoval, RemoveInterstitialAdsPriceCents,
                EntitlementSet.Of(Entitlement.NoInterstitials), null));
            list.Add(new ProductInfo(ProductIds.NoAds, ProductKind.Bundle, NoAdsPriceCents, EntitlementSet.AdFree, null));
            list.Add(new ProductInfo(ProductIds.Ultimate, ProductKind.Bundle, UltimatePriceCents,
                EntitlementSet.PaidModes10.Union(EntitlementSet.AdFree).With(Entitlement.NoWatermark), null));
            return list.ToArray();
        }

        static Dictionary<string, ProductInfo> BuildIndex()
        {
            var d = new Dictionary<string, ProductInfo>(StringComparer.Ordinal);
            foreach (var p in s_all) d.Add(p.Id, p);
            return d;
        }

        static Dictionary<string, decimal> BuildPrices()
        {
            var d = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (var p in s_all) d.Add(p.Id, p.BasePriceUsd);
            return d;
        }
    }
}
