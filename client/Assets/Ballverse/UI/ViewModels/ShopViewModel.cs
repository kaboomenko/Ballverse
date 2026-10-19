using System;
using System.Collections.Generic;
using Ballverse.UI.Localization;

namespace Ballverse.UI.ViewModels
{
    public enum ProductState
    {
        Available = 0,
        Owned = 1,
        /// <summary>Payment pending (spec §5.4): no entitlement yet.</summary>
        Pending = 2,
        /// <summary>Purchase sheet open.</summary>
        Purchasing = 3,
        /// <summary>Price not loaded / store unavailable.</summary>
        Unavailable = 4,
    }

    /// <summary>
    /// One store card (release 1: Remove Export Ads, Remove Interstitial Ads, No Ads Bundle). Prices come from the store
    /// already localized (spec §4.7); the app computes the "instead of" sum of separate purchases from store prices.
    /// </summary>
    public sealed class ShopProductData
    {
        public string ProductId { get; }
        public string TitleKey { get; }
        public string DescriptionKey { get; }
        /// <summary>"What's included" lines.</summary>
        public IReadOnlyList<string> IncludesKeys { get; }
        public string PriceText { get; }
        /// <summary>Sum of the separate purchases ("вместо …"), or null.</summary>
        public string CompareAtText { get; }
        /// <summary>
        /// Savings vs the separate purchases, 0 = none. Shown as a plain comparison next to "вместо …", never as a sale
        /// badge (spec §5.1 / §5.0.3, EU Omnibus: no struck prices, no "скидка" / "акция").
        /// </summary>
        public int SavingsPercent { get; }
        public ProductState State { get; }
        public bool PartiallyOwned { get; }
        /// <summary>Larger, accented card.</summary>
        public bool Emphasized { get; }
        /// <summary>Extra line, e.g. "Последний шаг: …"; empty for none.</summary>
        public LocalizedText Note { get; }

        public ShopProductData(string productId, string titleKey, string descriptionKey, IReadOnlyList<string> includesKeys,
            string priceText, string compareAtText, int savingsPercent, ProductState state, bool partiallyOwned, bool emphasized,
            LocalizedText note)
        {
            ProductId = productId;
            TitleKey = titleKey;
            DescriptionKey = descriptionKey;
            IncludesKeys = includesKeys ?? Array.Empty<string>();
            PriceText = priceText;
            CompareAtText = compareAtText;
            SavingsPercent = savingsPercent;
            State = state;
            PartiallyOwned = partiallyOwned;
            Emphasized = emphasized;
            Note = note;
        }
    }

    /// <summary>Tab "Магазин" (spec §4.3, §5.2; release-1 catalog).</summary>
    public interface IShopViewModel : IObservableViewModel
    {
        /// <summary>Visible offers in display order (smart storefront already applied).</summary>
        IReadOnlyList<ShopProductData> Products { get; }
        /// <summary>
        /// Offers that grant NO_WATERMARK, for the sheet opened from the watermark row / upsell line (spec §5.0.3): the
        /// No Ads Bundle when no ad removal is owned, otherwise the one missing ad removal (owning both grants it in
        /// release 1); empty once the watermark can be removed.
        /// </summary>
        IReadOnlyList<ShopProductData> WatermarkOffers { get; }
        bool StoreAvailable { get; }
        bool AllPurchased { get; }
        bool IsRestoring { get; }
        /// <summary>Results of purchases / restore as toasts.</summary>
        event Action<LocalizedText> Notice;
        void Purchase(string productId);
        void RestorePurchases();
        void OpenTerms();
        void OpenPrivacyPolicy();
        /// <summary>The shop became visible (refresh prices).</summary>
        void SetScreenActive(bool active);
    }
}
