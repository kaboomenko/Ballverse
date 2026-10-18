# Core integration (`Ballverse.Core.Integration`)

Cross-module glue for release 1. Pure C#, deterministic, no Unity, no clock, no network; covered by
`core-tests/Ballverse.Core.Tests/Integration`.

## Remote Config key ownership

The Config module owns every §9.3 key: parsing, validation, defaults and the single JSON schema per key
(`../Config/REMOTE_CONFIG_KEYS.md`). The consumer modules keep only their own persisted-state JSON and take typed values:

- Ads: `AdPolicyConfig.FromJson` reads only its own camelCase file; `AdJson` is used only for Ads state/config files. The
  Default/Min/Max constants are aliases of `RemoteConfigLimits` / Config defaults.
- Generation: `ApplyRemoteJson`, `ToJson` and the remote DTOs were removed from `SpectacleProfileTable` /
  `GenerationBudgetTable`; use `With(...)`.
- Timeline: `ExportBitrateOverrides` is a typed object only; `TryFromRemoteConfig` and its JSON converter were removed.
- Layout, Params, Audio had no §9.3 parser left; they get typed values below.

## Adapters (`RemoteConfigAdapters`)

| Method | From → to |
|---|---|
| `ToAdPolicyConfig(values, local)` | 6 ad keys → `AdPolicyConfig` (IN3 quiet period from `local`, no key) |
| `ToGenerationBudgets(values)` | `generation_budget_by_tier` → `GenerationBudgetTable` |
| `ToSpectacleProfiles(values, builtIn)` | `generation_weights` merged over the built-in profiles → `AdapterResult<SpectacleProfileTable>` (conflicting density bounds reported, built-in kept) |
| `ToExportBitrates(values)` / `DefaultExportSettings(values, tier)` | `export_bitrates` (kbps) + `export_default_fps_by_tier` → `ExportBitrateTable` (bit/s) / `ExportSettings` |
| `ToPlatformUiMasks(values)` | `platform_safe_zones` → `PlatformUiMasks` (Config already merged defaults) |
| `LoadRemotePresets(values, schemaForMode)` | `remote_presets` → `RemotePresets.Load`; presets of unreleased modes are rejected |
| `ToMelodies(values)` / `MelodyOptionIds(melodies)` | `remote_melodies` → Audio `Melody` (validated by Audio's rules); melody option ids for `CommonParams.Build` |
| `WatermarkPolicy.ShouldShow(values, hasNoWatermark, keepWatermark)` | `watermark_enabled` + `NO_WATERMARK` entitlement + export toggle → `FrameRequest.showWatermark` |

With `RemoteConfigValues.Defaults` every adapter reproduces the consumer's built-in defaults exactly (tested).

## Release-1 catalog

`ModeCatalog.Released` = M01 `ring_escape`, M02 `geometry_bounce` ("Geometry Bounce" / "Геометрия отскоков"), M05 `helix_drop`
("Helix Drop" / "Хеликс-дроп", the RU name follows the EN one as in spec §2.8 and changes with it if the trademark check fails).
All three are free by default (`FreeByDefault` ⊆ `InRelease`, enforced by `ModeInfo`). Codes are stable; the retired ids
`grow_bounce` / `spiral_drop` are not reused. M05 is interactive with automatic controls (`HasAutoControl`), so generation
selects its duration unless the caller sets `Interactive` for the player control. M01/M02 have winner variants (`HasWinner`).
Config ignores unreleased modes in `free_modes` / `gallery_order`; see `REMOTE_CONFIG_KEYS.md` for enabling a mode later.

## Replay ↔ Params (`ReplayParams`)

- `CreateHeader(..., sanitizedSet, schema)`: `paramsJson` = `ParamJson.ToCanonicalJson` (compact). **Decision:** replay
  paramsHash (XXH64 of the paramsJson UTF-8) and `ParamJson.ParamsHash` are identical by construction; `Matches` compares them.
- `ReadParams(header, schema)`: never throws on stored data, no tier caps re-applied, missing ids → defaults;
  `IsExact` = the stored hash equals the re-canonicalized one.
- `ParamChange(tick, id, value, schema)`: PARM value = `ParamJson.WriteValue` of the sanitized value; only live parameters
  (structural changes restart the run, spec §2.2). `TryReadParamChange`, `ParamsAtTick` decode.

## Replay → Audio (`ReplaySoundEvents`)

EVNT type → `SoundEventType` by value, tick kept, impulse = `ReplayQuantization.DequantizeImpulse`. Body size and pan come from an
`ISoundBodySource` (the render stream in the app, `UniformSoundBodySource` for tests); returning false marks a bodyless event
(body id −1, exempt from the per-body limit).

## Export plan (`ExportFramePlanner`, `ExportSoundSchedule`)

- `ExportFramePlanner(timeline, settings, request)` composes the frame geometry once and `GetFrame(i)` returns an
  allocation-free `ExportFrame`: pts, `SimSample` (tick, playing/frozen), `VideoSample` (pts/frozen/black/ExpandSim/none),
  `FramePlan`, keyframe and segment flags, cover flag. `VideoAfterEnd.ExpandSim` switches to the layout's L4 form and keeps
  the watermark placement (no mid-video corner jump). 180 s at 60 fps plans in milliseconds with zero allocations (tested).
- `ExportSoundSchedule.Build(timeline, events)`: one `ScheduledSoundEvent` per time the timeline starts the event's tick
  (first pass with speed and slow-mo, Loop / SlowMoReplay repeats, ContinueSim); frames = floor(µs · 0.048), exactly
  tick × 800 at 1×. **Decision:** events repeat with every visible repetition; ticks before `trimIn` or cut by MatchVideo are
  not heard and do not advance the melody (a trimmed clip's melody starts with the clip). Render with
  `EventMixer.RenderOffline(scheduled, output)` over `AudioFrameCount(timeline)` frames.
