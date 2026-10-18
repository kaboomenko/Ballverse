namespace Ballverse.Core.Monetization
{
    /// <summary>Why the smart storefront hides an offer (spec §5.2 "Умная витрина").</summary>
    public enum OfferHiddenReason
    {
        /// <summary>Visible.</summary>
        None = 0,
        /// <summary>The product itself is owned.</summary>
        Owned = 1,
        /// <summary>Owning it would grant nothing new (e.g. single modes after All Modes).</summary>
        NothingNew = 2,
        /// <summary>A cheaper product grants exactly the same missing entitlements (see <see cref="StorefrontOffer.CheaperAlternativeId"/>).</summary>
        CheaperEquivalent = 3,
        /// <summary>
        /// Ultimate only: the user already owns a mode and an ad-removal right, so Ultimate would re-sell owned items
        /// and the remaining parts are cheaper (spec §5.2 rows Remove Export / Remove Interstitial:
        /// "Ultimate показывается только если нет режимов").
        /// </summary>
        CheaperViaParts = 4,
    }

    /// <summary>One product as the smart storefront presents it (spec §5.2). Immutable.</summary>
    public sealed class StorefrontOffer
    {
        public ProductInfo Product { get; }
        public string ProductId => Product.Id;
        public bool Visible => HiddenReason == OfferHiddenReason.None;
        public OfferHiddenReason HiddenReason { get; }
        /// <summary>The cheaper product with the same missing grants when <see cref="HiddenReason"/> is CheaperEquivalent; else null.</summary>
        public string CheaperAlternativeId { get; }
        /// <summary>The product itself is among the owned products.</summary>
        public bool Owned { get; }
        /// <summary>Entitlements the user would newly receive by buying this product (derived rules included).</summary>
        public EntitlementSet Gain { get; }
        /// <summary>All Modes only: number of paid modes the user already owns (spec §5.2 "У вас уже N режимов"); 0 for others.</summary>
        public int OwnedModeCount { get; }
        /// <summary>All Modes only: the user owns 1…9 of its 10 modes.</summary>
        public bool PartiallyOwned { get; }
        /// <summary>
        /// Visible non-Ultimate product that completes the full Ultimate contents, so it also removes the watermark
        /// (spec §5.2 "последний шаг: … и водяной знак уйдут", §5.1 NO_WATERMARK by parts).
        /// </summary>
        public bool IsLastStep { get; }
        /// <summary>Buying it grants NO_WATERMARK; on Ultimate this is the "+ без водяного знака" badge (spec §5.2, §3.10).</summary>
        public bool RemovesWatermark => Gain.Contains(Entitlement.NoWatermark);
        /// <summary>Show larger than its neighbours (mode sheet: "крупнее — Все 10 режимов", spec §5.2).</summary>
        public bool Emphasized { get; }

        internal StorefrontOffer(ProductInfo product, OfferHiddenReason hiddenReason, string cheaperAlternativeId, bool owned,
            EntitlementSet gain, int ownedModeCount, bool partiallyOwned, bool isLastStep, bool emphasized)
        {
            Product = product;
            HiddenReason = hiddenReason;
            CheaperAlternativeId = cheaperAlternativeId;
            Owned = owned;
            Gain = gain;
            OwnedModeCount = ownedModeCount;
            PartiallyOwned = partiallyOwned;
            IsLastStep = isLastStep;
            Emphasized = emphasized;
        }

        internal StorefrontOffer WithEmphasis(bool emphasized)
        {
            return new StorefrontOffer(Product, HiddenReason, CheaperAlternativeId, Owned, Gain, OwnedModeCount, PartiallyOwned,
                IsLastStep, emphasized);
        }

        public override string ToString()
        {
            return Product.Id + (Visible ? "" : " [hidden: " + HiddenReason + "]");
        }
    }
}
