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
| `StudioModule` | `StudioServices` with the App's renderer factory (`StudioSimRenderer` over Ballverse.Render), `ModeStreamRebuilder` (evicted render streams re-simulated from the replay with hash check), procedural sounds, rewarded gate bridge. If it cannot start (storage error, full disk) the failure is reported and `UnavailableStudioBackend` takes its place: the app still comes up, the simulation runs unrecorded, «В видео» / projects / export answer with a toast (§7.12) |
| `ExperienceModule` | `AppComposition` (view models, live simulation, audio, haptics), UI shell, first-run flow, per-frame loop |

All engine / SDK pieces come from an `AppEnvironment` (`DeviceEnvironment.Create` on device). EditMode tests pass fakes
through the same catalog, so the test app is the real app minus the GPU and the SDKs.

## Folders

- `Composition/` — `AppComposition` (wiring, pause / resume / low memory, RC updates, tier changes), `AppContext` +
  `AdBreaks` (interstitials at natural breaks), `RemoteSettings` (RC → generation budgets, quality profiles, bitrates,
  platform safe zones, watermark flag, remote presets, disabled modes), `DeviceTierDetector`, `AppSettingsStore`
  (`settings/app_settings.json`, atomic), `UserPresetStore`, `AppNotices` (toasts / error reports; `AppWorkQueue` for
  worker-thread results), `AppText` (EN / RU texts of keys the UI tables lack), `UnavailableStudioBackend`,
  `ModeStreamRebuilder`.
- `Ports/` — the narrow interfaces the view models use (`IStudioBackend`, `ISimSurface`, `IAppUi`, …).
- `ViewModels/` — Gallery, Simulation (+ params panel, presets), Studio (+ tools), Export (+ `RewardedGateBridge`),
  Projects, Shop, Settings, Tracking pre-prompt, System appearance, Quest celebration.
- `Modules/` — the `IAppModule`s above.
- `Unity/` — device adapters: Unity UI shell host, platform actions, texture loader, the live sim surface and gallery
  previews (Ballverse.Sim), the Studio adapters (`StudioBackend`, `StudioSimRenderer`, sound provider), the runner.

`Assets/Ballverse/Sim/` (`Ballverse.Sim`) is the live runtime: `LiveSimController` (one `ModeSession` paced per display
frame: ticks per frame = 60 / **measured** display Hz × time scale, at most 12 — `DisplayRateEstimator` smooths the real
frame time, so a 30 FPS device still plays at 1×; touch → `ArenaGestureTracker` → device-independent `ArenaGesture`s →
the **mode's own** `IArenaInputMapper` → `SimInput` at tick boundaries; replay / render stream / events written while
playing; runs kept in the studio's ring of the last 5), `LiveAudioEngine` (Core `EventMixer`, SPSC queue, rendered in
`OnAudioFilterRead` without locks or allocation, resampled to the device rate), `LiveAudioOutput` (silent looping
`AudioSource` that does **not** set `bypassEffects` — that would bypass its own `OnAudioFilterRead` — plus the app-wide
`AudioListener` the camera-less boot scene lacks; Studio's preview output reuses it), `LiveHaptics` (Core
`HapticsLimiter`; off by the setting, the run's `haptics` parameter or the system power saver), procedural sample banks
and music, `LiveSimView` (Ballverse.Render presenter), `GalleryPreviewRenderer` (at most one loop restart per frame),
`UnityHapticsDevice` (+ iOS plugin).

### Arena input contract

CONTRACT.md §3 rule 5: `SimInput.Type` is mode-defined. The App never invents an encoding: a mode receives touches only
when `LiveSimOptions.InputMappers` (`ArenaInputMapperRegistry.CreateRelease1()`) has a mapper for its id **and** the
mapper accepts the run's parameters. Release 1: `HelixDropInputMapper` (`helix_drop`, only with `control = player`; `auto`
and `bot` record no touches) — Spin (1, A = mrad/s) on press (0) and whenever the swipe speed changes, Release (2, A =
fling mrad/s) on lift, no tap; speed = M05 `HelixDropInput.FromSwipe(Δx pt/s, swipeSensitivity)` with the arena width in
points from the device (`IDeviceInfo.ScreenWidthPoints`, 390 pt fallback). The constants mirror M05's (checked against
the M05 branch's code: identical on a ≈ 19 000-point grid and on a live `HelixDrop` session); when M05 is merged, the mapper
can call `HelixDropInput` directly. A future interactive mode registers its own mapper.

## Flows

- **First run** (§4.3): straight into Ring Escape with its first built-in preset and the hints; later starts open the
  gallery. Android back on the root → `moveTaskToBack`.
- **Kill switch**: a mode in RC `modes_disabled` is out of the gallery and cannot be opened from anywhere (gallery,
  saved clip, studio «Перегенерировать», `Open`): `SimulationViewModel.Open` / `OpenWith` refuse with a toast.
