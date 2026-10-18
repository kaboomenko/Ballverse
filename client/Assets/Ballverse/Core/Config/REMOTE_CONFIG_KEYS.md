# Remote Config keys (spec §9.3) — one owner, one schema per key

The Config module (`Ballverse.Core.Config`) is the **only** parser of the §9.3 keys. `RemoteConfigParser.Parse` turns the raw
Firebase strings into an immutable `RemoteConfigValues`; `RemoteConfigWriter.ToRaw` writes the same schema back (in-app
defaults, console templates). Consumer modules never parse these keys: they take typed values through
`Ballverse.Core.Integration.RemoteConfigAdapters` (see `../Integration/INTEGRATION.md`).

General rules (details in the `RemoteConfigParser` XML docs): missing/blank → default; malformed JSON or wrong type → default +
Error; out-of-range numbers → clamped + Warning; invalid list/map elements → dropped + Warning; integers must be integral
(`3.0` and `1e2` are accepted, `2.5` and `"3"` are not inside JSON); numbers inside JSON must be JSON numbers (quoted numbers are
rejected); duplicate property names make the JSON invalid. Issues are capped per key (≤ 2 × 20 + 1).

| Key | Schema (raw string) | Default | Range / rules | Consumer (adapter) |
|---|---|---|---|---|
| `free_modes` | JSON array of mode codes (`"M01"`, `"m1"`) or ids (`"ring_escape"`) | `["M01","M02","M05"]` | extension only: the catalog's FreeByDefault modes are always appended; **modes not in this build's release are ignored** (Info `UnreleasedMode`) | Monetization (`EffectiveFreeModes`) |
| `modes_disabled` | JSON array of modes | `[]` | any catalog mode (a future mode may be pre-disabled) | gallery / Monetization |
| `gallery_order` | JSON array of modes | `["M01","M02","M05"]` | always a permutation of the **released** modes; unreleased ignored, missing released appended | gallery UI |
| `interstitial_every_n_games` | integer | `3` | 1–20 | Ads `AdPolicyConfig` (`ToAdPolicyConfig`) |
| `interstitial_min_interval_sec` | integer | `60` | 0–600 | Ads |
| `interstitial_grace_games` | integer | `0` | 0–50 | Ads |
| `rewarded_export_enabled` | `true`/`false`/`1`/`0` | `true` | | Ads |
| `rewarded_wait_timeout_sec` | integer | `5` | 1–30 | Ads |
| `rewarded_retry_window_min` | integer | `10` | 0–60 | Ads |
| `paid_mode_trial_enabled` | bool | `true` | no effect in release 1 (no paid modes) | Monetization (later) |
| `paid_mode_trial_seconds` | integer | `30` | 10–120 | Monetization (later) |
| `export_bitrates` | `{"720p30":6000,"720p60":10000,"1080p30":10000,"1080p60":16000}` (kbps, video only) | spec §3.8 | 1000–50000 kbps; field-wise merge | Timeline `ExportBitrateTable` in bit/s, audio stays 192 kbps (`ToExportBitrates`, `DefaultExportSettings`) |
| `export_default_fps_by_tier` | `{"A":60,"B":60,"C":30}` | as shown | 30 or 60; field-wise merge | Timeline `ExportSettings` (`DefaultExportSettings`) |
| `generation_weights` | `{"<mode>": {"w1":…,"w2":…,"w3":…,"w4":…,"w5":…,"densityMin":…,"densityMax":…,"qualityThreshold":…}}` or `{"<mode>":[w1,w2,w3,w4,w5]}` | `{}` (built-in profiles) | w1…w5 **all together or none**, each 0–100; densityMin ≥ 0, densityMax > 0, min ≤ max; \|qualityThreshold\| ≤ 1000; at least one known field; entry-wise merge | Generation `SpectacleProfileTable` (`ToSpectacleProfiles`, merged over the built-in profile; absent fields keep built-in values) |
| `generation_budget_by_tier` | `{"A":{"candidates":24,"ms":1500},"B":{…},"C":{…}}` | A 24/1500, B 16/2500, C 8/3500 | candidates 1–64, ms 100–10000; field-wise merge | Generation `GenerationBudgetTable` (`ToGenerationBudgets`) |
| `tier_overrides` | `{"<device model>":"A"\|"B"\|"C"}` | `{}` | entry-wise merge | device tier detection (Unity) |
| `platform_safe_zones` | `{"tiktok":[{"x":0,"y":0,"w":1,"h":0.08},…],…}` (normalized 9:16 frame) | tiktok / reels / shorts approximations | rects clipped to the frame; `[]` clears a platform; entry-wise merge | Layout `PlatformUiMasks` (`ToPlatformUiMasks`) |
| `remote_presets` | JSON array of preset objects (Params `PresetJson` form) | `[]` | each element must be an object with finite numbers (read as doubles, like Params) | Params `RemotePresets.Load` (`LoadRemotePresets`; presets of unreleased modes rejected) |
| `remote_melodies` | `[{"id":"…","name":"…","notes":[{"pitch":64,"durWeight":1,"velocity":0.8},…]}]` | `[]` | pitch integer 0–127, durWeight finite > 0 (as float), velocity 0–1; a melody with any bad note is dropped; unique ids | Audio `Melody` (`ToMelodies`, `MelodyOptionIds`) |
| `text_templates` | `{"<lang>":[{"id":"…","text":"…"},…]}` | `en`, `ru` | entry-wise merge; `[]` clears a language | studio text layers (Unity) |
| `watermark_enabled` | bool | `true` | `false` removes the watermark for everyone | `WatermarkPolicy.ShouldShow` |
| `rate_prompt_after_exports` | integer | `2` | 1–20 | rating prompt (Unity) |

Schema decisions taken at integration (the module branches had diverged):

- `generation_budget_by_tier` uses `ms` (Generation's former `timeMs` field is no longer read anywhere).
- `generation_weights` is the union of the two former schemas: Config's `[w1..w5]` / `{w1..w5}` plus Generation's
  `densityMin` / `densityMax` / `qualityThreshold`. Weights stay all-or-none (a weight vector is tuned as a whole); the three
  tuning fields are individually optional. Weight and threshold bounds are Generation's (`SpectacleWeights.MaxWeight`,
  `SpectacleProfile.MaxAbsThreshold`), now `RemoteConfigLimits.WeightMax` / `QualityThresholdAbsMax`.
- `export_bitrates` uses Config's kbps keys (`"1080p60"`); Timeline's former `{"video1080p60Bps":…,"audioBps":…}` payload is gone.
  The audio bitrate has no key.
- `remote_presets` numbers are stored at double precision (the former decimal pass lost values below 1e-28).

How a later release enables more modes: set `inRelease` (and, for a free mode, `freeByDefault`) on the mode's row in
`Foundation/ModeCatalog.cs` and ship the build. Config then accepts the mode in `free_modes` / `gallery_order`, adds it to the
default gallery order, and (if free by default) to the default free set. Remote Config alone can never make an unreleased mode
available; it can only disable a released one (`modes_disabled`).
