using System;
using Ballverse.UI.Components;
using Ballverse.UI.Localization;
using Ballverse.UI.Navigation;
using Ballverse.UI.ViewModels;
using UnityEngine.UIElements;

namespace Ballverse.UI.Screens
{
    /// <summary>
    /// Tab "Магазин" (spec §4.3, §5.2; release-1 catalog: Remove Export Ads, Remove Interstitial Ads, No Ads Bundle).
    /// Cards and prices are on solid surfaces (spec §4.5: prices are never on glass); restore purchases; terms and
    /// privacy links. The smart storefront (what is shown / hidden) is decided by the view model.
    /// </summary>
    public sealed class ShopScreen : ScreenController
    {
        IShopViewModel _vm;
        VisualElement _products;

        public override ScreenId Id => ScreenId.Shop;
        protected override string Layout => "Shop";

        protected override void OnCreate()
        {
            _vm = Ctx.ViewModels.Shop;
            _products = Q<VisualElement>("products");
            Q<ScrollView>("scroll").verticalScrollerVisibility = ScrollerVisibility.Hidden;
            Tap.On(Root, "settings", () => Ctx.Navigator.Push(ScreenId.Settings));
            Tap.On(Root, "restore", () => _vm.RestorePurchases());
            Tap.On(Root, "terms", () => _vm.OpenTerms());
            Tap.On(Root, "privacy", () => _vm.OpenPrivacyPolicy());
        }

        protected override void OnShow()
        {
            _vm.Changed += Refresh;
            _vm.Notice += OnNotice;
            _vm.SetScreenActive(true);
            Refresh();
        }

        protected override void OnHide()
        {
            _vm.Changed -= Refresh;
            _vm.Notice -= OnNotice;
            _vm.SetScreenActive(false);
        }

        protected override void OnLanguageChanged()
        {
            Refresh();
        }

        void OnNotice(LocalizedText text)
        {
            Ctx.Toasts.Show(Ctx.L(text));
        }

        void Refresh()
        {
            Q<VisualElement>("unavailable").style.display = _vm.StoreAvailable ? DisplayStyle.None : DisplayStyle.Flex;
            Q<VisualElement>("all-purchased").style.display = _vm.AllPurchased ? DisplayStyle.Flex : DisplayStyle.None;
            var restore = Q<VisualElement>("restore");
            restore.SetEnabled(!_vm.IsRestoring);
            restore.EnableInClassList("bv-btn--busy", _vm.IsRestoring);
            ShopCards.Fill(Ctx, _products, _vm);
        }
    }

    /// <summary>Product cards shared by the shop tab and the offer sheet.</summary>
    public static class ShopCards
    {
        public static void Fill(UiContext ctx, VisualElement container, IShopViewModel vm)
        {
            container.Clear();
            foreach (var p in vm.Products) container.Add(Build(ctx, vm, p));
        }

