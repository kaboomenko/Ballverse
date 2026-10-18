namespace Ballverse.Core.Monetization
{
    /// <summary>Product family (spec §5.1). Every product is non-consumable.</summary>
    public enum ProductKind
    {
        /// <summary>One paid mode, $1.99.</summary>
        SingleMode = 0,
        /// <summary>One ad-removal right (Remove Export Ads, Remove Interstitial Ads).</summary>
        AdRemoval = 1,
        /// <summary>All Modes, No Ads, Ultimate.</summary>
        Bundle = 2,
    }

    /// <summary>A catalog product: id, base price and the entitlements it grants directly (spec §5.1).</summary>
    public sealed class ProductInfo
    {
        public string Id { get; }
        public ProductKind Kind { get; }
        /// <summary>Base US price in cents (owner decision Р5). Store price tiers give the localized price.</summary>
        public int BasePriceUsdCents { get; }
        /// <summary>Base US price in dollars, e.g. 1.99m.</summary>
        public decimal BasePriceUsd => BasePriceUsdCents / 100m;
        /// <summary>Entitlements granted by owning this product alone (Ultimate includes NO_WATERMARK, Р13).</summary>
        public EntitlementSet Grants { get; }
        /// <summary>The unlocked mode for <see cref="ProductKind.SingleMode"/>; null otherwise.</summary>
        public ModeCode? Mode { get; }

        public ProductInfo(string id, ProductKind kind, int basePriceUsdCents, EntitlementSet grants, ModeCode? mode)
        {
            Id = id;
            Kind = kind;
            BasePriceUsdCents = basePriceUsdCents;
            Grants = grants;
            Mode = mode;
        }

        public override string ToString()
        {
            return Id;
        }
    }
}
