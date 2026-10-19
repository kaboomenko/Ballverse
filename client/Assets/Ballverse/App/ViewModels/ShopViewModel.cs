using System;
using System.Collections.Generic;
using System.Globalization;
using Ballverse.Core.Monetization;
using Ballverse.Services;
using Ballverse.UI.Localization;
using Ballverse.UI.ViewModels;

namespace Ballverse.App
{
    /// <summary>
    /// Tab «Магазин» (spec §4.3, §5.0.3, release 1): the smart storefront of Core <see cref="Storefront"/> over the release
    /// catalog in force (<c>IPurchaseService.Config</c>: only Remove Export Ads, Remove Interstitial Ads and No Ads Bundle are
    /// sellable; the mode products, All Modes and Ultimate stay in the catalog but are never offered), store-localized prices,
    /// the "вместо …" sum computed from store prices, pending / owned states, restore, terms and privacy links. Main thread.
    /// </summary>
    public sealed class ShopViewModel : ViewModelBase, IShopViewModel
    {
        /// <summary>Owner placeholders until the store listing exists (spec §8.8); see the App README.</summary>
        public const string TermsUrl = "https://ballverse.app/terms";
        public const string PrivacyUrl = "https://ballverse.app/privacy";

        readonly AppContext _ctx;
        readonly List<ShopProductData> _products = new List<ShopProductData>();
        readonly List<ShopProductData> _watermarkOffers = new List<ShopProductData>();
        string _purchasing;
        bool _allPurchased;

        public ShopViewModel(AppContext ctx)
            : base(ctx.Notices.Report)
        {
            _ctx = ctx;
            IPurchaseService p = ctx.Services.Purchases;
            p.EntitlementsChanged += _ => Rebuild();
            p.ProductsChanged += Rebuild;
            p.StateChanged += _ => Rebuild();
            p.ConfigChanged += _ => Rebuild();
            Rebuild();
        }

        public IReadOnlyList<ShopProductData> Products => _products;
        public IReadOnlyList<ShopProductData> WatermarkOffers => _watermarkOffers;
        public bool StoreAvailable => _ctx.Services.Purchases.State == PurchaseServiceState.Ready;
        public bool AllPurchased => _allPurchased;
        public bool IsRestoring { get; private set; }

        public event Action<LocalizedText> Notice;

        public void Purchase(string productId)
        {
            if (_purchasing != null || productId == null) return;
            _purchasing = productId;
            if (_ctx.Ads != null) _ctx.Ads.InPurchaseFlow = true;
            Rebuild();
            try
            {
                _ctx.Services.Purchases.Purchase(productId, "shop", OnPurchased);
            }
            catch (Exception e)
            {
                _purchasing = null;
                if (_ctx.Ads != null) _ctx.Ads.InPurchaseFlow = false;
                _ctx.Notices.Report("shop.purchase", e);
                Raise(_ctx.Notices.TextOf(AppStrings.PurchaseFailed));
                Rebuild();
            }
        }

        void OnPurchased(PurchaseOutcome outcome)
        {
            _purchasing = null;
            if (_ctx.Ads != null) _ctx.Ads.InPurchaseFlow = false;
            switch (outcome.Kind)
            {
                case PurchaseResultKind.Purchased:
                case PurchaseResultKind.AlreadyOwned:
                    Raise(_ctx.Notices.TextOf(AppStrings.PurchaseSuccess));
                    break;
                case PurchaseResultKind.Pending:
                    Raise(_ctx.Notices.TextOf(AppStrings.PurchasePending));
                    break;
                case PurchaseResultKind.Cancelled:
                    break;
                case PurchaseResultKind.NotAvailable:
                    Raise(_ctx.Notices.TextOf(AppStrings.StoreUnavailable));
                    break;
                default:
                    _ctx.Notices.Report("shop.purchase", new InvalidOperationException(outcome.ToString()));
                    Raise(_ctx.Notices.TextOf(AppStrings.PurchaseFailed));
                    break;
            }
            Rebuild();
        }

        public void RestorePurchases()
        {
            if (IsRestoring) return;
            IsRestoring = true;
            Notify();
            try
            {
                _ctx.Services.Purchases.Restore(outcome =>
                {
                    IsRestoring = false;
                    if (outcome.Success) Raise(_ctx.Notices.TextOf(AppStrings.RestoreDone));
                    else
                    {
                        _ctx.Notices.Report("shop.restore", new InvalidOperationException(outcome.ErrorCode ?? "restore failed"));
                        Raise(_ctx.Notices.TextOf(AppStrings.RestoreFailed));
                    }
                    Rebuild();
                });
            }
            catch (Exception e)
            {
                IsRestoring = false;
                _ctx.Notices.Report("shop.restore", e);
                Raise(_ctx.Notices.TextOf(AppStrings.RestoreFailed));
                Notify();
            }
        }

        public void OpenTerms() => OpenUrl(TermsUrl);

        public void OpenPrivacyPolicy() => OpenUrl(PrivacyUrl);

        void OpenUrl(string url)
        {
            try
            {
                _ctx.Platform.OpenUrl(url);
            }
            catch (Exception e)
            {
                _ctx.Notices.Report("shop.open_url", e);
            }
        }

        public void SetScreenActive(bool active)
        {
            if (!active) return;
            try
            {
                if (_ctx.Services.Purchases.State == PurchaseServiceState.Ready) _ctx.Services.Purchases.RefreshPurchases();
            }
            catch (Exception e)
            {
                _ctx.Notices.Report("shop.refresh", e);
            }
            Rebuild();
        }

        void Raise(LocalizedText text)
        {
            Action<LocalizedText> h = Notice;
            if (h != null) h(text);
            else _ctx.Notices.Toast(text);
        }

