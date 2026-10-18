using System.Collections.Generic;
using Ballverse.Core.Monetization;
using NUnit.Framework;

namespace Ballverse.Services.Tests
{
    public sealed class PurchaseServiceTests
    {
        ManualClock _clock;
        MemoryKeyValueStore _store;
        RecordingAnalyticsService _analytics;
        MemoryServiceLog _log;

        [SetUp]
        public void SetUp()
        {
            _clock = new ManualClock();
            _store = new MemoryKeyValueStore();
            _analytics = new RecordingAnalyticsService();
            _analytics.SetCollectionEnabled(true);
            _log = new MemoryServiceLog();
        }

        PurchaseService Create(IStoreBackend backend, IReceiptValidator validator = null, MonetizationConfig config = null)
        {
            var cache = new EntitlementStore(_store, new PrefsDeviceKeyStore(_store), _clock, _log);
            return new PurchaseService(backend, cache, validator ?? new FakeReceiptValidator(), config, _clock, _analytics, _log);
        }

        /// <summary>A store whose purchases reads are answered by the test (delays, reordering) and whose orders are scripted.</summary>
        sealed class ScriptedStoreBackend : IStoreBackend
        {
            IStoreBackendListener _listener;

            public int FetchRequests;
            public int ConfirmCalls;

            public bool IsAvailable => true;

            public IStoreBackendListener Listener { set => _listener = value; }

            public void Connect(IReadOnlyList<string> productIds)
            {
                _listener.OnConnected();
                var products = new List<StoreProduct>();
                foreach (string id in productIds) products.Add(new StoreProduct(id, "$1", id, id, "USD", 1m, true));
                _listener.OnProductsFetched(products);
            }

            public void FetchPurchases()
            {
                FetchRequests++;
            }

            public void Answer(params StorePurchase[] purchases)
            {
                _listener.OnPurchasesFetched(purchases);
            }

            public void DeliverPending(string productId)
            {
                _listener.OnPurchasePending(new StorePurchase(productId, "tx-" + productId, StorePurchaseState.AwaitingConfirmation, "{}", null));
            }

            public void Purchase(string productId)
            {
            }

            public void Confirm(StorePurchase purchase)
            {
                ConfirmCalls++;
            }

            public void Restore(System.Action<bool, string> done)
            {
                done?.Invoke(true, null);
            }

            public static StorePurchase Confirmed(string productId)
            {
                return new StorePurchase(productId, "tx-" + productId, StorePurchaseState.Confirmed, "{\"Payload\":\"signed\"}", null);
            }
        }

        [Test]
        public void ConnectsFetchesRelease1ProductsAndPurchases()
        {
            var config = new FakeStoreConfig();
            config.InitiallyOwned.Add(ProductIds.RemoveExportAds);
            var backend = new FakeStoreBackend(config);
            var service = Create(backend);
            Assert.AreEqual(PurchaseServiceState.NotInitialized, service.State);
            Assert.IsTrue(service.Entitlements.IsEmpty);

            service.Initialize();

            Assert.AreEqual(PurchaseServiceState.Ready, service.State);
            Assert.AreEqual(3, service.Products.Count);
            Assert.AreEqual(ProductIds.RemoveExportAds, service.Products[0].Id);
            Assert.AreEqual("$6.99", service.Products[0].LocalizedPrice);
            Assert.AreEqual("$8.99", service.Products[2].LocalizedPrice);
            CollectionAssert.AreEqual(new[] { ProductIds.RemoveExportAds }, service.OwnedProductIds);
            Assert.IsTrue(service.Entitlements.Contains(Entitlement.NoExportAds));
            Assert.IsNotNull(service.LastStoreReadUnixMs);
        }

