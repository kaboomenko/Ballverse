using System;
using System.Collections.Generic;
using Ballverse.Core.Monetization;

namespace Ballverse.Services
{
    /// <summary>
    /// <see cref="IPurchaseService"/> over an <see cref="IStoreBackend"/>. Policy (spec §5.4, §5.5):
    /// <list type="bullet">
    /// <item>start: rights from the signed cache (<see cref="EntitlementStore"/>), then connect; a store that does not answer
    /// within <see cref="ConnectTimeoutMs"/> leaves the service <see cref="PurchaseServiceState.Unavailable"/> on the cache;</item>
    /// <item>a successful purchases read is authoritative: it replaces the owned list (refunds disappear) and refreshes the cache —
    /// except for a purchase granted after that read was requested (the read predates it): it is kept and the purchases are
    /// read once more;</item>
    /// <item>every paid purchase is verified (<see cref="IReceiptValidator"/>) before it grants anything — new, interrupted and
    /// already confirmed ones alike (launch, reinstall, restore); a new one is granted, then confirmed ("grant then confirm",
    /// so a crash in between re-delivers it at the next start); an unverified one is neither granted nor confirmed;</item>
    /// <item>rights are Core <c>Entitlements.From(owned, config)</c> (NO_WATERMARK by Core's permanent rule: Ultimate, or
    /// both ad rights from any products); nothing about them is stored besides the owned product ids;</item>
    /// <item>the release catalog is a Core <see cref="MonetizationConfig"/> (<see cref="MonetizationConfig.Release1"/>, with
    /// Remote Config <c>free_modes</c> / <c>modes_disabled</c> applied through <see cref="SetConfig"/>); it decides what is
    /// sold and offered, never which rights owned products give;</item>
    /// <item>deferred purchases grant nothing and are listed in <see cref="PendingProductIds"/>;</item>
    /// <item>a purchases read that gets no answer within <see cref="FetchTimeoutMs"/> counts as failed, so later reads are not
    /// blocked, and an automatic one is retried once (Unity IAP on Google Play can hand the answer of an app read to its own
    /// read on focus gain, which never reports back);</item>
    /// <item>a restore is decided by its own read, issued after the store's restore call finished. Reads are answered in
    /// request order, so answers and failures of reads that were still live when it was issued belong to those earlier reads
    /// and do not end it; it fails with <c>fetch_timeout</c> when its own read goes unanswered — unless an earlier read was
    /// answered after it was issued (Google Play may answer concurrent reads once), in which case that answer decides.</item>
    /// </list>
    /// </summary>
    public sealed class PurchaseService : IPurchaseService, IStoreBackendListener, ITickable
    {
        public const long ConnectTimeoutMs = 15_000;

        /// <summary>A purchases read without an answer after this long is treated as failed.</summary>
        public const long FetchTimeoutMs = 20_000;

        /// <summary>
        /// Safety bound on reads issued back to back by the stale-read protection (each one answered without a kept grant).
        /// Reads that timed out stay counted as unanswered, so later answers count as older reads than they are — safe (grants
        /// settle later, never earlier) but a refund of a recent grant may then need a few cheap re-reads to be applied.
        /// </summary>
        const int MaxConsecutiveRereads = 20;

        const string Tag = "Purchases";

        sealed class ActivePurchase
        {
            public string ProductId;
            public string Source;
            public Action<PurchaseOutcome> Done;
        }

        readonly IStoreBackend _backend;
        readonly EntitlementStore _cache;
        readonly IReceiptValidator _validator;
        readonly IClock _clock;
        readonly IAnalyticsService _analytics;
        readonly IServiceLog _log;

