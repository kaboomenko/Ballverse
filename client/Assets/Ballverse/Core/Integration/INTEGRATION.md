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
| `ToPlatformUiMasks(values)` | `platform_safe_zones` → `PlatformUiMasks` (Config already merged defaults; never throws, see below) |
| `LoadRemotePresets(values, schemaForMode)` | `remote_presets` → `RemotePresets.Load`; presets of unreleased modes are rejected |
| `ToMelodies(values)` / `MelodyOptionIds(melodies)` | `remote_melodies` → Audio `Melody` (validated by Audio's rules; built-in ids reserved); melody option ids for `CommonParams.Build` |
| `values.WatermarkEnabled` (no adapter) | `remoteWatermarkEnabled` argument of `Ballverse.Core.Projects.WatermarkPolicy.Resolve` |

With `RemoteConfigValues.Defaults` every adapter reproduces the consumer's built-in defaults exactly (tested).

Decisions (QA round 1):

- **One watermark rule.** The canonical rule is `Ballverse.Core.Projects.WatermarkPolicy.Resolve(projectToggle,
  hasNoWatermarkEntitlement, remoteWatermarkEnabled)` (Projects module, on main). Integration has no watermark class (a third
  `WatermarkPolicy` made `using Ballverse.Core.Integration; using Ballverse.Core.Projects;` fail with CS0104);
  `IntegrationOwnershipTests` keeps Integration type names unique. Monetization keeps only the presentation
  (`WatermarkRow` / `GetRow`); merge task for the Monetization track: make `Monetization.WatermarkPolicy.ShouldApply` delegate to
  `Projects.WatermarkPolicy.Resolve` (same rule, the entitlement set reduced to `Contains(NoWatermark)`).
- **One Ad policy mapper.** `ToAdPolicyConfig` is the only `RemoteConfigValues → AdPolicyConfig` mapping. Merge task for the
  unity-services track: call `RemoteConfigAdapters.ToAdPolicyConfig` and delete `AdPolicyConfigMapper` (`Services/Ads/AdPolicyStore.cs`).
- **Platform ids.** Config treats ids equal ignoring case *or* equal after `Trim().ToLowerInvariant()` (Layout's
  normalization) as duplicates (`PlatformSafeZone.SameId`, also enforced by `RemoteConfigBuilder`), so every snapshot builds
  a `PlatformUiMasks`; the adapter additionally skips any id that would still collide (first wins) instead of throwing.
  Example: U+212A KELVIN SIGN and `k` are distinct under `OrdinalIgnoreCase` but one id in Layout.
- **Remote melodies only add.** An entry whose id equals a built-in melody id (`CommonParams.Melodies`) is skipped and
  reported, so a project's `"melody"` always means the same notes; Config keeps its own copy of the melody rules and the
  adapter re-validates with `Melody.TryCreate` (a randomized test keeps the two in agreement).

## Release-1 catalog

`ModeCatalog.Released` = M01 `ring_escape`, M02 `geometry_bounce` ("Geometry Bounce" / "Геометрия отскоков"), M05 `helix_drop`
("Helix Drop" / "Хеликс-дроп", the RU name follows the EN one as in spec §2.8 and changes with it if the trademark check fails).
All three are free by default (`FreeByDefault` ⊆ `InRelease`, enforced by `ModeInfo`). Codes are stable; the retired ids
`grow_bounce` / `spiral_drop` are not reused. M05 is interactive with automatic controls (`HasAutoControl`), so generation
selects its duration unless the caller sets `Interactive` for the player control; an interactive session scores with w1 = 0
against `QualityThreshold − w1` (release-1 card M05: "w1 … равен 0 в «Игроке»"), so pass/fail equals the auto profile with
fitDuration = 1 while `Breakdown.Total` follows the spec formula. M01/M02 have winner variants (`HasWinner`). Director goals
(release-1 spec §2.5, `GenerationSessionOptions.SupportsDirectorGoal`): "last second" for M01 (release 1) and roadmap M03/M06,
"comeback" for M03/M06 only.

Merge task for the Monetization track (its branch still assumes M01–M05 free): derive `MonetizationConfig.ReleasedModes` from
`ModeCatalog.Released` (drop its own `s_release1Modes`), and update its tests and docs to `FreeByDefault` = {M01, M02, M05};
8 Monetization tests fail after merging until then. Add a cross-module test that `MonetizationConfig.Release1` equals
`ModeCatalog.Released` and `RemoteConfigValues.Defaults.EffectiveFreeModes`.
Config ignores unreleased modes in `free_modes` / `gallery_order`; see `REMOTE_CONFIG_KEYS.md` for enabling a mode later.

## Replay header `physicsBackend`

`PhysicsBackend` follows spec §7.4 on main: 0 = none, **1 = Box2D v3** (reserved for wave-3 roadmap modes; the value of the
accepted Replay module), **2 = Core.Sim** (added for release 1). The values are persisted and never renumbered. Release-1
replays write 2 (`PhysicsBackend.CoreSim`); clip recompute selects the solver by this byte.
`ReplayResidualQaTests.PhysicsBackendNumberingMatchesTheSpec` pins bytes 0 / 1 / 2 in the written header (§8.5), and
`ReplayWriterTests.HeaderLayoutMatchesSpec` expects byte 2 for a release-1 header. The golden file
`TestData/Replay/sample_v1.bvr` is unchanged: its header byte 1 means Box2D v3, and `ReplayTestUtil.Header` uses `Box2DV3`.
(QA round 2: round 1 had briefly renumbered to 1 = Core.Sim from a superseded spec draft; reverted.)

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
(body id −1, exempt from the per-body limit). `Convert` never throws on replay data: an event whose tick does not fit an int
(a corrupted file; the reader bounds ticks only by the u32 duration) is skipped and counted.

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
