using System;
using System.Collections.Generic;
using System.Globalization;
using Ballverse.Core.Monetization;

namespace Ballverse.Services
{
#if !BALLVERSE_STORE // fakes are not compiled into Store builds (spec §8.9)
    public enum FakePurchaseBehaviour
    {
        Succeed = 0,
        Cancel = 1,
        Decline = 2,
        /// <summary>Ask to Buy / cash payment: deferred until <see cref="FakeStoreBackend.ApproveDeferred"/>.</summary>
        Defer = 3,
    }

    /// <summary>Test configuration of <see cref="FakeStoreBackend"/> (spec §7.9: "покупки управляются тестовым конфигом").</summary>
    public sealed class FakeStoreConfig
    {
        /// <summary>Products the fake store account already owns.</summary>
        public List<string> InitiallyOwned { get; } = new List<string>();

        public FakePurchaseBehaviour Behaviour { get; set; } = FakePurchaseBehaviour.Succeed;

        public bool ConnectSucceeds { get; set; } = true;

        public bool FetchSucceeds { get; set; } = true;

        /// <summary>When false, <see cref="FakeStoreBackend.Connect"/> waits for <see cref="FakeStoreBackend.CompleteConnect"/>.</summary>
        public bool ConnectImmediately { get; set; } = true;
    }

    /// <summary>
    /// Store without a store: synchronous, scriptable, with USD base prices from the Core catalog formatted as a store would.
    /// Used in the editor (unless real IAP is forced) and in tests.
    /// </summary>
    public sealed class FakeStoreBackend : IStoreBackend
    {
        readonly FakeStoreConfig _config;
        readonly SortedSet<string> _owned = new SortedSet<string>(StringComparer.Ordinal);
        readonly SortedSet<string> _deferred = new SortedSet<string>(StringComparer.Ordinal);
        readonly Dictionary<string, StorePurchase> _awaiting = new Dictionary<string, StorePurchase>(StringComparer.Ordinal);
        IReadOnlyList<string> _productIds = Array.Empty<string>();
        IStoreBackendListener _listener;
        int _nextTransaction = 1;

        public FakeStoreBackend(FakeStoreConfig config = null)
        {
            _config = config ?? new FakeStoreConfig();
            foreach (string id in _config.InitiallyOwned) _owned.Add(id);
        }

        public FakeStoreConfig Config => _config;

        public bool IsAvailable => true;

        public IStoreBackendListener Listener { set => _listener = value; }

        public int ConfirmCalls { get; private set; }
        public int FetchCalls { get; private set; }
        public int RestoreCalls { get; private set; }

        /// <summary>Products the store account owns (confirmed).</summary>
        public IReadOnlyCollection<string> StoreOwned => _owned;

        public void Connect(IReadOnlyList<string> productIds)
        {
            _productIds = productIds ?? Array.Empty<string>();
            if (_config.ConnectImmediately) CompleteConnect();
        }

        /// <summary>Finishes a held <see cref="Connect"/>.</summary>
        public void CompleteConnect()
        {
            if (!_config.ConnectSucceeds)
            {
                _listener?.OnConnectFailed("fake: store unavailable");
                return;
            }
            _listener?.OnConnected();
            var products = new List<StoreProduct>();
            foreach (string id in _productIds)
            {
                ProductCatalog.TryGet(id, out var info);
                decimal price = info != null ? info.BasePriceUsd : 0m;
                products.Add(new StoreProduct(id, "$" + price.ToString("0.00", CultureInfo.InvariantCulture),
                    id, string.Empty, "USD", price, info != null));
            }
            _listener?.OnProductsFetched(products);
        }

        public void FetchPurchases()
        {
            FetchCalls++;
            if (!_config.FetchSucceeds)
            {
                _listener?.OnPurchasesFetchFailed("fake: fetch failed");
                return;
            }
            var list = new List<StorePurchase>();
            foreach (string id in _owned) list.Add(new StorePurchase(id, string.Empty, StorePurchaseState.Confirmed, string.Empty, null));
            foreach (var p in _awaiting.Values) list.Add(p);
            foreach (string id in _deferred) list.Add(new StorePurchase(id, string.Empty, StorePurchaseState.Deferred, string.Empty, null));
            _listener?.OnPurchasesFetched(list);
        }

        public void Purchase(string productId)
        {
            switch (_config.Behaviour)
            {
                case FakePurchaseBehaviour.Cancel:
                    _listener?.OnPurchaseFailed(productId, "UserCancelled");
                    break;
                case FakePurchaseBehaviour.Decline:
                    _listener?.OnPurchaseFailed(productId, "PaymentDeclined");
                    break;
                case FakePurchaseBehaviour.Defer:
                    _deferred.Add(productId);
                    _listener?.OnPurchaseDeferred(productId);
                    break;
                default:
                    Deliver(productId);
                    break;
            }
        }

        public void Confirm(StorePurchase purchase)
        {
            ConfirmCalls++;
            _awaiting.Remove(purchase.TransactionId);
            _owned.Add(purchase.ProductId);
            _listener?.OnPurchaseConfirmed(purchase.ProductId);
        }

        public void Restore(Action<bool, string> done)
        {
            RestoreCalls++;
            done?.Invoke(true, null);
        }

        /// <summary>The parent approved / the cash payment arrived.</summary>
        public void ApproveDeferred(string productId)
        {
            if (_deferred.Remove(productId)) Deliver(productId);
        }

        /// <summary>A refund: the purchase disappears from the store (Android); <paramref name="notify"/> also sends the iOS revocation.</summary>
        public void Refund(string productId, bool notify)
        {
            _owned.Remove(productId);
            if (notify) _listener?.OnEntitlementRevoked(productId);
        }

        /// <summary>Simulates a purchase paid but never confirmed (app killed before confirming, spec §5.4).</summary>
        public void AddUnconfirmed(string productId)
        {
            var p = NewPurchase(productId);
            _awaiting[p.TransactionId] = p;
        }

        void Deliver(string productId)
        {
            var p = NewPurchase(productId);
            _awaiting[p.TransactionId] = p;
            _listener?.OnPurchasePending(p);
        }

        StorePurchase NewPurchase(string productId)
        {
            string tx = "fake-" + (_nextTransaction++).ToString(CultureInfo.InvariantCulture);
            return new StorePurchase(productId, tx, StorePurchaseState.AwaitingConfirmation, "{\"Store\":\"fake\"}", null);
        }
    }
#endif

    /// <summary>Player builds without Unity IAP: never grants anything; rights come only from the cache.</summary>
    public sealed class UnavailableStoreBackend : IStoreBackend
    {
        public bool IsAvailable => false;

        public IStoreBackendListener Listener { set { } }

        public void Connect(IReadOnlyList<string> productIds) { }

        public void FetchPurchases() { }

        public void Purchase(string productId) { }

        public void Confirm(StorePurchase purchase) { }

        public void Restore(Action<bool, string> done)
        {
            done?.Invoke(false, "store_unavailable");
        }
    }
}
