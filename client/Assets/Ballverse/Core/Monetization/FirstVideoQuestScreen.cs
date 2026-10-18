using System;
using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>Visual weight of a button on a sale-point screen (spec §4.3, §5.2 "без давления").</summary>
    public enum SalePointButtonWeight
    {
        /// <summary>Same size and prominence as every other button on the screen: no visual priority.</summary>
        Equal = 0,
    }

    /// <summary>
    /// The one-time screen after the first-video quest (spec §4.3 "короткий праздничный экран и предложение попробовать
    /// платный режим (без давления, кнопка «Позже» равнозначна по размеру)", §5.2 "Один экран с All Modes — Один раз").
    /// Immutable; produced by <see cref="SalePointPolicy.FirstVideoQuest"/>.
    /// <para>
    /// "Без давления": the offer is never <see cref="StorefrontOffer.Emphasized"/>, and the screen always carries a «Позже»
    /// (later) button whose <see cref="LaterButtonWeight"/> equals <see cref="OfferButtonWeight"/>: same size, neither
    /// is visually preferred. «Позже» only closes the screen; the caller persists the one-shot flag either way.
    /// </para>
    /// </summary>
    public sealed class FirstVideoQuestScreen
    {
        static readonly StorefrontOffer[] s_noOffers = new StorefrontOffer[0];

        /// <summary>Nothing to show (already shown, right after an interstitial, or All Modes hidden by the smart storefront).</summary>
        public static readonly FirstVideoQuestScreen None = new FirstVideoQuestScreen(null);

        readonly StorefrontOffer[] _offers;
        readonly IReadOnlyList<StorefrontOffer> _offersView;

        /// <summary>Whether the screen is displayed (spec §5.2 "Один раз").</summary>
        public bool Show => Offer != null;

        /// <summary>The All Modes offer (never emphasized, spec §4.3); null when <see cref="Show"/> is false.</summary>
        public StorefrontOffer Offer { get; }

        /// <summary>The offer as a list: empty or exactly <see cref="Offer"/>. Read-only.</summary>
        public IReadOnlyList<StorefrontOffer> Offers => _offersView;

        /// <summary>Whether a «Позже» dismiss button is rendered: always, when the screen is shown (spec §4.3).</summary>
        public bool HasLaterButton => Show;

        /// <summary>Weight of the buy button (spec §4.3): <see cref="SalePointButtonWeight.Equal"/>.</summary>
        public SalePointButtonWeight OfferButtonWeight => SalePointButtonWeight.Equal;

        /// <summary>Weight of the «Позже» button (spec §4.3 "равнозначна по размеру"): equal to <see cref="OfferButtonWeight"/>.</summary>
        public SalePointButtonWeight LaterButtonWeight => SalePointButtonWeight.Equal;

        internal FirstVideoQuestScreen(StorefrontOffer offer)
        {
            if (offer != null && offer.Emphasized)
                throw new ArgumentException("The quest offer must not be emphasized (spec §4.3).", nameof(offer));
            Offer = offer;
            _offers = offer == null ? s_noOffers : new[] { offer };
            _offersView = Array.AsReadOnly(_offers);
        }

        public override string ToString()
        {
            return Show ? "FirstVideoQuest: " + Offer.ProductId + " | Later (equal)" : "FirstVideoQuest: none";
        }
    }
}