        SortedSet<string> _owned = new SortedSet<string>(StringComparer.Ordinal);
        readonly SortedSet<string> _pending = new SortedSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> _confirming = new HashSet<string>(StringComparer.Ordinal);
        // Product → number of purchases reads requested when it was granted by OnPurchasePending (stale-read protection).
        readonly Dictionary<string, int> _grantedAtFetch = new Dictionary<string, int>(StringComparer.Ordinal);
        int _fetchesIssued;
        int _fetchesAnswered;
        int _consecutiveRereads;
        MonetizationConfig _config;
        IReadOnlyList<StoreProduct> _fetchedProducts = Array.Empty<StoreProduct>();
        List<StoreProduct> _products = new List<StoreProduct>();
        string[] _ownedView = Array.Empty<string>();
        string[] _pendingView = Array.Empty<string>();
        ActivePurchase _active;
        Action<RestoreOutcome> _restoreWaiters;
        bool _restoring;
        // The restore's own purchases read has been issued (after the store's restore call finished).
        bool _restoreReadIssued;
        // Earlier reads still live when the restore's read was issued: the next answers or failures are theirs.
        int _restoreEarlierReads;
        // An earlier read was answered after the restore's read was issued (used when the restore's read itself times out).
        bool _restoreEarlierAnswered;
        int _restoreGeneration;
        string _restoreError;
        // Reads requested and not answered yet, minus those written off by a timeout; > 0 means a read is in flight.
        int _liveReads;
        bool _fetchIsRetry;
        long _fetchDeadlineMs;
        long _connectStartedMs;

        /// <param name="config">
        /// Release catalog (required, as everywhere in Core Monetization): <see cref="MonetizationConfig.Release1"/>, or
        /// <c>Release1.WithFreeModes(...)</c> after Remote Config (<see cref="MonetizationConfigMapper"/>).
        /// </param>
        public PurchaseService(IStoreBackend backend, EntitlementStore cache, IReceiptValidator validator, MonetizationConfig config,
            IClock clock, IAnalyticsService analytics, IServiceLog log)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _validator = validator ?? AcceptAllReceiptValidator.Instance;
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _analytics = analytics ?? throw new ArgumentNullException(nameof(analytics));
            _log = log ?? NullServiceLog.Instance;

            foreach (string id in _cache.Load()) _owned.Add(id);
            RefreshViews();
            Entitlements = ComputeEntitlements();
            _backend.Listener = this;
        }

        public PurchaseServiceState State { get; private set; }

        public IReadOnlyList<StoreProduct> Products => _products;

        public IReadOnlyList<string> OwnedProductIds => _ownedView;

        public IReadOnlyList<string> PendingProductIds => _pendingView;

        public EntitlementSet Entitlements { get; private set; }

        public MonetizationConfig Config => _config;

        public bool IsPurchaseInProgress => _active != null;

        public long? LastStoreReadUnixMs => _cache.SavedAtUnixMs;

        public event Action<EntitlementSet> EntitlementsChanged;

        public event Action ProductsChanged;

        public event Action<PurchaseServiceState> StateChanged;

        public event Action<MonetizationConfig> ConfigChanged;

        /// <summary>
        /// Replaces the release catalog (Remote Config <c>free_modes</c> / <c>modes_disabled</c> changed, see
        /// <see cref="MonetizationConfigMapper"/>). The shown products are re-filtered from the last store answer; rights do not
        /// change (Core computes them from the owned products alone). A product that becomes sellable after the store connected
        /// is listed once the store has returned it, at the latest after the next connect. Raises <see cref="ConfigChanged"/>.
        /// </summary>
        public void SetConfig(MonetizationConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (MonetizationConfigMapper.SameCatalog(config, _config)) return;
            _config = config;
            var products = FilterSellable(_fetchedProducts);
            bool productsChanged = !SameIds(products, _products);
            _products = products;
            ConfigChanged?.Invoke(config);
            if (productsChanged) ProductsChanged?.Invoke();
        }

        public bool TryGetProduct(string productId, out StoreProduct product)
        {
            foreach (var p in _products)
            {
                if (p.Id == productId)
                {
                    product = p;
                    return true;
                }
            }
            product = null;
            return false;
        }

        public void Initialize()
        {
            if (State == PurchaseServiceState.Initializing || State == PurchaseServiceState.Ready) return;
            if (!_backend.IsAvailable)
            {
                SetState(PurchaseServiceState.Unavailable);
                _log.Info(Tag, "No store in this build; rights come from the cache.");
                return;
            }
            SetState(PurchaseServiceState.Initializing);
            _connectStartedMs = _clock.MonotonicMs;
            try
            {
                _backend.Connect(_config.SellableProductIds);
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Store connect threw.", e);
                SetState(PurchaseServiceState.Unavailable);
            }
        }