        public static VisualElement Build(UiContext ctx, IShopViewModel vm, ShopProductData p)
        {
            var card = new VisualElement();
            card.AddToClassList("bv-product");
            card.AddToClassList("bv-surface-solid");
            card.EnableInClassList("bv-product--emphasized", p.Emphasized);
            card.EnableInClassList("bv-product--owned", p.State == ProductState.Owned);

            var head = new VisualElement();
            head.AddToClassList("bv-product__head");
            head.Add(ctx.KeyLabel(p.TitleKey, "bv-product__title"));
            if (p.SavingsPercent > 0)
            {
                var badge = new VisualElement();
                badge.AddToClassList("bv-badge");
                badge.AddToClassList("bv-badge--accent");
                badge.Add(UiContext.TextLabel(ctx.Format("shop.savings", p.SavingsPercent), "bv-badge__label"));
                head.Add(badge);
            }
            card.Add(head);
            if (!string.IsNullOrEmpty(p.DescriptionKey)) card.Add(ctx.KeyLabel(p.DescriptionKey, "bv-product__description"));
            var includes = new VisualElement();
            includes.AddToClassList("bv-product__includes");
            foreach (string key in p.IncludesKeys)
            {
                var line = new VisualElement();
                line.AddToClassList("bv-product__include");
                line.Add(Icons.Create(IconKind.Check, "bv-product__include-icon"));
                line.Add(ctx.KeyLabel(key, "bv-product__include-label"));
                includes.Add(line);
            }
            card.Add(includes);
            if (!p.Note.IsEmpty) card.Add(UiContext.TextLabel(ctx.L(p.Note), "bv-product__note"));
            if (p.PartiallyOwned) card.Add(ctx.KeyLabel("shop.partially_owned", "bv-product__note"));

            var buy = new VisualElement();
            buy.AddToClassList("bv-btn");
            buy.AddToClassList("bv-btn--block");
            buy.AddToClassList("bv-product__buy");
            switch (p.State)
            {
                case ProductState.Owned:
                    buy.AddToClassList("bv-btn--secondary");
                    buy.Add(Icons.Create(IconKind.Check, "bv-btn__icon"));
                    buy.Add(ctx.KeyLabel("shop.owned", "bv-btn__label"));
                    buy.SetEnabled(false);
                    break;
                case ProductState.Pending:
                    buy.AddToClassList("bv-btn--secondary");
                    buy.Add(ctx.KeyLabel("shop.pending", "bv-btn__label"));
                    buy.SetEnabled(false);
                    break;
                case ProductState.Purchasing:
                    buy.AddToClassList("bv-btn--primary");
                    buy.Add(new Spinner());
                    buy.SetEnabled(false);
                    break;
                case ProductState.Unavailable:
                    buy.AddToClassList("bv-btn--secondary");
                    buy.Add(ctx.KeyLabel("shop.price_unavailable", "bv-btn__label"));
                    buy.SetEnabled(false);
                    break;
                default:
                    buy.AddToClassList("bv-btn--primary");
                    buy.Add(UiContext.TextLabel(p.PriceText ?? "", "bv-btn__label"));
                    string id = p.ProductId;
                    Tap.On(buy, () => vm.Purchase(id));
                    break;
            }
            card.Add(buy);
            if (!string.IsNullOrEmpty(p.CompareAtText) && p.State == ProductState.Available)
                card.Add(UiContext.TextLabel(ctx.Format("shop.instead_of", p.CompareAtText), "bv-product__compare"));
            return card;
        }
    }

    /// <summary>
    /// Offer sheet opened from the locked watermark row or the upsell line (spec §3.10, §5.2): the current storefront
    /// offers on solid cards. In release 1 the watermark goes with the No Ads Bundle (or both ad removals).
    /// </summary>
    public static class ShopOfferSheet
    {
        public static BottomSheet Open(UiContext ctx, string titleKey)
        {
            var vm = ctx.ViewModels.Shop;
            if (vm == null) return null;
            VisualElement list = null;
            Action refresh = null;
            Action<LocalizedText> notice = t => ctx.Toasts.Show(ctx.L(t));
            var sheet = new BottomSheet(ctx, new SheetOptions
            {
                TitleKey = titleKey,
                Detents = SheetDetents.HalfAndFull,
                StartExpanded = true,
                Glass = false,
                Closed = () =>
                {
                    vm.Changed -= refresh;
                    vm.Notice -= notice;
                    vm.SetScreenActive(false);
                },
            });
            sheet.Content.Add(ctx.KeyLabel("shop.watermark_offer.body", "bv-note"));
            list = new VisualElement();
            list.AddToClassList("bv-shop__products");
            sheet.Content.Add(list);
            var restore = FormBuilder.CreateButton(ctx, "shop.restore", IconKind.Restore, "plain");
            restore.AddToClassList("bv-btn--block");
            Tap.On(restore, () => vm.RestorePurchases());
            sheet.Content.Add(restore);
            refresh = () =>
            {
                ShopCards.Fill(ctx, list, vm);
                if (vm.AllPurchased) sheet.Close();
            };
            vm.Changed += refresh;
            vm.Notice += notice;
            vm.SetScreenActive(true);
            ShopCards.Fill(ctx, list, vm);
            return sheet.Show();
        }
    }
}
