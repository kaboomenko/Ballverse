# Mode contract (`Ballverse.Core.Modes`) — release 1

Status: **published contract** for the release-1 modes M01 `ring_escape` (this track), M02 `geometry_bounce`
(`Core/Modes/GeometryBounce`) and M05 `helix_drop` (`Core/Modes/HelixDrop`), built in parallel against this file.
Spec: §2.1–§2.7, §2.9 (mode cards), §7.4 (determinism, `.bvr`, `.bvs`), §7.6 (contract), §8.5 (tests).
Code: `Core/Modes/Framework/*.cs`. Everything here is pure C# in the `Ballverse.Core` assembly (netstandard2.1, C# 9, no
`record`/`init`, banned APIs per `core-tests/BannedSymbols.txt`). The view is Unity-side and reads only what the framework
records (render stream, render events, sound events).

Changing this contract after publication: only additive changes (new optional members on framework classes). The two
interfaces below do not change in release 1.

## 1. Pieces

| Type | Role |
|---|---|
| `IModeDefinition` | Stateless singleton per mode: catalog info, `SimVersion`, schema, presets, cross-parameter rules, candidate generator, scene factory. |
| `IModeScene : Core.Sim.IModeRules` | One run: rules driven by `SimRunner`, plus quantization, frame writer, live-param hook, generation summary, RSLT. |
| `ModeSceneContext` | Given to `BuildScene`: values, seed, `ModeVisualTable`, `ModeEventBuffer`. |
| `ModeVisualTable` | Render-stream shape / label table of the run (same ids as `RenderStreamWriter`). |
| `ModeEventBuffer` | Per-tick render (`RenderGeometry.RenderEvent`), sound (`Audio.SoundEvent`) and replay (`Replay.ReplayEvent`) events. |
| `ModeMetrics` | Allocation-free collector for `Generation.CandidateRunSummary` (collisions, significant ticks, standings). |
| `ModeSound` | Sound events with size / pan from quantized render values (spec §7.8). |
| `ModeParams` | `Prepare` = schema sanitize + `Constrain`; `ApplyPreset`; `Defaults`; `IsRunnable`. |
| `HudKeys` | Shared HUD keys of render frames. |
| `ModeRegistry` | Explicit registration (spec §7.6), kill switch filter. |
| `ModeSession` | (this track) runs a definition with values + seed through `SimRunner`, feeds sinks (render stream, render-event log, sound list, replay writer), live and headless stepping, `GameRunSummary`. |
| `ModeReplay` | (this track) re-simulates a `.bvr`, verifies HASH records (`HashVerifier`), regenerates the render stream. |
| `ModeGenerator` | (this track) drives `GenerationSession` with headless candidates; returns the chosen seed and parameters. |

## 2. `IModeDefinition`

```csharp
ModeInfo Info { get; }               // ModeCatalog.Get(ModeCode.Mxx) — same instance
ushort SimVersion { get; }           // replay header modeSimVersion; bump on any change of results
ParamSchema Schema { get; }          // ParamSchema.Merge(CommonParams.ForMode(Info), layer)
IReadOnlyList<Preset> BuiltInPresets { get; }
ParamSet Constrain(ParamSet sanitized, DeviceTier tier, List<ParamIssue> issues);
bool TryGetDynamicRange(string paramId, ParamSet values, DeviceTier tier, out double min, out double max);
int GetParticipantCount(ParamSet values);
ParamSet GenerateCandidate(in ModeCandidateRequest request);
IModeScene BuildScene(PhysicsWorld world, ModeSceneContext context);
```

Rules:

1. **Schema.** Mode parameters go in group `ParamGroup.Mode` (common overrides keep their group). A mode with a winner
   variant whose code is not in `CommonParams.ForcedWinnerModes` (M01, M02) defines its own `forcedWinner`
   (`IntParam("forcedWinner", 0, 32, 1, 0)` in `ParamGroup.Director`, 0 = any, else the 1-based participant) with a
   `visibleIf` on its variant parameter. Live parameters (`AsLive()`) are the card's "Live = да" column plus cosmetics.
   Ball budgets: `ballCount` override with `.WithTierCap(BodyBudgets.BallCap(code))`.
2. **Constrain** implements the card's "Валидация" list exactly (§2.9: one function for panel, presets, Remote Config,
   project import and generator). Return the nearest valid set; one `ParamIssue` per changed parameter (code
   `OutOfRange`, message starting with the rule number). Must be idempotent and never throw on a sanitized set. Values it
   produces must lie on the parameter's step grid (use `FloatParam.Snap` / snap ints).
3. **Dynamic range** returns, for every parameter a rule depends on, the slider range that keeps the rules (panel narrows
   the slider, spec §2.9). `ModeSession` also clamps live edits with it.
