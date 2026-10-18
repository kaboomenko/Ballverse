using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Ballverse.Core.Monetization
{
    /// <summary>
    /// Remote-configurable monetization knobs used by the pure entitlement logic (spec §5.1) plus the release catalog: which
    /// modes ship in this release and which products the store sells (owner decisions for release 1, 00_brief).
    /// Immutable; build a new instance when Remote Config changes (see <see cref="WithFreeModes"/>).
    /// <para>
    /// Parsing of the Remote Config <c>free_modes</c> payload belongs to the config module (spec §9.3); the App layer passes
    /// its resolved <see cref="ModeCode"/> list, so one payload never yields two different free sets.
    /// </para>
    /// <para>
    /// Presets: <see cref="Release1"/> (what release 1 ships: M01, M02, M05 free; only the three ad products are sold) and
    /// <see cref="FullCatalog"/> (the spec §5.1 catalog: all 15 modes, all 15 products). The entitlement architecture
    /// (<see cref="ProductCatalog"/>, <see cref="Entitlements"/>) always covers the full catalog, so restored or future
    /// products keep their rights whatever the preset; the preset only limits what is shown and sold.
    /// </para>
    /// </summary>
    public sealed class MonetizationConfig
    {
        static readonly ModeCode[] s_defaultFree = BuildDefaultFree();
        static readonly ModeCode[] s_allModes = BuildAllModes();
        static readonly ModeCode[] s_release1Modes = { ModeCode.M01, ModeCode.M02, ModeCode.M05 };
        static readonly string[] s_release1Sellable =
        {
            ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds, ProductIds.NoAds,
        };

        /// <summary>
        /// The full spec §5.1 catalog: free modes M01–M05, every mode released, all 15 products sellable,
        /// <see cref="WatermarkByParts"/> on, <see cref="WatermarkWithNoAds"/> off. Also what a null config means everywhere
        /// in this module (kept for compatibility); release builds pass <see cref="Release1"/> explicitly.
        /// </summary>
        public static readonly MonetizationConfig Default =
            new MonetizationConfig(s_defaultFree, true, ProductIds.All, s_allModes, false);

        /// <summary>The full spec §5.1 catalog preset; the same instance as <see cref="Default"/>.</summary>
        public static MonetizationConfig FullCatalog => Default;

        /// <summary>
        /// Release 1 (owner decisions, 00_brief): only M01 Ring Escape, M02 Geometry Bounce and M05 Helix Drop ship, all
        /// free; the store sells only Remove Export Ads ($6.99), Remove Interstitial Ads ($5.99) and No Ads Bundle ($8.99);
        /// NO_WATERMARK comes with No Ads or both ad removals (<see cref="WatermarkWithNoAds"/> on). The free set stays
        /// M01–M05 (spec §5.1 "только расширение"), but M03/M04 are not released, so they are not playable.
        /// </summary>
        public static readonly MonetizationConfig Release1 =
            new MonetizationConfig(s_defaultFree, true, s_release1Sellable, s_release1Modes, true);

        readonly ModeCode[] _freeModes;
        readonly ReadOnlyCollection<ModeCode> _freeModesView;
        readonly ModeCode[] _releasedModes;
        readonly ReadOnlyCollection<ModeCode> _releasedModesView;
        readonly string[] _sellable;
        readonly ReadOnlyCollection<string> _sellableView;
        // Lookup only, never iterated: iteration order cannot leak.
        readonly HashSet<string> _sellableSet;
        readonly EntitlementSet _sellableGrants;

        /// <summary>
        /// Effective free set (spec §5.1 "M01–M05 доступны всегда", §9.3 <c>free_modes</c> "только расширение"): the
        /// default free modes (<see cref="ModeInfo.FreeByDefault"/>, M01–M05) united with the configured ones. Sorted,
        /// distinct, read-only. Config can only widen the set, so an empty or broken payload never locks M01–M05;
        /// a paid mode removed from a widened set stays open for users who used it (see <see cref="ModeAvailability"/>).
        /// A free mode is playable only if it is also in <see cref="ReleasedModes"/>.
        /// </summary>
        public IReadOnlyList<ModeCode> FreeModes => _freeModesView;

        /// <summary>
        /// Modes available in this release (owner decision, release 1: M01, M02, M05). Sorted, distinct, read-only. A mode
        /// outside this set is not playable for anyone (<see cref="ModeAccess.NotReleased"/>), whatever is free, grandfathered
        /// or owned, and no sellable product may grant it.
        /// </summary>
        public IReadOnlyList<ModeCode> ReleasedModes => _releasedModesView;

        /// <summary>
        /// Products the store sells in this release, in spec §5.1 table order (<see cref="ProductIds.All"/> order),
        /// read-only. The storefront, mode sheet, watermark offers and sale points never offer anything else
        /// (<see cref="OfferHiddenReason.NotSellable"/>); owned unsellable products still grant their rights.
        /// </summary>
        public IReadOnlyList<string> SellableProductIds => _sellableView;

        /// <summary>
        /// Spec §5.1 "NO_WATERMARK по частям": owning the full Ultimate contents from any products also grants NO_WATERMARK.
        /// On in both presets; the owner may revoke the rule, then only Ultimate (or <see cref="WatermarkWithNoAds"/>) removes
        /// the watermark and the storefront keeps offering Ultimate for the missing NO_WATERMARK.
        /// </summary>
        public bool WatermarkByParts { get; }

        /// <summary>
        /// Release-1 rule (owner decision): owning NO_EXPORT_ADS + NO_INTERSTITIALS (No Ads Bundle, or both ad removals)
        /// also grants NO_WATERMARK. On in <see cref="Release1"/>, off in <see cref="FullCatalog"/>. Turning it off never
        /// revokes the right from users who obtained it under the rule: persist <see cref="WatermarkGrandfathering.Update(bool, IEnumerable{string}, MonetizationConfig)"/>
        /// and pass the flag to <see cref="Entitlements.From"/>.
        /// </summary>
        public bool WatermarkWithNoAds { get; }

        /// <param name="freeModes">Configured <c>free_modes</c> (already resolved by the config module); united with M01–M05.</param>
        /// <param name="watermarkByParts">See <see cref="WatermarkByParts"/>.</param>
        /// <remarks>Creates a full-catalog config (every mode released, every product sellable, <see cref="WatermarkWithNoAds"/> off).</remarks>
        /// <exception cref="ArgumentNullException"><paramref name="freeModes"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A mode outside M01…M15.</exception>
        public MonetizationConfig(IEnumerable<ModeCode> freeModes, bool watermarkByParts = true)
            : this(freeModes, watermarkByParts, ProductIds.All, s_allModes ?? BuildAllModes(), false)
        {
        }

        /// <param name="freeModes">Configured <c>free_modes</c>; united with M01–M05.</param>
        /// <param name="watermarkByParts">See <see cref="WatermarkByParts"/>.</param>
        /// <param name="sellableProductIds">See <see cref="SellableProductIds"/>; duplicates are collapsed.</param>
        /// <param name="releasedModes">See <see cref="ReleasedModes"/>; duplicates are collapsed.</param>
        /// <param name="watermarkWithNoAds">See <see cref="WatermarkWithNoAds"/>.</param>
        /// <exception cref="ArgumentNullException">A list is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A mode outside M01…M15.</exception>
        /// <exception cref="ArgumentException">
        /// A null or unknown product id, or a sellable product that grants a mode outside <paramref name="releasedModes"/>
        /// (the store must not sell products that promise unavailable modes).
        /// </exception>
        public MonetizationConfig(IEnumerable<ModeCode> freeModes, bool watermarkByParts, IEnumerable<string> sellableProductIds,
            IEnumerable<ModeCode> releasedModes, bool watermarkWithNoAds)
        {
            if (freeModes == null) throw new ArgumentNullException(nameof(freeModes));
            if (sellableProductIds == null) throw new ArgumentNullException(nameof(sellableProductIds));
            if (releasedModes == null) throw new ArgumentNullException(nameof(releasedModes));

            var free = new SortedSet<ModeCode>(s_defaultFree ?? BuildDefaultFree());
            foreach (var m in freeModes) free.Add(CheckMode(m, nameof(freeModes)));
            _freeModes = new ModeCode[free.Count];
            free.CopyTo(_freeModes);
            _freeModesView = Array.AsReadOnly(_freeModes);

            var released = new SortedSet<ModeCode>();
            foreach (var m in releasedModes) released.Add(CheckMode(m, nameof(releasedModes)));
            _releasedModes = new ModeCode[released.Count];
            released.CopyTo(_releasedModes);
            _releasedModesView = Array.AsReadOnly(_releasedModes);

            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in sellableProductIds)
            {
                if (!ProductCatalog.TryGet(id, out var p))
                    throw new ArgumentException("Unknown product id: " + (id ?? "null"), nameof(sellableProductIds));
                foreach (var e in p.Grants.ToArray())
                {
                    if (EntitlementInfo.TryGetMode(e, out var mode) && !released.Contains(mode))
                        throw new ArgumentException("Product " + id + " grants unreleased mode " + mode, nameof(sellableProductIds));
                }
                wanted.Add(id);
            }
            // Catalog order, so HashSet iteration order never leaks.
            var sellable = new List<string>();
            foreach (var id in ProductIds.All)
            {
                if (wanted.Contains(id)) sellable.Add(id);
            }
            _sellable = sellable.ToArray();
            _sellableView = Array.AsReadOnly(_sellable);
            _sellableSet = new HashSet<string>(_sellable, StringComparer.Ordinal);
            var grants = EntitlementSet.Empty;
            foreach (var id in _sellable) grants = grants.Union(ProductCatalog.Get(id).Grants);
            _sellableGrants = grants;

            WatermarkByParts = watermarkByParts;
            WatermarkWithNoAds = watermarkWithNoAds;
        }

        /// <summary>
        /// The same release catalog and rules with another Remote Config <c>free_modes</c> payload, e.g.
        /// <c>MonetizationConfig.Release1.WithFreeModes(resolved)</c>. Still united with M01–M05.
        /// </summary>
        public MonetizationConfig WithFreeModes(IEnumerable<ModeCode> freeModes)
        {
            return new MonetizationConfig(freeModes, WatermarkByParts, _sellable, _releasedModes, WatermarkWithNoAds);
        }

        /// <summary>The same config with <see cref="WatermarkWithNoAds"/> set to <paramref name="value"/>.</summary>
        public MonetizationConfig WithWatermarkWithNoAds(bool value)
        {
            return new MonetizationConfig(_freeModes, WatermarkByParts, _sellable, _releasedModes, value);
        }

        /// <summary>The same config with <see cref="WatermarkByParts"/> set to <paramref name="value"/>.</summary>
        public MonetizationConfig WithWatermarkByParts(bool value)
        {
            return new MonetizationConfig(_freeModes, value, _sellable, _releasedModes, WatermarkWithNoAds);
        }

        /// <summary>Whether <paramref name="mode"/> is in the effective free set (spec §5.1). Says nothing about release.</summary>
        public bool IsFree(ModeCode mode)
        {
            return Array.BinarySearch(_freeModes, mode) >= 0;
        }

        /// <summary>Whether <paramref name="mode"/> ships in this release (<see cref="ReleasedModes"/>).</summary>
        public bool IsReleased(ModeCode mode)
        {
            return Array.BinarySearch(_releasedModes, mode) >= 0;
        }

        /// <summary>Whether the store sells <paramref name="productId"/> in this release; false for null and unknown ids.</summary>
        public bool IsSellable(string productId)
        {
            return productId != null && _sellableSet.Contains(productId);
        }

        /// <summary>
        /// Whether some sellable product grants the entitlement of <paramref name="mode"/>; false for modes without one.
        /// </summary>
        public bool IsModeSold(ModeCode mode)
        {
            if (!EntitlementInfo.TryGetModeEntitlement(mode, out var e)) return false;
            foreach (var id in _sellable)
            {
                if (ProductCatalog.Get(id).Grants.Contains(e)) return true;
            }
            return false;
        }

        /// <summary>Union of the direct grants of every sellable product (derived rules not applied).</summary>
        public EntitlementSet SellableGrants => _sellableGrants;

        static ModeCode CheckMode(ModeCode m, string paramName)
        {
            if ((int)m < (int)ModeCode.M01 || (int)m > (int)ModeCode.M15)
                throw new ArgumentOutOfRangeException(paramName, "Unknown mode " + (int)m);
            return m;
        }

        static ModeCode[] BuildDefaultFree()
        {
            var list = new List<ModeCode>();
            foreach (var m in ModeCatalog.All)
            {
                if (m.FreeByDefault) list.Add(m.Code);
            }
            return list.ToArray();
        }

        static ModeCode[] BuildAllModes()
        {
            var list = new List<ModeCode>();
            foreach (var m in ModeCatalog.All) list.Add(m.Code);
            return list.ToArray();
        }
    }
}
