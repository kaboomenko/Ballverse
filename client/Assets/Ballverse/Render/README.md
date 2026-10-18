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

```csharp
var view = gameObject.AddComponent<BallverseView>();      // or a prefab with the RawImage assigned
view.SetTier(deviceTier);                                  // A/B/C start profile, dynamic fallback below 55 FPS
RenderContext ctx = view.Context;
ctx.Quantization = header.Quantization;                    // live writer's header or reader.Header
ctx.Shapes = shapeTable;                                   // IReadOnlyList<ShapeDefinition>
ctx.Labels = labelLookup;                                  // Func<int, string>, assign once
ctx.ModeId = "ring_escape";                                // "geometry_bounce", "helix_drop"
ctx.Events = renderEventLog;                               // RenderEventLog filled by the mode
ctx.FrameSource = frameRing;                               // RenderFrameRing pushed every sim tick (trails, camera)
ctx.Look.Apply(paramSet, themeCatalog);                    // live Look parameters
view.Provider = myProvider;                                // IBallverseFrameProvider: frame + tick to show
```

## Export (offline, §7.5 step a)

```csharp
using var renderer = new BallverseRenderer(sharedResources);
ctx.FrameSource = new RenderStreamFrameSource(reader);
for (int i = 0; i < frameCount; i++)
{
    int tick = timeline.TickAt(i);
    reader.ReadFrame(tick, frame);
    ctx.Tick = tick;
    renderer.RenderOffline(frame, ctx, slotTexture);       // Export profile: full bloom / trails, 4x MSAA, scale 1
    // compositor + RGB->NV12 + readback follow
}
```

`slotTexture` should be an sRGB ARGB32 render texture at the slot's export resolution. `renderer.Hud` exposes the frame's HUD
values for the UI / compositor; `renderer.HudVisible` reflects the `hud` look parameter.
