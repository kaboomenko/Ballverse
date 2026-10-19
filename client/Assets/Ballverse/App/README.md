# Ballverse.App — composition root

`Ballverse.App` is the only assembly that knows every feature assembly. It boots them through `AppHost`, builds the
real view models over Core / Services / Studio / Sim and hands them to the UI shell (`BallverseUIRoot.Create`). The
feature assemblies never reference App.

```
Bootstrap (MonoBehaviour, Boot scene)
  └─ AppHost(AppModuleCatalog.Create(platform))          App/AppModuleCatalog.cs, App/Unity/DeviceEnvironment.cs
       Platform → Services → Media → Modes → Studio → Experience   (Immediate)
       DeferredStart                                               (AfterFirstFrame: "continue interrupted export")
```

| Module | What it does |
|---|---|
| `PlatformModule` | lifecycle, frame-rate policy, screen-awake, safe area, storage paths |
| `ServicesModule` | `ServicesInstaller.Create` (§7.9: crash reporter, cached rights, ad policy on frame 0); `ServicesRunner` phases RC → IAP → UMP → ads (≥ 3 s, after consent) → ATT after the first ad |
| `MediaModule` | `MediaBridgeFactory.Create()` (device codecs, gallery, share sheet, device signals) |
| `ModesModule` | `ReleaseModes.CreateRegistry()` — modes join **only** by registration (`IModeDefinition`); M02 / M05 appear automatically |
| `StudioModule` | `StudioServices` with the App's renderer factory (`StudioSimRenderer` over Ballverse.Render), `ModeStreamRebuilder` (evicted render streams re-simulated from the replay with hash check), procedural sounds, rewarded gate bridge |
| `ExperienceModule` | `AppComposition` (view models, live simulation, audio, haptics), UI shell, first-run flow, per-frame loop |

All engine / SDK pieces come from an `AppEnvironment` (`DeviceEnvironment.Create` on device). EditMode tests pass fakes
through the same catalog, so the test app is the real app minus the GPU and the SDKs.

## Folders

- `Composition/` — `AppComposition` (wiring, pause / resume / low memory, RC updates, tier changes), `AppContext` +
  `AdBreaks` (interstitials at natural breaks), `RemoteSettings` (RC → generation budgets, quality profiles, bitrates,
  platform safe zones, watermark flag, remote presets, disabled modes), `DeviceTierDetector`, `AppSettingsStore`
  (`settings/app_settings.json`, atomic), `UserPresetStore`, `AppNotices` (toasts / error reports), `ModeStreamRebuilder`.
- `Ports/` — the narrow interfaces the view models use (`IStudioBackend`, `ISimSurface`, `IAppUi`, …).
- `ViewModels/` — Gallery, Simulation (+ params panel, presets), Studio (+ tools), Export (+ `RewardedGateBridge`),
  Projects, Shop, Settings, Tracking pre-prompt, System appearance, Quest celebration.
- `Modules/` — the `IAppModule`s above.
- `Unity/` — device adapters: Unity UI shell host, platform actions, texture loader, the live sim surface and gallery
  previews (Ballverse.Sim), the Studio adapters (`StudioBackend`, `StudioSimRenderer`, sound provider), the runner.

