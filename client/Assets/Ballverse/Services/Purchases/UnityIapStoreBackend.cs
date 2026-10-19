#if BALLVERSE_UNITY_IAP
using System;
using System.Collections.Generic;
using UnityEngine.Purchasing;
using IapProduct = UnityEngine.Purchasing.Product;

namespace Ballverse.Services
{
    /// <summary>
    /// <see cref="IStoreBackend"/> over Unity IAP 5 (<c>UnityIAPServices.StoreController()</c>: StoreKit 2 on iOS, Play Billing
    /// on Android). Pending orders found by <c>FetchPurchases</c> are reported in the purchases list (automatic
    /// <c>OnPurchasePending</c> re-delivery is switched off), so <see cref="PurchaseService"/> sees every transaction once per read.
    /// Callbacks are moved to the main thread through <see cref="MainThreadQueue"/>.
    /// </summary>
    public sealed class UnityIapStoreBackend : IStoreBackend
    {
        const string Tag = "UnityIAP";

        readonly MainThreadQueue _queue;
        readonly IServiceLog _log;
        StoreController _controller;
        IStoreBackendListener _listener;
        List<ProductDefinition> _definitions = new List<ProductDefinition>();
        volatile bool _disconnected;
        volatile string _disconnectReason;
        bool _connected;

        public UnityIapStoreBackend(MainThreadQueue queue, IServiceLog log)
        {
            _queue = queue ?? throw new ArgumentNullException(nameof(queue));
            _log = log ?? NullServiceLog.Instance;
        }

        public bool IsAvailable => true;

        public IStoreBackendListener Listener { set => _listener = value; }

        public void Connect(IReadOnlyList<string> productIds)
        {
            _definitions = new List<ProductDefinition>();
            foreach (string id in productIds) _definitions.Add(new ProductDefinition(id, ProductType.NonConsumable));

            if (_controller == null)
            {
                _controller = UnityIAPServices.StoreController();
                Subscribe(_controller);
                _controller.ProcessPendingOrdersOnPurchasesFetched(false);
            }
            _disconnected = false;
            _disconnectReason = null;
            ConnectAsync(_controller);
        }

        async void ConnectAsync(StoreController controller)
        {
            try
            {
                // Completes on success and also after a final disconnection (Unity IAP reports that via OnStoreDisconnected).
                await controller.Connect();
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Store connect threw.", e);
                _queue.Post(() => _listener?.OnConnectFailed(e.Message));
                return;
            }
            _queue.Post(() =>
            {
                if (_disconnected)
                {
                    _connected = false;
                    _listener?.OnConnectFailed(_disconnectReason ?? "disconnected");
                    return;
                }
                _connected = true;
                _listener?.OnConnected();
                controller.FetchProducts(_definitions);
            });
        }

        void Subscribe(StoreController controller)
        {
            // 5.4+: Connect() warns when no OnStoreConnected handler exists; the outcome itself is read in ConnectAsync.
            controller.OnStoreConnected += () => _queue.Post(() => _log.Info(Tag, "Store connected."));
            controller.OnStoreDisconnected += failure =>
            {
                _disconnectReason = failure != null ? failure.Message : "disconnected";
                _disconnected = true;
                _queue.Post(() =>
                {
                    if (!_connected) return; // reported by ConnectAsync
                    _connected = false;
                    _listener?.OnDisconnected(_disconnectReason);
                });
            };
            controller.OnProductsFetched += products => _queue.Post(() => _listener?.OnProductsFetched(MapProducts(products)));
            controller.OnProductsFetchFailed += failure => _queue.Post(() =>
                _listener?.OnProductsFetchFailed(failure != null ? failure.FailureReason : "unknown"));
            controller.OnPurchasesFetched += orders => _queue.Post(() => _listener?.OnPurchasesFetched(MapOrders(orders)));
            controller.OnPurchasesFetchFailed += failure => _queue.Post(() =>
                _listener?.OnPurchasesFetchFailed(failure != null ? failure.FailureReason.ToString() : "unknown"));
            controller.OnPurchasePending += order => _queue.Post(() =>
            {
                foreach (var p in MapOrder(order, StorePurchaseState.AwaitingConfirmation)) _listener?.OnPurchasePending(p);
            });
            controller.OnPurchaseConfirmed += order => _queue.Post(() =>
            {
                var failed = order as FailedOrder;
                foreach (string id in ProductIdsOf(order))
                {
                    if (failed != null) _listener?.OnPurchaseConfirmFailed(id, failed.FailureReason.ToString());
                    else _listener?.OnPurchaseConfirmed(id);
                }
            });
            controller.OnPurchaseFailed += order => _queue.Post(() =>
            {
                string reason = order != null ? order.FailureReason.ToString() : "Unknown";
                var ids = ProductIdsOf(order);
                if (ids.Count == 0) _listener?.OnPurchaseFailed(null, reason);
                foreach (string id in ids) _listener?.OnPurchaseFailed(id, reason);
            });
            controller.OnPurchaseDeferred += order => _queue.Post(() =>
            {
                foreach (string id in ProductIdsOf(order)) _listener?.OnPurchaseDeferred(id);
            });

            var apple = controller.AppleStoreExtendedPurchaseService;
            if (apple != null) apple.OnEntitlementRevoked += productId => _queue.Post(() => _listener?.OnEntitlementRevoked(productId));
        }

