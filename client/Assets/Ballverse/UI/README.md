# Ballverse.UI — app shell (UI Toolkit)

Release-1 interface of Ballverse (spec §4 with the release-1 scope: three free modes, shop with three products).
Assembly `Ballverse.UI` references `Ballverse.Core` only; the app (`Ballverse.App`) binds real view models.

## Folders

| Folder | Contents |
|---|---|
| `Model/` | Engine-free logic (no `UnityEngine`): localization tables and plural rules, navigation stack and back order, the settings-sheet model built from `ParamSchema`, sheet detents, timeline math, chrome visibility, glass tier policy and WCAG contrast math, appearance resolution, display formats. Covered by `Tests~/`. |
| `ViewModels/` | Interfaces the screens bind to (`IBallverseViewModels` and one interface per screen) plus their data classes. |
| `Runtime/` | Unity side: shell, navigation, components (bottom sheet, dialogs, toasts, slider, switch, segmented control, chips, vector icons), glass, theme, safe area, screens. |
| `Fakes/` | Assembly `Ballverse.UI.Fakes` (editor and development builds only: define constraint `UNITY_EDITOR \|\| DEVELOPMENT_BUILD \|\| BALLVERSE_UI_FAKES`): `FakeViewModels` (timed fake behaviour for every screen) and `UiPreviewBootstrap`. Not in release players. |
| `Resources/BallverseUI/` | UXML layouts, USS styles, runtime theme (`.tss`), string tables (`Localization/en.json`, `ru.json`), glass shader. |
| `Tests~/` | `dotnet test` project for `Model/`, the shipped resources and documented static checks of Unity-side fixes (ignored by Unity because of the `~`). |
| `Tests/Editor/` | Unity EditMode tests (`Ballverse.UI.Tests`): the shell on the fake view models (shell size, hashtag user text, tool-sheet switching, back order, export stages, teardown, window layout). GameCI's EditMode run picks them up. |

## Using the shell from the app

```csharp
var ui = BallverseUIRoot.Create(viewModels);   // creates UIDocument + PanelSettings at runtime
ui.Shell.SetDeviceTier(tier);                  // glass quality (spec §4.5, §8.1)
ui.Shell.OpenMode("ring_escape");              // first launch goes straight to M01 (spec §4.3)
ui.Shell.ShowAdNotice(() => ads.ShowInterstitial());   // 0.5 s "Ad" plaque (spec §5.3.2 IN8)
ui.Shell.ShowTrackingPrePrompt();              // own ATT explanation before the system dialog (spec §5.6)
ui.Shell.Navigator.ExitRequested += MoveTaskToBack;    // Android back on the Modes root
```

`IBallverseViewModels` exposes: `IGalleryViewModel`, `ISimulationViewModel` (with `IParamPanelViewModel`,
`IPresetsViewModel`), `IStudioViewModel` (with the five tool interfaces), `IExportViewModel`, `IProjectsViewModel`,
`IShopViewModel`, `IAppSettingsViewModel`, `ITrackingConsentViewModel`, `ISystemAppearance`, `IQuestCelebrationViewModel`. Each raises `Changed`
on the main thread; actions are methods. Navigation that follows an action is done by the screen (e.g. after
`ToVideo()` the simulation screen pushes the studio). The export flow is driven entirely by
`IExportViewModel.Stage` (settings sheet → rewarded gate dialog → progress → done / failure; at launch
`ResumeOffer` shows "Продолжить экспорт" after the system killed the app, spec §3.8).

Studio values use the Core types directly: `Ballverse.Core.Projects.PipShape / TextPlate / TextAlign / FitBackgroundKind`,
`Ballverse.Core.Layout.SlotKind / FloatVec2 / FloatRect`, the split as the Core ratio snapped by
`Core.Layout.RatioMagnets.Snap` (every 1% step reachable, magnets exactly 1/3, 1/2, 2/3), the V offset as
`UserVideoTiming.OffsetUs` (may be negative) and the finale slow-mo as 0 or 1–3 s (`ClipTiming` bounds). Segment
indices map to enums through `Model/Studio/StudioValues` (never by cast). On release of a PiP drag the view model
snaps the window with `Core.Layout.PipMagnets`.

Editor preview without the rest of the app: add `UiPreviewBootstrap` to any GameObject in an empty scene and press Play.

## Screens

