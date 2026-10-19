using System;
using System.Collections.Generic;

namespace Ballverse.Services
{
    public enum PurchaseServiceState
    {
        NotInitialized = 0,
        Initializing = 1,
        /// <summary>Connected; products and purchases come from the store.</summary>
        Ready = 2,
        /// <summary>Store not reachable (offline, no store account, no IAP in this build). Rights come from the signed cache (spec §5.4).</summary>
        Unavailable = 3,
    }

    /// <summary>A store product with store-localized price strings (never formatted by the app, spec §5.1).</summary>
    public sealed class StoreProduct
    {
        public StoreProduct(string id, string localizedPrice, string localizedTitle, string localizedDescription,
            string isoCurrencyCode, decimal price, bool availableToPurchase)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            LocalizedPrice = localizedPrice ?? string.Empty;
            LocalizedTitle = localizedTitle ?? string.Empty;
            LocalizedDescription = localizedDescription ?? string.Empty;
            IsoCurrencyCode = isoCurrencyCode ?? string.Empty;
            Price = price;
            AvailableToPurchase = availableToPurchase;
        }

        public string Id { get; }

        /// <summary>E.g. "$6.99", "6,99 €" — as the store formats it.</summary>
        public string LocalizedPrice { get; }
        public string LocalizedTitle { get; }
        public string LocalizedDescription { get; }
        public string IsoCurrencyCode { get; }
        public decimal Price { get; }
        public bool AvailableToPurchase { get; }
    }

    public enum StorePurchaseState
    {
        /// <summary>Paid and finished (acknowledged / Transaction.finish).</summary>
        Confirmed = 0,
        /// <summary>Paid, not yet confirmed by the app (new purchase, or one interrupted by a crash, spec §5.4).</summary>
        AwaitingConfirmation = 1,
        /// <summary>Waiting for payment or parental approval (Ask to Buy, cash): grants nothing (spec §5.4).</summary>
        Deferred = 2,
    }

    /// <summary>One store transaction as reported by the backend.</summary>
    public sealed class StorePurchase
    {
        public StorePurchase(string productId, string transactionId, StorePurchaseState state, string receipt, object handle)
        {
            ProductId = productId ?? throw new ArgumentNullException(nameof(productId));
            TransactionId = transactionId ?? string.Empty;
            State = state;
            Receipt = receipt ?? string.Empty;
            Handle = handle;
        }

        public string ProductId { get; }

        /// <summary>Store transaction id; may be empty for confirmed orders.</summary>
        public string TransactionId { get; }
        public StorePurchaseState State { get; }

        /// <summary>Unity IAP receipt JSON (Android: signed Play purchase data); may be empty.</summary>
        public string Receipt { get; }

        /// <summary>Backend object needed to confirm (Unity IAP <c>PendingOrder</c>).</summary>
        public object Handle { get; }
    }

    public enum PurchaseResultKind
    {
        Purchased = 0,
        /// <summary>Waiting for payment/approval; rights arrive later (spec §5.4).</summary>
        Pending = 1,
        Cancelled = 2,
        Failed = 3,
        /// <summary>The user already owns everything the product grants (smart storefront protection, spec §5.2).</summary>
        AlreadyOwned = 4,
        /// <summary>Not sold in release 1, or the store is unavailable.</summary>
        NotAvailable = 5,
    }

    public readonly struct PurchaseOutcome
    {
        public PurchaseOutcome(string productId, PurchaseResultKind kind, string errorCode)
        {
            ProductId = productId;
            Kind = kind;
            ErrorCode = errorCode;
        }

        public string ProductId { get; }
        public PurchaseResultKind Kind { get; }

        /// <summary>Analytics-safe code for failures (e.g. <c>payment_declined</c>), else null.</summary>
        public string ErrorCode { get; }

        public override string ToString()
        {
            return ProductId + ": " + Kind + (ErrorCode != null ? " (" + ErrorCode + ")" : string.Empty);
        }
    }

    public readonly struct RestoreOutcome
    {
        public RestoreOutcome(bool success, int ownedCount, string errorCode)
        {
            Success = success;
            OwnedCount = ownedCount;
            ErrorCode = errorCode;
        }

        public bool Success { get; }

        /// <summary>Owned products after the restore.</summary>
        public int OwnedCount { get; }
        public string ErrorCode { get; }
    }

    /// <summary>Receives backend events, always on the main thread.</summary>
    public interface IStoreBackendListener
    {
        void OnConnected();
        void OnConnectFailed(string reason);
        void OnDisconnected(string reason);
        void OnProductsFetched(IReadOnlyList<StoreProduct> products);
        void OnProductsFetchFailed(string reason);

        /// <summary>The store's current purchases (authoritative on success, spec §5.4 refunds disappear here).</summary>
        void OnPurchasesFetched(IReadOnlyList<StorePurchase> purchases);
        void OnPurchasesFetchFailed(string reason);

        /// <summary>A paid purchase awaits validation and confirmation (new or interrupted, spec §5.4).</summary>
        void OnPurchasePending(StorePurchase purchase);
        void OnPurchaseDeferred(string productId);
        void OnPurchaseFailed(string productId, string reasonCode);
        void OnPurchaseConfirmed(string productId);
        void OnPurchaseConfirmFailed(string productId, string reasonCode);

        /// <summary>iOS: a refund or Family Sharing revocation (StoreKit revocationDate, spec §5.4).</summary>
        void OnEntitlementRevoked(string productId);
    }

    /// <summary>
    /// Thin store adapter under <see cref="PurchaseService"/>: Unity IAP 5 in builds, <see cref="FakeStoreBackend"/> in the
    /// editor and tests. No policy here.
    /// </summary>
    public interface IStoreBackend
    {
        bool IsAvailable { get; }

        IStoreBackendListener Listener { set; }

        /// <summary>Connects and fetches the products (all release-1 products are non-consumable).</summary>
        void Connect(IReadOnlyList<string> productIds);

        void FetchPurchases();

        void Purchase(string productId);

        void Confirm(StorePurchase purchase);

        /// <summary>Apple <c>AppStore.sync()</c> / Play restore; <paramref name="done"/> gets success and an error.</summary>
        void Restore(Action<bool, string> done);
    }
}
