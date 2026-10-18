using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Ballverse.Core.Monetization
{
    /// <summary>
    /// Remote-configurable monetization knobs used by the pure entitlement logic (spec §5.1).
    /// Immutable; build a new instance when Remote Config changes.
    /// <para>
    /// Parsing of the Remote Config <c>free_modes</c> payload belongs to the config module (spec §9.3); the App layer passes
    /// its resolved <see cref="ModeCode"/> list to the constructor, so one payload never yields two different free sets.
    /// </para>
    /// </summary>
    public sealed class MonetizationConfig
    {
        static readonly ModeCode[] s_defaultFree = BuildDefaultFree();

        /// <summary>Free modes M01–M05 (<see cref="ModeInfo.FreeByDefault"/>) and the "by parts" watermark rule on.</summary>
        public static readonly MonetizationConfig Default = new MonetizationConfig(s_defaultFree, true);

        readonly ModeCode[] _freeModes;
        readonly ReadOnlyCollection<ModeCode> _freeModesView;

        /// <summary>
        /// Effective free set (spec §5.1 "M01–M05 доступны всегда", §9.3 <c>free_modes</c> "только расширение"): the
        /// default free modes (<see cref="ModeInfo.FreeByDefault"/>, M01–M05) united with the configured ones. Sorted,
        /// distinct, read-only. Config can only widen the set, so an empty or broken payload never locks M01–M05;
        /// a paid mode removed from a widened set stays open for users who used it (see <see cref="ModeAvailability"/>).
        /// </summary>
        public IReadOnlyList<ModeCode> FreeModes => _freeModesView;

        /// <summary>
        /// Spec §5.1 "NO_WATERMARK по частям": owning the full Ultimate contents from any products also grants NO_WATERMARK.
        /// On by default; the owner may revoke the rule, then only Ultimate removes the watermark and the storefront
        /// keeps offering Ultimate for the missing NO_WATERMARK.
        /// </summary>
        public bool WatermarkByParts { get; }

        /// <param name="freeModes">Configured <c>free_modes</c> (already resolved by the config module); united with M01–M05.</param>
        /// <param name="watermarkByParts">See <see cref="WatermarkByParts"/>.</param>
        /// <exception cref="ArgumentNullException"><paramref name="freeModes"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A mode outside M01…M15.</exception>
        public MonetizationConfig(IEnumerable<ModeCode> freeModes, bool watermarkByParts = true)
        {
            if (freeModes == null) throw new ArgumentNullException(nameof(freeModes));
            var set = new SortedSet<ModeCode>(s_defaultFree ?? BuildDefaultFree());
            foreach (var m in freeModes)
            {
                if ((int)m < (int)ModeCode.M01 || (int)m > (int)ModeCode.M15)
                    throw new ArgumentOutOfRangeException(nameof(freeModes), "Unknown mode " + (int)m);
                set.Add(m);
            }
            _freeModes = new ModeCode[set.Count];
            set.CopyTo(_freeModes);
            _freeModesView = Array.AsReadOnly(_freeModes);
            WatermarkByParts = watermarkByParts;
        }

        /// <summary>Whether <paramref name="mode"/> is in the effective free set (spec §5.1).</summary>
        public bool IsFree(ModeCode mode)
        {
            return Array.BinarySearch(_freeModes, mode) >= 0;
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
    }
}
