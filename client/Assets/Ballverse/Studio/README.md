# Ballverse.Studio — studio runtime

Unity runtime of the studio (spec §3.1–§3.10, §7.4–§7.5, §7.11): device storage, recent runs and clip library, projects with
autosave, the frame compositor, the realtime preview and the MP4 export. **Preview and export draw every frame through the same
code path** (`StudioFrameRenderer` → `StudioCompositor`), only the canvas size differs (spec §3 principle).

Assembly `Ballverse.Studio` references `Ballverse.Core`, `Ballverse.Media`, `Ballverse.Platform`. It does **not** reference
`Ballverse.Render`: the simulation is drawn through `ISimFrameRenderer`, which the App implements over `BallverseRenderer`.
Pure logic lives in Core (`Assets/Ballverse/Core/Studio`, namespace `Ballverse.Core.Studio`, tested by `dotnet test core-tests`);
this folder is the thin Unity layer.

## Wiring (App composition root)

```csharp
IMediaBridge media = MediaBridgeFactory.Create();
StudioServices studio = StudioServices.Create(new StudioServicesOptions
{
    Paths = appPaths,                 // IAppPaths (persistent root, render-stream cache, export scratch)
    Media = media,
    SimRenderer = new RenderAdapter(), // ISimFrameRenderer over Ballverse.Render (see below)
    Lifecycle = appLifecycle,         // IAppLifecycle: autosave + export pause on Paused
    ScreenAwake = screenAwake,        // IScreenAwake: screen stays on while exporting
    Sounds = soundProvider,           // IStudioSoundProvider: sound pack + bank + melody, music PCM
    Fonts = fontProvider,             // IStudioFontProvider: the 8 OFL fonts by project font id
    Gate = rewardedExportGate,        // IExportGate: Services' RewardedExport (Core RewardedExportGate decides)
    Tier = deviceTier,
});
Debug.Log("[Studio] startup cleanup: " + studio.CleanupReport);
```

### Recording a run (simulation screen)

```csharp
RunRecording run = studio.Clips.BeginRun();
// the simulation writes run.ReplayPath (clip.bvr), run.RenderStreamPath (clip.bvs), run.EventsPath (clip.bve)
ClipEntry entry = run.Commit(modeId, durationTicks, winnerId);   // joins the ring of the last 5 runs (streams ≤ 500 MB)
// or run.Abandon();
studio.Clips.SaveRunAsClip(entry.Id, name);                       // "Сохранить клип"
```

### Studio screen

```csharp
ProjectDocument doc = studio.Projects.CreateFromRun(runId);       // "В видео" (or CreateFromClip)
StudioProjectSession session = studio.Projects.Open(doc.Id, out LoadResult load);
StudioPreviewPlayer preview = studio.OpenPreview(session);
preview.TargetChanged += rt => slot.style.backgroundImage = Background.FromRenderTexture(rt);
preview.SetOverlays(WatermarkPolicy.Resolve(doc.Output.Watermark, hasNoWatermark, remote.WatermarkEnabled), masks);
session.Changed += preview.SetProject;                            // every edit is visible within one frame
session.Apply(d => d.Layout.Ratio = 0.55, coalesceKey: "ratio");  // edits: sanitized, undo (50 steps), autosave after 1 s
preview.SetDisplaySize(widthPx, heightPx); preview.Play(); preview.Seek(us); preview.Pause();
await studio.Projects.ImportUserVideoAsync(session, progress, ct); // gallery picker → media/ → user video (≤ 180 s)
...
preview.Dispose(); session.Dispose();                              // Dispose saves synchronously
```

### Export

```csharp
studio.Export.Progress += p => sheet.Show(p.Fraction, p.RemainingMs, p.Throttled, p.Paused);
StudioExportResult r = await studio.ExportAsync(projectId, new StudioExportOptions
{
    Resolution = ExportResolution.P1080, Fps = 60,                  // null = last choice / tier default ("Экспортировать снова")
    ShowWatermark = WatermarkPolicy.Resolve(...), PlatformMasks = masks, Bitrates = remoteBitrates,
    SaveToGallery = settings.AutoSave,
}, cts.Token);                                                     // cancel → partial files deleted
if (r.Succeeded) media.Share(r.OutputPath, caption); else if (r.SuggestLowerSettings) OfferExport720p30();
// next app start: foreach (var s in studio.Export.ListResumable()) offer "Продолжить экспорт" → studio.ResumeExportAsync(s.JobId, ct)
```

`(GpuExportFrameRenderer)studio.Export.CurrentRenderer`'s `PreviewTexture` is the frame being encoded (progress sheet preview).

### Renderer adapter (App, over Ballverse.Render)

