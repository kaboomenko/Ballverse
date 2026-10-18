# Render event contract (VFX, flashes, splats, shake)

Status: release 1. Code: `Ballverse.Core.RenderGeometry` — `RenderEvent`, `RenderEventLog`, `VfxSystem`, `RenderComposer`.
Spec: §7.4 rule 7 (cosmetics seeded from tick + event id), §7.7 (own particle system, preview = export), mode cards M01 / M02 / M05 (VFX lines).

## 1. Why events

The render stream (`.bvs`) holds the state of bodies per tick. Effects (sparks, shards, confetti, ring flashes, splats, camera shake) are
**not** bodies: they are derived by the renderer from a separate, tiny list of *render events*. Every effect is a pure function of its
event and its age, so

- any tick can be drawn without history (scrubbing, export resume, export at another fps), and
- live preview and export show the same particles (same seeds, same closed-form motion).

Mode / simulation code emits the events; it never spawns particles itself.

## 2. The struct

```csharp
public struct RenderEvent
{
    public int Tick;             // simulation tick of the event (effects start here)
    public RenderEventType Type; // Hit 0, Break 1, Eliminate 2, Finish 3, Spawn 4 (= ReplayEventType numbers), Splat 16, Escape 17, Pierce 18, Grow 19
    public int Id;               // unique among the events of the same tick; seeds the effect
    public float X, Y;           // world metres, render-stream space
    public float NormalX, NormalY; // unit direction (Hit: contact normal towards the ball), 0 = none
    public byte ColorIndex;      // palette index (see PaletteSlots), usually the ball's colour
    public float Intensity;      // 0..1, normalized by the mode (Hit: impulse / mode's loud impulse)
    public float Size;           // metres: ball radius (Hit, Spawn, Grow), blot radius (Splat), burst radius (Finish/Escape)
    public int BodyId;           // ball the effect attaches to (Hit squash, Spawn / Grow pulse) or -1
    public int TargetBodyId;     // body that was hit / gets the splat or -1
    public int Feature;          // polyline segment index / ring arc index of the target, -1 = unknown
    public float LocalAngle;     // body-local contact angle on a ring / platform (radians, ring-gap convention)
    public int ShapeId;          // Break: shape-table id of the broken body, -1 = none
    public float Angle;          // Break: body angle at the tick (radians)
    public float Scale;          // Break: dequantized body scale at the tick
}
```

Use `RenderEvent.Create(...)` (all optional fields set to "none"), `RenderEvent.Hit(...)`, `RenderEvent.Break(...)`,
`RenderEvent.Spawn(...)` and `RenderEvent.Grow(...)`.

### Rules

1. **Order.** Events are appended to a `RenderEventLog` in non-decreasing `Tick`. Within a tick, emit them in the solver's sorted event
   order (§7.4 rule 4) and give each a distinct `Id` — e.g. its index in that tick's sorted `SimEvent` list, plus a fixed offset per
   event kind (`Break` 1000+, `Finish` 2000+…). `RenderEventLog.Add` rejects out-of-order ticks and duplicate ids.
2. **Determinism.** Every field must come from the deterministic simulation state of that tick (never from wall time, frame time or
   `UnityEngine.Random`). The renderer's random stream is `Rng.FromSeed((tick << 32) | id, VfxSystem.VfxStreamId)`.
3. **Space.** Positions are in the same metres as the render stream bodies. Helix Drop: stream space (x along the solver's vertical cut,
   y = height); platform-attached data uses the platform's local azimuth (`LocalAngle`, rotates with the platform).
4. **Colours** are palette indices, not RGB: the theme and `ballColors` can change after recording and effects follow.
5. **Bodies referenced by id** (`BodyId`, `TargetBodyId`) are looked up in the frame being drawn; if the body no longer exists (a ring
   broke in the same tick) the body-attached part is skipped. A `Break` therefore carries the broken body's geometry
   (`ShapeId`, `X/Y`, `Angle`, `Scale`) itself.