4. **Determinism.** `BuildScene` and the scene depend only on `(context.Values, context.Seed)` and recorded inputs / PARM
   changes. Random streams: `Rng.FromSeed(seed, streamId)` with a constant stream id per subsystem (scene layout, rules,
   …); never `System.Random`, clocks, `Dictionary` iteration order, `System.Math.Sin/Cos` (use `SimMath`).
5. **GenerateCandidate** is a pure function of the request; it returns `ModeParams.Prepare(...)`-clean values (sanitized for
   `request.Tier`, constrained). Keep the user's look / sound parameters; decide only what the card's generator decides.
   With `FixedParticipants > 0` keep the participant count (ball count / colours / variant) unchanged.
   `GenerationStyle.Any` = pick one of the mode's styles from the seed.
6. **Participants** (spec §2.5): release 1 counts colours. Team assignment is fixed when the scene is built
   (`ballColors` and `theme` are live look parameters; a live change re-colours, never re-teams).

## 3. `IModeScene`

```csharp
// IModeRules (Core.Sim): Start, Tick(runner, inputs), AfterPhysics, OnTimeLimit, Phase, Result, AppendStateHash
RenderQuantization Quantization { get; }
int ParticipantCount { get; }
void ApplyLiveParam(string paramId, ParamValue value);
void WriteFrame(RenderFrame frame);
CandidateRunSummary BuildSummary(SimRunner runner, bool endedByLimit);
ReplayResult BuildReplayResult(SimRunner runner);
```

Call order per run (done by `ModeSession`; a mode test may drive `SimRunner` itself the same way):

```
world = new PhysicsWorld(); scene = def.BuildScene(world, ctx)          // registers static shapes in ctx.Visuals
events.Tick = 0; runner = new SimRunner(world, scene); runner.Start()    // scene.Start may emit events
drain events; scene.WriteFrame(frame) -> frame 0
loop:
  apply PARM changes due at World.Tick  -> scene.ApplyLiveParam(...)
  events.Tick = World.Tick + 1
  runner.Step()                          // scene.Tick(inputs) -> World.Step() -> scene.AfterPhysics() [-> scene.OnTimeLimit()]
  drain events (render log, sound list, EVNT); scene.WriteFrame(frame) -> frame World.Tick
  if World.Tick % 60 == 0: HASH = runner.ComputeStateHash()
end: RSLT = scene.BuildReplayResult(runner)
```

Rules:

1. **Phases.** Running → Finale → Finished, never backwards (`SimRunner` checks). Enter Finale on the card's end
   conditions; stay in Finale for the finale effect (0–3 s, `finaleStyle`: `stop` may go straight to Finished), then
   Finished. `OnTimeLimit` (177 s by default) must switch to Finale by score or to Finished. A `targetDuration` end
   condition must not cut a run the generator selected: end by `targetDuration × 1.3` (the generator's headless limit,
   §2.4 step 3) — M01 does so.
2. **AppendStateHash** adds every rule variable that influences the future (counters, flags, phase, rule RNG state as two
   `ulong`s or a draw counter, pending timers). The world hash is added by the runner.
3. **WriteFrame** writes bodies in a fixed order with stable ids; ids unique per frame; balls keep their id for their life.
   Values outside `Quantization` are clamped by the writer silently, so the scene removes bodies past
   `PositionRange − 1 m`. Shape ids come from `context.Visuals.DefineShape`, label / skin ids from `DefineLabel` (define
   once, at build or when first needed — definitions allocate). Colours are palette indices (`RenderGeometry.PaletteSlots`):
   participant colours 0…223 (ball i of team k → k), rings / walls `PaletteSlots.Arena` / `ArenaAlt` / theme decor
   224+. Flags: `BodyFlags.Visible` (+`Trail` for balls when `trail ≠ off`, `Dead` for passed-but-kept walls,
   `Highlight`), `RenderBodyFlags.CameraTarget` / `FadeOutsideSafeZone` per RenderEventContract.md §5. HUD: `HudKeys`.
4. **Events** go into `context.Events` (ticks and render ids are stamped by the buffer). Per solver event, in the solver's
   sorted order (§7.4 rule 4), emit at most: one `RenderEvent` (RenderEventContract.md §4 mapping for the mode), one
   `SoundEvent` (`ModeSound.ForBall`, impulse = `SimEvent.Impulse`), one replay event
   (`AddReplay(type, ballRenderId, ReplayQuantization.QuantizeImpulse(impulse), extra)`). Check `WantsRender` /
   `WantsSound` / `WantsReplay` to skip work in headless runs. Bodiless replay events use
   `ModeEventBuffer.BodilessReplayBodyId`. Sound for the same fact must come from the same `SimEvent` as its render event.
   Hit events below the card's audible / visible threshold may be dropped (resting contacts: `Speed < 0.5 m/s`).