`ISimFrameRenderer.BeginClip(SimClipInfo)` gives the mode id, quantization, shape/label tables and the paths of `clip.bvs` /
`clip.bve`; `SetFrameTime(SimFrameTime)` gives the tick, whether it is a still, and a discontinuity counter (seek, loop) before each
`Render(frame, shapes, labels, target, quality)`. The target is an sRGB RGBA8 render texture of the whole 9:16 arena sized by Core
`SimSlotSizing`; `StudioRenderQuality.Export` maps to the renderer's Export profile. Trails and VFX that need ticks between
two drawn frames (30 fps, speed ≠ 1) can read them from their own `RenderStreamReader` on `SimClipInfo.RenderStreamPath`.

## Public API

| Type | Role |
|---|---|
| `StudioServices`, `StudioServicesOptions` | Composition root; `OpenPreview`, `ExportAsync`, `ResumeExportAsync`, `ResolveExportSettings`, `RenderCover` (thumbs/cover.png) |
| `StudioStorage`, `UnityFileStore`, `DiskFileCopier`, `DirectorySync` | Stores on `persistentDataPath` / cache (spec §3.9), atomic replace + fsync, startup cleanup, usage |
| `ClipLibrary`, `RunRecording` | Ring of the last 5 runs, saved clips (Core `ClipStore`, `RunRetention`) |
| `StudioProjects`, `StudioProjectSession` | Create / open / duplicate / rename / delete, autosave, undo, user video import, export history (Core `ProjectLibrary`, `ProjectEditSession`) |
| `ISimFrameRenderer`, `SimClipInfo`, `SimFrameTime`, `StudioRenderQuality`, `ClipSource` | Rendering seam and clip access |
| `StudioCompositor`, `StudioFrameRenderer`, `StudioTextRenderer`, `CompositeStyle`, `SlotPicture`, `IStudioFontProvider` | Frame composition |
| `StudioPreviewPlayer`, `PreviewAudioOutput` | Realtime preview |
| `ExportPipeline`, `GpuExportFrameRenderer`, `StudioExportHost`, `StudioExportAudio`, `IExportGate`, `ExportProgressInfo` | MP4 export |
| `StudioAudioMixer`, `IStudioSoundProvider`, `PcmFileSource` | Offline mix for preview and export |
| Core: `StudioExportResult`, `ExportErrorCode`, `ExportPhase`, `ExportPolicy`, `ExportJob`, `ExportResumeState` | Results, error codes, rules |

## Threading

- **Main thread** (Unity): everything public here. `StudioRunner.Tick` drives preview playback, autosave and the export frame loop;
  `StudioRunner.NextFrame()` paces async loops. All `IMediaBridge` calls happen here (the bridge enforces it) and its tasks
  complete here.
- **Thread pool**: the offline mix (`StudioMixBuilder` over replay events, user PCM file, music) and PCM file writes
  (`StudioAudioMixer`); the autosave JSON write (`StudioProjectSession`, never overlapping another write of the same project;
  on pause the save is synchronous).
- **Audio thread**: `PreviewAudioOutput.OnAudioFilterRead` → Core `PcmPlaybackCursor` (lock-free: immutable buffer swap, atomic
  seek/play words, no allocation).
- **Native threads**: decoder, encoder, muxer (Media bridge); their callbacks are marshalled to the main thread by the bridge.
- **GPU**: compositor command buffers execute immediately (`Graphics.ExecuteCommandBuffer`, no camera); readbacks are
  `AsyncGPUReadback.RequestIntoNativeArray` polled in frame order (synchronous `ReadPixels` fallback without async readback support).

The export frame loop processes as many frames per Unity frame as fit in ~25 ms (`ExportPolicy.FitsInBudget`, at least one), so
the UI keeps updating; it never blocks on the decoder (a frame whose video frame is not decoded yet is retried next Unity frame,
15 s stall → `Timeout`) or the encoder (`TryAppendVideoFrame`; a full queue waits for the next Unity frame).

## Export pipeline (spec §3.8, §7.5; release 1)

preflight (≥ 2 × estimate + 200 MB) → rewarded gate (new exports only) → audio mix → one PCM file → frame loop
(render the simulation at the slot size with the Export profile, upload the user video frame for its pts, compose 1080×1920 or
720×1280, flip + sRGB-encode into an RGBA8 UNorm target, async readback into one of 3 buffers, append to the 2-second segment
writer with pts relative to the segment) → finalize each segment + atomic resume state → join segments without re-encoding + one
AAC track, `moov` first → verify frame count and duration (± 1 frame) → move to `outputs/` (until next start) or the project's
`exports/` → gallery album "Ballverse" (setting) → delete the job folder.
Interruptions: background → the current segment is discarded and redone; killed → `ListResumable` / `ResumeExportAsync`
(no second ad); hot device (`Serious`/`Critical`) → 1.5 s / 4 s pause between segments, `Throttled` in the progress;
encoder error → one retry of the segment (a new file name per attempt), then `EncoderFailed` (`SuggestLowerSettings`);
cancel → `Cancelled`, files deleted; disk full → `InsufficientSpace`. Every outcome is reported to `IExportGate.OnExportFinished`.

