# Ballverse.Services

Ads, purchases, consent, analytics, Remote Config and crash reporting behind interfaces (spec §5, §7.3, §7.9, §9), each with a
fake for the editor and tests. Release-1 scope: three free modes, store sells only Remove Export Ads, Remove Interstitial Ads
and No Ads Bundle.

## Wiring (App)

```csharp
var services = ServicesInstaller.CreateAndRun();   // frame 0; everything else starts on the next frames
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

`services.Entitlements` holds the rights (release-1 rule: No Ads Bundle, or both ad removals, also give `NO_WATERMARK`).

## Initialization order (spec §7.9)

Frame 0: crash reporter → cached rights (signed cache) and ad policy state → UI. Frame 1+: Remote Config (cached values, 3 s
background fetch), IAP connect and purchases read, UMP consent. Ads SDK starts once consent allows ads and ≥ 3 s after the first
frame. ATT is asked right before the first ad is shown.

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
  iOS `ca-app-pub-3940256099942544~1458002511`) and the ATT usage description text (NSUserTrackingUsageDescription).
- iOS minimum version ≥ 14 (the ATT bridge `Plugins/iOS/BallverseTracking.mm` links AppTrackingTransparency).
- The entitlement cache key is a random key in PlayerPrefs (`PrefsDeviceKeyStore`): integrity against casual edits only, not a
  security boundary (spec §7.9); Keychain/Keystore can replace it behind `ISecureKeyStore`.
