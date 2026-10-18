# Ballverse.Services

Ads, purchases, consent, analytics, Remote Config and crash reporting behind interfaces (spec §5, §7.3, §7.9, §9), each with a
fake for the editor, tests and test builds (none of the fakes is compiled into a Store build). Release-1 scope: three free modes, store sells only Remove Export Ads, Remove Interstitial Ads
and No Ads Bundle.

## Wiring (App)

The App (unity/bootstrap `Ballverse.App.IAppModule`) wraps the services in a `ServicesModule` under
`Assets/Ballverse/App/Modules/`: `Register` → `ServicesInstaller.Create(options)` and publish the facades; `Start` →
`ServicesRunner.Attach(services)`; `Shutdown` → `runner.Detach()`. The services phase their own frame-1+ work
(`ServicesStartup`), so exactly one owner ticks them: the runner, or the App's loop calling `services.Tick()`,
`services.OnApplicationResumed()` (from `IAppLifecycle.Resumed`) and `services.OnApplicationFocusChanged(hasFocus)` (from
`IAppLifecycle.FocusChanged`) — not both. See the `ServicesInstaller` docs.

```csharp
var services = ServicesInstaller.CreateAndRun();   // without the App: frame 0; everything else starts on the next frames
services.RewardedExport.SetInStudio(true);          // studio entered (RW1 preload)
var decision = services.RewardedExport.RequestExport(projectId);
// ShowOffer → dialog → services.RewardedExport.WatchAd(projectId, step => ...) / Decline(projectId, purchaseChosen)
// export finished → services.RewardedExport.OnExportFinished(projectId, result)
services.Interstitials.OnGameCompleted(runSummary);
var d = services.Interstitials.EvaluateNaturalBreak(kind, inStudio, exporting, inPurchaseFlow);
// d.IsShow → 0.5 s «Реклама» label → services.Interstitials.ShowInterstitial(shown => continue)
services.Purchases.Purchase(ProductIds.NoAds, "shop", outcome => ...);
services.Purchases.Restore(outcome => ...);
services.Consent.AttPrePrompt = myExplanationScreen;  // iOS pre-prompt before the system ATT dialog (Show / Dismiss)
```

`services.Entitlements` holds the rights: Core `Entitlements.From(owned, config)`. `NO_WATERMARK` follows Core's permanent rule
(spec §5.0.2): Ultimate, or `NO_EXPORT_ADS` + `NO_INTERSTITIALS` from any products (No Ads Bundle or both ad removals); the
services store nothing but the owned product ids (signed cache), so every right restores from the store on any device. The
export sheet row is Core `WatermarkRowPolicy.GetRow(services.Entitlements, watermark_enabled)`.

The catalog comes only from Core `MonetizationConfig`: the build's preset (`ServicesOptions.Monetization`, default
`MonetizationConfig.Release1`: M01, M02, M05 free; only Remove Export Ads, Remove Interstitial Ads and No Ads Bundle sold) with
the typed Remote Config `free_modes` / `modes_disabled` applied (`MonetizationConfigMapper.From` =
`Release1.WithFreeModes(values.FreeModes).WithDisabledModes(values.ModesDisabled)`), re-applied on every Remote Config update.
`services.Purchases.Config` (and its `ConfigChanged` event) is what the UI passes to Core `Storefront`, `ModeAvailability` and
`SalePointPolicy`. Ad policies take Remote Config through Core `RemoteConfigAdapters.ToAdPolicyConfig`.

## Build variants (spec §7.9, §8.9)

The variant is the App's `Ballverse.Platform.BuildVariant` (`BuildVariants.Current`; this assembly references
`Ballverse.Platform`, there is no second copy). The bootstrap build script compiles one symbol into the player; `ServicesPolicy`
turns the variant into what gets wired:

| Variant (symbol) | Ads | Consent | Purchases | Fakes / test ids in the binary |
|---|---|---|---|---|
| `Dev` (`BALLVERSE_DEV`, or no symbol: editor, players built without the script) | Google Mobile Ads with Google's **test** units | UMP; EEA test geography / test devices from `ServicesConfig` allowed | fake store (account persisted, see below); `ServicesConfig.testBuildStore = Sandbox` → Unity IAP sandbox | yes |
| `OwnerTest` (`BALLVERSE_OWNERTEST`, release IL2CPP) | same as Dev | same as Dev | same as Dev | yes |
| `Store` (`BALLVERSE_STORE`) | real AdMob units from `ServicesConfig`; missing, malformed or test units → ads off (exports without ads, Р14), never test ads | UMP, no debug settings | Unity IAP (StoreKit 2 / Play Billing) | no: `FakeStoreBackend`, `FakeAdNetwork`, `FakeConsentService`, the other `Fake*` helpers and the Google test unit / app id constants are under `#if !BALLVERSE_STORE`; a requested `ServicesMode.Fake` is ignored with an error |

`ServicesMode.Fake` (all fakes) and `Real` (real store too) override the defaults in Dev and OwnerTest; the editor follows
`ServicesConfig.editorMode` (fakes). Dev and OwnerTest ignore configured real ad units. Store builds keep only the Google test
publisher prefix, to refuse test units (`AdUnitIds.AnyTestId`). The log-based analytics / crash reporter and the build-default
Remote Config (`BuildDefaultsRemoteConfigService`: `RemoteConfigValues.Defaults`, no test values) are the release-1 production
path while Firebase is off, not test fakes.

The fake store keeps its account — owned products, deferred and paid-but-unconfirmed orders — in the app's key-value store
(PlayerPrefs, key `ballverse.fake_store.account.v1`) after every change, so purchases made in a Dev / OwnerTest build survive
restarts and are re-delivered by the next purchases read, like a real store account. `services.FakeStore` (developer menu)
offers `ApproveDeferred`, `Refund`, `AddUnconfirmed` and `ResetAccount`. Tests that pass `ServicesOptions.FakeStore` get an
in-memory account unless `PersistFakeStore = true`.

`Editor/StoreBuildCheck.cs` fails a Store build whose `ServicesConfig` lacks real units when the variant is on the command line
(`-ballverseVariant Store`, CI). Menu builds and the App Bundle default pass the variant only inside the bootstrap build script:
it should call `StoreBuildCheck.StoreAdUnitsProblem(...)` next to its AdMob app id check (open issue below).

## Initialization order (spec §7.9)

Frame 0: crash reporter → cached rights (signed cache) and ad policy state → UI. Frame 1+: Remote Config (cached values, 3 s
background fetch), IAP connect and purchases read, UMP consent. Ads SDK starts once consent allows ads and ≥ 3 s after the first
frame. ATT is asked right before the first ad is shown; a pre-prompt that throws ends the ATT flow, and one that never answers is
abandoned after 60 s (the export runs without the ad, the interstitial is skipped: Р14, IN4): the pre-prompt gets
`IAttPrePrompt.Dismiss()` and a late "continue" no longer opens the system dialog. A pre-prompt that already answered (the system
dialog is up, or its callback was lost) is never dismissed when the flow is abandoned: it closed itself and the UI may already
show another screen.

Purchases are re-read 2 s after every return to the app and every focus gain (pending approvals, refunds; spec §5.4). The delay
matters on Google Play: Unity IAP 5 reads purchases itself on each focus gain and keeps a single fetch callback, so an app read
issued before that would be answered into Unity IAP's cache only. A read without an answer after 20 s counts as failed (it never
blocks later reads) and an automatic one is retried once. A restore is decided only by its own read, issued after the store's
restore call (`AppStore.sync()` / Play restore) finished: answers, failures and timeouts of earlier reads that arrive while that
call is running do not end it; its own read timing out fails it with `fetch_timeout`.

## Consent and analytics (spec §9.1)

Analytics events stay buffered (bounded) until the user's UMP decision. `ConsentModeRules` maps the decision to Consent Mode:
outside consent regions everything is granted; after an answer `analytics_storage` follows the TCF purposes UMP wrote
(`IABTCF_PurposeConsents`: purposes 1, 8 and 9 all consented, or GDPR not applying), the ad signals are left to the TCF string.
Changes from the privacy options form are applied the same way. Without a decision (offline first launch, form failed) consent is
gathered again with backoff (30 s doubling to 5 min) and on every return to the app.

