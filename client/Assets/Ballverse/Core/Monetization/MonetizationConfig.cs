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
    /// products keep their rights whatever the preset; the preset only limits what is shown and sold (spec §5.0.4).
    /// </para>
    /// <para>
    /// The NO_WATERMARK rule is not configurable (spec §5.0.2 "Правило постоянное и не зависит от конфигурации или пресета
    /// каталога"): <see cref="Entitlements.From"/> applies the same rule with every config.
    /// </para>
    /// </summary>
    public sealed class MonetizationConfig
    {
        static readonly ModeCode[] s_defaultFree = BuildDefaultFree();
        static readonly ModeCode[] s_allModes = BuildModes(false);
        static readonly ModeCode[] s_buildModes = BuildModes(true);
        static readonly string[] s_release1Sellable =
        {
            ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds, ProductIds.NoAds,
        };

        /// <summary>
        /// The full spec §5.1 catalog: default free modes (<see cref="ModeInfo.FreeByDefault"/>: M01, M02, M05), every catalog
        /// mode (<see cref="ModeCatalog.All"/>) released, all 15 products sellable. Not the shipping build: pass it explicitly
        /// (tests, later releases).
        /// </summary>
        public static readonly MonetizationConfig FullCatalog =
            new MonetizationConfig(s_defaultFree, ProductIds.All, s_allModes);

        /// <summary>
        /// Release 1 (owner decisions Р18, Р20, Р21): the modes of this build are <see cref="ModeCatalog.Released"/> (M01 Ring
        /// Escape, M02 Geometry Bounce, M05 Helix Drop), all free by default (spec §5.0.2, §9.3 <c>free_modes</c> =
        /// <c>["M01","M02","M05"]</c>); the store sells only Remove Export Ads ($6.99), Remove Interstitial Ads ($5.99) and No
        /// Ads Bundle ($8.99) (spec §5.0.1); NO_WATERMARK comes with No Ads or both ad removals (the permanent rule of spec
        /// §5.0.2). M03/M04 become free only through <c>free_modes</c> at their release, never by default in code.
        /// </summary>
        public static readonly MonetizationConfig Release1 =
            new MonetizationConfig(s_defaultFree, s_release1Sellable, s_buildModes);

        /// <summary>
        /// The preset the shipping build uses: <see cref="Release1"/>. Every App-facing function of this module takes the
        /// config as a required argument and rejects null, so a forgotten argument fails to compile instead of silently
        /// selling the full catalog; start from this preset (e.g. <c>MonetizationConfig.Default.WithFreeModes(resolved)</c>).
        /// </summary>
        public static MonetizationConfig Default => Release1;

        readonly ModeCode[] _freeModes;
        readonly ReadOnlyCollection<ModeCode> _freeModesView;
        readonly ModeCode[] _buildModes;
        readonly ModeCode[] _disabledModes;
        readonly ReadOnlyCollection<ModeCode> _disabledModesView;
        readonly ModeCode[] _releasedModes;
        readonly ReadOnlyCollection<ModeCode> _releasedModesView;
        readonly string[] _buildSellable;
        readonly string[] _sellable;
        readonly ReadOnlyCollection<string> _sellableView;
        readonly string[] _unsold;
        readonly ReadOnlyCollection<string> _unsoldView;
        // Lookup only, never iterated: iteration order cannot leak.
        readonly HashSet<string> _sellableSet;
        readonly EntitlementSet _sellableGrants;

        /// <summary>
        /// Effective free set (spec §5.0.2, §5.1 "Бесплатные режимы доступны всегда", §9.3 <c>free_modes</c> "только
        /// расширение"): the default free modes (<see cref="ModeInfo.FreeByDefault"/>; release 1: M01, M02, M05) united with
        /// the configured ones. Sorted, distinct, read-only. Config can only widen the set, so an empty or broken payload
        /// never locks the default free modes; a paid mode removed from a widened set stays open for users who used it (see
        /// <see cref="ModeAvailability"/>). A free mode is playable only if it is also in <see cref="ReleasedModes"/>.
        /// </summary>
        public IReadOnlyList<ModeCode> FreeModes => _freeModesView;

        /// <summary>
        /// Modes playable in this build: the build's modes (<c>releasedModes</c>, i.e. <c>ModeRegistry</c>; release 1:
        /// <see cref="ModeCatalog.Released"/> = M01, M02, M05) minus <see cref="DisabledModes"/>. Sorted, distinct, read-only.
        /// A mode outside this set is not playable for anyone (<see cref="ModeAccess.NotReleased"/>), whatever is free,
        /// grandfathered or owned, and no product granting it is sold (spec §5.0.4 rule (b)). A released mode should be free
        /// or sold; otherwise its card stays <see cref="ModeAccess.Locked"/> with nothing to buy.
        /// </summary>
        public IReadOnlyList<ModeCode> ReleasedModes => _releasedModesView;

        /// <summary>
        /// Remote Config <c>modes_disabled</c> (spec §5.0.4, §7.6 emergency kill switch): hidden like an unreleased mode, and
        /// every product granting it stops being sold. Sorted, distinct, read-only; empty by default.
        /// </summary>
        public IReadOnlyList<ModeCode> DisabledModes => _disabledModesView;

        /// <summary>
        /// Products the store sells (spec §5.0.4): the build's list (<c>sellableProductIds</c>; release 1: the three ad
        /// products of §5.0.1) restricted to products whose modes are all in <see cref="ReleasedModes"/>. Spec §5.1 table
        /// order (<see cref="ProductIds.All"/> order), read-only. The storefront, mode sheet, watermark offers and sale points
        /// never offer anything else (<see cref="OfferHiddenReason.NotSellable"/>); owned unsellable products still grant
        /// their rights.
        /// </summary>
        public IReadOnlyList<string> SellableProductIds => _sellableView;

        /// <summary>
        /// Products of the build's list that are not sold because they grant a mode outside <see cref="ReleasedModes"/>
        /// (unreleased or disabled; spec §5.0.4 rule (b)), e.g. All Modes, Ultimate and the single when one paid mode is
        /// disabled. <see cref="ProductIds.All"/> order, read-only; empty for the shipping presets.
        /// </summary>
        public IReadOnlyList<string> UnsoldProductIds => _unsoldView;

        /// <summary>
        /// Test shorthand: a full-catalog config (every mode released, every product sellable). Internal, so the App cannot
        /// build the full catalog by accident from a Remote Config payload; it starts from <see cref="Default"/>
        /// (<c>WithFreeModes</c>).
        /// </summary>
        internal MonetizationConfig(IEnumerable<ModeCode> freeModes)
            : this(freeModes, ProductIds.All, s_allModes ?? BuildModes(false))
        {
        }

        /// <summary>A config with nothing disabled; see the four-argument constructor.</summary>
        public MonetizationConfig(IEnumerable<ModeCode> freeModes, IEnumerable<string> sellableProductIds,
            IEnumerable<ModeCode> releasedModes)
            : this(freeModes, sellableProductIds, releasedModes, Array.Empty<ModeCode>())
        {
        }

        /// <param name="freeModes">Configured <c>free_modes</c>; united with the default free modes.</param>
        /// <param name="sellableProductIds">The build's list of sold products (spec §5.0.4 rule (a)); duplicates are collapsed.</param>
        /// <param name="releasedModes">The build's modes (<c>ModeRegistry</c>); duplicates are collapsed.</param>
        /// <param name="disabledModes">Remote Config <c>modes_disabled</c>; any catalog mode, duplicates are collapsed.</param>
        /// <remarks>
        /// Rule (b) of spec §5.0.4 is a filter, not a validation: a listed product granting a mode that is not released or
        /// is disabled is left unsold (<see cref="UnsoldProductIds"/>) instead of failing, so a kill switch never makes the
        /// config unconstructible. A released mode that is neither free nor sold is accepted (a locked card with an empty
        /// <see cref="Storefront.ModeSheet"/>); release configs should not contain such a mode.
        /// </remarks>
        /// <exception cref="ArgumentNullException">A list is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A mode outside M01…M15.</exception>
        /// <exception cref="ArgumentException">A null or unknown product id.</exception>
        public MonetizationConfig(IEnumerable<ModeCode> freeModes, IEnumerable<string> sellableProductIds,
            IEnumerable<ModeCode> releasedModes, IEnumerable<ModeCode> disabledModes)
        {
            if (freeModes == null) throw new ArgumentNullException(nameof(freeModes));
            if (sellableProductIds == null) throw new ArgumentNullException(nameof(sellableProductIds));
            if (releasedModes == null) throw new ArgumentNullException(nameof(releasedModes));
            if (disabledModes == null) throw new ArgumentNullException(nameof(disabledModes));

            var free = new SortedSet<ModeCode>(s_defaultFree ?? BuildDefaultFree());
            foreach (var m in freeModes) free.Add(CheckMode(m, nameof(freeModes)));
            _freeModes = ToArray(free);
            _freeModesView = Array.AsReadOnly(_freeModes);

            var build = new SortedSet<ModeCode>();
            foreach (var m in releasedModes) build.Add(CheckMode(m, nameof(releasedModes)));
            _buildModes = ToArray(build);

            var disabled = new SortedSet<ModeCode>();
            foreach (var m in disabledModes) disabled.Add(CheckMode(m, nameof(disabledModes)));
            _disabledModes = ToArray(disabled);
            _disabledModesView = Array.AsReadOnly(_disabledModes);

            var released = new SortedSet<ModeCode>(build);
            released.ExceptWith(disabled);
            _releasedModes = ToArray(released);
            _releasedModesView = Array.AsReadOnly(_releasedModes);

            var wanted = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in sellableProductIds)
            {
                if (!ProductCatalog.TryGet(id, out _))
                    throw new ArgumentException("Unknown product id: " + (id ?? "null"), nameof(sellableProductIds));
                wanted.Add(id);
            }
            // Catalog order, so HashSet iteration order never leaks.
            var buildList = new List<string>();
            var sellable = new List<string>();
            var unsold = new List<string>();
            foreach (var id in ProductIds.All)
            {
                if (!wanted.Contains(id)) continue;
                buildList.Add(id);
                if (GrantsOnlyModesIn(ProductCatalog.Get(id), released)) sellable.Add(id);
                else unsold.Add(id);
            }
            _buildSellable = buildList.ToArray();
            _sellable = sellable.ToArray();
            _sellableView = Array.AsReadOnly(_sellable);
            _unsold = unsold.ToArray();
            _unsoldView = Array.AsReadOnly(_unsold);
            _sellableSet = new HashSet<string>(_sellable, StringComparer.Ordinal);
            var grants = EntitlementSet.Empty;
            foreach (var id in _sellable) grants = grants.Union(ProductCatalog.Get(id).Grants);
            _sellableGrants = grants;
        }

        /// <summary>
        /// The same release catalog and rules with another Remote Config <c>free_modes</c> payload, e.g.
        /// <c>MonetizationConfig.Release1.WithFreeModes(resolved)</c>. Still united with the default free modes; keeps
        /// <see cref="DisabledModes"/>.
        /// </summary>
        public MonetizationConfig WithFreeModes(IEnumerable<ModeCode> freeModes)
        {
            return new MonetizationConfig(freeModes, _buildSellable, _buildModes, _disabledModes);
        }

        /// <summary>
        /// The same release catalog and free set with another Remote Config <c>modes_disabled</c> payload (spec §5.0.4,
        /// §7.6), e.g. <c>MonetizationConfig.Release1.WithFreeModes(free).WithDisabledModes(disabled)</c>. Replaces the
        /// previous disabled set (an empty list re-enables every mode of the build), so products dropped by an earlier
        /// payload are sold again.
        /// </summary>
        public MonetizationConfig WithDisabledModes(IEnumerable<ModeCode> disabledModes)
        {
            return new MonetizationConfig(_freeModes, _buildSellable, _buildModes, disabledModes);
        }

        /// <summary>Whether <paramref name="mode"/> is in the effective free set (spec §5.1). Says nothing about release.</summary>
        public bool IsFree(ModeCode mode)
        {
            return Array.BinarySearch(_freeModes, mode) >= 0;
        }

        /// <summary>Whether <paramref name="mode"/> is playable in this build (<see cref="ReleasedModes"/>: shipped, not disabled).</summary>
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

        static bool GrantsOnlyModesIn(ProductInfo p, SortedSet<ModeCode> released)
        {
            foreach (var e in p.Grants.ToArray())
            {
                if (EntitlementInfo.TryGetMode(e, out var mode) && !released.Contains(mode)) return false;
            }
            return true;
        }

        static ModeCode[] ToArray(SortedSet<ModeCode> set)
        {
            var a = new ModeCode[set.Count];
            set.CopyTo(a);
            return a;
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

        static ModeCode[] BuildModes(bool releasedOnly)
        {
            var list = new List<ModeCode>();
            foreach (var m in releasedOnly ? ModeCatalog.Released : ModeCatalog.All) list.Add(m.Code);
            return list.ToArray();
        }
    }
}