        [Test]
        public void PurchaseGrantsThenConfirmsAndCachesForTheNextLaunch()
        {
            var backend = new FakeStoreBackend();
            var service = Create(backend);
            service.Initialize();
            var changes = new List<EntitlementSet>();
            service.EntitlementsChanged += e => changes.Add(e);

            PurchaseOutcome? outcome = null;
            service.Purchase(ProductIds.NoAds, "shop", o => outcome = o);

            Assert.AreEqual(PurchaseResultKind.Purchased, outcome.Value.Kind);
            Assert.AreEqual(1, backend.ConfirmCalls);
            Assert.IsTrue(service.Entitlements.Contains(Entitlement.NoWatermark));
            Assert.AreEqual(1, changes.Count);
            Assert.AreEqual(1, _analytics.EventsNamed(AnalyticsEvents.PurchaseStart).Count);
            Assert.AreEqual(1, _analytics.EventsNamed(AnalyticsEvents.PurchaseSuccess).Count);

            // Next launch, store unreachable: rights come from the signed cache (spec §5.4).
            var offline = new FakeStoreBackend(new FakeStoreConfig { ConnectSucceeds = false });
            var next = Create(offline);
            Assert.IsTrue(next.Entitlements.Contains(Entitlement.NoExportAds));
            next.Initialize();
            Assert.AreEqual(PurchaseServiceState.Unavailable, next.State);
            Assert.IsTrue(next.Entitlements.Contains(Entitlement.NoInterstitials));
        }

        [Test]
        public void TamperedCacheIsIgnored()
        {
            var backend = new FakeStoreBackend();
            var service = Create(backend);
            service.Initialize();
            service.Purchase(ProductIds.RemoveInterstitialAds, "shop", _ => { });

            string json = _store.GetString(EntitlementStore.CacheKey);
            _store.SetString(EntitlementStore.CacheKey, json.Replace("remove_interstitial", "remove_export"));
            var next = Create(new FakeStoreBackend(new FakeStoreConfig { ConnectSucceeds = false }));
            Assert.IsTrue(next.Entitlements.IsEmpty);
            Assert.Greater(_log.WarningCount, 0);
        }

        [Test]
        public void CancelAndDeclineGrantNothing()
        {
            var config = new FakeStoreConfig { Behaviour = FakePurchaseBehaviour.Cancel };
            var backend = new FakeStoreBackend(config);
            var service = Create(backend);
            service.Initialize();

            PurchaseOutcome? outcome = null;
            service.Purchase(ProductIds.RemoveExportAds, "rewarded_gate", o => outcome = o);
            Assert.AreEqual(PurchaseResultKind.Cancelled, outcome.Value.Kind);

            config.Behaviour = FakePurchaseBehaviour.Decline;
            service.Purchase(ProductIds.RemoveExportAds, "rewarded_gate", o => outcome = o);
            Assert.AreEqual(PurchaseResultKind.Failed, outcome.Value.Kind);
            Assert.AreEqual("payment_declined", outcome.Value.ErrorCode);
            Assert.IsTrue(service.Entitlements.IsEmpty);
            Assert.AreEqual(2, _analytics.EventsNamed(AnalyticsEvents.PurchaseFail).Count);
            Assert.IsFalse(service.IsPurchaseInProgress);
        }

        [Test]
        public void DeferredPurchaseGrantsOnlyAfterApproval()
        {
            var config = new FakeStoreConfig { Behaviour = FakePurchaseBehaviour.Defer };
            var backend = new FakeStoreBackend(config);
            var service = Create(backend);
            service.Initialize();

            PurchaseOutcome? outcome = null;
            service.Purchase(ProductIds.RemoveInterstitialAds, "shop", o => outcome = o);
            Assert.AreEqual(PurchaseResultKind.Pending, outcome.Value.Kind);
            CollectionAssert.AreEqual(new[] { ProductIds.RemoveInterstitialAds }, service.PendingProductIds);
            Assert.IsTrue(service.Entitlements.IsEmpty);
            Assert.AreEqual(1, _analytics.EventsNamed(AnalyticsEvents.PurchasePending).Count);

            // Still pending on the next read (spec §5.4: re-checked at every start and resume).
            service.RefreshPurchases();
            Assert.AreEqual(1, service.PendingProductIds.Count);

            backend.ApproveDeferred(ProductIds.RemoveInterstitialAds);
            Assert.IsTrue(service.Entitlements.Contains(Entitlement.NoInterstitials));
            Assert.AreEqual(0, service.PendingProductIds.Count);
            Assert.AreEqual(1, backend.ConfirmCalls);
        }