5. **Inputs** (M05 «Игрок»): `SimInput.Type` = mode-defined byte, payload in `A..D` (quantized ints). The session records
   INPT as `(tick, type, [A, B, C, D])` and replays it identically. An autopilot («Бот») runs inside the rules
   (deterministic from state) and is not recorded.
6. **Live parameters**: `ApplyLiveParam` is called only for parameters with `Live = true`; ignore ids you do not use.
   `timeScale` is handled by the session (pacing) and also passed through.
7. **Summary**: build with `ModeMetrics.ToSummary(runner, endedByLimit, coverage, finishTension, winner, rejected)`.
   `rejected = true` for the card's "Отбраковка" rules (e.g. no escape before the run ended): the session never selects
   such candidates (`CandidateRunSummary.Stuck`). Collisions = what the card counts for eventDensity; significant ticks =
   the card's "значимые события".
8. **Allocation**: no managed allocation per tick in steady state (pre-size arrays in `BuildScene`; `world.ReserveBalls`).
   The solver's own per-tick event buffers grow once to their high-water mark in crowded scenes; measure after that
   (`ModeConformance.MeasureSteadyStateAllocation(mode, values, seed, warmupTicks, measureTicks)`).
9. **Performance**: the release-1 budget scenes of the card must step in < 1 ms per tick on desktop CI (smoke test).

## 4. Recording conventions

- `.bvs` header: `RenderStreamHeader(Info.Id, 60, 60, scene.Quantization)`; frame k = state after k ticks.
- `.bvr` header: `Integration.ReplayParams.CreateHeader(Info.Id, SimVersion, appVersion, PhysicsBackend.CoreSim, seed, values, Schema)`;
  HASH every 60 ticks = `SimRunner.ComputeStateHash()` (world + rules); PARM via `ReplayParams.ParamChange`; RSLT at the end.
- Render events are kept in a `RenderGeometry.RenderEventLog` (`clip.bve`), sound events in a `List<SoundEvent>`.
- Ads: the session reports `Ads.GameRunSummary` (Finished + duration = tick of Finished, Countdown included).

## 5. Framework services (this track; build your mode against §2–§3 only)

```csharp
var session = new ModeSession(def, values, seed, new ModeSessionOptions { RenderStream = s1, Replay = s2, RenderEvents = log, SoundEvents = list });
session.AdvanceDisplayFrame(60);  // live: ticks due for one display frame at the time scale (0.25×–3×)
session.Step(); session.RunHeadless(maxTicks);
session.SetLiveParam(id, value);  session.EnqueueInput(type, a, b, c, d);
ModeSessionOutcome o = session.Complete();          // finishes sinks, RSLT, GameRunSummary
ModeReplayResult r = ModeReplay.Resimulate(def, replayFile, options);   // HashVerifier + optional render stream rebuild
ModeGenerationResult g = ModeGenerator.Generate(def, request);         // chosen seed + values (+ forced winner / goal)
```

Session details: `SetLiveParam` sanitizes for the session tier, clamps numbers to `TryGetDynamicRange`, applies the value
at the next tick boundary (PARM at that tick) — except the pacing of `timeScale`, which changes at once. `EnqueueInput`
returns the tick the input applies to (the next tick) and records INPT. `Complete()` writes RSLT only when the run reached
its finale or the cap; `GameRunSummary` is Finished when the rules finished (or the cap hit), otherwise Abandoned (or the
caller's override, e.g. `RestartedBeforeFinale`). `ModeGenerator.RunCandidate` runs exactly the live code path headless.

Registration of M02 / M05 happens in a later integration step: `registry.Register(GeometryBounceMode.Instance)` /
`registry.Register(HelixDropMode.Instance)` next to `RingEscapeMode.Instance` in `ReleaseModes.CreateRegistry()`.

## 6. Tests every mode provides (spec §8.5, §7.6 checklist)

In `core-tests/Ballverse.Core.Tests/Modes/<Mode>/`: presets valid (`ModeParams.ApplyPreset` → no issue), 10 000 random
sets after `Prepare` satisfy the card's rules, determinism (same values + seed twice → identical hashes; identical replay
bytes via `ModeSession`), replay round trip (`ModeReplay.Resimulate` matches every HASH), render stream round trip at the
card's extreme parameters (nothing clamped), headless invariants of the card, generation duration fit (§2.4 acceptance)
where the mode has a finale, zero steady-state allocations, performance smoke on the budget scene.
`core-tests/Ballverse.Core.Tests/Modes/ModeConformance.cs` (this track) runs the generic part for any `IModeDefinition`.
