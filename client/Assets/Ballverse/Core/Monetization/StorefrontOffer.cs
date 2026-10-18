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
        /// <summary>A cheaper product grants exactly the same new value to the user (see <see cref="StorefrontOffer.CheaperAlternativeId"/>).</summary>
        CheaperEquivalent = 3,
        /// <summary>
        /// Ultimate only: the user owns a mode entitlement and the cheapest other products completing the Ultimate contents
        /// (<see cref="Storefront.CheapestPartsToComplete"/>) cost less, so Ultimate would re-sell owned items
        /// (spec §5.2 "Ultimate (он дороже, чем недостающая часть)"; "Ultimate показывается только если нет режимов").
        /// </summary>
        CheaperViaParts = 4,
        /// <summary>
        /// It would only re-sell paid modes the user can already play (free by config or grandfathered, spec §5.1) and does
        /// not complete NO_WATERMARK: "Покупатель не должен платить за то, что у него уже есть" (spec §5.2).
        /// </summary>
        AlreadyAvailable = 5,
    }

    /// <summary>One product as the smart storefront presents it (spec §5.2). Immutable.</summary>
    public sealed class StorefrontOffer
    {
        public ProductInfo Product { get; }
        public string ProductId => Product.Id;
        public bool Visible => HiddenReason == OfferHiddenReason.None;
        public OfferHiddenReason HiddenReason { get; }
        /// <summary>
        /// When <see cref="HiddenReason"/> is CheaperEquivalent: the cheaper product with the same value, i.e. the same new
        /// entitlements excluding paid modes already available (free by config or grandfathered, spec §5.1, §5.2). Its
        /// <see cref="Gain"/> may differ only by such available modes. Else null.
        /// </summary>
        public string CheaperAlternativeId { get; }
        /// <summary>The product itself is among the owned products.</summary>
        public bool Owned { get; }
        /// <summary>Entitlements the user would newly receive by buying this product (derived rules included).</summary>
        public EntitlementSet Gain { get; }
        /// <summary>
        /// All Modes only: number of its 10 paid modes the user already has (spec §5.2 "У вас уже N режимов"): entitled,
        /// free by config or grandfathered; 0 for others.
        /// </summary>
        public int OwnedModeCount { get; }
        /// <summary>All Modes only: the user has 1…9 of its 10 modes.</summary>
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
        /// <summary>
        /// Single modes only: the mode is playable without its entitlement (free by config or grandfathered, spec §5.1).
        /// Such an offer is visible only as a last step (buying it still grants MODE_* toward NO_WATERMARK by parts);
        /// the UI should mark it "уже доступен".
        /// </summary>
        public bool AlreadyAvailable { get; }

        internal StorefrontOffer(ProductInfo product, OfferHiddenReason hiddenReason, string cheaperAlternativeId, bool owned,
            EntitlementSet gain, int ownedModeCount, bool partiallyOwned, bool isLastStep, bool emphasized, bool alreadyAvailable)
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
            AlreadyAvailable = alreadyAvailable;
        }

        internal StorefrontOffer WithEmphasis(bool emphasized)
        {
            return new StorefrontOffer(Product, HiddenReason, CheaperAlternativeId, Owned, Gain, OwnedModeCount, PartiallyOwned,
                IsLastStep, emphasized, AlreadyAvailable);
        }

        public override string ToString()
        {
            return Product.Id + (Visible ? "" : " [hidden: " + HiddenReason + "]");
        }
    }
}
