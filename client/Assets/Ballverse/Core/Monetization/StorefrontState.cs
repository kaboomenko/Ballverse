using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>The Shop tab content computed by <see cref="Storefront.Build"/> (spec §5.2). Immutable.</summary>
    public sealed class StorefrontState
    {
        readonly StorefrontOffer[] _offers;
        readonly StorefrontOffer[] _visible;

        /// <summary>All 15 products in display order (Ultimate, All Modes, No Ads, Remove Export, Remove Interstitial, M06…M15), hidden ones included.</summary>
        public IReadOnlyList<StorefrontOffer> Offers => _offers;
        /// <summary>Visible offers in display order.</summary>
        public IReadOnlyList<StorefrontOffer> VisibleOffers => _visible;
        /// <summary>The user's current entitlements.</summary>
        public EntitlementSet Entitlements { get; }
        /// <summary>Every entitlement is owned: show "Всё куплено ✓" and no offers (spec §5.2 last row).</summary>
        public bool AllPurchased { get; }

        internal StorefrontState(StorefrontOffer[] offers, EntitlementSet entitlements)
        {
            _offers = offers;
            var visible = new List<StorefrontOffer>();
            foreach (var o in offers)
            {
                if (o.Visible) visible.Add(o);
            }
            _visible = visible.ToArray();
            Entitlements = entitlements;
            AllPurchased = entitlements.IsSupersetOf(EntitlementSet.AllDefined);
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
