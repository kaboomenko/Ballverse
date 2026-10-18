using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>The Shop tab content computed by <see cref="Storefront.Build"/> (spec §5.2). Immutable.</summary>
    public sealed class StorefrontState
    {
        readonly StorefrontOffer[] _offers;
        readonly IReadOnlyList<StorefrontOffer> _offersView;
        readonly IReadOnlyList<StorefrontOffer> _visible;

        /// <summary>All 15 products in display order (Ultimate, All Modes, No Ads, Remove Export, Remove Interstitial, M06…M15), hidden ones included.</summary>
        public IReadOnlyList<StorefrontOffer> Offers => _offersView;
        /// <summary>Visible offers in display order.</summary>
        public IReadOnlyList<StorefrontOffer> VisibleOffers => _visible;
        /// <summary>The user's current entitlements.</summary>
        public EntitlementSet Entitlements { get; }
        /// <summary>
        /// Nothing more can be bought: the user has every entitlement that owning all sellable products would give, paid
        /// modes they can already play without one (free by config or grandfathered, spec §5.1) counted as had
        /// (spec §5.2 last row "Всё куплено ✓"; full catalog: every entitlement; release 1: NO_EXPORT_ADS, NO_INTERSTITIALS
        /// and NO_WATERMARK). True exactly when no offer is visible.
        /// </summary>
        public bool AllPurchased { get; }

        internal StorefrontState(StorefrontOffer[] offers, EntitlementSet entitlements, bool allPurchased)
        {
            _offers = offers;
            _offersView = System.Array.AsReadOnly(offers);
            var visible = new List<StorefrontOffer>();
            foreach (var o in offers)
            {
                if (o.Visible) visible.Add(o);
            }
            _visible = visible.AsReadOnly();
            Entitlements = entitlements;
            AllPurchased = allPurchased;
        }

        /// <summary>The offer for <paramref name="productId"/>, or null for unknown ids.</summary>
        public StorefrontOffer Get(string productId)
        {
            foreach (var o in _offers)
            {
                if (o.ProductId == productId) return o;
            }
            return null;
        }
    }
}
