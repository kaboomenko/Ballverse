# PlayMode boot smoke test (`Ballverse.PlayModeTests`)

The EditMode tests in `Tests/EditMode/App` boot the app through the real `AppModuleCatalog` but with fakes for every engine
piece (`TestApp` / `TestFakes`). This assembly boots the **real** app in Play Mode, the way the APK starts, so the Unity
wiring is checked in CI before anyone installs a build: `Bootstrap`, `DeviceEnvironment`, `ServicesRunner`,
`MediaBridgeFactory`, `StudioServices` / `StudioRunner`, `LiveSimView`, `GalleryPreviewRenderer`, `LiveAudioOutput`,
`UnityAppUi` / `BallverseUIRoot` / `UiShell`, and `AppRunner`.

## What the test does (`AppBootSmokeTest.RealAppBootsAndRunsTheMainFlow`)

Before Play Mode starts, `BootSceneSetup` (a Unity Test Framework `IPrebuildSetup`) runs `Ballverse.Editor.ProjectSetup.Run()`.
Every device build runs that step first too (`BuildScript`). It assigns the URP 2D pipeline, creates `Assets/Scenes/Boot.unity`
with its `Bootstrap` object and puts it first in the build scene list, and applies the player settings. A fresh checkout has
none of these assets committed.

| # | Step | What is checked |
|---|------|-----------------|
| 0 | environment | Logs the Unity version, graphics device, screen size and data path. If `-ballverseSmokeRequireGpu` was passed and there is no graphics device, the test fails. Every renderer shader (`ExpectedLogs.RendererShaders`, the six `RenderResources` loads) resolves through `Resources` or `Shader.Find`, and both GPU-instancing material templates load: a missing shader or template is a packaging defect and fails with or without a device. |
| 1 | boot scene | `EditorSceneManager.LoadSceneAsyncInPlayMode(ProjectSetup.BootScenePath)` loads the scene and `Bootstrap.Awake` boots the host. The host reaches `Running` (deferred modules started after the first frame) with no module failures, and every module starts. |
| 2 | wiring | Services run in `Fake` mode in the editor. The media backend is `Fake`: `MediaBridgeFactory` compiles the Android / iOS bridges only with `!UNITY_EDITOR`. The studio backend is the real `StudioBackend`, not `UnavailableStudioBackend`. The UI host is `UnityAppUi`. The surface is `UnitySimSurface` and the previews are `UnityGalleryPreviews`. These objects exist: `AppRunner`, `ServicesRunner`, `LiveAudioOutput`, `BallverseUIRoot` and `StudioRunner`. There is exactly one `AudioListener`, and its object has no `AudioSource` and no `OnAudioFilterRead` script (`DeviceAudio.FindPlacementProblem`). |
| 3 | first run → Ring Escape | On a first run (no `settings/app_settings.json`), the simulation screen shows `ring_escape`. Otherwise the test taps the Ring Escape card in the gallery the way `GalleryScreen` does: `OpenMode` + `Push(Simulation)`. Either way a live run starts. |
| 4 | live run at 3x | `IParamPanelViewModel.Apply(timeScale, 3)` (the settings sheet slider) speeds the run up. The test then plays ≥ 3 s of real frames and ≥ 90 frames: the run advances and `LiveSimView` has a render target. |
| 5 | generate (dice) | `Generate()` (short 🎲) runs within the tier's budget (RC `GenerationBudgets`) and the test waits for the result. The session seed is pinned to one whose first candidate is viable for the exact request (`SmokeChecks.ViableSessionSeedSource`). Candidate 0 always runs, so the 🎲 selects a run whatever a slow runner's time budget cuts. A random seed can legitimately find nothing. A generated run must have been applied (`SmokeChecks.DiceFailure`): a new parameter set, a new live seed, the «Свои» preset, and no «nothing found» (`app.generate.none`) or «generation failed» (`app.generate.failed`) notice. The test listens to `SimulationViewModel.Notice` as well as `AppNotices.Shown`, because the 🎲 notices go to the simulation screen, not to the toast host. A forced-winner choice dialog would be closed with system back; then the current run must stay unchanged (`DeclineDecision`). |
| 6 | to video → studio | Once the run has 150 ticks (a 2.5 s clip), the test does what `SimulationScreen.ToVideo` does: `ToVideo()` + `Push(Studio)`. A project with a non-empty clip opens. |
| 6b | studio preview | The preview texture opens, plays for 60 frames, then pauses. |
| 7 | export | `Begin` → settings sheet → `Confirm` → rewarded gate → `WatchAdAndExport` (the fake ad grants the reward at once) → export with the editor's FAKE media writer (PNG sequence + WAV). The result must be `Succeeded`, with frames, an output on disk and the `ExportDone` screen. |
| 8 | back to gallery | System back (`UiNavigator.Back`, i.e. Android back) through ExportDone → Studio → Simulation → Gallery. The studio closes and the simulation stops. |
| 9 | projects tab / cover | The Projects tab lists the new project and renders its cover. |
| 9b | every registered mode | For each card in `Gallery.Modes` (today only `ring_escape`; Geometry Bounce and Helix Drop are covered as soon as `ReleaseModes` registers them), the gallery preview renders. Then every mode other than Ring Escape, which the main flow already ran, is opened like a card tap (`OpenMode` + `Push(Simulation)`). It must play, advance 60 live ticks and give `LiveSimView` a render target, and system back returns to the gallery. A photosensitive mode (`SimulationViewModel.PhotosensitiveModes`) gets its warning marked as acknowledged first; teardown removes the mark. |
| 10 | pause, resume, low memory | `AppLifecycle.NotifyPause(true)` saves the settings (the store is clean and the file exists). Resume and low memory (caches dropped) follow; the host is still `Running` with no failures. |
| 11 | cleanup | Deletes the project the test created. In teardown, destroying `Bootstrap` shuts the host down (`ShutDown`, `Bootstrap.Host == null`). Objects that are still alive are logged as soft findings and removed. |