The native Firebase SDK starts before any C# and by default collects automatic events (`first_open`, `session_start`) with every
consent type granted. `Editor/FirebaseConsentDefaults.cs` therefore writes Firebase's documented defaults into every build —
Android `<meta-data>` `firebase_analytics_collection_enabled=false` and `google_analytics_default_allow_analytics_storage`,
`_ad_storage`, `_ad_user_data`, `_ad_personalization_signals` = `false` (unityLibrary manifest); iOS Info.plist
`FIREBASE_ANALYTICS_COLLECTION_ENABLED` and `GOOGLE_ANALYTICS_DEFAULT_ALLOW_ANALYTICS_STORAGE` / `_AD_STORAGE` / `_AD_USER_DATA` /
`_AD_PERSONALIZATION_SIGNALS` = `NO` — and the services enable collection and Consent Mode after the decision. Values already
set by the owner or a plugin are kept; the keys are inert while Firebase is not in the build.

## What switches the real SDKs on

| SDK | Install | Define |
|---|---|---|
| Google Mobile Ads + UMP | `com.google.ads.mobile` 9.x–11.x (OpenUPM) | `BALLVERSE_ADMOB` — automatic via asmdef version define |
| Unity IAP | `com.unity.purchasing` ≥ 5.4.2 < 6 | `BALLVERSE_UNITY_IAP` — automatic |
| Firebase Analytics, Crashlytics, Remote Config | Firebase Unity SDK + `google-services.json` / `GoogleService-Info.plist` | `BALLVERSE_FIREBASE` — add by hand in Player Settings |

Without a package the matching service degrades safely: no ads (exports run without ads, Р14), store unavailable (rights from
the cache only, never fake purchases in a device build), log-based analytics/crash, Remote Config build defaults.
If the ads plugin is imported as a `.unitypackage` instead of the UPM package, add `BALLVERSE_ADMOB` by hand.

## Owner setup before store release

- `ServicesConfig` asset (Create → Ballverse → Services Config) at `Resources/BallverseServicesConfig.asset`: real AdMob ad unit
  ids (used only by Store builds; Dev and OwnerTest always use Google's test units) and the Google Play licensing public key
  (on-device signature check, spec §5.5). Once the store apps exist, `testBuildStore = Sandbox` lets OwnerTest builds buy with
  license testers / sandbox accounts.
- Google Mobile Ads settings asset: AdMob app ids (test app ids: Android `ca-app-pub-3940256099942544~3347511713`,
  iOS `ca-app-pub-3940256099942544~1458002511`). The ATT usage description (NSUserTrackingUsageDescription) is added to the
  Xcode project's Info.plist by `Editor/IosTrackingUsageDescription.cs` when the plugin settings leave it empty (English text;
  localize later with InfoPlist.strings); the native bridge never requests ATT without it.
- iOS minimum version ≥ 14 (the ATT bridge `Plugins/iOS/BallverseTracking.mm` links AppTrackingTransparency); the bootstrap
  `ProjectSetup.IosMinVersion` is 16.0, which satisfies it.
- The entitlement cache key is a random key in PlayerPrefs (`PrefsDeviceKeyStore`): integrity against casual edits only, not a
  security boundary (spec §7.9); Keychain/Keystore can replace it behind `ISecureKeyStore`.

## Open issues

- Bootstrap `BuildScript.CheckAdIds` should also fail Store builds on `StoreBuildCheck.StoreAdUnitsProblem(...)` (menu builds and
  the App Bundle default do not put the variant on the command line). Until then a misconfigured Store build runs with ads off.
- Compilation under `BALLVERSE_STORE` / `BALLVERSE_OWNERTEST` is checked here with the real Google Mobile Ads 11.6.0 assemblies
  and stand-ins for UnityEngine / Unity IAP 5.4.4 (signatures copied from the package) only; CI builds only OwnerTest on push.
- A product that becomes sellable through a Remote Config change after the store connected (not possible with the release-1
  catalog, whose three products grant no mode) is listed only once the store has returned it, at the latest after the next
  connect.
- The TCF purpose set for `analytics_storage` (1 + 8 + 9) is a product/legal decision to confirm with the owner.
- Not verifiable in this container: Unity/GameCI compilation, the `.mm` bridge, behaviour on devices.
