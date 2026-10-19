using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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
    /// Used in the editor, in tests and in Dev / OwnerTest device builds until the store apps exist (spec §7.9).
    /// <para>
    /// With an <see cref="IKeyValueStore"/> the fake store account — owned products, deferred and paid-but-unconfirmed orders —
    /// is persisted under <see cref="AccountKey"/> after every change and read back at construction, like a real store account
    /// that outlives the app process: purchases made in an OwnerTest build survive restarts and are re-delivered by the next
    /// purchases read. Without one the account lives in memory only (tests).
    /// </para>
    /// </summary>
    public sealed class FakeStoreBackend : IStoreBackend
    {
        /// <summary>Key of the persisted account (line format, see <see cref="SerializeAccount"/>).</summary>
        public const string AccountKey = "ballverse.fake_store.account.v1";

        const string Header = "ballverse-fake-store v1";

        readonly FakeStoreConfig _config;
        readonly IKeyValueStore _persistence;
        readonly SortedSet<string> _owned = new SortedSet<string>(StringComparer.Ordinal);
        readonly SortedSet<string> _deferred = new SortedSet<string>(StringComparer.Ordinal);
        readonly SortedDictionary<string, StorePurchase> _awaiting = new SortedDictionary<string, StorePurchase>(StringComparer.Ordinal);
        IReadOnlyList<string> _productIds = Array.Empty<string>();
        IStoreBackendListener _listener;
        int _nextTransaction = 1;

        /// <param name="config">Behaviour and the products the account owns from the start (added to a persisted account).</param>
        /// <param name="persistence">Where the account is kept across restarts; null keeps it in memory.</param>
        public FakeStoreBackend(FakeStoreConfig config = null, IKeyValueStore persistence = null)
        {
            _config = config ?? new FakeStoreConfig();
            _persistence = persistence;
            if (_persistence != null) LoadAccount();
            bool added = false;
            foreach (string id in _config.InitiallyOwned)
            {
                if (id != null && _owned.Add(id)) added = true;
            }
            if (added) SaveAccount();
        }

        public FakeStoreConfig Config => _config;

        public bool IsAvailable => true;

        public IStoreBackendListener Listener { set => _listener = value; }

        public int ConfirmCalls { get; private set; }
        public int FetchCalls { get; private set; }
        public int RestoreCalls { get; private set; }

        /// <summary>True when the account is persisted (an <see cref="IKeyValueStore"/> was given).</summary>
        public bool IsPersistent => _persistence != null;

        /// <summary>Products the store account owns (confirmed).</summary>
        public IReadOnlyCollection<string> StoreOwned => _owned;

        /// <summary>Products waiting for <see cref="ApproveDeferred"/>.</summary>
        public IReadOnlyCollection<string> StoreDeferred => _deferred;

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
                    SaveAccount();
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
            SaveAccount();
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
            if (_owned.Remove(productId)) SaveAccount();
            if (notify) _listener?.OnEntitlementRevoked(productId);
        }

        /// <summary>Simulates a purchase paid but never confirmed (app killed before confirming, spec §5.4).</summary>
        public void AddUnconfirmed(string productId)
        {
            var p = NewPurchase(productId);
            _awaiting[p.TransactionId] = p;
            SaveAccount();
        }

        /// <summary>
        /// Empties the account (developer menu: buy again from scratch). The app's rights follow at its next purchases read,
        /// as after refunds.
        /// </summary>
        public void ResetAccount()
        {
            _owned.Clear();
            _deferred.Clear();
            _awaiting.Clear();
            SaveAccount();
        }

        void Deliver(string productId)
        {
            var p = NewPurchase(productId);
            _awaiting[p.TransactionId] = p;
            SaveAccount();
            _listener?.OnPurchasePending(p);
        }

        StorePurchase NewPurchase(string productId)
        {
            string tx = "fake-" + (_nextTransaction++).ToString(CultureInfo.InvariantCulture);
            return Unconfirmed(productId, tx);
        }

        static StorePurchase Unconfirmed(string productId, string transactionId)
        {
            return new StorePurchase(productId, transactionId, StorePurchaseState.AwaitingConfirmation, "{\"Store\":\"fake\"}", null);
        }

        // ---- persistence ----

        /// <summary>
        /// The account as text: a header line, then <c>next N</c>, <c>owned ID</c>, <c>deferred ID</c> and
        /// <c>awaiting TX ID</c> lines in ordinal order. Product and transaction ids never contain whitespace.
        /// </summary>
        public string SerializeAccount()
        {
            var sb = new StringBuilder();
            sb.Append(Header).Append('\n');
            sb.Append("next ").Append(_nextTransaction.ToString(CultureInfo.InvariantCulture)).Append('\n');
            foreach (string id in _owned) sb.Append("owned ").Append(id).Append('\n');
            foreach (string id in _deferred) sb.Append("deferred ").Append(id).Append('\n');
            foreach (var p in _awaiting.Values) sb.Append("awaiting ").Append(p.TransactionId).Append(' ').Append(p.ProductId).Append('\n');
            return sb.ToString();
        }

        void LoadAccount()
        {
            string text;
            try
            {
                text = _persistence.GetString(AccountKey);
            }
            catch (Exception)
            {
                return; // unreadable storage: start with an empty account, as a fresh store account would be
            }
            if (string.IsNullOrEmpty(text)) return;
            string[] lines = text.Split('\n');
            if (lines.Length == 0 || lines[0].Trim() != Header) return; // unknown format: ignored, overwritten by the next change
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Trim().Split(' ');
                switch (parts[0])
                {
                    case "next" when parts.Length == 2:
                        if (int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int next) && next > _nextTransaction)
                            _nextTransaction = next;
                        break;
                    case "owned" when parts.Length == 2 && IsProductId(parts[1]):
                        _owned.Add(parts[1]);
                        break;
                    case "deferred" when parts.Length == 2 && IsProductId(parts[1]):
                        _deferred.Add(parts[1]);
                        break;
                    case "awaiting" when parts.Length == 3 && parts[1].Length > 0 && IsProductId(parts[2]):
                        _awaiting[parts[1]] = Unconfirmed(parts[2], parts[1]);
                        break;
                }
            }
        }

        void SaveAccount()
        {
            if (_persistence == null) return;
            try
            {
                _persistence.SetString(AccountKey, SerializeAccount());
                _persistence.Save();
            }
            catch (Exception)
            {
                // A fake store that cannot persist keeps working in memory for this session.
            }
        }

        static bool IsProductId(string id)
        {
            return ProductCatalog.TryGet(id, out _);
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