        [Test]
        public void UnconfirmedPurchaseFromACrashIsGrantedAndConfirmedAtStart()
        {
            var backend = new FakeStoreBackend();
            backend.AddUnconfirmed(ProductIds.NoAds);
            var service = Create(backend);
            service.Initialize();
            Assert.IsTrue(service.Entitlements.Contains(Entitlement.NoExportAds));
            Assert.AreEqual(1, backend.ConfirmCalls);
            CollectionAssert.Contains(backend.StoreOwned, ProductIds.NoAds);
        }

        [Test]
        public void UnverifiedPurchaseIsNeitherGrantedNorConfirmed()
        {
            var backend = new FakeStoreBackend();
            var validator = new FakeReceiptValidator { Accept = false };
            var service = Create(backend, validator);
            service.Initialize();

            PurchaseOutcome? outcome = null;
            service.Purchase(ProductIds.NoAds, "shop", o => outcome = o);
            Assert.AreEqual(PurchaseResultKind.Failed, outcome.Value.Kind);
            Assert.AreEqual("validation_failed", outcome.Value.ErrorCode);
            Assert.IsTrue(service.Entitlements.IsEmpty);
            Assert.AreEqual(0, backend.ConfirmCalls);
        }

        [Test]
        public void AlreadyConfirmedPurchasesAreVerifiedOnEveryRead()
        {
            // Spec §5.5: restore, reinstall and every launch read acknowledged purchases; they must pass the signature check too.
            var backend = new ScriptedStoreBackend();
            var validator = new FakeReceiptValidator { Accept = false };
            var service = Create(backend, validator);
            service.Initialize();
            backend.Answer(ScriptedStoreBackend.Confirmed(ProductIds.NoAds));
            Assert.AreEqual(1, validator.Calls);
            Assert.AreEqual(0, service.OwnedProductIds.Count);
            Assert.IsTrue(service.Entitlements.IsEmpty);
            Assert.Greater(_log.WarningCount, 0);

            validator.Accept = true;
            service.RefreshPurchases();
            backend.Answer(ScriptedStoreBackend.Confirmed(ProductIds.NoAds));
            CollectionAssert.AreEqual(new[] { ProductIds.NoAds }, service.OwnedProductIds);
            Assert.IsTrue(service.Entitlements.Contains(Entitlement.NoWatermark));
            Assert.AreEqual(0, backend.ConfirmCalls, "confirmed orders are not confirmed again");
        }

        [Test]
        public void ReadRequestedBeforeAPurchaseDoesNotDropIt()
        {
            var backend = new ScriptedStoreBackend();
            var service = Create(backend);
            service.Initialize();
            Assert.AreEqual(1, backend.FetchRequests);
            backend.Answer();

            // Resume read requested, then the purchase sheet closes with a paid purchase, then the old read answers.
            service.RefreshPurchases();
            Assert.AreEqual(2, backend.FetchRequests);
            backend.DeliverPending(ProductIds.NoAds);
            Assert.IsTrue(service.Entitlements.Contains(Entitlement.NoExportAds));
            backend.Answer(); // stale: predates the purchase

            Assert.IsTrue(service.Entitlements.Contains(Entitlement.NoExportAds), "right kept");
            Assert.AreEqual(3, backend.FetchRequests, "purchases read again");
            var reloaded = Create(new FakeStoreBackend(new FakeStoreConfig { ConnectSucceeds = false }));
            Assert.IsTrue(reloaded.Entitlements.Contains(Entitlement.NoExportAds), "cache not overwritten without it");

            // The fresh read is authoritative: with the purchase it stays; a later read without it (refund) removes it.
            backend.Answer(ScriptedStoreBackend.Confirmed(ProductIds.NoAds));
            Assert.IsTrue(service.Entitlements.Contains(Entitlement.NoExportAds));
            service.RefreshPurchases();
            backend.Answer();
            Assert.IsTrue(service.Entitlements.IsEmpty);
            Assert.AreEqual(4, backend.FetchRequests);
        }

