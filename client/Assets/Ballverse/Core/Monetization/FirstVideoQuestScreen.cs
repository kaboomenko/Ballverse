using System;
using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>
    /// The offer part of the one-time screen after the first-video quest (spec §4.3 "короткий праздничный экран и
    /// предложение попробовать платный режим (без давления, кнопка «Позже» равнозначна по размеру)", §5.2 "Один экран с
    /// All Modes — Один раз"). Immutable; produced by <see cref="SalePointPolicy.FirstVideoQuest"/>.
    /// <para>
    /// Scope: the §4.3 celebration ("короткий праздничный экран") is outside Core and is shown unconditionally whenever the
    /// quest completes, for paying, restored and free users alike, even right after an interstitial (§5.2 "После
    /// interstitial — Не предлагается" forbids only the purchase offer). This type decides only whether that screen also
    /// carries the purchase offer block (<see cref="ShowOffer"/>).
    /// </para>
    /// <para>
    /// "Без давления": the offer is never <see cref="StorefrontOffer.Emphasized"/>, and the offer block always carries a
    /// «Позже» (later) button of the same size as the buy button (<see cref="LaterButtonSameSizeAsOffer"/>); neither is
    /// visually preferred. «Позже» only closes the offer; the caller persists the one-shot flag either way.
    /// </para>
    /// </summary>
    public sealed class FirstVideoQuestScreen
    {
        static readonly StorefrontOffer[] s_noOffers = new StorefrontOffer[0];

        /// <summary>
        /// No offer block (already shown, right after an interstitial, or All Modes hidden by the smart storefront). The
        /// §4.3 celebration itself is still shown by the UI.
        /// </summary>
        public static readonly FirstVideoQuestScreen None = new FirstVideoQuestScreen(null);

        readonly StorefrontOffer[] _offers;
        readonly IReadOnlyList<StorefrontOffer> _offersView;

        /// <summary>
        /// Whether the post-quest screen carries the purchase offer block (spec §5.2 "Один раз"). Does not govern the §4.3
        /// celebration, which is always shown.
        /// </summary>
        public bool ShowOffer => Offer != null;

        /// <summary>The All Modes offer (never emphasized, spec §4.3); null when <see cref="ShowOffer"/> is false.</summary>
        public StorefrontOffer Offer { get; }

        /// <summary>The offer as a list: empty or exactly <see cref="Offer"/>. Read-only.</summary>
        public IReadOnlyList<StorefrontOffer> Offers => _offersView;

        /// <summary>
        /// Whether the offer block renders a «Позже» dismiss button of the same size and prominence as the buy button
        /// (spec §4.3 "без давления, кнопка «Позже» равнозначна по размеру"): true exactly when <see cref="ShowOffer"/>.
        /// </summary>
        public bool LaterButtonSameSizeAsOffer => ShowOffer;

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
            return ShowOffer ? "FirstVideoQuest: " + Offer.ProductId + " | Later (same size)" : "FirstVideoQuest: no offer";
        }
    }
}
