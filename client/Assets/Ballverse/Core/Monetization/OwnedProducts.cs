using System;
using System.Collections.Generic;

namespace Ballverse.Core.Monetization
{
    /// <summary>Store transaction state as reported by StoreKit 2 / Play Billing (spec §5.4, §5.5).</summary>
    public enum PurchaseState
    {
        /// <summary>Verified and paid.</summary>
        Purchased = 0,
        /// <summary>Awaiting payment or parental approval: grants nothing yet.</summary>
        Pending = 1,
        /// <summary>Refunded or revoked (iOS revocationDate): grants nothing.</summary>
        Revoked = 2,
    }

    /// <summary>One transaction of a product. Immutable.</summary>
    public readonly struct PurchaseRecord
    {
        public string ProductId { get; }
        public PurchaseState State { get; }

        public PurchaseRecord(string productId, PurchaseState state)
        {
            ProductId = productId;
            State = state;
        }
    }

    /// <summary>Turns store transactions into the owned-product list fed to <see cref="Entitlements.From"/> (spec §5.4).</summary>
    public static class OwnedProducts
    {
        /// <summary>
        /// Ids with at least one <see cref="PurchaseState.Purchased"/> record (pending and refunded ones grant nothing),
        /// sorted ordinal, distinct. Null/empty ids are dropped; unknown ids are kept for forward compatibility.
        /// </summary>
        public static IReadOnlyList<string> From(IEnumerable<PurchaseRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var r in records)
            {
                if (r.State == PurchaseState.Purchased && !string.IsNullOrEmpty(r.ProductId)) set.Add(r.ProductId);
            }
            var result = new string[set.Count];
            set.CopyTo(result);
            return result;
        }
    }
}