        void Rebuild()
        {
            _products.Clear();
            _watermarkOffers.Clear();
            IPurchaseService p = _ctx.Services.Purchases;
            try
            {
                StorefrontState state = Storefront.Build(p.OwnedProductIds, p.Config, null);
                _allPurchased = state.AllPurchased;
                var prices = Prices(p);
                foreach (StorefrontOffer offer in state.Offers)
                {
                    // Owned products stay listed as "Куплено ✓"; everything hidden for another reason is not shown.
                    if (!offer.Visible && !(offer.Owned && p.Config.IsSellable(offer.ProductId))) continue;
                    _products.Add(ToData(offer, p, prices));
                }
                foreach (StorefrontOffer offer in Storefront.WatermarkOffers(p.OwnedProductIds, p.Config, null))
                {
                    _watermarkOffers.Add(ToData(offer, p, prices));
                }
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
                _ctx.Notices.Report("shop.storefront", e);
            }
            Notify();
        }

        static Dictionary<string, decimal> Prices(IPurchaseService p)
        {
            var prices = new Dictionary<string, decimal>(StringComparer.Ordinal);
            foreach (StoreProduct product in p.Products)
            {
                if (product.Price > 0) prices[product.Id] = product.Price;
            }
            return prices;
        }

        ShopProductData ToData(StorefrontOffer offer, IPurchaseService p, Dictionary<string, decimal> prices)
        {
            string id = offer.ProductId;
            bool haveProduct = p.TryGetProduct(id, out StoreProduct product);
            ProductState state;
            if (offer.Owned) state = ProductState.Owned;
            else if (_purchasing == id) state = ProductState.Purchasing;
            else if (Contains(p.PendingProductIds, id)) state = ProductState.Pending;
            else if (p.State != PurchaseServiceState.Ready || !haveProduct || !product.AvailableToPurchase) state = ProductState.Unavailable;
            else state = ProductState.Available;

            string compareAt = null;
            int savings = 0;
            if (offer.Product.Kind == ProductKind.Bundle && haveProduct)
            {
                try
                {
                    BundleSavings s = Savings.ForBundle(id, prices);
                    if (s != null && s.Percent > 0)
                    {
                        savings = s.Percent;
                        compareAt = FormatPrice(s.InsteadOf, product);
                    }
                }
                catch (Exception e) when (e is ArgumentException || e is KeyNotFoundException || e is InvalidOperationException)
                {
                    _ctx.Notices.Report("shop.savings", e);
                }
            }
            LocalizedText note = offer.IsLastStep && offer.RemovesWatermark ? LocalizedText.FromKey("shop.last_step_watermark") : default;
            return new ShopProductData(id, TitleKey(id), DescriptionKey(id), Includes(offer), haveProduct ? product.LocalizedPrice : null,
                compareAt, savings, state, offer.PartiallyOwned, offer.Emphasized, note);
        }

        static bool Contains(IReadOnlyList<string> list, string id)
        {
            if (list == null) return false;
            foreach (string s in list)
            {
                if (s == id) return true;
            }
            return false;
        }

        static string Key(string id)
        {
            switch (id)
            {
                case ProductIds.NoAds: return "no_ads";
                case ProductIds.RemoveExportAds: return "remove_export";
                case ProductIds.RemoveInterstitialAds: return "remove_interstitial";
                default: return id.Replace("ballverse.", "").Replace('.', '_');
            }
        }

        static string TitleKey(string id) => "shop.product." + Key(id) + ".title";
        static string DescriptionKey(string id) => "shop.product." + Key(id) + ".description";

        static IReadOnlyList<string> Includes(StorefrontOffer offer)
        {
            EntitlementSet grants = offer.Product.Grants.Union(offer.Gain);
            var keys = new List<string>(3);
            if (grants.Contains(Entitlement.NoExportAds)) keys.Add("shop.includes.no_export_ads");
            if (grants.Contains(Entitlement.NoInterstitials)) keys.Add("shop.includes.no_interstitials");
            if (grants.Contains(Entitlement.NoWatermark) || offer.ProductId == ProductIds.NoAds) keys.Add("shop.includes.no_watermark");
            return keys;
        }

        /// <summary>
        /// Formats the "instead of" sum in the store's currency (spec §4.7: prices only from the store): the culture whose
        /// region uses the product's ISO currency; the store price string is the fallback pattern.
        /// </summary>
        static string FormatPrice(decimal amount, StoreProduct sample)
        {
            string iso = sample.IsoCurrencyCode;
            CultureInfo culture = CultureForCurrency(iso);
            if (culture != null) return amount.ToString("C", culture);
            return amount.ToString("0.00", CultureInfo.InvariantCulture) + (string.IsNullOrEmpty(iso) ? "" : " " + iso);
        }

        static readonly Dictionary<string, CultureInfo> s_currencyCultures = new Dictionary<string, CultureInfo>(StringComparer.OrdinalIgnoreCase);

        static CultureInfo CultureForCurrency(string iso)
        {
            if (string.IsNullOrEmpty(iso)) return null;
            if (s_currencyCultures.TryGetValue(iso, out CultureInfo cached)) return cached;
            CultureInfo found = null;
            CultureInfo current = CultureInfo.CurrentCulture;
            try
            {
                if (!current.IsNeutralCulture && new RegionInfo(current.Name).ISOCurrencySymbol == iso) found = current;
                if (found == null)
                {
                    foreach (CultureInfo c in CultureInfo.GetCultures(CultureTypes.SpecificCultures))
                    {
                        if (new RegionInfo(c.Name).ISOCurrencySymbol == iso)
                        {
                            found = c;
                            break;
                        }
                    }
                }
            }
            catch (ArgumentException)
            {
                found = null;
            }
            s_currencyCultures[iso] = found;
            return found;
        }
    }
}
