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

    /// <summary>One state report of a product transaction. Immutable.</summary>
    public readonly struct PurchaseRecord
    {
        public string ProductId { get; }
        public PurchaseState State { get; }
        /// <summary>
        /// Store transaction id (StoreKit originalTransactionID / Play purchaseToken), or null/empty. Records sharing a
        /// non-empty id are successive states of one transaction; the last one in the sequence wins (e.g. Purchased then
        /// Revoked). An empty id (native bridges often marshal a missing string as "") counts as no id (spec §5.4).
        /// </summary>
        public string TransactionId { get; }

        public PurchaseRecord(string productId, PurchaseState state, string transactionId = null)
        {
            ProductId = productId;
            State = state;
            TransactionId = transactionId;
        }
    }

    /// <summary>Turns store transactions into the owned-product list fed to <see cref="Entitlements.From"/> (spec §5.4).</summary>
    public static class OwnedProducts
    {
        /// <summary>
        /// Ids with at least one transaction whose current state is <see cref="PurchaseState.Purchased"/> (pending and
        /// refunded ones grant nothing, spec §5.4 "Права не выдаются до подтверждения", "Возврат денег … право снимается";
        /// this includes Play Billing PENDING purchases, which already carry a purchaseToken), sorted ordinal, distinct.
        /// The current state of a transaction is its last record with the same <see cref="PurchaseRecord.TransactionId"/>;
        /// a record without an id (null or empty) is taken as the current state of a transaction of its own, so pass ids when the input
        /// mixes old and updated states (e.g. iOS Transaction.updates), or pass only current states (StoreKit
        /// currentEntitlements, Play queryPurchasesAsync). Null/empty product ids are dropped; unknown ids are kept for
        /// forward compatibility.
        /// </summary>
        public static IReadOnlyList<string> From(IEnumerable<PurchaseRecord> records)
        {
            if (records == null) throw new ArgumentNullException(nameof(records));
            var latest = new Dictionary<string, PurchaseRecord>(StringComparer.Ordinal);
            var set = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var r in records)
            {
                if (!string.IsNullOrEmpty(r.TransactionId)) latest[r.TransactionId] = r;
                else if (r.State == PurchaseState.Purchased && !string.IsNullOrEmpty(r.ProductId)) set.Add(r.ProductId);
            }
            // Order of the dictionary does not leak: results go into a sorted set.
            foreach (var r in latest.Values)
            {
                if (r.State == PurchaseState.Purchased && !string.IsNullOrEmpty(r.ProductId)) set.Add(r.ProductId);
            }
            var result = new string[set.Count];
            set.CopyTo(result);
            return result;
        }
    }
}