### Failure rules

- **Logs, with a graphics device (CI)**: Unity Test Framework's default `LogAssert` behaviour is kept, so any `Error` /
  `Exception` / `Assert` log fails the test. `SmokeLog` also listens to `Application.logMessageReceivedThreaded`, so messages
  from every thread are caught. It fails at the next frame and names the step, the message and its stack trace.
- **Logs, without a graphics device (local runs only)**: the test sets `LogAssert.ignoreFailingMessages = true` and
  `SmokeLog.Classify` enforces the rules instead. An error-class message is:
  - *whitelisted* if it matches `ExpectedLogs`;
  - *soft* (reported in the teardown summary, not failed) if it comes from GPU-side code (`GpuSources`: the
    `[Ballverse.Render]` tag, or a frame of `Ballverse.Render.*`, `LiveSimView`, `GalleryPreviewRenderer`, the studio
    compositor / frame and text renderers / emoji atlas / video uploader / export frame renderer / preview player / timeline
    thumbnails, `StudioServices.RenderCover`, `Ballverse.UI.Glass.*`, or Unity's `RenderTexture` / `Graphics` / `Rendering.*`
    / `Material` / `Shader` / `Texture2D.ReadPixels`, in the message or its stack trace), in **any** step. This matters
    because the live view renders every frame and covers and previews render from per-frame ticks, not only inside the
    GPU-flagged steps;
  - also *soft* if it arrives during the **time window** of a step flagged GPU-dependent (studio preview, export, project
    cover, gallery previews). A logic error raised during those steps is therefore soft without a device. That is a known
    gap of local no-GPU runs; CI requires a device, which makes every error hard;
  - *unexpected* (fails) otherwise.
- **Whitelist** (`ExpectedLogs`, exact regexes with their sources): with a graphics device it is **empty**. Without one, it
  accepts only the two renderer messages that `RenderResources` / `BallverseRenderer` log when a shader cannot be used. That
  is the product's documented degraded path. `RenderResources.CreateMaterial` uses the same "missing or unsupported" text
  when a shader is not in the project at all. Step 0 therefore asserts that every renderer shader resolves, so only the
  "unsupported" half is actually tolerated.
- **Module failures, wrong wiring, steps that never finish**: hard failures with a diagnostics line: step, host state,
  screen, sim phase / tick, studio / export state, and the toasts the app showed.
- **Timeouts** are at least 10× what each step needs (boot 180 s, frame steps 240 s, 🎲 max(240 s, 20 × budget), studio
  open 300 s, export 30 min, test 60 min via NUnit `[Timeout]`; the UTF default is 180 s). No assertion measures a
  duration. Software GL, `-debugCodeOptimization` and code coverage, which GameCI turns on, only make steps slower.

### Graphics device

GameCI's ubuntu runner does **not** run the editor with `-nographics`:

- `game-ci/unity-test-runner@v4.3.2` `dist/platforms/ubuntu/run_tests.sh` calls
  `unity-editor -batchmode -logFile … -projectPath … -runTests -testPlatform playmode … $CUSTOM_PARAMETERS`. It adds
  `-nographics` only to the *standalone player* run.
- `unity-editor` in the `unityci/editor` image (game-ci/docker `images/ubuntu/editor/Dockerfile`) is
  `xvfb-run -ae /dev/stdout "$UNITY_PATH/Editor/Unity" -batchmode "$@"`, i.e. a virtual X display.
- `unityci/base` (`images/ubuntu/base/Dockerfile`) installs `xvfb`, whose dependency chain on Ubuntu 22.04 is
  `libgl1` → `libglx0` → `libglx-mesa0` → `libgl1-mesa-dri` (Mesa, incl. the llvmpipe software rasterizer).

So the editor gets an OpenGL Core device on Mesa llvmpipe and every GPU path (live view, previews, studio preview, export
readback, covers) runs on the CPU. The workflow passes `-ballverseSmokeRequireGpu`, so a future image or runner without a
device fails loudly instead of silently softening the GPU checks.

