using System;
using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>
    /// Remote-configurable monetization knobs used by the pure entitlement logic (spec §5.1).
    /// Immutable; build a new instance when Remote Config changes.
    /// </summary>
    public sealed class MonetizationConfig
    {
        static readonly ModeCode[] s_defaultFree = BuildDefaultFree();

        /// <summary>Free modes M01–M05 (<see cref="ModeInfo.FreeByDefault"/>) and the "by parts" watermark rule on.</summary>
        public static readonly MonetizationConfig Default = new MonetizationConfig(s_defaultFree, true);

        readonly ModeCode[] _freeModes;

        /// <summary>
        /// Remote Config <c>free_modes</c> (spec §5.1): modes available without entitlements. Sorted, distinct.
        /// Widening is always allowed; narrowing never removes grandfathered modes (see <see cref="ModeAvailability"/>).
        /// </summary>
        public IReadOnlyList<ModeCode> FreeModes => _freeModes;

        /// <summary>
        /// Spec §5.1 "NO_WATERMARK по частям": owning the full Ultimate contents from any products also grants NO_WATERMARK.
        /// On by default; the owner may revoke the rule, then only Ultimate removes the watermark and the storefront
        /// keeps offering Ultimate for the missing NO_WATERMARK.
        /// </summary>
        public bool WatermarkByParts { get; }

        public MonetizationConfig(IEnumerable<ModeCode> freeModes, bool watermarkByParts = true)
        {
            if (freeModes == null) throw new ArgumentNullException(nameof(freeModes));
            var set = new SortedSet<ModeCode>();
            foreach (var m in freeModes)
            {
                if ((int)m < (int)ModeCode.M01 || (int)m > (int)ModeCode.M15)
                    throw new ArgumentOutOfRangeException(nameof(freeModes), "Unknown mode " + (int)m);
                set.Add(m);
            }
            _freeModes = new ModeCode[set.Count];
            set.CopyTo(_freeModes);
            WatermarkByParts = watermarkByParts;
        }

        /// <summary>
        /// Builds the free set from Remote Config strings ("M01"…"M15"). Unknown or malformed entries are ignored;
        /// a null list keeps the default free set (a broken payload must not lock free modes).
        /// </summary>
        public static MonetizationConfig FromRemote(IEnumerable<string> freeModeCodes, bool watermarkByParts = true)
        {
            if (freeModeCodes == null) return new MonetizationConfig(s_defaultFree, watermarkByParts);
            var codes = new List<ModeCode>();
            foreach (var s in freeModeCodes)
            {
                if (ModeCatalog.TryParseCode(s, out var code)) codes.Add(code);
            }
            return new MonetizationConfig(codes, watermarkByParts);
        }

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