        public void Tick()
        {
            if (State == PurchaseServiceState.Initializing && _clock.MonotonicMs - _connectStartedMs >= ConnectTimeoutMs)
            {
                _log.Warning(Tag, "Store did not connect in time; using cached rights.");
                SetState(PurchaseServiceState.Unavailable);
            }
            if (_liveReads > 0 && _clock.MonotonicMs >= _fetchDeadlineMs) OnFetchTimedOut();
        }

        /// <summary>True while a purchases read is waiting for its answer.</summary>
        public bool IsFetchingPurchases => _liveReads > 0;

        public void Purchase(string productId, string source, Action<PurchaseOutcome> done)
        {
            if (!_config.IsSellable(productId))
            {
                done?.Invoke(new PurchaseOutcome(productId, PurchaseResultKind.NotAvailable, "not_sold"));
                return;
            }
            if (IsRedundantPurchase(productId))
            {
                done?.Invoke(new PurchaseOutcome(productId, PurchaseResultKind.AlreadyOwned, null));
                return;
            }
            if (_active != null)
            {
                done?.Invoke(new PurchaseOutcome(productId, PurchaseResultKind.Failed, "purchase_in_progress"));
                return;
            }
            if (State != PurchaseServiceState.Ready)
            {
                LogFail(productId, source, "store_unavailable");
                done?.Invoke(new PurchaseOutcome(productId, PurchaseResultKind.NotAvailable, "store_unavailable"));
                return;
            }
            _active = new ActivePurchase { ProductId = productId, Source = source ?? string.Empty, Done = done };
            _analytics.LogEvent(AnalyticsEvents.PurchaseStart,
                AnalyticsParam.Str(AnalyticsParams.ProductId, productId),
                AnalyticsParam.Str(AnalyticsParams.Source, _active.Source));
            try
            {
                _backend.Purchase(productId);
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Store purchase threw.", e);
                OnPurchaseFailed(productId, "Unknown");
            }
        }

        public void Restore(Action<RestoreOutcome> done)
        {
            if (State != PurchaseServiceState.Ready)
            {
                LogRestore(false, "store_unavailable");
                done?.Invoke(new RestoreOutcome(false, _owned.Count, "store_unavailable"));
                if (State == PurchaseServiceState.Unavailable) RetryConnect();
                return;
            }
            _restoreWaiters += done;
            if (_restoring) return;
            _restoring = true;
            _restoreReadIssued = false;
            _restoreError = null;
            int generation = ++_restoreGeneration;
            try
            {
                _backend.Restore((ok, error) =>
                {
                    if (generation != _restoreGeneration || !_restoring)
                    {
                        // A restore that already ended (store disconnected): its late answer only refreshes the purchases.
                        if (State == PurchaseServiceState.Ready) FetchPurchasesNow(force: false);
                        return;
                    }
                    if (!ok) _restoreError = string.IsNullOrEmpty(error) ? "restore_failed" : ErrorCodes.FromStoreReason(error);
                    IssueRestoreRead();
                });
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Store restore threw.", e);
                if (generation == _restoreGeneration && _restoring && !_restoreReadIssued)
                {
                    _restoreError = "restore_failed";
                    IssueRestoreRead();
                }
            }
        }

        /// <summary>The restore's own purchases read; only its answer, failure or timeout ends the restore.</summary>
        void IssueRestoreRead()
        {
            _restoreReadIssued = true;
            _restoreEarlierReads = _liveReads;
            _restoreEarlierAnswered = false;
            FetchPurchasesNow(force: true, isRestoreRead: true);
        }

        /// <summary>
        /// A restore is waiting for the read it issued. Answers, failures and timeouts that come while the store's restore call
        /// is still running belong to earlier reads and do not end the restore.
        /// </summary>
        bool RestoreAwaitsRead => _restoring && _restoreReadIssued;