When the editor does run without a device (`GraphicsDeviceType.Null`, e.g. a local `-nographics` run without the switch),
every step still runs. GPU-dependent outcomes become **soft**, meaning logged warnings and not failures: the studio
preview, export, cover, gallery previews and the live render target. Error logs follow the no-device rules under "Failure
rules" above: GPU-side errors are soft in any step, and so is any error during a GPU-flagged step.

## Audio outputs (`AudioOutputsPlayModeTest`)

The first smoke run failed in step 1 with "GameObject has multiple AudioSources and/or AudioListeners attached":
`LiveAudioOutput` had added the app's `AudioListener` to its own object. That object carries the `AudioSource` and the
`OnAudioFilterRead` script, and Unity inserts the filter into only one of the two. `DeviceAudio` (App) now owns the only
listener, on an object of its own. This test creates the live output and the studio preview output the way `DeviceEnvironment`
wires them, lets them play for 10 frames and checks `DeviceAudio.FindPlacementProblem`. Any error log fails it. The EditMode
twin, `Tests/EditMode/App/Unity/AudioListenerPlacementTests`, checks the same placement in an empty edit-mode scene.

## What it cannot cover

- **Device-only code**: the Android MediaCodec / iOS AVFoundation bridges (the editor always uses the FAKE backend, so
  the export writes a PNG sequence + WAV, not an MP4, and nothing is saved to the gallery), the real AdMob / UMP / ATT /
  store SDKs (editor services are fakes, and the fake ad grants its reward instantly), haptics, the safe area / notch,
  the Android back button event itself (the test calls `UiNavigator.Back`, the action it triggers), thermal / low-power
  signals, tier detection on real hardware, and IL2CPP / stripping (the editor runs Mono).
- **Real GPU behaviour**: llvmpipe checks that the render, readback and export code paths run without errors, not how
  they look or perform on a phone GPU (Adreno / Mali / Apple), nor ES 3.1 / Vulkan / Metal shader variants.
- **Audio output**: the container's ALSA default is a null sink. `LiveAudioOutput` / `OnAudioFilterRead` run, but nobody
  listens.
- **UI hit-testing**: the test calls the view-model method and navigator action each button calls (`Tap.On` handlers),
  so the button bindings and layout are not exercised, only the screens' show / hide / refresh code that this navigation
  drives.
- **Non-cancellable dialogs**: the photosensitivity warning (`UiShell.ShowPhotosensitivityWarning`, Helix Drop once it is
  registered) has only an OK button inside the UI Toolkit tree, with no public hook. The test marks the warning as
  acknowledged up front, so it does not exercise the dialog itself. The EditMode tests cover the warn / acknowledge logic with
  `FakeUi`.
- **Isolation**: the app keeps its data under `Application.persistentDataPath` (the real app path). The CI container is
  fresh, so CI always takes the first-run branch. A local run reuses the editor's app data: no first-run check, and the
  test deletes only the project it created.

## Running

- **CI**: `.github/workflows/unity-tests.yml`, job `PlayMode tests` (`game-ci/unity-test-runner@v4.3.2`,
  `testMode: playmode`). It needs the Unity license secrets (docs/dev/unity.md) and is skipped with a notice without them.
  Results (`playmode-results.xml`, `playmode.log`) are uploaded as the `unity-playmode-results` artifact.
- **Editor**: Window > General > Test Runner > PlayMode > `AppBootSmokeTest`. Running it applies Ballverse > Project
  Setup to the project first.
- **Without Unity**: `CompileCheck~/run.sh` (.NET 8). It runs in CI as the `PlayMode smoke test checks (.NET 8, no Unity)`
  job of `unity-tests.yml`, which needs no secrets and gates the PlayMode job. It does three things:
  1. It greps the real sources for every hand-copied line: the UI shell / navigator / `ProjectSetup` signatures, and the
     renderer shader list against `RenderResources.cs`.
  2. It compiles this assembly against the real App / Sim / Studio / Media / Services / Render / UI model sources and Core.
     The editor branch (`UNITY_EDITOR`) and the player branch are both compiled; warnings are errors.
  3. It runs `LogicTests/` (NUnit). These test the 🎲 post-condition against the real view models booted by the App
     harness's `TestApp`, including QA's "🎲 then cancel" case that the first version let pass. They also test the log
     classification and the whitelist.

  The App harness's stand-ins (`Tests/AppHarness~/Stubs/UnityStubs*.cs`, `UiRuntimeStubs.cs`) are reused unchanged, with
  their classes made `partial` in `obj/` (`CompileCheck~/HarnessStubs.targets`). `Stubs/PlayModeStubs.cs` adds only what
  this test needs. GameCI's Unity compile remains the authority.
  `.github/workflows/unity-free-checks.yml` runs on every push and pull request, but it does not run this script yet. This
  track may not edit that file, so `unity-tests.yml` runs it instead, on `claude/**` pushes and manual runs.