`Assets/Ballverse/Sim/` (`Ballverse.Sim`) is the live runtime: `LiveSimController` (one `ModeSession` paced per display
frame: ticks per frame = 60 / display Hz × time scale, at most 12; touch → `ArenaGestureTracker` → `SimInput` at tick
boundaries; replay / render stream / events written while playing; runs kept in the studio's ring of the last 5),
`LiveAudioEngine` (Core `EventMixer`, SPSC queue, rendered in `OnAudioFilterRead` without locks or allocation, resampled
to the device rate), `LiveHaptics` (Core `HapticsLimiter`), procedural sample banks and music, `LiveSimView`
(Ballverse.Render presenter), `GalleryPreviewRenderer`, `UnityHapticsDevice` (+ iOS plugin).

## Flows

- **First run** (§4.3): straight into Ring Escape with its first built-in preset and the hints; later starts open the
  gallery. Android back on the root → `moveTaskToBack`.
- **Simulation**: gallery tap → last parameters or first preset; finished runs → result card, recent-runs strip,
  `GameCompleted` to the interstitial coordinator; «Сгенерировать» runs `ModeGenerator` on a worker thread (RC budgets),
  «В видео» → `CreateFromRun` → studio; «Сохранить клип» → clip library.
- **Ads**: interstitials only at Retry / Generate / Back from result / to gallery, never at «В видео», in the studio,
  during export or purchase (the services' coordinator decides; the UI shows the 0.5 s «Реклама» plaque first). The
  rewarded gate is decided in `ExportViewModel.Confirm`; the pipeline's `IExportGate` only approves that export.
  Offline / no ads → export without an ad (Р14). NO_EXPORT_ADS skips the offer.
- **Export**: settings (RC bitrates, safe-zone masks, watermark policy) → gate → `IStudioBackend.ExportAsync` →
  done (gallery status, upsell every 3rd export after the 2nd) / failed (lower settings retry) / resume after
  interruption (deferred start).
- **Pause**: settings, presets and the open project are saved; the live run and generation stop; the studio pauses
  export itself. **Low memory**: thumbnails, previews, audio caches dropped.

## Tests

- `Assets/Ballverse/Tests/EditMode/App/` (assembly `Ballverse.App.Tests`, Unity Test Framework): `AppFlowTests`
  (host boots with fakes, first run, gallery → sim → clip → studio → export with a fake writer, interstitial and rewarded
  rules, entitlements, offline, failures, pause / low memory, tier), `SimRuntimeTests` (live run replay verified by
  re-simulation, stream rebuild, pacing, gestures, audio mixing, tier detection, settings).
- `Assets/Ballverse/Tests/AppHarness~/run.sh` (no Unity, .NET 8): `CompileCheck` compiles App + Sim + every wired
  assembly's real sources against Unity stand-ins (warnings are errors), `Runtime` runs the EditMode tests above under
  plain .NET. GameCI's Unity compile remains the authority.

## Placeholders and gaps (owner action)

- **UI strings**: the `app.*` keys in `AppStrings` (Composition/AppNotices.cs) are not in
  `UI/Resources/BallverseUI/Localization/{en,ru}.json` yet; the UI shows the key until added. English stand-ins:
  `app.generate.weak` "Couldn't find a great variant — showing the closest one", `app.generate.failed` "Generation
  failed", `app.sim.start_failed` "Couldn't start the simulation", `app.sim.run_too_short` "Let it run a bit longer
  first", `app.sim.recording_failed` "This run can't be saved", `app.studio.open_failed` "Couldn't open the project",
  `app.studio.import_failed` "Couldn't import the video", `app.studio.import_unsupported` "This video format isn't
  supported", `app.studio.import_no_space` "Not enough space to import", `app.studio.import_large` "Large video — editing
  may be slower", `app.studio.import_hdr` "HDR video converted to SDR", `app.studio.import_trimmed` "Video trimmed to
  3 minutes", `app.studio.preview_failed` "Preview problem — export still works", `app.studio.save_failed` "Couldn't save
  the project", `app.export.gallery_failed` "Couldn't save to the gallery", `app.export.gallery_permission` "Allow photo
  access to save videos", `app.export.share_failed` "Couldn't share", `app.export.caption_copied` "Caption copied",
  `app.export.resume_failed` "Couldn't continue the export", `app.shop.purchase_failed` "Purchase failed",
  `app.shop.restore_failed` "Couldn't restore purchases", `app.settings.privacy_failed` "Couldn't open privacy
  settings", `app.projects.action_failed` "Something went wrong", `app.result.stat.score` "Score",
  `app.result.stat.progress` "Progress", `app.error.generic` "Something went wrong".
- **Preset names**: Core presets use `presets.m01.*` name keys, which the UI tables do not contain.
- **Audio / music / fonts**: there are no audio assets; sound packs are synthesized (`ProceduralSampleBank`), the
  "Melody" pack uses two public-domain tunes, the three music tracks are procedural 30 s loops. Replace with real
  assets by swapping `LiveSoundSetup.BankFor` / `StudioSoundProvider`.
- **Gallery previews** are live low-resolution simulations, not pre-rendered video loops.
- **Helix Drop input**: the generic arena input contract (`ArenaInputTypes`: horizontal drag velocity + tap) is defined
  here; M05 must consume it when it is registered. A mode is interactive when its `control` parameter is not default.
- **Store**: bundle id placeholder `com.ballverse.app`; `UnityPlatformActions.AppStoreId` is empty (rate-app opens
  nothing on iOS until set); Terms / Privacy URLs `https://ballverse.app/terms|privacy` are placeholders.
- **Device-only behaviour** (rendering, `OnAudioFilterRead`, haptics, back button, safe area, tier detection on real
  hardware) is compiled but not exercised by the tests; it needs a pass on real iOS and Android devices.