6. **Storage.** The live run keeps the log in memory. A saved clip stores it as `clip.bve` (`RenderEventLog.ClipFileName`) next to
   `clip.bvs`: format `BVE1`, 68 bytes per event, XXH64 trailer (`RenderEventLog.WriteTo` / `ReadFrom`). It is part of the derived
   visual cache together with the `.bvs` and follows the same rules: written whenever the `.bvs` is written (same recording pass),
   deleted and rebuilt together with it (re-simulating the `.bvr`, which stays the source of truth, §7.4). So preview and export read
   only `clip.bvs` + `clip.bve` and never recompute physics, as §7.4 promises. The `.bvr` EVNT records cannot replace it: they hold
   only (Δtick, type, bodyId, impulse, extra), while effects need position, normal, local angle, size and the broken shape. A typical
   clip has a few thousand events (≈ 200 KB/min).
   **Open — needs the projects / render-stream owners (outside this module):** today nothing outside the renderer knows the file, so
   `ProjectPaths.IsReservedFileName` does not protect it (project.json could name it as `userVideo.file` / `exports[].file`), the
   storage screen counts it as «Other» and cache clearing does not touch it. One of the two must land before saved clips are relied
   on for VFX:
   - (a) Projects: `ProjectPaths.ClipEventsFile = "clip.bve"` (equal to `RenderEventLog.ClipFileName`), reserved like `clip.bvs`,
     `StorageCategory.RenderCache` in `ProjectRepository`, written / deleted / rebuilt together with `clip.bvs`; §7.4 layout line
     «`clip.bve` события рендера (может отсутствовать — восстанавливается вместе с clip.bvs)»; tests: reserved name, storage category.
   - (b) RenderStream: carry the same 68-byte records as a block of a `.bvs` v2 file, so the layer lives and dies with the stream.
   Either way the record format stays. Until then a missing or corrupt `clip.bve` must be treated like a missing `clip.bvs` (cache
   miss → re-simulate the `.bvr`), never as "no effects".
7. **Window.** The renderer needs the events of the last `VfxSystem.MaxEffectTicks` (180) ticks: `log.GetWindow(tick, 180)`.
8. **Kinds shared with the replay.** `Hit`, `Break`, `Eliminate`, `Finish` and `Spawn` have the same numbers as `ReplayEventType`;
   render-only kinds start at 16. `RenderEventTypes.TryGetReplayType` is the explicit mapping (Escape → Finish, Pierce → Break,
   Splat and Grow → none: their sound comes from the accompanying / causing Hit). Modes that emit both streams emit them from the same `SimEvent`.

## 3. How effects are derived

| Type | Particles (by `impactFx` / `breakFx`) | Body effects | Lifetime |
|---|---|---|---|
| `Hit` | `sparks`: 4–12 streaks along `Normal` ±70°; `confetti`: 6–14 theme-colour rects; `shards`: 4–9 triangles; plus a ripple ring when `Intensity` > 0.25; `off`: nothing | ball `BodyId` squashes along the normal (0.14 s); target `TargetBodyId` flashes: ring arc ±12° around `LocalAngle`, or polyline / polygon edge `Feature` (0.25 s, M02 "грань вспыхивает") | ≤ 0.5 s (confetti 1.3 s) |
| `Break` | `shards`: 12–24 arc shards spread over the solid arcs of the broken ring (M01 1.2 s; in the Helix view horizontal 3D shards, 1 s) or 10–16 triangles for other shapes; `flash`: a big glow; `dissolve`: 24–40 rising dots; `burst`: 20–32 radial streaks. 2D view adds a light wave along the ring | — | ≤ 1.4 s |
| `Spawn` | expanding bubble ring + 6 dots (M02 «пузырь при рождении шара») | the new ball `BodyId` pops (radius up to +22 %, 0.3 s) | 0.4 s |
| `Grow` | — (no bubble: it belongs to birth) | ball `BodyId` pulses (radius up to +22 %, 0.3 s), M02 «пульсация шара при росте» | 0.3 s |
| `Eliminate` | 16-dot puff; with a `Normal` (exit direction) also a streak band behind the ball along it plus 8 fast sparks (M02 «полоса-шлейф при вылете через проём») | — | 0.7 s |
| `Finish` | 70 confetti from `X/Y` upwards + shockwave of radius `Size` (default 3 m) | — | 2.6 s |
| `Escape` | 50 confetti + 6 m shockwave (M01 escape) | — | 2.6 s |
| `Splat` | — | colour blot of radius `Size` on `TargetBodyId` at `LocalAngle` (Helix: on the platform top, rotating with it); 2D: disc at `X/Y` | 3 s |
| `Pierce` | fiery burst (danger → accent colours) + flash | camera shake ±0.12 m for 0.3 s (off with "reduce motion") | 0.7 s |

Counts scale with `Intensity` (where meaningful) and with the quality profile's particle fraction (A/Export 100 %, B 75 %, C 50 %),
and are capped per frame (600 / 400 / 200, newest events kept). Each particle consumes a fixed number of random draws, so a lower
profile shows a subset of the same particles. Motion is closed form: linear drag `k` and gravity `g`,
`p(t) = p0 + v0·(1 − e^{−kt})/k + g·(t − (1 − e^{−kt})/k)/k`.

## 4. Release-1 mapping (what modes emit)