- **Live look in the video**: live look edits (theme, colours, skin, trail, background, glow, impact / break FX, labels,
  HUD) are recorded as PARM; `StudioSimRenderer` applies them at their tick through `ClipLookTimeline`, so the preview
  and the MP4 repeat them (§2.2).
- **Studio preview**: every opening has its own generation and cancellation; closing / switching the project or the
  export's `SuspendPreview` while an opening is still rebuilding the render stream discards its result.
- **Watermark**: the Settings switch of a NO_WATERMARK owner is the "keep the watermark" default of each new project
  (applied without an undo step); each project's export sheet still changes its own. Both follow `WatermarkPolicy`
  with the RC kill switch.
- **Simulation**: gallery tap → last parameters or first preset; finished runs → result card, recent-runs strip,
  `GameCompleted` to the interstitial coordinator; «Сгенерировать» runs `ModeGenerator` on a worker thread (RC budgets),
  «В видео» → `CreateFromRun` → studio; «Сохранить клип» → clip library.
- **Ads**: interstitials only at natural pauses (IN2): «Ещё раз» / 🎲 **on the result card**, Back, exit to the gallery.
  ↻ / 🎲 pressed while the run is still going start at once without evaluating a break. Never at «В видео», in the
  studio, during or right after an export («Новое видео» on the export result evaluates nothing — a pending interstitial
  waits for the next real break) or in a purchase (the services' coordinator decides; the UI shows the 0.5 s «Реклама»
  plaque first). The
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
  re-simulation, stream rebuild, pacing, gestures, audio mixing, tier detection, settings), `QaRegressionTests` (live
  audio static check, mode-owned input with recorded INPT, live look timeline, export option mapping, real-rate pacing at
  30 / 45 / 60 FPS and parity with `ModeSession.AdvanceDisplayFrame`, IN2 / IN3, preview sequencing, kill switch,
  degraded studio, power-saver haptics, localization coverage, watermark default, generation success / cancel, ATT
  bridge).
- `Assets/Ballverse/Tests/AppHarness~/run.sh` (no Unity, .NET 8): `CompileCheck` compiles App + Sim + every wired
  assembly's real sources against Unity stand-ins (warnings are errors), `Runtime` runs the EditMode tests above under
  plain .NET. GameCI's Unity compile remains the authority.

## Placeholders and gaps (owner action)

- **UI strings** (UI track): the `app.*` keys in `AppStrings` and the built-in preset names (`presets.m01.*`,
  `presets.m02.*`, `presets.m05.*`) are not in `UI/Resources/BallverseUI/Localization/{en,ru}.json`. Until the UI track
  adds them, `AppText` (Composition/AppText.cs) supplies the EN / RU texts as literals in the interface language, so a
  Russian user sees Russian; move those texts into the tables and delete the entries there. The two result-card stat
  labels (`app.result.stat.score` "Score" / «Очки», `app.result.stat.progress` "Progress" / «Прогресс») are label keys
  the UI resolves itself and need the tables. `QaRegressionTests.EveryTextTheAppRaisesResolvesInEnglishAndRussian`
  fails when a new key has neither.
- **Audio / music / fonts**: there are no audio assets; sound packs are synthesized (`ProceduralSampleBank`), the
  "Melody" pack uses two public-domain tunes, the three music tracks are procedural 30 s loops. Replace with real
  assets by swapping `LiveSoundSetup.BankFor` / `StudioSoundProvider`.
- **Gallery previews** are live low-resolution simulations, not pre-rendered video loops.
- **Helix Drop input**: `HelixDropInputMapper` mirrors M05's `HelixDropInput` constants (M05 is on its own branch); once
  M05 is merged and registered, add a test that drives a real `HelixDropMode` session through `LiveSimController`
  (the scratch cross-check did exactly that) and consider calling `HelixDropInput.FromSwipe` directly.
- **Real StudioServices in EditMode**: `StudioServices.Create` creates the `StudioRunner` MonoBehaviour with
  `DontDestroyOnLoad` (throws outside Play Mode) and the export loop runs on its `Update`, so the device studio
  backend (`StudioBackend`, `StudioProjectAdapter`, the GPU export renderer) needs a PlayMode / device test; EditMode
  covers its option mapping (`StudioBackend.ToStudioOptions`), the gate contract through the fake backend, and Studio's
  own harness covers `ExportPipeline` with the gate.
- **Store**: bundle id placeholder `com.ballverse.app`; `UnityPlatformActions.AppStoreId` is empty (rate-app opens
  nothing on iOS until set); Terms / Privacy URLs `https://ballverse.app/terms|privacy` are placeholders.
- **Device-only behaviour** (rendering, `OnAudioFilterRead`, haptics, back button, safe area, tier detection on real
  hardware) is compiled but not exercised by the tests; it needs a pass on real iOS and Android devices.