        /// <summary>
        /// Whether a read answer or failure that just arrived is the restore's own read. Reads are answered in request order, so
        /// while reads issued before it are outstanding, the answer belongs to the oldest of them.
        /// </summary>
        bool IsRestoreReadResponse(bool answered)
        {
            if (!RestoreAwaitsRead) return false;
            if (_restoreEarlierReads > 0)
            {
                _restoreEarlierReads--;
                if (answered) _restoreEarlierAnswered = true;
                return false;
            }
            return true;
        }

        public void RefreshPurchases()
        {
            if (State == PurchaseServiceState.Ready) FetchPurchasesNow(force: false);
            else if (State == PurchaseServiceState.Unavailable) RetryConnect();
        }

        void RetryConnect()
        {
            if (!_backend.IsAvailable) return;
            SetState(PurchaseServiceState.NotInitialized);
            Initialize();
        }

        // ---- IStoreBackendListener ----

        public void OnConnected()
        {
            SetState(PurchaseServiceState.Ready);
            _log.Info(Tag, "Store connected.");
        }

        public void OnConnectFailed(string reason)
        {
            _log.Warning(Tag, "Store connect failed: " + reason);
            SetState(PurchaseServiceState.Unavailable);
        }

        public void OnDisconnected(string reason)
        {
            _log.Warning(Tag, "Store disconnected: " + reason);
            SetState(PurchaseServiceState.Unavailable);
            if (_active != null) CompleteActive(_active.ProductId, PurchaseResultKind.Failed, "store_disconnected");
            if (_restoring) CompleteRestore(false, "store_disconnected");
            _liveReads = 0;
            _fetchesAnswered = _fetchesIssued; // outstanding reads are not answered after a disconnect
        }

        public void OnProductsFetched(IReadOnlyList<StoreProduct> products)
        {
            _fetchedProducts = products ?? (IReadOnlyList<StoreProduct>)Array.Empty<StoreProduct>();
            _products = FilterSellable(_fetchedProducts);
            ProductsChanged?.Invoke();
            FetchPurchasesNow(force: false);
        }

        /// <summary>The store's products that the current config sells, in its <see cref="MonetizationConfig.SellableProductIds"/> order.</summary>
        List<StoreProduct> FilterSellable(IReadOnlyList<StoreProduct> products)
        {
            var list = new List<StoreProduct>();
            foreach (string id in _config.SellableProductIds)
            {
                foreach (var p in products)
                {
                    if (p != null && p.Id == id)
                    {
                        list.Add(p);
                        break;
                    }
                }
            }
            return list;
        }