        [Test]
        public void UnansweredReadTimesOutIsRetriedOnceAndNeverBlocksLaterReads()
        {
            // Unity IAP on Google Play can hand the answer of an app read to its own focus read (single fetch callback).
            var backend = new ScriptedStoreBackend();
            var service = Create(backend);
            service.Initialize();
            backend.Answer();
            service.RefreshPurchases();
            Assert.AreEqual(2, backend.FetchRequests);
            Assert.IsTrue(service.IsFetchingPurchases);

            service.RefreshPurchases(); // still in flight: coalesced
            Assert.AreEqual(2, backend.FetchRequests);
            _clock.Advance(PurchaseService.FetchTimeoutMs - 1);
            service.Tick();
            Assert.AreEqual(2, backend.FetchRequests);

            _clock.Advance(1);
            service.Tick();
            Assert.AreEqual(3, backend.FetchRequests, "retried once");
            _clock.Advance(PurchaseService.FetchTimeoutMs);
            service.Tick();
            Assert.AreEqual(3, backend.FetchRequests, "only one retry");
            Assert.IsFalse(service.IsFetchingPurchases);
            Assert.AreEqual(PurchaseServiceState.Ready, service.State);

            // The next resume reads again and its answer is applied (approved cash / parental purchase).
            for (int i = 0; i < 5; i++)
            {
                service.RefreshPurchases();
                service.Tick();
            }
            Assert.AreEqual(4, backend.FetchRequests);
            backend.Answer(new StorePurchase(ProductIds.NoAds, "tx-1", StorePurchaseState.AwaitingConfirmation, "{}", null));
            Assert.IsTrue(service.Entitlements.Contains(Entitlement.NoExportAds));
            Assert.AreEqual(1, backend.ConfirmCalls, "acknowledged (spec §5.5: within 3 days)");
            service.RefreshPurchases();
            Assert.AreEqual(5, backend.FetchRequests);
        }

        [Test]
        public void RestoreWhoseReadIsNeverAnsweredFails()
        {
            var backend = new ScriptedStoreBackend();
            var service = Create(backend);
            service.Initialize();
            backend.Answer();
            RestoreOutcome? outcome = null;
            service.Restore(o => outcome = o);
            Assert.IsNull(outcome);
            _clock.Advance(PurchaseService.FetchTimeoutMs);
            service.Tick();
            Assert.IsFalse(outcome.Value.Success);
            Assert.AreEqual("fetch_timeout", outcome.Value.ErrorCode);
            Assert.AreEqual(2, backend.FetchRequests, "no automatic retry of a restore");
            service.Restore(o => outcome = o);
            Assert.AreEqual(3, backend.FetchRequests, "the user can restore again");
        }

        [Test]
        public void LateAnswerOfATimedOutReadDoesNotDropANewerPurchase()
        {
            var backend = new ScriptedStoreBackend();
            var service = Create(backend);
            service.Initialize();
            backend.Answer();
            service.RefreshPurchases();                 // read 2
            _clock.Advance(PurchaseService.FetchTimeoutMs);
            service.Tick();                             // read 2 timed out, read 3 issued
            Assert.AreEqual(3, backend.FetchRequests);
            backend.DeliverPending(ProductIds.NoAds);   // paid after reads 2 and 3 were requested

            backend.Answer();                           // read 2 answers late, without it
            Assert.IsTrue(service.Entitlements.Contains(Entitlement.NoExportAds), "kept after the late answer");
            backend.Answer();                           // read 3, also older than the purchase
            Assert.IsTrue(service.Entitlements.Contains(Entitlement.NoExportAds), "kept after the older read");
            Assert.GreaterOrEqual(backend.FetchRequests, 4, "read again");
            backend.Answer(ScriptedStoreBackend.Confirmed(ProductIds.NoAds));
            Assert.IsTrue(service.Entitlements.Contains(Entitlement.NoExportAds));

            // Settled once a read newer than the grant answered: a later refund (read without it) removes the right.
            for (int i = 0; i < 4 && !service.Entitlements.IsEmpty; i++)
            {
                service.RefreshPurchases();
                backend.Answer();
            }
            Assert.IsTrue(service.Entitlements.IsEmpty);
        }

