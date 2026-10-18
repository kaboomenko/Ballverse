# Ballverse.UI — app shell (UI Toolkit)

Release-1 interface of Ballverse (spec §4 with the release-1 scope: three free modes, shop with three products).
Assembly `Ballverse.UI` references `Ballverse.Core` only; the app (`Ballverse.App`) binds real view models.

## Folders

| Folder | Contents |
|---|---|
| `Model/` | Engine-free logic (no `UnityEngine`): localization tables and plural rules, navigation stack and back order, the settings-sheet model built from `ParamSchema`, sheet detents, timeline math, chrome visibility, glass tier policy and WCAG contrast math, appearance resolution, display formats. Covered by `Tests~/`. |
| `ViewModels/` | Interfaces the screens bind to (`IBallverseViewModels` and one interface per screen) plus their data classes. |
| `Runtime/` | Unity side: shell, navigation, components (bottom sheet, dialogs, toasts, slider, switch, segmented control, chips, vector icons), glass, theme, safe area, screens. |
| `Fakes/` | `FakeViewModels` (timed fake behaviour for every screen) and `UiPreviewBootstrap`. |
| `Resources/BallverseUI/` | UXML layouts, USS styles, runtime theme (`.tss`), string tables (`Localization/en.json`, `ru.json`), glass shader. |
| `Tests~/` | `dotnet test` project for `Model/` and the shipped resources (ignored by Unity because of the `~`). |

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
`IShopViewModel`, `IAppSettingsViewModel`, `ITrackingConsentViewModel`, `ISystemAppearance`. Each raises `Changed`
on the main thread; actions are methods. Navigation that follows an action is done by the screen (e.g. after
`ToVideo()` the simulation screen pushes the studio). The export flow is driven entirely by
`IExportViewModel.Stage` (settings sheet → rewarded gate dialog → progress → done / failure).

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
   pixel the tint opacity is the minimum that keeps the theme's text colour at **WCAG AA 4.5:1** over the blurred
   backdrop (`GlassContrast.RequiredTintAlpha`, never below the theme's 30–40% base). Only `Graphics.Blit` with a
   material is used, so it works with URP and without camera hooks.
2. **Each surface** (class `bv-glass`) shows that one texture as its background image, sized to the panel and offset by
   the element's `worldBound` every frame (`GlassSurfaces`, background-size / background-position API), so it displays
   exactly what is behind it. Rounded corners, the crisp 1 px edge (lighter top-left border) and all content stay
   native UI Toolkit, and animated sheets stay aligned because the offsets follow `worldBound`.

| Situation | Result |
|---|---|
| Tier A / B | Full: 1/4 buffer, refraction, highlight, adaptive tint |
| Tier C | Simplified: 1/8 buffer, blur + tint, no refraction / highlight |
| Reduce Transparency (iOS system flag via `ISystemAppearance`, own switch in Settings) | `bv-glass-mode-solid`: matte surfaces at 92% opacity, no backdrop pass |
| No backdrop source (e.g. tab bar over the gallery) or shader unsupported | `bv-glass-mode-frosted`: translucent theme tint over the app background |
| More than 8 visible surfaces | extra surfaces get `bv-glass--overflow` (frosted) |

Prices, purchase buttons and long lists are never glass (solid surfaces, spec §4.5). Cost: one prefilter, 2–6
dual-filter passes and one compose pass at ≤ 1/4 resolution per frame, only while a glass surface is visible over a
source. The ≤ 1.5 ms tier-B budget must be measured on device (spec spike S3).

## Theming, layout, accessibility

- `Tokens.uss`: 4 pt grid, 16/24 paddings, 44 pt touch targets, type sizes 12/14/17/22/34; `bv-text-115` /
  `bv-text-130` scale the type variables (system text size up to 130%).
- `Theme.uss`: `bv-theme-dark` (default) / `bv-theme-light`, `bv-contrast`; one accent colour.
- The panel is scaled to a 390-unit-wide reference, so panel units ≈ points.
- Safe area: layouts contain spacer elements `bv-safe-top` / `bv-safe-bottom` sized by `SafeAreaController`; the
  simulation renders edge to edge underneath.
- Reduce motion (`bv-reduce-motion`): all tweens complete instantly, USS transitions are zeroed.
- Icons are drawn as vectors (`Painter2D`, 2 pt stroke, round caps) in the current text colour: no image assets.

## Localization (spec §4.7)

- Tables: `Resources/BallverseUI/Localization/{en,ru}.json`, flat `key → string` or `key → {one, few, many, other}`
  (CLDR plurals), `{0}` placeholders. Language: Settings choice, else the device language when supported, else English;
  switching at runtime re-applies every key.
- Layouts contain keys only: `text="#gallery.title"`. Code uses `Ctx.L(key)` / `LocalizedText`; view models pass
  `LocalizedText` (key + args, or a literal for user content and store prices).
- Conventions: `params.{id}` and `params.{id}.{option}` (from `ParamDef.LabelKey` / `OptionParam.OptionLabelKey`),
  `params.group.*`, `unit.*` (patterns such as `"{0} м/с"`), `mode.{modeId}.name|description`.
  Mode modules add the keys of their own parameters to both tables; a missing key shows a readable stand-in and is
  listed in `Localizer.MissingKeys`.

## Input

System back (Android back button, Escape in the editor) uses the legacy Input Manager when enabled, otherwise the
Input System package (`BALLVERSE_INPUT_SYSTEM`, set by the asmdef version define).

## Tests

```bash
dotnet test "client/Assets/Ballverse/UI/Tests~/Ballverse.UI.Model.Tests"
```

Compiles `Core` + `UI/Model` without UnityEngine (keeps the model engine-free) and checks: plural rules, fallback,
navigation and back order, settings-sheet live / structural rules, sheet detents, magnets, trimming, chrome policy,
safe-area math, glass tiers and AA contrast, appearance resolution, formats, and that both string tables have the same
keys and contain every UXML key and every common parameter label / option.
