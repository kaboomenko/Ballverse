# Ballverse.Render

Draws one `RenderFrame` of the render stream (spec §7.4, §7.7). The same call renders the live preview, the studio scrub and every
frame of the MP4 export, so preview equals export.

- Pure logic (tessellation, trails, camera, Helix pseudo-3D, VFX, themes, quality): `Assets/Ballverse/Core/RenderGeometry`
  (namespace `Ballverse.Core.RenderGeometry`, tested with `dotnet test core-tests`). The render-event contract for modes is in
  `Core/RenderGeometry/RenderEventContract.md`.
- Unity layer (this folder): uploads the composer's draw lists and records one command buffer into an HDR scene buffer, then bloom
  and composite into the target. Shaders live in `Resources/BallverseShaders` (always in the build; no pipeline renderer feature,
  camera or Volume needed).

## Live preview (studio slot / run screen)

`BallversePresenter` owns the renderer, the render texture and the dynamic quality governor; it does not depend on a UI toolkit.
The release-1 UI is UI Toolkit: give the presenter the slot's size in pixels and show `Target` as the element's background.

```csharp
var presenter = new BallversePresenter(sharedResources);   // or BallverseView (MonoBehaviour driver, below)
presenter.SetTier(deviceTier);                             // A/B/C start profile, dynamic fallback below 55 FPS
RenderContext ctx = presenter.Context;
ctx.Quantization = header.Quantization;                    // live writer's header or reader.Header
ctx.Shapes = shapeTable;                                   // IReadOnlyList<ShapeDefinition>
ctx.Labels = labelLookup;                                  // Func<int, string>, assign once
ctx.ModeId = "ring_escape";                                // "geometry_bounce", "helix_drop"
ctx.Events = renderEventLog;                               // RenderEventLog filled by the mode
ctx.FrameSource = frameRing;                               // RenderFrameRing pushed every sim tick (live), or a stream source
ctx.Look.Apply(paramSet, themeCatalog);                    // when the Look parameters change (no allocation)
presenter.Provider = myProvider;                           // IBallverseFrameProvider: frame (or null = read from FrameSource) + tick
presenter.TargetChanged += rt => TextureSlot.Set(slotElement, rt);   // UI Toolkit (Background.FromRenderTexture)

// every LateUpdate:
if (BallversePresenter.TryGetPixelSize(slotElement, 0f, out int w, out int h)) presenter.Present(w, h, Time.unscaledDeltaTime);
```

`BallverseView` does the same per `LateUpdate` from a GameObject: assign `view.Element` (UI Toolkit slot), or
`view.PixelSizeOverride`, or the optional uGUI `RawImage` field.

## Export (offline, §7.5 step a)

Let the renderer read the frames from the source: it reads the history ticks and the shown tick in increasing order, so the stream
reader never rewinds, at any ticks-per-output-frame ratio (30 fps export, speed ≠ 1). Do **not** call `reader.ReadFrame(tick, frame)`
yourself and then render with the same reader as `FrameSource`: the skipped ticks would be read backwards and the reader would
re-decode its 1-second block on every output frame.

```csharp
using var renderer = new BallverseRenderer(sharedResources);
ctx.FrameSource = new RenderStreamFrameSource(reader);
for (int i = 0; i < frameCount; i++)
{
    ctx.Tick = timeline.TickAt(i);
    renderer.RenderOffline(ctx, slotTexture);              // Export profile: full bloom / trails, 4x MSAA, scale 1
    // compositor + RGBA readback follow (release 1, owner decision Р24; NV12 conversion is a later optimisation)
}
```

`renderer.LastFrame` is the frame that was read. `slotTexture` should be an sRGB ARGB32 render texture at the slot's export
resolution. `renderer.Hud` exposes the frame's HUD values for the UI / compositor; `renderer.HudVisible` reflects the `hud` look
parameter. A saved clip's events are `clip.bve` next to `clip.bvs` (`RenderEventLog.ReadFrom`), see the render event contract.

## Player builds

Balls and particles are drawn with GPU instancing only. Their materials are clones of `Resources/BallverseMaterials/*.mat`
(GPU instancing on), which keep the instancing shader variants in player builds under the default "Instancing Variants: Strip
Unused". `Editor/InstancingVariantsBuildCheck` verifies (and if needed recreates) the templates before every player build and fails
the build if the project strips all instancing variants. The shader and material `.meta` files of these templates are committed with
fixed GUIDs (the material references its shader by GUID); keep them when moving files.
