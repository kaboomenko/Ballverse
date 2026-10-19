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
  and the MP4 repeat them (§2.2). The timeline decodes every change once when the clip opens and applies only the changes
  since the last frame (binary search for the tick, a backward seek replays decoded values): no allocation while the look
  holds, one small look set on a frame that crosses changes (a slider drag records one PARM per tick).
- **Studio preview**: every opening has its own generation and cancellation; closing / switching the project or the
  export's `SuspendPreview` while an opening is still rebuilding the render stream discards its result.
- **Watermark**: the Settings switch of a NO_WATERMARK owner is the "keep the watermark" default of each new project
  (applied without an undo step); each project's export sheet still changes its own. Both follow `WatermarkPolicy`
  with the RC kill switch.
- **Simulation**: gallery tap → last parameters or first preset; finished runs → result card, recent-runs strip,
  `GameCompleted` to the interstitial coordinator; «Сгенерировать» runs `ModeGenerator` on a worker thread (RC budgets),
  «В видео» → `CreateFromRun` → studio; «Сохранить клип» → clip library. Without the studio (it failed to start) both
  toast «Студия недоступна» and leave the live run alone.
- **🎲 outcomes** (§2.4, §2.5): generation runs with `AutoTakeNearest = false`. A forced winner not found within the
  budget (M01 «Соревнование») shows a modal choice (`IAppUi.ShowChoice`, the UI shell's `DialogHost`): «Искать дольше»
  continues the same Core session for one more budget on a worker (`GenerationDecision.SearchLonger`, the user's
  decision time excluded; the candidate's parameters are rebuilt from its seed — `GenerateCandidate` is pure), «Взять
  ближайший вариант» completes it and starts the nearest run with the notice «Нужный победитель не найден — показан
  ближайший вариант»; dismissing keeps the current run; leaving the screen or a new 🎲 closes a stale choice. Other
  notices: below the quality threshold (§2.4 step 5) → «Показан лучший вариант. Попробуйте увеличить длительность» /
  "Here is the best variant. Try a longer duration", director goal missed → «Цель режиссуры не достигнута — показан лучший
  вариант», and «Не удалось найти рабочий вариант» when nothing was chosen (nothing starts). Headless (no UI) the nearest
  variant is taken with its notice.
- **Director goal «Победа в последнюю секунду»** (§2.5, M01 «Соревнование»): the UI's 🎲 mini sheet shows whatever
  `ISimulationViewModel.GenerationStyles` lists, so while the current set can take the goal
  (`DirectorGoals.LastSecondAvailable`: a mode Core accepts it for — `GenerationSessionOptions.SupportsDirectorGoal` — not
  interactive, ≥ 2 participants, i.e. M01 with «Соревнование» and ≥ 2 colours) the list gains the choice `last_second`
  «Победа в последнюю секунду». Choosing it sends `ModeGenerationRequest.Goal = DirectorGoal.LastSecond` with the
  neutral «Любой» style (`DirectorGoals.GoalStyle`): Core ranks the candidates by finishTension for the goal, and no
  style's distribution aims at the leaders' final gap — «Напряжённый финал» narrows the escape's last gap and met the
  goal less often (QA probe, tiers A–C: 96/144 vs 112/144 with «Любой»). It generates through `ParticipantsKeepingMode`,
  which fixes the user's ball count, colours and «Соревнование» in every candidate (Core fixes them only for a forced
  winner). `SimulationViewModel.LastGenerationGoal` / `LastGenerationStyle` tell what the last 🎲 asked for. Being one
  choice among the styles, the goal cannot be combined with another style until the UI has a separate goal switch (see
  "Placeholders and gaps"). The choice is remembered: while the set has fewer than 2 colours it reads and generates as
  «Напряжённый финал» without a goal (Core would reject the goal), and neither a short 🎲 nor the sheet sending back the
  shown «Напряжённый финал» replaces the remembered goal; choosing another style does.
- **Setup hints** (§2.9 M01): `SetupHintRegistry` (mode-owned, like `ArenaInputMapperRegistry`) — release 1 registers
  `RingEscapeDurationHintProvider`. Whenever the structural parameters of the shown run change (open, preset,
  structural edit, restart) and the screen is visible, the provider runs Core `RingEscapeDurationHint.Estimate` on a
  worker within the tier's 🎲 time budget, stopped by the worker's clock (`SimulationViewModel.ProbeClockMs`, a
  Stopwatch by default; §2.9 M01: «останавливает пробу по своим часам»; the tick cap `SetupHintRegistry.MaxProbeTicks`
  = 3 × 180 s only bounds it). A fast device runs the probe to its end (conclusive). On a slow one the clock stops it
  first and Core extrapolates each seed's rhythm, which overshoots once rings have been passed (a sweep of 100 sets
  stopped after every round: a false hint at some stop for 47 of them), so the provider keeps such a verdict only when
  it is clear — every seed passed a ring, ≥ 35 s of game time per seed, projection ≥ 1.5 × 180 s
  (`RingEscapeDurationHintProvider.ShowsHint`; same sweep: no false hint, about half the true hints kept); otherwise a
  slow device shows no hint and the run simply ends at the 180 s cap. The probe is cancelled by the next change or when
  the screen hides;
  the same structural set is probed once (a cleared hint or a mode change forgets the set, so it is probed again). A
  hint is raised as a `Notice` (toast) and kept in `SimulationViewModel.SetupHint`; its actions («Шире прорези»: gapSize
  +10°, «Меньше колец»: ringCount −25 %) run through `ApplySetupHintAction(id)`.
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
- **Pause**: settings, presets and the open project are saved; the live run and generation stop; a finger still down
  is lifted (`ResetInput` sends a zero-speed release at the next tick boundary, so M05's tower stops); the studio pauses
  export itself. Exception: Unity pauses the app while a full-screen ad shows (Android; iOS through
  `SetiOSAppPauseOnBackground(true)`), so while `AdBreaks.AdCoversScreen` (an interstitial break under way or the services'
  `Ads.FullScreenChanged` reporting an ad) the pause does not cancel the 🎲 waiting behind the result-card interstitial.
  The same holds for a screen the break pushes over the simulation (the iOS ATT pre-prompt before the first ad, §5.6:
  the navigator hides the simulation screen → `SetScreenActive(false)`): the 🎲 behind that break survives, and the
  break's continuation drops it only if the screen is still hidden then. **Low memory**: thumbnails, previews, audio caches dropped.

## Tests

- `Assets/Ballverse/Tests/EditMode/App/` (assembly `Ballverse.App.Tests`, Unity Test Framework): `AppFlowTests`
  (host boots with fakes, first run, gallery → sim → clip → studio → export with a fake writer, interstitial and rewarded
  rules, entitlements, offline, failures, pause / low memory, tier), `SimRuntimeTests` (live run replay verified by
  re-simulation, stream rebuild, pacing, gestures, audio mixing, tier detection, settings), `QaRegressionTests` (live
  audio static check, mode-owned input with recorded INPT, live look timeline, export option mapping, real-rate pacing at
  30 / 45 / 60 FPS and parity with `ModeSession.AdvanceDisplayFrame`, IN2 / IN3, preview sequencing, kill switch,
  degraded studio, power-saver haptics, localization coverage, watermark default, generation success / cancel, ATT
  bridge; round 2 in `QaRegressionTests.Round2.cs`: 🎲 surviving the ad's app pause, look-timeline cost and parity with
  `ReplayParams.ParamsAtTick`, the forced-winner choice against Core's `AutoSearchLonger` / `AutoTakeNearest`, the M01
  duration hint, the degraded studio on «В видео», the touch lost mid-drag; round 3 in `QaRegressionTests.Round3.cs`: no
  hint from a probe cut short after the merged Core estimator, «Победа в последнюю секунду» offered for M01
  «Соревнование» only, sent as `Goal = LastSecond` with the colours kept and its miss notice, the fallback without
  colours, the 🎲 behind the first ad surviving the ATT pre-prompt screen (and dropped when the screen stays hidden), the
  hint after a mode switch, the probe on tier A driven by an injected clock (never running out: the full probe of 80
  rings at 35° gives no hint; running out at once: stopped after the first round, no hint), an extrapolated verdict
  kept only when clear, the goal over «Любой» and the remembered goal surviving a short 🎲 on a one-ball set, the
  «Попробуйте увеличить длительность» text). Tests that expect a hint pin `ProbeClockMs`, so no verdict depends on the
  test machine's speed.
- `Assets/Ballverse/Tests/AppHarness~/run.sh` (no Unity, .NET 8): `CompileCheck` compiles App + Sim + every wired
  assembly's real sources against Unity stand-ins (warnings are errors), `Runtime` runs the EditMode tests above under
  plain .NET. GameCI's Unity compile remains the authority.

## Placeholders and gaps (owner action)

- **CI**: `.github/workflows/unity-free-checks.yml` does not run this harness yet (outside this track's files). Add a step
  `bash "client/Assets/Ballverse/Tests/AppHarness~/run.sh" --blame-hang-timeout 5m` with `client/Assets/Ballverse/App/**`,
  `Sim/**` and `Tests/**` in its path triggers (integration).
- **Setup hint row** (UI track): the M01 duration hint reaches the user as a toast; its two actions exist in the view
  model (`SimulationViewModel.SetupHint`, `ApplySetupHintAction`) but `IParamPanelViewModel` has no hint slot, so the
  settings sheet cannot show them as buttons until the UI track adds a hint row bound to those members.
- **Director goal participants** (Core): `ModeGenerator` sets `ModeCandidateRequest.FixedParticipants` only for a forced
  winner, so a goal of «Соревнование» could pick a one-ball candidate; the App wraps the mode in `ParticipantsKeepingMode`
  for a 🎲 with a goal. Once Core fixes the participants whenever `Goal != None`, drop the wrapper.
- **Goal label** (UI track): the choice's label comes from `AppText` (`app.generate.style.last_second`) until the UI
  tables gain a `generate.style.last_second` key. A dedicated Director-group control (a goal switch visible only with
  «Соревнование», beside the style) would be clearer than a chip among the styles and would let the goal combine with a
  style such as «Хаос»; `ISimulationViewModel` has no such slot, so the goal is a style choice generated over «Любой».
- **Generation resume API** (Core): `ModeGenerator` has no "continue this session" entry, so `GenerationDecision.SearchLonger`
  mirrors its sequential loop for the extra budget (equivalence with `AutoSearchLonger = 1` is tested).

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
