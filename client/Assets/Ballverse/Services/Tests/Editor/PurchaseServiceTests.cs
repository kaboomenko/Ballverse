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

        PurchaseService Create(FakeStoreBackend backend, IReceiptValidator validator = null)
        {
            var cache = new EntitlementStore(_store, new PrefsDeviceKeyStore(_store), _clock, _log);
            return new PurchaseService(backend, cache, validator ?? new FakeReceiptValidator(), _clock, _analytics, _log);
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