        [Test]
        public void FreshReadAfterAPurchaseIsAuthoritative()
        {
            var backend = new ScriptedStoreBackend();
            var service = Create(backend);
            service.Initialize();
            backend.Answer();
            backend.DeliverPending(ProductIds.RemoveExportAds);
            service.RefreshPurchases(); // requested after the grant
            backend.Answer();           // e.g. refunded already
            Assert.IsTrue(service.Entitlements.IsEmpty);
            Assert.AreEqual(2, backend.FetchRequests, "no extra read");
        }

        [Test]
        public void Release1RulesComeFromCoreMonetizationConfig()
        {
            var service = Create(new FakeStoreBackend());
            Assert.AreSame(MonetizationConfig.Release1, service.Config);
            service.Initialize();
            CollectionAssert.AreEqual(MonetizationConfig.Release1.SellableProductIds, ProductIdsOf(service.Products));

            var config = new FakeStoreConfig();
            config.InitiallyOwned.Add(ProductIds.RemoveExportAds);
            var one = Create(new FakeStoreBackend(config));
            one.Initialize();
            Assert.IsFalse(one.Entitlements.Contains(Entitlement.NoWatermark), "one ad removal keeps the watermark");
            Assert.IsFalse(one.WatermarkGrandfathered);
        }

        [Test]
        public void WatermarkRightSurvivesALaterReleaseThatDropsTheNoAdsRule()
        {
            // docs-release1 §5.0.2: a right bought earlier is never taken away.
            var config = new FakeStoreConfig();
            config.InitiallyOwned.Add(ProductIds.RemoveExportAds);
            config.InitiallyOwned.Add(ProductIds.RemoveInterstitialAds);
            var release1 = Create(new FakeStoreBackend(config));
            release1.Initialize();
            Assert.IsTrue(release1.Entitlements.Contains(Entitlement.NoWatermark));
            Assert.IsTrue(release1.WatermarkGrandfathered);
            Assert.AreEqual("1", _store.GetString(EntitlementStore.WatermarkGrandfatheredKey));

            var later = MonetizationConfig.Release1.WithWatermarkWithNoAds(false);
            var next = Create(new FakeStoreBackend(config), config: later);
            next.Initialize();
            Assert.IsTrue(next.Entitlements.Contains(Entitlement.NoWatermark), "grandfathered");

            // A user who never qualified under the rule gets nothing from the flag.
            _store = new MemoryKeyValueStore();
            var newcomer = Create(new FakeStoreBackend(config), config: later);
            newcomer.Initialize();
            Assert.IsFalse(newcomer.Entitlements.Contains(Entitlement.NoWatermark));
            Assert.IsFalse(newcomer.WatermarkGrandfathered);
        }

        static List<string> ProductIdsOf(IReadOnlyList<StoreProduct> products)
        {
            var ids = new List<string>();
            foreach (var p in products) ids.Add(p.Id);
            return ids;
        }

        [Test]
        public void RefundsRemoveRights()
        {
            var config = new FakeStoreConfig();
            config.InitiallyOwned.Add(ProductIds.NoAds);
            var backend = new FakeStoreBackend(config);
            var service = Create(backend);
            service.Initialize();
            Assert.IsTrue(service.Entitlements.Contains(Entitlement.NoWatermark));

            // Android: the refunded purchase disappears from the next purchases read.
            backend.Refund(ProductIds.NoAds, notify: false);
            service.RefreshPurchases();
            Assert.IsTrue(service.Entitlements.IsEmpty);

            // iOS: StoreKit revocation event.
            var config2 = new FakeStoreConfig();
            config2.InitiallyOwned.Add(ProductIds.RemoveExportAds);
            var backend2 = new FakeStoreBackend(config2);
            var service2 = Create(backend2);
            service2.Initialize();
            backend2.Refund(ProductIds.RemoveExportAds, notify: true);
            Assert.IsTrue(service2.Entitlements.IsEmpty);
        }