| Mode | Simulation fact (`Core.Sim.SimEvent` / rules) | Render event |
|---|---|---|
| M01 Ring Escape | rings (stream) | «Сжатие»: every ring carries `RenderBodyFlags.FadeOutsideSafeZone`; «Отдаление камеры»: the ball and the next 4 rings carry `RenderBodyFlags.CameraTarget` and the camera is `AutoZoom` + `AutoZoomCentered` |
| | `Hit` ball → ring | `RenderEvent.Hit(tick, id, Point, Normal, ballColor, impulse/loud, ballId, ballRadius, ringBodyId, Feature, LocalAngle)` |
| | ring passed and destroyed | `RenderEvent.Break(tick, id, ringBodyId, ringShapeId, ringX, ringY, ringAngle, ringScale, ringColor, 1)` |
| | escape from the last ring | `Escape` at the ball, `ColorIndex` = ball colour |
| | `BallHit` | `Hit` with `TargetBodyId = -1` (no flash) |
| M02 Geometry Bounce | `Hit` ball → wall segment | `Hit` with `TargetBodyId` = wall body, `Feature` = segment index (face flash) |
| | ball born ("Размножение") | `Spawn` at the new ball, `Size` = its radius, `BodyId` = its id |
| | ball grows (growth variant) | `RenderEvent.Grow(...)` at the ball, `BodyId` = its id, `Size` = its new radius (pulse only) |
| | ball leaves through an opening | `Eliminate` at the exit point, `Normal` = exit direction (outwards), `Size` = ball radius |
| | finale | `Finish` |
| M05 Helix Drop | bounce on platform k | `Hit` (`BodyId` = ball, `Normal` = (0, 1)) + `Splat` (`TargetBodyId` = platform body, `LocalAngle` = contact azimuth in the platform frame = `SimEvent.LocalAngle`, `Size` ≈ 0.6 × ball radius) |
| | platform passed / broken | `Break` with the platform ring's shape and transform |
| | fireball smash | `Pierce` at the contact + `Break` |
| | finish platform | `Finish` |

## 5. Body flag conventions and the Helix Drop stream (the renderer's view contract)

`BodyFlags` bits 5–7 are unnamed in the render stream; the renderer gives two of them a meaning (`RenderBodyFlags`).
**Open — RenderStream owner:** `RenderStream/BodyFlags.cs` still documents bits 5–7 as free for modes; it should name
`CameraTarget = 1 << 5` and `FadeOutsideSafeZone = 1 << 6` and leave only bit 7 free (`RenderBodyFlags` then aliases them). Until then
modes must not use bits 5 and 6 for anything else.

- `CameraTarget` (bit 5): when any visible body of a frame has it, `AutoZoom` fits only those bodies; otherwise it fits every visible
  body (balls and shapes, by their bounding circle), as §2.1 «плавно вписывает все активные тела» says.
- `FadeOutsideSafeZone` (bit 6): in the 2D view the body's alpha falls from 1 to 0.2 as its bounding circle reaches 0 → 4 m beyond
  the circle inscribed in the safe zone (radius 4 m around the arena centre) — M01 «Кольца за краем безопасной зоны рисуются с
  затуханием» (`SafeZoneFade`).
- Bit 7 stays free for modes.

Helix Drop:

- Every platform is one `ShapeKind.Ring` body: body `Y` = height of the platform's **top** face, body `X` = tower axis (0), body angle
  = platform rotation θ_k, ring radius = (R_in + R_out) / 2, thickness = R_out − R_in, gaps = the openings (local angles, CCW, the same
  convention as the solver's `SpinPlatform`). Deadly sectors are a second ring body at the same height with the `Danger` palette slot
  (`PaletteSlots.Danger`) whose gaps are everything but the deadly sectors; give it a **higher id** than its platform so it draws on top.
- The ball is a circle body with `X` = R_path · sin(α) (α = ball azimuth, 0 = towards the viewer) and `Y` = its height; the view puts it
  on the front half of its path circle.
- Passed platforms that stay (destroyPassed = off) carry `BodyFlags.Dead` (drawn muted).
- The fireball ball carries `BodyFlags.Highlight` (glow and accent trail).
- Mode M05 (`helix_drop`) selects the view (`ModeViews.ForMode`, via `ModeCatalog`); camera: vertical follow, the **drawn** ball at
  38 % from the top, only downwards. The anchor includes the projection's front-of-path drop evaluated at the smoothed follow target's
  lateral position, so a ball drifted sideways (`ballDrift`) stays at 38 % too. Set `HelixViewSettings.PlatformSpacing` from the mode's
  `platformSpacing`: «видно до 12 платформ» is a cap, so the default view height is min(zoom-1 height (16 m on 9:16),
  12 × spacing × cos tilt) (`HelixViewSettings.ViewHeightFor`): 1.2 m zooms in to 12.7 m, the 1.8 m default and larger keep 16 m;
  0 keeps 16 m.
- During a transit (ball top above the bottom face of platform k) the ball is drawn over platform k; a platform goes over the ball
  only when its whole slab (`PlatformThickness`, default 0.15 m) is above the ball's top.