        public void FetchPurchases()
        {
            if (_controller == null || !_connected)
            {
                _listener?.OnPurchasesFetchFailed("StoreNotConnected");
                return;
            }
            _controller.FetchPurchases();
        }

        public void Purchase(string productId)
        {
            IapProduct product = _controller != null ? _controller.GetProductById(productId) : null;
            if (product == null || !product.availableToPurchase)
            {
                _listener?.OnPurchaseFailed(productId, "ProductUnavailable");
                return;
            }
            _controller.PurchaseProduct(product);
        }

        public void Confirm(StorePurchase purchase)
        {
            if (_controller != null && purchase.Handle is PendingOrder order) _controller.ConfirmPurchase(order);
        }

        public void Restore(Action<bool, string> done)
        {
            if (_controller == null || !_connected)
            {
                done?.Invoke(false, "StoreNotConnected");
                return;
            }
            _controller.RestoreTransactions((ok, error) => _queue.Post(() => done?.Invoke(ok, error)));
        }

        static List<StoreProduct> MapProducts(List<IapProduct> products)
        {
            var list = new List<StoreProduct>();
            if (products == null) return list;
            foreach (var p in products)
            {
                if (p == null || p.definition == null) continue;
                var m = p.metadata;
                list.Add(new StoreProduct(p.definition.id,
                    m != null ? m.localizedPriceString : string.Empty,
                    m != null ? m.localizedTitle : string.Empty,
                    m != null ? m.localizedDescription : string.Empty,
                    m != null ? m.isoCurrencyCode : string.Empty,
                    m != null ? m.localizedPrice : 0m,
                    p.availableToPurchase));
            }
            return list;
        }

        static List<StorePurchase> MapOrders(Orders orders)
        {
            var list = new List<StorePurchase>();
            if (orders == null) return list;
            foreach (var o in orders.ConfirmedOrders) list.AddRange(MapOrder(o, StorePurchaseState.Confirmed));
            foreach (var o in orders.PendingOrders) list.AddRange(MapOrder(o, StorePurchaseState.AwaitingConfirmation));
            foreach (var o in orders.DeferredOrders) list.AddRange(MapOrder(o, StorePurchaseState.Deferred));
            return list;
        }

        static List<StorePurchase> MapOrder(Order order, StorePurchaseState state)
        {
            var list = new List<StorePurchase>();
            if (order == null) return list;
            string tx = order.Info != null ? order.Info.TransactionID : null;
            string receipt = order.Info != null ? order.Info.Receipt : null;
            foreach (string id in ProductIdsOf(order)) list.Add(new StorePurchase(id, tx, state, receipt, order));
            return list;
        }

        static List<string> ProductIdsOf(Order order)
        {
            var ids = new List<string>();
            if (order == null || order.CartOrdered == null) return ids;
            foreach (var item in order.CartOrdered.Items())
            {
                var product = item != null ? item.Product : null;
                if (product != null && product.definition != null && !string.IsNullOrEmpty(product.definition.id) && !ids.Contains(product.definition.id))
                    ids.Add(product.definition.id);
            }
            return ids;
        }
    }
}
#endif