        static bool SameIds(List<StoreProduct> a, List<StoreProduct> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if (!ReferenceEquals(a[i], b[i])) return false;
            }
            return true;
        }

        public void OnProductsFetchFailed(string reason)
        {
            _log.Warning(Tag, "Products fetch failed: " + reason);
            FetchPurchasesNow(force: false);
        }

        public void OnPurchasesFetched(IReadOnlyList<StorePurchase> purchases)
        {
            if (_liveReads > 0) _liveReads--;
            // Reads are answered in request order; this answer belongs to read number `answered`.
            int answered = _fetchesAnswered < _fetchesIssued ? ++_fetchesAnswered : _fetchesIssued;
            _confirming.Clear();
            var owned = new SortedSet<string>(StringComparer.Ordinal);
            var pending = new SortedSet<string>(StringComparer.Ordinal);
            var toConfirm = new List<StorePurchase>();
            foreach (var p in purchases)
            {
                if (p == null) continue;
                switch (p.State)
                {
                    case StorePurchaseState.Confirmed:
                        // Spec §5.5: verified on every read, not only when new (restore, reinstall, every launch).
                        if (_validator.Validate(p, out string confirmedError)) owned.Add(p.ProductId);
                        else _log.Warning(Tag, "Unverified purchase of " + p.ProductId + " ignored (" + confirmedError + ").");
                        break;
                    case StorePurchaseState.AwaitingConfirmation:
                        if (_validator.Validate(p, out string error))
                        {
                            owned.Add(p.ProductId);
                            toConfirm.Add(p);
                        }
                        else
                        {
                            _log.Warning(Tag, "Unverified pending purchase of " + p.ProductId + " ignored (" + error + ").");
                        }
                        break;
                    case StorePurchaseState.Deferred:
                        pending.Add(p.ProductId);
                        break;
                }
            }
            bool reread = KeepGrantsNewerThan(answered, owned);
            pending.ExceptWith(owned);
            _pending.Clear();
            _pending.UnionWith(pending);
            ReplaceOwned(owned);
            foreach (var p in toConfirm) Confirm(p);
            if (IsRestoreReadResponse(answered: true)) CompleteRestore(_restoreError == null, _restoreError);
            if (!reread)
            {
                _consecutiveRereads = 0;
            }
            else if (_consecutiveRereads < MaxConsecutiveRereads)
            {
                _consecutiveRereads++;
                FetchPurchasesNow(force: false);
            }
            else
            {
                _log.Warning(Tag, "Purchases re-read limit reached; recent purchases stay granted until the next read.");
            }
        }

        /// <summary>
        /// A read requested before a purchase was granted may be answered after it, without it. Such grants stay owned (the
        /// caller then reads again); a read requested after the grant is authoritative and settles it.
        /// </summary>
        bool KeepGrantsNewerThan(int answeredRead, SortedSet<string> owned)
        {
            if (_grantedAtFetch.Count == 0) return false;
            bool reread = false;
            List<string> settled = null;
            foreach (var grant in _grantedAtFetch)
            {
                if (answeredRead > grant.Value)
                {
                    (settled ?? (settled = new List<string>())).Add(grant.Key);
                }
                else if (!owned.Contains(grant.Key))
                {
                    owned.Add(grant.Key);
                    reread = true;
                    _log.Info(Tag, "Purchases read predates the purchase of " + grant.Key + "; kept, reading again.");
                }
            }
            if (settled != null)
            {
                foreach (string id in settled) _grantedAtFetch.Remove(id);
            }
            return reread;
        }

        public void OnPurchasesFetchFailed(string reason)
        {
            ReadFailed(reason, restoreReadThrew: false);
        }

        void ReadFailed(string reason, bool restoreReadThrew)
        {
            if (_liveReads > 0) _liveReads--;
            if (_fetchesAnswered < _fetchesIssued) _fetchesAnswered++;
            _log.Warning(Tag, "Purchases fetch failed (" + reason + "); keeping cached rights.");
            // A restore read that threw failed synchronously: it is the restore's own read whatever else is live.
            if (restoreReadThrew ? RestoreAwaitsRead : IsRestoreReadResponse(answered: false)) CompleteRestore(false, "fetch_failed");
        }

        public void OnPurchasePending(StorePurchase purchase)
        {
            if (purchase == null) return;
            if (!_validator.Validate(purchase, out string error))
            {
                LogFail(purchase.ProductId, ActiveSourceFor(purchase.ProductId), error ?? "validation_failed");
                if (IsActive(purchase.ProductId)) CompleteActive(purchase.ProductId, PurchaseResultKind.Failed, error ?? "validation_failed", logFail: false);
                return;
            }
            bool changed = _owned.Add(purchase.ProductId);
            bool wasPending = _pending.Remove(purchase.ProductId);
            _grantedAtFetch[purchase.ProductId] = _fetchesIssued;
            if (changed || wasPending) CommitOwned();
            Confirm(purchase);
            if (IsActive(purchase.ProductId))
            {
                _analytics.LogEvent(AnalyticsEvents.PurchaseSuccess,
                    AnalyticsParam.Str(AnalyticsParams.ProductId, purchase.ProductId),
                    AnalyticsParam.Str(AnalyticsParams.Source, _active.Source));
                CompleteActive(purchase.ProductId, PurchaseResultKind.Purchased, null);
            }
            else if (changed)
            {
                // Approved deferred purchase or one interrupted by a crash (spec §5.4).
                _analytics.LogEvent(AnalyticsEvents.PurchaseSuccess,
                    AnalyticsParam.Str(AnalyticsParams.ProductId, purchase.ProductId),
                    AnalyticsParam.Str(AnalyticsParams.Source, "deferred"));
            }
        }

        public void OnPurchaseDeferred(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return;
            if (_pending.Add(productId)) RefreshViews();
            _analytics.LogEvent(AnalyticsEvents.PurchasePending,
                AnalyticsParam.Str(AnalyticsParams.ProductId, productId),
                AnalyticsParam.Str(AnalyticsParams.Source, ActiveSourceFor(productId)));
            if (IsActive(productId)) CompleteActive(productId, PurchaseResultKind.Pending, null);
        }

        public void OnPurchaseFailed(string productId, string reasonCode)
        {
            string code = ErrorCodes.FromStoreReason(reasonCode);
            string target = string.IsNullOrEmpty(productId) && _active != null ? _active.ProductId : productId;
            PurchaseResultKind kind;
            switch (reasonCode)
            {
                case "UserCancelled": kind = PurchaseResultKind.Cancelled; break;
                case "ExistingPurchasePending": kind = PurchaseResultKind.Pending; break;
                default: kind = PurchaseResultKind.Failed; break;
            }
            if (reasonCode == "DuplicateTransaction" || reasonCode == "ExistingPurchasePending") FetchPurchasesNow(force: false);
            if (IsActive(target)) CompleteActive(target, kind, code);
            else LogFail(target, ActiveSourceFor(target), code);
        }

        public void OnPurchaseConfirmed(string productId)
        {
            _log.Info(Tag, "Confirmed " + productId + ".");
        }

        public void OnPurchaseConfirmFailed(string productId, string reasonCode)
        {
            // The right stays granted; the next purchases read re-delivers the transaction and confirms it again.
            _log.Warning(Tag, "Confirm of " + productId + " failed: " + reasonCode);
        }

        public void OnEntitlementRevoked(string productId)
        {
            if (string.IsNullOrEmpty(productId)) return;
            _grantedAtFetch.Remove(productId);
            if (_owned.Remove(productId))
            {
                _log.Info(Tag, "Revoked " + productId + " (refund).");
                CommitOwned();
            }
        }

        // ---- internals ----

        void FetchPurchasesNow(bool force, bool isRetry = false, bool isRestoreRead = false)
        {
            if (_liveReads > 0 && !force) return;
            _liveReads++;
            _fetchIsRetry = isRetry;
            _fetchDeadlineMs = _clock.MonotonicMs + FetchTimeoutMs;
            _fetchesIssued++;
            try
            {
                _backend.FetchPurchases();
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Store purchases fetch threw.", e);
                ReadFailed("exception", restoreReadThrew: isRestoreRead);
            }
        }

        void OnFetchTimedOut()
        {
            _liveReads = 0;
            // _fetchesAnswered is not advanced: a late answer then counts as this read; if none ever comes, later answers count
            // as older reads than they are, which only delays settling grants (KeepGrantsNewerThan stays conservative).
            // The deadline is the latest read's: once the restore issued its read, that is the restore's own read. An earlier
            // read that times out while the store's restore call is still running is an ordinary read (retried below).
            if (RestoreAwaitsRead)
            {
                if (_restoreEarlierAnswered)
                {
                    // Google Play can answer concurrent reads once: the answer taken as an earlier read's was the latest state.
                    _log.Warning(Tag, "Purchases read for restore got no own answer in time; using the answer received after it was issued.");
                    CompleteRestore(_restoreError == null, _restoreError);
                    return;
                }
                _log.Warning(Tag, "Purchases read for restore got no answer in time.");
                CompleteRestore(false, "fetch_timeout");
                return;
            }
            if (_fetchIsRetry || State != PurchaseServiceState.Ready)
            {
                _log.Warning(Tag, "Purchases read got no answer in time again; keeping current rights until the next read.");
                return;
            }
            _log.Warning(Tag, "Purchases read got no answer in time; reading again.");
            FetchPurchasesNow(force: false, isRetry: true);
        }

        void Confirm(StorePurchase purchase)
        {
            string key = string.IsNullOrEmpty(purchase.TransactionId) ? "product:" + purchase.ProductId : purchase.TransactionId;
            if (!_confirming.Add(key)) return;
            try
            {
                _backend.Confirm(purchase);
            }
            catch (Exception e)
            {
                _log.Error(Tag, "Store confirm threw.", e);
            }
        }

        void ReplaceOwned(SortedSet<string> owned)
        {
            _owned = owned;
            CommitOwned();
        }

        void CommitOwned()
        {
            _cache.Save(_owned);
            RefreshViews();
            var updated = ComputeEntitlements();
            if (updated == Entitlements) return;
            Entitlements = updated;
            EntitlementsChanged?.Invoke(updated);
        }

        EntitlementSet ComputeEntitlements()
        {
            return Ballverse.Core.Monetization.Entitlements.From(_owned, _config);
        }

        /// <summary>True when the current rights already contain everything <paramref name="productId"/> grants.</summary>
        bool IsRedundantPurchase(string productId)
        {
            return ProductCatalog.TryGet(productId, out var product) && Entitlements.IsSupersetOf(product.Grants);
        }

        void RefreshViews()
        {
            var owned = new string[_owned.Count];
            _owned.CopyTo(owned);
            _ownedView = owned;
            var pending = new string[_pending.Count];
            _pending.CopyTo(pending);
            _pendingView = pending;
        }

        bool IsActive(string productId)
        {
            return _active != null && _active.ProductId == productId;
        }

        string ActiveSourceFor(string productId)
        {
            return IsActive(productId) ? _active.Source : string.Empty;
        }

        void CompleteActive(string productId, PurchaseResultKind kind, string errorCode, bool logFail = true)
        {
            var active = _active;
            _active = null;
            if (logFail && (kind == PurchaseResultKind.Failed || kind == PurchaseResultKind.Cancelled))
                LogFail(productId, active != null ? active.Source : string.Empty, errorCode ?? "unknown");
            active?.Done?.Invoke(new PurchaseOutcome(productId, kind, errorCode));
        }

        void CompleteRestore(bool success, string errorCode)
        {
            _restoring = false;
            _restoreReadIssued = false;
            _restoreEarlierReads = 0;
            _restoreEarlierAnswered = false;
            var waiters = _restoreWaiters;
            _restoreWaiters = null;
            LogRestore(success, errorCode);
            waiters?.Invoke(new RestoreOutcome(success, _owned.Count, errorCode));
        }

        void LogFail(string productId, string source, string errorCode)
        {
            _analytics.LogEvent(AnalyticsEvents.PurchaseFail,
                AnalyticsParam.Str(AnalyticsParams.ProductId, productId ?? string.Empty),
                AnalyticsParam.Str(AnalyticsParams.Source, source ?? string.Empty),
                AnalyticsParam.Str(AnalyticsParams.ErrorCode, errorCode ?? "unknown"));
        }

        void LogRestore(bool success, string errorCode)
        {
            var parameters = new List<AnalyticsParam>
            {
                AnalyticsParam.Str(AnalyticsParams.Result, success ? "ok" : "fail"),
                AnalyticsParam.Int(AnalyticsParams.RestoredCount, _owned.Count),
            };
            if (errorCode != null) parameters.Add(AnalyticsParam.Str(AnalyticsParams.ErrorCode, errorCode));
            _analytics.LogEvent(AnalyticsEvents.RestorePurchases, parameters.ToArray());
        }

        void SetState(PurchaseServiceState state)
        {
            if (State == state) return;
            State = state;
            StateChanged?.Invoke(state);
        }
    }

    /// <summary>Analytics-safe error codes for store failure reasons.</summary>
    public static class ErrorCodes
    {
        /// <summary>
        /// Unity IAP <c>PurchaseFailureReason</c> names (PascalCase) to snake_case, e.g. <c>PaymentDeclined</c> →
        /// <c>payment_declined</c>; anything else that is not a plain identifier becomes <c>store_error</c>.
        /// </summary>
        public static string FromStoreReason(string reason)
        {
            if (string.IsNullOrEmpty(reason)) return "unknown";
            var sb = new System.Text.StringBuilder(reason.Length + 8);
            for (int i = 0; i < reason.Length; i++)
            {
                char c = reason[i];
                if (c >= 'A' && c <= 'Z')
                {
                    if (i > 0) sb.Append('_');
                    sb.Append((char)(c + 32));
                }
                else if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')
                {
                    sb.Append(c);
                }
                else
                {
                    return "store_error";
                }
                if (sb.Length > 40) return "store_error";
            }
            return sb.ToString();
        }
    }
}