Gallery (3 mode cards with looping preview slot, badges, no locks) · Simulation (edge-to-edge render target, glass top
bar, floating glass bottom bar ⟲ ⏯ 🎲 ⚡ «В видео», long press 🎲 → style / duration sheet, result card with the last
5 runs, first-run hints and quest, auto-hiding bars) · Settings sheet (generated from the mode's `ParamSchema`;
group switch; slider / ∞ / toggle / segmented / chips / colour / colour list / names / seed editors; live edits apply
at once, structural edits while running are collected and applied with "Перезапустить"; reset to preset) · Presets
sheet · Studio (9:16 preview, play / scrub / undo / redo / fullscreen with platform zones, timeline S / V / text /
music with trim handles and V shift, glass tool tabs Макет · Видео · Симуляция · Текст · Звук with their sheets,
"+ Добавить своё видео" and "Без видео") · Export (settings sheet, rewarded gate on a solid surface, full-screen glass
progress that keeps the screen awake, done screen with save / share / platform buttons / upsell) · Projects (projects
and clips grid, long-press menu: open, rename, duplicate, export again, delete; empty state) · Shop (release-1
products on solid cards, savings, owned / pending states, restore, terms and privacy) · Settings (sound, haptics,
quality, language, theme, accessibility, watermark, auto-save, storage, restore, privacy, rate, feedback, about) ·
Storage · About · ATT pre-prompt.

## BallverseGlass (spec §4.5) — chosen approach

UI Toolkit in Unity 6 has no long-stable way to run a custom shader per element (the UI Shader Graph route of
Unity 6.3 is new and not used here). The material is therefore split:

1. **One backdrop pass per frame** (`GlassBackdropRenderer`, shader `Hidden/Ballverse/Glass`): the texture behind the
   UI — the simulation render target or the studio preview, which the UI already shows — is mapped to screen space
   ("cover" fit of its slot), downsampled to **1/4** of the screen, blurred with the **dual-filter (Kawase)** chain
   (1–3 iterations chosen from the 8–32 px radius), then composed for the rectangles of all visible glass surfaces
   (≤ 8): **refraction** (sample offset along the rounded-rectangle SDF normal near the edge, 0–12 px), a soft **edge
   highlight** whose direction follows device tilt by ±4° (off under reduced motion) and the **adaptive tint**: per
   pixel the tint opacity is the minimum that keeps the *weakest* text colour used on glass at **WCAG AA 4.5:1** over
   the blurred backdrop (never below the theme's 30–40% base). On glass the secondary / tertiary text colours are
   opaque (`Theme.uss` redefines `--color-text-2/-3` inside `.bv-glass`, values in `Model/Glass/GlassTextPalette`,
   kept in sync by a test), so every glass text colour keeps 4.5:1, not only the primary one. Only `Graphics.Blit` with a
   material is used, so it works with URP and without camera hooks.
2. **Each surface** (class `bv-glass`) shows that one texture as its background image, sized to the panel and offset by
   the element's `worldBound` every frame (`GlassSurfaces`, background-size / background-position API), so it displays
   exactly what is behind it. Rounded corners, the crisp 1 px edge (lighter top-left border) and all content stay
   native UI Toolkit, and animated sheets stay aligned because the offsets follow `worldBound`.

| Situation | Result |
|---|---|
| Tier A / B | Full: 1/4 buffer, refraction, highlight, adaptive tint |
| Tier C | Simplified: 1/8 buffer, blur + tint + edge highlight, no refraction (spec §4.5) |
| Reduce Transparency (iOS system flag via `ISystemAppearance`, own switch in Settings) | `bv-glass-mode-solid`: matte surfaces at 92% opacity, no backdrop pass |
| No backdrop source (e.g. tab bar over the gallery) or shader unsupported | `bv-glass-mode-frosted`: translucent theme tint over the app background |
| More than 8 visible surfaces | extra surfaces get `bv-glass--overflow` (frosted) |

Prices, purchase buttons and long lists are never glass (solid surfaces, spec §4.5). Cost: one prefilter, 2–6
dual-filter passes and one compose pass at ≤ 1/4 resolution per frame, only while a glass surface is visible over a
source. The ≤ 1.5 ms tier-B budget must be measured on device (spec spike S3).

## Theming, layout, accessibility

- `Tokens.uss`: 4 pt grid, 16/24 paddings, 44 pt touch targets, type sizes 12/14/17/22/34; `bv-text-115` /
  `bv-text-130` scale the type variables (system text size up to 130%).
- `Theme.uss`: `bv-theme-dark` (default) / `bv-theme-light`, `bv-contrast`; one accent colour. `ThemePreference.Dark`
  is the zero value and the first-run default; light follows the device only when "Системная" is chosen.
- Touch targets: every tappable control is at least 44 × 44 pt (`--touch`); settings rows with a switch toggle on a tap
  anywhere in the row; timeline handles are 44 pt hit areas centred on the segment edges.
- The panel is scaled to a 390-unit-wide reference, so panel units ≈ points.
- Safe area: layouts contain spacer elements `bv-safe-top` / `bv-safe-bottom` sized by `SafeAreaController`; the
  simulation renders edge to edge underneath.
- Reduce motion (`bv-reduce-motion`): all tweens complete instantly, USS transitions are zeroed.
- Icons are drawn as vectors (`Painter2D`, 2 pt stroke, round caps) in the current text colour: no image assets.

## Localization (spec §4.7)

- Tables: `Resources/BallverseUI/Localization/{en,ru}.json`, flat `key → string` or `key → {one, few, many, other}`
  (CLDR plurals), `{0}` placeholders. Language: Settings choice, else the device language when supported, else English;
  switching at runtime re-applies every key.
- Layouts contain keys only: `text="#gallery.title"`. `#key` texts are bound once, right after a layout is
  instantiated (`UiContext.InstantiateLayout`), never inside text fields and never on sheets / dialogs that already hold
  user content: "#satisfying" in a text layer stays user text. Code uses `Ctx.L(key)` / `KeyLabel` / `LocalizedText`;
  view models pass `LocalizedText` (key + args, or a literal for user content and store prices).
- Numbers, dates and sizes use the device locale (`Localizer.FormatCulture`, from `SystemAppearanceState.LanguageTag`
  or `CultureInfo.CurrentCulture`), strings the interface language. Without a native `ISystemAppearance` the device
  language comes from `Application.systemLanguage`.
- Conventions: `params.{id}` and `params.{id}.{option}` (from `ParamDef.LabelKey` / `OptionParam.OptionLabelKey`),
  `params.group.*`, `unit.*` (patterns such as `"{0} м/с"`), `mode.{modeId}.name|description`.
  Mode modules add the keys of their own parameters to both tables; a missing key shows a readable stand-in and is
  listed in `Localizer.MissingKeys`.

## Input and gestures

- System back (Android back button, Escape in the editor) uses the legacy Input Manager when enabled, otherwise the
  Input System package (`BALLVERSE_INPUT_SYSTEM`, set by the asmdef version define).
- iOS: a swipe from the left edge (≤ 20 pt from the edge, ≥ 72 pt to the right) is back (`EdgeSwipeBack`,
  `UiShell.EdgeSwipeBackEnabled`, on by default on iOS); in the interactive game it pauses with a confirmation; on a tab
  root it does nothing.
- Interactive arena: presses within 24 pt of a screen edge or in the bar strips never reach the game
  (`ArenaInputPolicy`, spec §2.6 / §4.3).
- Studio preview (`PreviewGestures`): drag the PiP window (snapped by Core magnets on release), drag / pinch a slot to
  pan / zoom its content, double tap resets the slot. Timeline: trim S / V, shift V (also before S), drag text spans
  (body = move, edges = in / out) and the music bar (start inside the track).

## Screen readers (spec §4.8)

Every element registered with `Tap.On` / `TapOrLongPress` is a button for VoiceOver / TalkBack; icon-only buttons get
labels by element name (`A11yLabels`, keys `a11y.*`), others are read by their text; screen, sheet and dialog titles
are headings. `ScreenReaderBridge` mirrors the top-most modal layer into Unity 6's `AssistiveSupport.activeHierarchy`
while a screen reader is on (iOS / Android). It needs the built-in Accessibility module
(`com.unity.modules.accessibility` in `Packages/manifest.json`; the asmdef sets `BALLVERSE_ACCESSIBILITY` when it is
present). Without the module the bridge is a no-op; the same module supplies the system font scale fallback
(`AccessibilitySettings.fontScale`).

## Tests

```bash
dotnet test "client/Assets/Ballverse/UI/Tests~/Ballverse.UI.Model.Tests"
```

Compiles `Core` + `UI/Model` without UnityEngine (keeps the model engine-free) and checks: plural rules, fallback,
navigation and back order, settings-sheet live / structural rules, sheet detents, split ratios against Core
`RatioMagnets`, slow-mo values against Core `ClipTiming`, segment → Core enum mapping, trimming and V offsets, preview
and edge gestures, arena dead zone, chrome policy, safe-area math, glass tiers and AA contrast of every glass text
colour, appearance resolution (dark default, language fallback), device-locale formats, and that both string tables
have the same keys and contain every UXML key, every key literal in `Runtime/` and every common parameter label /
option. Static checks keep the Unity-side fixes in place (inline sizing of the shell container, key binding only on
fresh layouts, 44 pt touch targets, no roadmap teasers or sale wording).

The EditMode tests in `Tests/Editor` run in Unity (GameCI). `Tests~` is not wired into a workflow yet (the workflow
files belong to the infrastructure track).

## Open issues (outside this assembly)

- CI: `Tests~` (dotnet) is not run by any workflow yet; add a `dotnet test "client/Assets/Ballverse/UI/Tests~/Ballverse.UI.Model.Tests"` step
  (infrastructure track). The EditMode tests in `Tests/Editor` run with GameCI's EditMode job once it is on main.
- Screen readers and the system font scale need `com.unity.modules.accessibility` in `client/Packages/manifest.json`
  (not listed in the bootstrap manifest today); without it they are compiled out.
- Mode names: the tables follow spec §2.8 ("Геометрия отскоков", "Хеликс-дроп"); `Core.ModeCatalog` on the
  core-integration branch carries its own `NameRu` values, so one of the two should become the single source.
- Not verified on a device: glass cost and look, gesture feel (PiP drag, pinch, edge swipe), VoiceOver / TalkBack
  frames. No Unity editor is available in the development container.
