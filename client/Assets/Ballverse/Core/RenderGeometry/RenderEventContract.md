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
    public RenderEventType Type; // Hit, Break, Spawn, Eliminate, Finish, Splat, Escape, Pierce
    public int Id;               // unique among the events of the same tick; seeds the effect
    public float X, Y;           // world metres, render-stream space
    public float NormalX, NormalY; // unit direction (Hit: contact normal towards the ball), 0 = none
    public byte ColorIndex;      // palette index (see PaletteSlots), usually the ball's colour
    public float Intensity;      // 0..1, normalized by the mode (Hit: impulse / mode's loud impulse)
    public float Size;           // metres: ball radius (Hit, Spawn), blot radius (Splat), burst radius (Finish/Escape)
    public int BodyId;           // ball the effect attaches to (Hit squash, Spawn) or -1
    public int TargetBodyId;     // body that was hit / gets the splat or -1
    public int Feature;          // polyline segment index / ring arc index of the target, -1 = unknown
    public float LocalAngle;     // body-local contact angle on a ring / platform (radians, ring-gap convention)
    public int ShapeId;          // Break: shape-table id of the broken body, -1 = none
    public float Angle;          // Break: body angle at the tick (radians)
    public float Scale;          // Break: dequantized body scale at the tick
}
```

Use `RenderEvent.Create(...)` (all optional fields set to "none"), `RenderEvent.Hit(...)` and `RenderEvent.Break(...)`.

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
6. **Storage.** The live run keeps the log in memory. For saved clips the log is either written next to the stream
   (`RenderEventLog.WriteTo`, format `BVE1`, 68 bytes per event, XXH64 trailer) or rebuilt by re-simulating the replay, exactly like
   the render stream (§7.4: the `.bvr` replay stays the source of truth). A typical clip has a few thousand events (≈ 200 KB/min).
7. **Window.** The renderer needs the events of the last `VfxSystem.MaxEffectTicks` (180) ticks: `log.GetWindow(tick, 180)`.

## 3. How effects are derived

| Type | Particles (by `impactFx` / `breakFx`) | Body effects | Lifetime |
|---|---|---|---|
| `Hit` | `sparks`: 4–12 streaks along `Normal` ±70°; `confetti`: 6–14 theme-colour rects; `shards`: 4–9 triangles; plus a ripple ring when `Intensity` > 0.25; `off`: nothing | ball `BodyId` squashes along the normal (0.14 s); target `TargetBodyId` flashes: ring arc ±12° around `LocalAngle`, or polyline / polygon edge `Feature` (0.25 s, M02 "грань вспыхивает") | ≤ 0.5 s (confetti 1.3 s) |
| `Break` | `shards`: 12–24 arc shards spread over the solid arcs of the broken ring (M01 1.2 s; in the Helix view horizontal 3D shards, 1 s) or 10–16 triangles for other shapes; `flash`: a big glow; `dissolve`: 24–40 rising dots; `burst`: 20–32 radial streaks. 2D view adds a light wave along the ring | — | ≤ 1.4 s |
| `Spawn` | expanding bubble ring + 6 dots | — | 0.4 s |
| `Eliminate` | 16-dot puff | — | 0.7 s |
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
| M01 Ring Escape | `Hit` ball → ring | `RenderEvent.Hit(tick, id, Point, Normal, ballColor, impulse/loud, ballId, ballRadius, ringBodyId, Feature, LocalAngle)` |
| | ring passed and destroyed | `RenderEvent.Break(tick, id, ringBodyId, ringShapeId, ringX, ringY, ringAngle, ringScale, ringColor, 1)` |
| | escape from the last ring | `Escape` at the ball, `ColorIndex` = ball colour |
| | `BallHit` | `Hit` with `TargetBodyId = -1` (no flash) |
| M02 Geometry Bounce | `Hit` ball → wall segment | `Hit` with `TargetBodyId` = wall body, `Feature` = segment index (face flash) |
| | ball born ("Размножение") | `Spawn` at the new ball, `Size` = its radius, `BodyId` = its id |
| | ball leaves through an opening | `Eliminate` at the exit point |
| | finale | `Finish` |
| M05 Helix Drop | bounce on platform k | `Hit` (`BodyId` = ball, `Normal` = (0, 1)) + `Splat` (`TargetBodyId` = platform body, `LocalAngle` = contact azimuth in the platform frame = `SimEvent.LocalAngle`, `Size` ≈ 0.6 × ball radius) |
| | platform passed / broken | `Break` with the platform ring's shape and transform |
| | fireball smash | `Pierce` at the contact + `Break` |
| | finish platform | `Finish` |

## 5. Helix Drop stream conventions (the renderer's view contract)

- Every platform is one `ShapeKind.Ring` body: body `Y` = height of the platform's **top** face, body `X` = tower axis (0), body angle
  = platform rotation θ_k, ring radius = (R_in + R_out) / 2, thickness = R_out − R_in, gaps = the openings (local angles, CCW, the same
  convention as the solver's `SpinPlatform`). Deadly sectors are a second ring body at the same height with the `Danger` palette slot
  (`PaletteSlots.Danger`) whose gaps are everything but the deadly sectors; give it a **higher id** than its platform so it draws on top.
- The ball is a circle body with `X` = R_path · sin(α) (α = ball azimuth, 0 = towards the viewer) and `Y` = its height; the view puts it
  on the front half of its path circle.
- Passed platforms that stay (destroyPassed = off) carry `BodyFlags.Dead` (drawn muted).
- The fireball ball carries `BodyFlags.Highlight` (glow and accent trail).
- Mode id `helix_drop` selects the view (`ModeViews.ForMode`); camera: vertical follow, ball at 38 % from the top, only downwards.
