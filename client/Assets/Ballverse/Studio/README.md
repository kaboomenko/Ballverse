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
    SimRendererFactory = () => new RenderAdapter(), // a new ISimFrameRenderer per preview / export / cover / thumbnails
    StreamRebuilder = streamRebuilder, // IRenderStreamRebuilder: replay → clip.bvs/.bve via Core.Sim + hash check
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
if (studio.Projects.RunReadiness(runId) == ClipReadiness.Unavailable) HideToVideoButton(); // stream evicted, no rebuilder
ProjectDocument doc = studio.Projects.CreateFromRun(runId);       // "В видео" (or CreateFromClip)
StudioProjectSession session = studio.Projects.Open(doc.Id, out LoadResult load);
StudioPreviewPlayer preview = await studio.OpenPreviewAsync(session, null, prepareProgress, ct); // rebuilds an evicted stream
TimelineThumbnails thumbs = studio.OpenThumbnails(session);      // S / V track thumbnails, rendered in the background
thumbs.Changed += timeline.Redraw; session.Changed += thumbs.SetProject;
preview.TargetChanged += rt => slot.style.backgroundImage = Background.FromRenderTexture(rt);
preview.SetOverlays(WatermarkPolicy.Resolve(doc.Output.Watermark, hasNoWatermark, remote.WatermarkEnabled), masks);
session.Changed += preview.SetProject;                            // every edit is visible within one frame
session.Apply(d => d.Layout.Ratio = 0.55, coalesceKey: "ratio");  // edits: sanitized, undo (50 steps), autosave after 1 s
preview.SetDisplaySize(widthPx, heightPx); preview.Play(); preview.Seek(us); preview.Pause();
await studio.Projects.ImportUserVideoAsync(session, progress, ct); // gallery picker → media/ → user video (≤ 180 s)
...
thumbs.Dispose(); preview.Dispose(); session.Dispose();            // Dispose saves synchronously
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
else if (r.Resumable) OfferContinue(r.JobId);                      // only the final join failed (e.g. suspended): ResumeExportAsync
// next app start: foreach (var s in studio.Export.ListResumable()) offer "Продолжить экспорт" → studio.ResumeExportAsync(s.JobId, ct)
```

`(GpuExportFrameRenderer)studio.Export.CurrentRenderer`'s `PreviewTexture` is the frame being encoded (progress sheet preview).

### Renderer adapter (App, over Ballverse.Render)

`ISimFrameRenderer.BeginClip(SimClipInfo)` gives the mode id, quantization, shape/label tables and the paths of `clip.bvs` /
`clip.bve`; `SetFrameTime(SimFrameTime)` gives the tick, whether it is a still, and a discontinuity counter (seek, loop) before each
`Render(frame, shapes, labels, target, quality)`. The target is an sRGB RGBA8 render texture of the whole 9:16 arena sized by Core
`SimSlotSizing` for zoom 1 (zooming magnifies it; the target never exceeds the slot's unzoomed size). An adapter that can draw a
window of the arena returns `SupportsViewport = true` and then gets `SimFrameTime.Viewport` (normalized, top-left origin) and a
target of the slot's resolution (spec §7.5 step a) — Ballverse.Render has no view override yet, so release 1 adapters return
false. `StudioRenderQuality.Export` maps to the renderer's Export profile. Trails and VFX that need ticks between
two drawn frames (30 fps, speed ≠ 1) can read them from their own `RenderStreamReader` on `SimClipInfo.RenderStreamPath`.
**One clip per instance:** the studio asks `SimRendererFactory` for a new adapter for every user (preview, each export, cover,
thumbnails) and disposes it (`IDisposable`) when done, so an export started over the open studio never shares the preview's.

## Public API

| Type | Role |
|---|---|
| `StudioServices`, `StudioServicesOptions` | Composition root; `OpenPreviewAsync`, `OpenThumbnails`, `ExportAsync`, `ResumeExportAsync`, `ResolveExportSettings`, `RenderCover` (thumbs/cover.png) |
| `StudioStorage`, `UnityFileStore`, `DiskFileCopier`, `DirectorySync` | Stores on `persistentDataPath` / cache (spec §3.9), atomic replace + fsync, startup cleanup, usage |
| `ClipLibrary`, `RunRecording` | Ring of the last 5 runs, saved clips (Core `ClipStore`, `RunRetention`) |
| `StudioProjects`, `StudioProjectSession` | Create / open / duplicate / rename / delete, autosave, undo, user video import, export history (Core `ProjectLibrary`, `ProjectEditSession`) |
| `ISimFrameRenderer`, `SimClipInfo`, `SimFrameTime`, `StudioRenderQuality`, `ClipSource` | Rendering seam and clip access |
| `IRenderStreamRebuilder`, `RenderStreamRepair`, `ClipReadiness` | Evicted render streams rebuilt from the replay (spec §7.5 step 2) |
| `StudioCompositor`, `StudioFrameRenderer`, `StudioTextRenderer`, `StudioEmojiAtlas`, `CompositeStyle`, `SlotPicture`, `IStudioFontProvider` | Frame composition |
| `StudioPreviewPlayer`, `PreviewAudioOutput`, `TimelineThumbnails` | Realtime preview, track thumbnails |
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
  seek/play words, no allocation). The filter runs only with an enabled `AudioListener` (the preview adds one to its own object
  when the scene has none) and with `bypassEffects` off. The picture follows the audio through Core `PreviewAudioSync`: held
  during the DSP latency after Play/Seek, on the frame clock whenever the cursor stops moving for 2 frames (≥ 3 audio blocks:
  no listener, interruption, focus loss), audio re-seeked to the picture when it runs again.
- **Mixes**: one at a time (`StudioAudioMixer`); a preview remix starts 300 ms after the last audio edit and a superseded mix
  stops within ~1 s (`StudioMixBuilder` checks its token between 1-second chunks). Overlapping requests share one decode of the
  user video's audio (each decode writes its own file, renamed into `audio-cache/`).
- **Native threads**: decoder, encoder, muxer (Media bridge); their callbacks are marshalled to the main thread by the bridge.
- **GPU**: compositor command buffers execute immediately (`Graphics.ExecuteCommandBuffer`, no camera); readbacks are
  `AsyncGPUReadback.RequestIntoNativeArray` polled in frame order (synchronous `ReadPixels` fallback without async readback support).

The export frame loop processes as many frames per Unity frame as fit in ~25 ms (`ExportPolicy.FitsInBudget`, at least one), so
the UI keeps updating; it never blocks on the decoder (a frame whose video frame is not decoded yet is retried next Unity frame)
or the encoder (`TryAppendVideoFrame`; a full queue waits for the next Unity frame). No progress of any kind (frame rendered,
frame accepted, segment finished) for `ExportPipeline.StallTimeoutMs` (15 s, thermal and background pauses excluded) restarts
the segment once like an encoder error; the second stall fails with `EncoderFailed` (frames in flight: hung encoder or
readback) or `Timeout` (a video frame never decodes).

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
cancel → `Cancelled`, files deleted; disk full → `InsufficientSpace`; corrupt replay / stream → `ClipDataMissing`; other mix
failures → `AudioFailed`; anything unexpected → `Unknown` (StartAsync/ResumeAsync never throw). A failed final join (muxer,
timeout, I/O — e.g. iOS suspends the app mid-join) waits for the foreground and retries once, then keeps the job:
`Resumable = true`, `ResumeExportAsync` only joins. Once the file is verified, a cancel no longer interrupts the gallery save
(the export counts as done). Every outcome is reported to `IExportGate.OnExportFinished`.

Pixel format: RGBA32, rows top-down, sRGB transfer (BT.709 matrix is applied by the platform encoder). Unity render textures are
bottom-up in memory on every graphics API (Unity flips rendering on top-left-origin APIs), so the readback pass flips rows;
the pass also encodes linear light to sRGB because the readback target is UNorm (no hardware conversion to depend on).

## Memory (spec §7.11 budgets: export 1080p ≤ 650 MB on tier C)

| Item | 1080p export | Preview (≈ 540×960) |
|---|---|---|
| Compositor canvas (RGBA8) | 8.3 MB | ≈ 2 MB |
| Readback targets 3 × RGBA8 + CPU buffers 3 × 8.3 MB | 50 MB | — |
| Simulation slot RT at any zoom (renderer's own scene/MSAA/bloom buffers come on top at the same size, see Render) | ≤ 8.3 MB | ≤ 2 MB |
| User video texture + decoder pool (3 frames, Media) | ≤ 33 MB | ≤ 8 MB (decoded at half size, zoom steps 1/2/4/8) |
| Fit-blur temporaries | < 1 MB | < 1 MB |
| Offline mix, 180 s stereo float: output 69 MB + user-video track 69 MB (+ music track if any) | ≤ 138 MB transient (written to PCM, then released) | 69 MB held + ≤ 138 MB while one remix runs |
| Encoder input queue (Media, 3 frames) | 25 MB native | — |
| Emoji atlas (1280×1408 RGBA) | 7.2 MB | 7.2 MB |
| Track thumbnails (2 × 16 × 96×170) | — | ≈ 2 MB |

The user video's audio is decoded to a PCM file and streamed (`PcmFileSource`), never held in memory as compressed input; the mix
places it into one track buffer. One mix runs at a time, so the preview's remix and an export's mix never overlap. An export
started while the preview is open adds the preview's canvas, mix and its own renderer adapter (≈ 80 MB on top of the table);
closing the preview first keeps the export within the tier-C budget.

## Text and watermark

Text layers are laid out once at the 1080×1920 reference by Core `TextBlockLayout` (size 12–160, stroke 0–8, shadow, plate,
alignment, wrap, keep-inside) with metrics from the font, and drawn as glyph quads from the font's dynamic atlas at the drawn
size (`StudioTextRenderer`), so preview and export match and the same boxes feed the watermark corner rule
(`FrameRequest.TextBounds`). **Decision:** TextMeshPro is not used: it needs the TMP Essential Resources (TMP Settings asset and
SDF shaders) that only the Unity editor can import and that are not in the project; the dynamic-font path needs no assets and no
camera. **Colour emoji** (spec §3.5) come from a bundled atlas of 438 common emoji rendered from Noto Color Emoji (SIL OFL 1.1,
`Resources/BallverseStudio/emoji_atlas_license.txt` — show it in the app's licences screen): `emoji_atlas.bytes` is a PNG
loaded with `ImageConversion.LoadImage` (no import settings), Core `EmojiCatalog` lists its cells and Core `EmojiSet` matches them
in text (base + U+FE0F + skin tone; text-default symbols need U+FE0F). Layout treats an emoji as a square glyph (1.15 em
advance), the renderer draws it in colour with the layer's shadow and stroke as a silhouette. Emoji outside the set and ZWJ
compositions fall back to their parts / nothing. Regenerate with `Tools~/make_emoji_atlas.py`. Entry animations and the live "{n}" counter (HUD Progress /
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
  .NET harness: `UnityFileStore` against the Core `IFileStore` contract tests, clip library, PCM source, the export pipeline
  end to end on the fake media backend (segments, join, verification, gate, low space, cancel, pause, kill + resume, encoder
  retry, thermal pause) and its failure paths (corrupt replay, unexpected exception, hung encoder, readback or decoder,
  cancel during the gallery save, interrupted / failing join → resumable), overlapping mixes and user-audio decodes, render
  stream rebuild, emoji atlas vs catalog. **Not in CI yet**: `.github/workflows` is outside this track; add
  `bash client/Assets/Ballverse/Studio/Tests/DotNet~/run.sh` to `media-checks.yml` (it needs the .NET 8 SDK only).
- `Tests/EditMode` — Unity EditMode checks (shader present, file store on the player runtime, project round trip).
- Device checklist (needs a phone): exported file plays in the system player and uploads to TikTok/Shorts/Reels; picture not
  upside down and colours not double-gamma; preview ≈ export (SSIM ≥ 0.98, spec §3.3); A/V sync in the preview; export speed
  targets (spec §3.8).