        [Test]
        public void RestoreReReadsPurchases()
        {
            var config = new FakeStoreConfig();
            config.InitiallyOwned.Add(ProductIds.RemoveExportAds);
            config.InitiallyOwned.Add(ProductIds.RemoveInterstitialAds);
            var backend = new FakeStoreBackend(config);
            var service = Create(backend);
            service.Initialize();

            RestoreOutcome? outcome = null;
            service.Restore(o => outcome = o);
            Assert.IsTrue(outcome.Value.Success);
            Assert.AreEqual(2, outcome.Value.OwnedCount);
            Assert.AreEqual(1, backend.RestoreCalls);
            Assert.IsTrue(service.Entitlements.Contains(Entitlement.NoWatermark));
            Assert.AreEqual(1, _analytics.EventsNamed(AnalyticsEvents.RestorePurchases).Count);
        }

        [Test]
        public void FailedPurchasesReadKeepsCachedRights()
        {
            var backend = new FakeStoreBackend();
            var service = Create(backend);
            service.Initialize();
            service.Purchase(ProductIds.RemoveExportAds, "shop", _ => { });
            backend.Config.FetchSucceeds = false;
            service.RefreshPurchases();
            Assert.IsTrue(service.Entitlements.Contains(Entitlement.NoExportAds));
        }

        [Test]
        public void ProductsOutsideRelease1AndRedundantPurchasesAreRefused()
        {
            var backend = new FakeStoreBackend();
            var service = Create(backend);
            service.Initialize();

            PurchaseOutcome? outcome = null;
            service.Purchase(ProductIds.Ultimate, "shop", o => outcome = o);
            Assert.AreEqual(PurchaseResultKind.NotAvailable, outcome.Value.Kind);

            service.Purchase(ProductIds.NoAds, "shop", _ => { });
            service.Purchase(ProductIds.RemoveExportAds, "shop", o => outcome = o);
            Assert.AreEqual(PurchaseResultKind.AlreadyOwned, outcome.Value.Kind);
            Assert.AreEqual(1, backend.ConfirmCalls);
        }

        [Test]
        public void StoreThatNeverAnswersTimesOut()
        {
            var backend = new FakeStoreBackend(new FakeStoreConfig { ConnectImmediately = false });
            var service = Create(backend);
            service.Initialize();
            Assert.AreEqual(PurchaseServiceState.Initializing, service.State);
            _clock.Advance(PurchaseService.ConnectTimeoutMs);
            service.Tick();
            Assert.AreEqual(PurchaseServiceState.Unavailable, service.State);

            PurchaseOutcome? outcome = null;
            service.Purchase(ProductIds.NoAds, "shop", o => outcome = o);
            Assert.AreEqual(PurchaseResultKind.NotAvailable, outcome.Value.Kind);

            // A late answer still makes the store usable.
            backend.CompleteConnect();
            Assert.AreEqual(PurchaseServiceState.Ready, service.State);
        }

        [Test]
        public void StoreReasonsBecomeSnakeCase()
        {
            Assert.AreEqual("payment_declined", ErrorCodes.FromStoreReason("PaymentDeclined"));
            Assert.AreEqual("user_cancelled", ErrorCodes.FromStoreReason("UserCancelled"));
            Assert.AreEqual("store_error", ErrorCodes.FromStoreReason("Store not connected."));
            Assert.AreEqual("unknown", ErrorCodes.FromStoreReason(null));
        }

        [Test]
        public void DeviceKeyIsStableAndRandom()
        {
            var store = new MemoryKeyValueStore();
            var a = new PrefsDeviceKeyStore(store).GetOrCreateKey();
            var b = new PrefsDeviceKeyStore(store).GetOrCreateKey();
            CollectionAssert.AreEqual(a, b);
            Assert.AreEqual(PrefsDeviceKeyStore.KeyLength, a.Length);
            var other = new PrefsDeviceKeyStore(new MemoryKeyValueStore()).GetOrCreateKey();
            CollectionAssert.AreNotEqual(a, other);
        }
    }
}
