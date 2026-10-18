using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>
    /// Permanent saving of a bundle against buying its parts (spec §5.1 "Экономия в наборах", Р6). Shown as
    /// "вместо {InsteadOf}" and a percent — never as a struck-through "old price" or a "discount" (EU Omnibus).
    /// Amounts are in the price map's currency (localized store prices). Immutable.
    /// </summary>
    public sealed class BundleSavings
    {
        readonly string[] _partIds;

        public string BundleId { get; }
        /// <summary>The parts the bundle is compared with, in catalog order.</summary>
        public IReadOnlyList<string> PartIds => _partIds;
        public decimal BundlePrice { get; }
        /// <summary>Sum of the parts' prices: the "вместо X" amount.</summary>
        public decimal InsteadOf { get; }
        /// <summary>InsteadOf − BundlePrice, always &gt; 0.</summary>
        public decimal Amount { get; }
        /// <summary>Amount / InsteadOf in whole percent, rounded half away from zero (75, 31, 14 / 64 at base prices).</summary>
        public int Percent { get; }

        internal BundleSavings(string bundleId, string[] partIds, decimal bundlePrice, decimal insteadOf, decimal amount, int percent)
        {
            BundleId = bundleId;
            _partIds = partIds;
            BundlePrice = bundlePrice;
            InsteadOf = insteadOf;
            Amount = amount;
            Percent = percent;
        }
    }
}
