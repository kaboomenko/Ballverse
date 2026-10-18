# Ballverse.Services

Ads, purchases, consent, analytics, Remote Config and crash reporting behind interfaces (spec §5, §7.3, §7.9, §9), each with a
fake for the editor and tests. Release-1 scope: three free modes, store sells only Remove Export Ads, Remove Interstitial Ads
and No Ads Bundle.

## Wiring (App)

The App (unity/bootstrap `Ballverse.App.IAppModule`) wraps the services in a `ServicesModule` under
`Assets/Ballverse/App/Modules/`: `Register` → `ServicesInstaller.Create(options)` and publish the facades; `Start` →
`ServicesRunner.Attach(services)`; `Shutdown` → `runner.Detach()`. The services phase their own frame-1+ work
(`ServicesStartup`), so exactly one owner ticks them: the runner, or the App's loop calling `services.Tick()` and
`services.OnApplicationResumed()` (from `IAppLifecycle.Resumed`) — not both. See the `ServicesInstaller` docs.

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
services.Consent.AttPrePrompt = myExplanationScreen;  // iOS pre-prompt before the system ATT dialog
```

`services.Entitlements` holds the rights: Core `Entitlements.From(owned, MonetizationConfig.Release1, grandfathered)` (No Ads
Bundle, or both ad removals, also give `NO_WATERMARK`). The catalog and rules come only from Core `MonetizationConfig`
(`ServicesOptions.Monetization`, default `Release1`); the Core `WatermarkGrandfathering` flag is persisted on every recompute so a
later release that drops the No Ads rule never takes the right away. `services.Purchases.Config` and
`.WatermarkGrandfathered` are what the UI passes to Core `Storefront` functions.

## Initialization order (spec §7.9)

Frame 0: crash reporter → cached rights (signed cache) and ad policy state → UI. Frame 1+: Remote Config (cached values, 3 s
background fetch), IAP connect and purchases read, UMP consent. Ads SDK starts once consent allows ads and ≥ 3 s after the first
frame. ATT is asked right before the first ad is shown; a pre-prompt that throws ends the ATT flow, and one that never answers is
abandoned after 60 s (the export runs without the ad, the interstitial is skipped: Р14, IN4).

## Consent and analytics (spec §9.1)

Analytics events stay buffered (bounded) until the user's UMP decision. `ConsentModeRules` maps the decision to Consent Mode:
outside consent regions everything is granted; after an answer `analytics_storage` follows the TCF purposes UMP wrote
(`IABTCF_PurposeConsents`: purposes 1, 8 and 9 all consented, or GDPR not applying), the ad signals are left to the TCF string.
Changes from the privacy options form are applied the same way. Without a decision (offline first launch, form failed) consent is
gathered again with backoff (30 s doubling to 5 min) and on every return to the app.

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
  ids (Google test ids are used until then) and the Google Play licensing public key (on-device signature check, spec §5.5).
- Google Mobile Ads settings asset: AdMob app ids (test app ids: Android `ca-app-pub-3940256099942544~3347511713`,
  iOS `ca-app-pub-3940256099942544~1458002511`). The ATT usage description (NSUserTrackingUsageDescription) is added to the
  Xcode project's Info.plist by `Editor/IosTrackingUsageDescription.cs` when the plugin settings leave it empty (English text;
  localize later with InfoPlist.strings); the native bridge never requests ATT without it.
- iOS minimum version ≥ 14 (the ATT bridge `Plugins/iOS/BallverseTracking.mm` links AppTrackingTransparency).
- The entitlement cache key is a random key in PlayerPrefs (`PrefsDeviceKeyStore`): integrity against casual edits only, not a
  security boundary (spec §7.9); Keychain/Keystore can replace it behind `ISecureKeyStore`.

## Open issues

- Core Monetization (`MonetizationConfig.Release1`, `WatermarkGrandfathering`) is still on the core/monetization branch; this
  assembly compiles in Unity only once it is merged.
- Release-1 build variants (docs-release1 §7.9 / §8.9, not merged yet): fakes and test ids are compiled into every build today;
  release builds refuse fake services unless the App passes `DeveloperBuild = true`. Gate them behind the build-variant symbol
  (OwnerTest = IL2CPP release build with the fake store; Store = no fakes) once that spec lands.
- The TCF purpose set for `analytics_storage` (1 + 8 + 9) is a product/legal decision to confirm with the owner.
- Not verifiable in this container: Unity/GameCI compilation, the `.mm` bridge, behaviour on devices.
