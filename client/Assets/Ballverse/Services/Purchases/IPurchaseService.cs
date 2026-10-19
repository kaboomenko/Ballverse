using System;
using System.Collections.Generic;
using Ballverse.Core.Monetization;

namespace Ballverse.Services
{
    /// <summary>
    /// In-app purchases (spec §5.1, §5.4, §5.5) with the release catalog of Core <see cref="MonetizationConfig"/> (release
    /// builds: <see cref="MonetizationConfig.Release1"/>, which sells only the three ad products). Rights are known
    /// from frame 0 through the signed cache and recomputed from the store on every start, resume and restore; pending
    /// purchases grant nothing until paid; refunds remove rights. Main thread only.
    /// </summary>
    public interface IPurchaseService
    {
        PurchaseServiceState State { get; }

        /// <summary>Release-1 products with store-localized prices, in shop order (empty until fetched).</summary>
        IReadOnlyList<StoreProduct> Products { get; }

        bool TryGetProduct(string productId, out StoreProduct product);

        /// <summary>Owned product ids (sorted, ordinal).</summary>
        IReadOnlyList<string> OwnedProductIds { get; }

        /// <summary>Products waiting for payment or approval (spec §5.4: show "ожидает подтверждения").</summary>
        IReadOnlyList<string> PendingProductIds { get; }

        /// <summary>
        /// Rights of the owned products: Core <c>Entitlements.From(OwnedProductIds, Config)</c>. NO_WATERMARK follows Core's
        /// permanent rule (Ultimate, or NO_EXPORT_ADS + NO_INTERSTITIALS from any products: No Ads Bundle or both ad removals);
        /// the export sheet row comes from Core <c>WatermarkRowPolicy.GetRow(Entitlements, watermark_enabled)</c>.
        /// </summary>
        EntitlementSet Entitlements { get; }

        /// <summary>
        /// The release catalog in force: Core <see cref="MonetizationConfig.Release1"/> with Remote Config <c>free_modes</c> /
        /// <c>modes_disabled</c> applied. Pass it to Core <c>Storefront</c>, <c>ModeAvailability</c> and <c>SalePointPolicy</c>.
        /// </summary>
        MonetizationConfig Config { get; }

        bool IsPurchaseInProgress { get; }

        /// <summary>Last successful store read, unix ms (from the cache until the store answers).</summary>
        long? LastStoreReadUnixMs { get; }

        event Action<EntitlementSet> EntitlementsChanged;

        event Action ProductsChanged;

        event Action<PurchaseServiceState> StateChanged;

        /// <summary><see cref="Config"/> was replaced (Remote Config changed); storefront and mode cards should be rebuilt.</summary>
        event Action<MonetizationConfig> ConfigChanged;

        /// <summary>Connects to the store in the background (spec §7.9 frame 1+). Returns immediately.</summary>
        void Initialize();

        /// <summary>
        /// Buys a release-1 product. <paramref name="source"/> is the analytics sale point (e.g. <c>shop</c>,
        /// <c>rewarded_gate</c>). <paramref name="done"/> is called once on the main thread.
        /// </summary>
        void Purchase(string productId, string source, Action<PurchaseOutcome> done);

        /// <summary>«Восстановить покупки» (Apple 3.1.1): store restore, then a fresh read of the purchases.</summary>
        void Restore(Action<RestoreOutcome> done);

        /// <summary>Re-reads purchases (on return to the app: pending purchases and refunds, spec §5.4).</summary>
        void RefreshPurchases();
    }
}
