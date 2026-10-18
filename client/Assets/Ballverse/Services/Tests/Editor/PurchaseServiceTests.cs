using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Config;
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
            return new PurchaseService(backend, cache, validator ?? new FakeReceiptValidator(), config ?? MonetizationConfig.Release1,
                _clock, _analytics, _log);
        }

        /// <summary>A store whose purchases reads are answered by the test (delays, reordering) and whose orders are scripted.</summary>
        sealed class ScriptedStoreBackend : IStoreBackend
        {
            IStoreBackendListener _listener;

            public int FetchRequests;
            public int ConfirmCalls;

            /// <summary>When true, <see cref="Restore"/> waits for <see cref="FinishRestore"/> (the store's restore call is running).</summary>
            public bool HoldRestore;
            System.Action<bool, string> _heldRestore;

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
                if (HoldRestore) _heldRestore = done;
                else done?.Invoke(true, null);
            }

            public void FinishRestore(bool ok = true, string error = null)
            {
                var done = _heldRestore;
                _heldRestore = null;
                done?.Invoke(ok, error);
            }

            public void FailRead(string reason)
            {
                _listener.OnPurchasesFetchFailed(reason);
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
            Assert.AreEqual(WatermarkRow.Locked, WatermarkRowPolicy.GetRow(one.Entitlements));
        }

        [Test]
        public void ConfigIsRequiredAsEverywhereInCoreMonetization()
        {
            var cache = new EntitlementStore(_store, new PrefsDeviceKeyStore(_store), _clock, _log);
            Assert.Throws<System.ArgumentNullException>(() =>
                new PurchaseService(new FakeStoreBackend(), cache, null, null, _clock, _analytics, _log));
            Assert.Throws<System.ArgumentNullException>(() => Create(new FakeStoreBackend()).SetConfig(null));
        }

        [Test]
        public void NoWatermarkIsCoresPermanentRuleFromTheOwnedProductsAlone()
        {
            // Spec §5.0.2 (Р21): NO_WATERMARK = Ultimate or both ad rights, from any products, in every catalog preset;
            // nothing but the owned product ids is stored, so it restores from the store on any device.
            var both = new FakeStoreConfig();
            both.InitiallyOwned.Add(ProductIds.RemoveExportAds);
            both.InitiallyOwned.Add(ProductIds.RemoveInterstitialAds);
            foreach (var config in new[] { MonetizationConfig.Release1, MonetizationConfig.FullCatalog })
            {
                _store = new MemoryKeyValueStore();
                var service = Create(new FakeStoreBackend(both), config: config);
                service.Initialize();
                Assert.AreEqual(Entitlements.From(both.InitiallyOwned, config), service.Entitlements);
                Assert.IsTrue(service.Entitlements.Contains(Entitlement.NoWatermark));
                Assert.AreEqual(WatermarkRow.Toggle, WatermarkRowPolicy.GetRow(service.Entitlements));
                Assert.AreEqual(WatermarkRow.Hidden, WatermarkRowPolicy.GetRow(service.Entitlements, watermarkEnabled: false));
            }

            // Reinstall on another device: empty local state, rights come back from the store read.
            _store = new MemoryKeyValueStore();
            var restored = Create(new FakeStoreBackend(both));
            Assert.IsTrue(restored.Entitlements.IsEmpty);
            restored.Initialize();
            Assert.IsTrue(restored.Entitlements.Contains(Entitlement.NoWatermark));

            var ultimate = new FakeStoreConfig();
            ultimate.InitiallyOwned.Add(ProductIds.Ultimate); // not sold in release 1, still grants its rights
            _store = new MemoryKeyValueStore();
            var u = Create(new FakeStoreBackend(ultimate));
            u.Initialize();
            Assert.IsTrue(u.Entitlements.Contains(Entitlement.NoWatermark));
            Assert.IsTrue(u.Entitlements.ContainsMode(ModeCode.M06));
        }

        [Test]
        public void RemoteConfigFreeModesAndKillSwitchReplaceTheCatalog()
        {
            var backend = new FakeStoreBackend();
            var service = Create(backend);
            service.Initialize();
            int configChanges = 0, productChanges = 0;
            service.ConfigChanged += _ => configChanges++;
            service.ProductsChanged += () => productChanges++;

            var same = MonetizationConfigMapper.From(MonetizationConfig.Release1, RemoteConfigValues.Defaults);
            Assert.IsTrue(MonetizationConfigMapper.SameCatalog(MonetizationConfig.Release1, same), "defaults = Release1");
            service.SetConfig(same);
            Assert.AreEqual(0, configChanges, "an equal catalog is not a change");

            var parsed = RemoteConfigParser.Parse(new Dictionary<string, string> { { RemoteConfigKeys.ModesDisabled, "[\"M02\"]" } });
            var killed = MonetizationConfigMapper.From(MonetizationConfig.Release1, parsed.Values);
            CollectionAssert.AreEqual(new[] { ModeCode.M02 }, killed.DisabledModes);
            service.SetConfig(killed);
            Assert.AreEqual(1, configChanges);
            Assert.AreSame(killed, service.Config);
            Assert.IsFalse(service.Config.IsReleased(ModeCode.M02));
            CollectionAssert.AreEqual(MonetizationConfig.Release1.SellableProductIds, ProductIdsOf(service.Products),
                "the ad products grant no mode, so they stay on sale");
            Assert.AreEqual(0, productChanges);

            // Back to the defaults: the kill switch is lifted (WithDisabledModes replaces the set).
            service.SetConfig(MonetizationConfigMapper.From(MonetizationConfig.Release1, RemoteConfigValues.Defaults));
            Assert.AreEqual(2, configChanges);
            Assert.IsTrue(service.Config.IsReleased(ModeCode.M02));
        }

        [Test]
        public void ProductsAreRefilteredWhenTheCatalogChanges()
        {
            var backend = new FakeStoreBackend();
            var service = Create(backend, config: MonetizationConfig.FullCatalog);
            service.Initialize();
            Assert.AreEqual(ProductIds.All.Count, service.Products.Count);
            int productChanges = 0;
            service.ProductsChanged += () => productChanges++;

            service.SetConfig(MonetizationConfig.FullCatalog.WithDisabledModes(new[] { ModeCode.M06 }));
            Assert.AreEqual(1, productChanges);
            var ids = ProductIdsOf(service.Products);
            CollectionAssert.DoesNotContain(ids, ProductIds.ModeBallBattle);
            CollectionAssert.DoesNotContain(ids, ProductIds.AllModes);
            CollectionAssert.DoesNotContain(ids, ProductIds.Ultimate);
            CollectionAssert.Contains(ids, ProductIds.NoAds);

            PurchaseOutcome? outcome = null;
            service.Purchase(ProductIds.Ultimate, "shop", o => outcome = o);
            Assert.AreEqual(PurchaseResultKind.NotAvailable, outcome.Value.Kind, "a product granting a disabled mode is not sold");
        }

        // ---- fake store account (OwnerTest builds keep their test purchases) ----

        [Test]
        public void FakeStoreAccountSurvivesARestart()
        {
            var account = new MemoryKeyValueStore();
            var config = new FakeStoreConfig();
            var backend = new FakeStoreBackend(config, account);
            Assert.IsTrue(backend.IsPersistent);
            var service = Create(backend);
            service.Initialize();
            service.Purchase(ProductIds.RemoveExportAds, "shop", _ => { });
            config.Behaviour = FakePurchaseBehaviour.Defer;
            PurchaseOutcome? deferred = null;
            service.Purchase(ProductIds.RemoveInterstitialAds, "shop", o => deferred = o);
            Assert.AreEqual(PurchaseResultKind.Pending, deferred.Value.Kind);
            backend.AddUnconfirmed(ProductIds.NoAds); // paid, app killed before it was confirmed

            // Restart with a wiped entitlement cache: only the fake store account remembers.
            _store = new MemoryKeyValueStore();
            var backend2 = new FakeStoreBackend(new FakeStoreConfig(), account);
            CollectionAssert.AreEqual(new[] { ProductIds.RemoveExportAds }, backend2.StoreOwned);
            CollectionAssert.AreEqual(new[] { ProductIds.RemoveInterstitialAds }, backend2.StoreDeferred);
            var next = Create(backend2);
            Assert.IsTrue(next.Entitlements.IsEmpty);
            next.Initialize();
            CollectionAssert.AreEqual(new[] { ProductIds.RemoveExportAds, ProductIds.NoAds }, next.OwnedProductIds, "ordinal order");
            CollectionAssert.AreEqual(new[] { ProductIds.RemoveInterstitialAds }, next.PendingProductIds);
            Assert.AreEqual(1, backend2.ConfirmCalls, "the interrupted purchase is confirmed after the restart");
            Assert.IsTrue(next.Entitlements.Contains(Entitlement.NoWatermark));

            // The deferred purchase approved in a later session.
            var backend3 = new FakeStoreBackend(new FakeStoreConfig(), account);
            var third = Create(backend3);
            third.Initialize();
            backend3.ApproveDeferred(ProductIds.RemoveInterstitialAds);
            CollectionAssert.IsEmpty(third.PendingProductIds);
            CollectionAssert.AreEqual(new[] { ProductIds.RemoveExportAds, ProductIds.RemoveInterstitialAds, ProductIds.NoAds },
                new FakeStoreBackend(null, account).StoreOwned);

            // Refunds and a reset persist too.
            backend3.Refund(ProductIds.NoAds, notify: false);
            CollectionAssert.DoesNotContain(new FakeStoreBackend(null, account).StoreOwned, ProductIds.NoAds);
            backend3.ResetAccount();
            CollectionAssert.IsEmpty(new FakeStoreBackend(null, account).StoreOwned);
        }

        [Test]
        public void FakeStoreIgnoresAnUnreadableAccount()
        {
            var account = new MemoryKeyValueStore();
            account.SetString(FakeStoreBackend.AccountKey, "garbage\nowned " + ProductIds.NoAds);
            var backend = new FakeStoreBackend(null, account);
            CollectionAssert.IsEmpty(backend.StoreOwned, "unknown format");

            account.SetString(FakeStoreBackend.AccountKey,
                "ballverse-fake-store v1\nnext 7\nowned unknown.product\nowned " + ProductIds.NoAds + "\nawaiting\nbogus line\n");
            backend = new FakeStoreBackend(null, account);
            CollectionAssert.AreEqual(new[] { ProductIds.NoAds }, backend.StoreOwned, "unknown ids and malformed lines skipped");
            backend.AddUnconfirmed(ProductIds.RemoveExportAds);
            StringAssert.Contains("awaiting fake-7 " + ProductIds.RemoveExportAds, account.GetString(FakeStoreBackend.AccountKey),
                "transaction ids continue across restarts");

            var memoryOnly = new FakeStoreBackend();
            Assert.IsFalse(memoryOnly.IsPersistent);
        }

        // ---- restore: only its own read decides ----

        [Test]
        public void EarlierReadTimingOutWhileTheStoreRestoresDoesNotFailTheRestore()
        {
            var backend = new ScriptedStoreBackend { HoldRestore = true };
            var service = Create(backend);
            service.Initialize();
            backend.Answer();
            service.RefreshPurchases();                    // read 2, never answered
            RestoreOutcome? outcome = null;
            service.Restore(o => outcome = o);             // AppStore.sync() running
            _clock.Advance(PurchaseService.FetchTimeoutMs);
            service.Tick();
            Assert.IsNull(outcome, "read 2 was not the restore's read");
            Assert.AreEqual(3, backend.FetchRequests, "read 2 is retried as usual");

            backend.FinishRestore();                       // restore's own read: 4
            Assert.AreEqual(4, backend.FetchRequests);
            Assert.IsNull(outcome);
            backend.Answer(ScriptedStoreBackend.Confirmed(ProductIds.NoAds));
            Assert.IsTrue(outcome.Value.Success);
            Assert.AreEqual(1, outcome.Value.OwnedCount);
        }

        [Test]
        public void EarlierAnswersAndFailuresWhileTheStoreRestoresDoNotEndTheRestore()
        {
            var backend = new ScriptedStoreBackend { HoldRestore = true };
            var service = Create(backend);
            service.Initialize();
            service.RefreshPurchases(); // coalesced with the connect read
            RestoreOutcome? outcome = null;
            service.Restore(o => outcome = o);
            backend.Answer();
            Assert.IsNull(outcome, "answer to the connect read");
            service.RefreshPurchases();
            backend.FailRead("Unknown");
            Assert.IsNull(outcome, "failure of an earlier read");

            backend.FinishRestore(ok: false, error: "UserCancelled");
            backend.Answer();
            Assert.IsFalse(outcome.Value.Success);
            Assert.AreEqual("user_cancelled", outcome.Value.ErrorCode);
        }

        [Test]
        public void RestoreReadTimeoutStillFailsAfterTheStoreRestoreFinished()
        {
            var backend = new ScriptedStoreBackend { HoldRestore = true };
            var service = Create(backend);
            service.Initialize();
            backend.Answer();
            RestoreOutcome? outcome = null;
            service.Restore(o => outcome = o);
            _clock.Advance(PurchaseService.FetchTimeoutMs * 3);
            service.Tick();
            Assert.IsNull(outcome, "no read of its own yet");
            backend.FinishRestore();
            _clock.Advance(PurchaseService.FetchTimeoutMs - 1);
            service.Tick();
            Assert.IsNull(outcome);
            _clock.Advance(1);
            service.Tick();
            Assert.AreEqual("fetch_timeout", outcome.Value.ErrorCode);
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