Pixel format: RGBA32, rows top-down, sRGB transfer (BT.709 matrix is applied by the platform encoder). Unity render textures are
bottom-up in memory on every graphics API (Unity flips rendering on top-left-origin APIs), so the readback pass flips rows;
the pass also encodes linear light to sRGB because the readback target is UNorm (no hardware conversion to depend on).

## Memory (spec §7.11 budgets: export 1080p ≤ 650 MB on tier C)

| Item | 1080p export | Preview (≈ 540×960) |
|---|---|---|
| Compositor canvas (RGBA8) | 8.3 MB | ≈ 2 MB |
| Readback targets 3 × RGBA8 + CPU buffers 3 × 8.3 MB | 50 MB | — |
| Simulation slot RT (renderer's own scene/bloom buffers come on top, see Render) | ≤ 8.3 MB | ≤ 2048 px side |
| User video texture + decoder pool (3 frames, Media) | ≤ 33 MB | ≤ 8 MB (decoded at half size) |
| Fit-blur temporaries | < 1 MB | < 1 MB |
| Offline mix, 180 s stereo float (+ music track if any) | 69 MB transient (written to PCM, then released) | 69 MB held while previewing |
| Encoder input queue (Media, 3 frames) | 25 MB native | — |

The user video's audio is decoded to a PCM file and streamed (`PcmFileSource`), never held in memory. Close the preview before
exporting (its mix and textures are released on `Dispose`).

## Text and watermark

Text layers are laid out once at the 1080×1920 reference by Core `TextBlockLayout` (size 12–160, stroke 0–8, shadow, plate,
alignment, wrap, keep-inside) with metrics from the font, and drawn as glyph quads from the font's dynamic atlas at the drawn
size (`StudioTextRenderer`), so preview and export match and the same boxes feed the watermark corner rule
(`FrameRequest.TextBounds`). **Decision:** TextMeshPro is not used: it needs the TMP Essential Resources (TMP Settings asset and
SDF shaders) that only the Unity editor can import and that are not in the project; the dynamic-font path needs no assets and no
camera. Colour emoji are therefore not rendered yet (the dynamic atlas has no colour glyphs); a TMP/emoji sprite renderer can
replace `StudioTextRenderer` behind the same `TextDraw` inputs. Entry animations and the live "{n}" counter (HUD Progress /
ProgressTotal) come from Core `TextLayerTiming` / `HudCounter`. The watermark is drawn by the compositor in the preview and the
export: the procedural glowing ball + "Ballverse" (white, 70 %, light shadow, 2.5 % of the frame height) or
`StudioServicesOptions.WatermarkTexture`.

## Files

| Store | Path | Content |
|---|---|---|
| persistent | `projects/{id}/` | project.json (+ .bak), clip.bvr/.bvs/.bve, media/, thumbs/cover.png, exports/ |
| persistent | `clips/{id}/` | saved clips (clip.json written last) |
| cache | `render-streams/runs/{id}/` | last 5 runs (run.json written last) |
| cache | `export/{jobId}/` | export_state.json, mix.pcm, segments/seg_NNNN[_rK].mp4 |
| cache | `export/outputs/`, `export/audio-cache/` | finished files for "Share", decoded user audio |

Startup cleanup removes unfinished runs/clips/project folders, temp files and every export job except resumable ones.

## Verification

- `dotnet test core-tests/Ballverse.Core.sln` — Core/Studio logic (composition, timeline/mix inputs, export job and frame flow,
  resume store, text layout, autosave session, clip store, retention, preview clock, PCM cursor, …).
- `Tests/DotNet~/run.sh` — compile-only check of this whole assembly against Unity API stand-ins (`CompileCheck/`) and the
  .NET harness: `UnityFileStore` against the Core `IFileStore` contract tests, clip library, PCM source, and the export pipeline
  end to end on the fake media backend (segments, join, verification, gate, low space, cancel, pause, kill + resume, encoder
  retry, thermal pause).
- `Tests/EditMode` — Unity EditMode checks (shader present, file store on the player runtime, project round trip).
- Device checklist (needs a phone): exported file plays in the system player and uploads to TikTok/Shorts/Reels; picture not
  upside down and colours not double-gamma; preview ≈ export (SSIM ≥ 0.98, spec §3.3); A/V sync in the preview; export speed
  targets (spec §3.8).
