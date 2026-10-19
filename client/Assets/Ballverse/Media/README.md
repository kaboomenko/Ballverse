# Ballverse.Media — MediaBridge (release 1)

Native video import / decode / encode / save / share behind `IMediaBridge` (spec §3.2, §3.8, §7.5).

| Backend | Where | Implementation |
|---|---|---|
| Android | `Android/` + `Assets/Plugins/Android/BallverseMedia/*.java` | Photo Picker (platform / Google Play services backport / `ACTION_OPEN_DOCUMENT`) in a transparent `PickerActivity`; MediaExtractor + MediaCodec → ImageReader (YUV_420_888 / P010) → `YuvConverter` (rotation, crop, scaling, BT.601/709/2020, HDR→SDR fallback; repeated frames copied, not re-converted); MediaCodec H.264 High (Baseline fallback) with an input surface fed through EGL (`lockHardwareCanvas` fallback) + `AacPcmEncoder` + MediaMuxer; `SegmentConcat` (MediaExtractor → MediaMuxer remux + one AAC track); MediaStore / legacy Movies save; `MediaFileProvider` share. |
| iOS | `Ios/` + `Assets/Plugins/iOS/BallverseMedia/*.mm` | PHPicker (UIImagePickerController on iOS 13), AVAssetReader + video composition (rotation, scaling, SDR tone mapping; reads up to the video track's end), AVAssetWriter + pixel-buffer adaptor (BGRA) + AAC + title/date metadata, `BVMediaConcat` (passthrough AVAssetReader → AVAssetWriter + AAC), PHPhotoLibrary add-only save (no album, see `GallerySaveResult.AddedToAlbum`), UIActivityViewController, thermal/low-power/accessibility signals. |
| Fake | `Fake/` | Editor/desktop only: test-pattern video, PNG sequence + WAV output, segment join of PNG directories. **Not a codec.** |

`MediaBridgeFactory.Create()` picks the backend. Build integration is automatic: `Editor/AndroidManifestPatcher`
adds the activity, provider, permission (API ≤ 28) and `<queries>` to the generated Gradle project;
`Editor/IosBuildPostprocessor` links the frameworks, compiles the `.mm` files with ARC and adds the add-only photo-library usage
string to Info.plist (placeholder text — localise before store submission). The Android patcher also appends R8/ProGuard
keep rules for `com.ballverse.media.**` (reached only by name from C#) to unityLibrary's `proguard-unity.txt`.

## Segmented export (spec §3.8, §7.5)

1. Per 2-second segment (`ExportPlan.GetSegments()`): `OpenWriter(segmentPath, VideoWriterOptions.ForSegment(settings))`
   (video only, no fast-start rewrite of the intermediate file), append its frames with pts = `FramePtsUs(i) − FramePtsUs(segment.StartFrame)`, `FinalizeAsync`, then
   persist the export state (Core). A segment interrupted by backgrounding is discarded and redone: `await writer.ReleaseAsync()`
   (or open the redo with `OpenWriterAsync`, which waits for it) — see *Writer files and release* below.
2. `ConcatSegmentsAsync(segmentResultPaths, pcmMixPath, outFile, SegmentConcatOptions.FromExportSettings(settings, metadata, deleteSegmentsBeforeFastStart: true), ct)`:
   remux without re-encoding, the PCM mix becomes one AAC track (trimmed to the video end on both platforms), moov at
   the front, title/date written. The result's `FrameCount` must equal `ExportPlan.FrameCount`.

**Disk space.** With E = the estimated export size, the join holds the segments (≈ E) + the remuxed output (≈ E) and,
on Android (MediaMuxer writes moov at the end), the fast-start temp copy (≈ E): with `DeleteSegmentsBeforeFastStart = false`
(the option's default) the peak is ≈ **3E**, above the §3.8 pre-check `ExportSettings.RequiredFreeSpaceBytes` = 2E + 200 MB —
the join can run out of space although the pre-check passed. **The studio should pass `deleteSegmentsBeforeFastStart: true`**
(as in step 2): the segments are deleted right after the remux, peak ≈ 2E; if the rewrite then fails, the export restarts
from the first segment. Keep `false` only when the caller checks 3E + 200 MB itself or must keep the segments. iOS peaks
at ≈ 2E either way.

## Writer files and release

- `OpenWriter(path)` deletes a previous result at `path` and work files left by a crashed run, then the encoder writes a
  temp file private to that writer: `<path>.bv<id>.part.mp4` (`.part.mp4` keeps an extension AVAssetWriter/MediaMuxer
  accept). Only a successful `FinalizeAsync` renames it to `path` (after the fast-start rewrite). Dispose, cancellation,
  encoder failure and every `FinalizeAsync` rejection delete only that temp file — natively by the writer that owns it
  (Java `VideoWriter` thread, iOS writer queue / finish handler), plus a C# safety net — never `path`. A writer that stops
  late can therefore never delete the output of a newer writer for the same path. `ConcatSegmentsAsync` does the same
  with its own temp file.
- `Dispose()` returns at once; `IVideoWriter.Released` / `ReleaseAsync()` complete when the native side has **stopped**:
  Android — the writer thread exited (codec, EGL, muxer released, temp file deleted; `VideoWriter.releaseAsync`); iOS —
  the serial queue drained and a running finish reached its completion handler (`BVWriter_Release`). `FinalizeAsync`
  completes (also when cancelled or rejected — including `FinalizeAsync` on an already disposed writer, which faults with
  `ObjectDisposedException` once its release finished) only after that. A release that hangs inside the platform is
  given up after 20 s with an error log (the hung writer can only touch its own temp file).
- The release chain always resumes on the media main-thread dispatcher (`MainThread.Context`), never on the caller's
  `SynchronizationContext.Current`: a release nobody awaits (plain `Dispose()`) leaves nothing queued on a foreign
  context (it hung the studio's xunit harness, whose tracking context waits for every post after the test returned).
- While a writer for a path has not settled (open, finalizing or still stopping), `OpenWriter(path)` throws
  `InvalidOperationException` (it cannot block: the native release completes through the main thread);
  `OpenWriterAsync(path)` — an extension method on `IMediaBridge` (`MediaBridgeExtensions`), so bridge decorators need
  no change — waits for it and for every other writer that is being released (frees the hardware encoder), then calls
  `OpenWriter`. Release completions bypass `NativeOps.CancelAll`, and Android uses one process-wide
  callback/registry (like iOS), so they also arrive after the bridge was disposed and re-created.
- Plain `OpenWriter` on a **different** path never waits in C#. On Android the Java `VideoWriter.open()` itself waits
  (bounded, 3 s) until every writer whose abort was requested has exited its thread and released its codec — many phones
  allow one hardware AVC encoder, and the studio retries a segment on a new path one frame after `Dispose()`. Writers that
  are still open are never waited for. iOS (VideoToolbox) runs several encoder sessions side by side, so no wait there.
  Callers should still prefer `OpenWriterAsync` / `await ReleaseAsync()` for a redo or retry (explicit on every platform).

## Data formats

- **Frames:** RGBA32, top-down, tightly packed (`width × 4` bytes per row), both directions. iOS swizzles to/from BGRA
  natively (vImage). Note for the compositor: `AsyncGPUReadback` returns rows in GPU memory order — bottom-up on OpenGL
  ES (`SystemInfo.graphicsUVStartsAtTop == false`), top-down on Vulkan/Metal — so flip on GL before appending.
- **Audio:** `PcmFile` = raw f32le, interleaved stereo, 48 kHz — `PcmFile.SampleRate` / `Channels` are the Core mix
  format `Ballverse.Core.Audio.AudioConstants.SampleRate` / `Channels` (the Java and ObjC++ plugins hard-code 48 kHz
  stereo). Decoders produce it (Android resamples/downmixes in C#
  with a windowed-sinc `SincResampler`); writers encode it to AAC-LC 48 kHz (192 kbit/s by default).
- **MP4:** H.264 + AAC, `moov` at the front (iOS `shouldOptimizeForNetworkUse`; Android `Mp4FastStart` rewrite), optional
  `Mp4Metadata` title/date in `moov/udta` (iOS AVAssetWriter; elsewhere the same C# rewrite). Never location data.

## Threading

- Every `IMediaBridge`, `IVideoReader`, `IVideoWriter` and `VideoFrame.Data` call is **main-thread only** (checked).
- Native completions (Java threads, GCD queues) are posted to the main thread via Unity's `SynchronizationContext`
  (`Internal/MainThread`); tasks complete and progress reports run on the main thread.
- Each reader owns one worker thread that drives the decoder synchronously (Android: JNI-attached). Writers encode on
  native threads (Android writer thread with its own EGL context; iOS serial dispatch queue).
- Cancellation completes the task immediately and tells native code to stop; partial files are deleted natively, C#
  temp files (`*.faststart.tmp`, `*.tmp`) by C#. The token is armed only after the native start (`NativeOps.Run`), so a
  native cancel never precedes its start; an already-cancelled token does not start native work at all. Exceptions:
  `FinalizeAsync` completes only after the writer's native release (see *Writer files and release*), and a cancelled
  `ConcatSegmentsAsync` (also on bridge disposal) completes only when the native join reported that it stopped and
  deleted its own temp file (`NativeOps.Run(..., cancelWaitsForNative: true)`; Android `SegmentConcat` / iOS
  `BVSegmentConcat` report after their cleanup; fallback after 20 s), so its `outFile` stays reserved until then.

## Memory

- Readers: a pool of **3** frame buffers (`FramePool`); a `VideoFrame` holds one until disposed. Forward reads drop older
  prefetched frames; backward jumps drop all. Holding up to 2 frames never blocks a read (speculative prefetches give
  their buffer back to a requested time); holding 3 frames stalls decoding (`ReadFrameAt` then times out).
- Writers: **3** frames in flight; `TryAppendVideoFrame` returns false when full, `AppendVideoFrame` waits (back-pressure).
  The caller's buffer is copied before the call returns (Android: one memcpy into a Java direct buffer; iOS: one swizzle
  into a pooled `CVPixelBuffer`).
- Native handles are released by `Dispose` (writers: completion via `Released`); finalizers release leaked
  readers/writers on the main thread.

## Tests and checks

- `Tests/EditMode` — NUnit EditMode tests (assembly `Ballverse.Media.Tests`, run by the Unity test workflow): reader
  frame selection, pool-starvation regressions, random read stress, NativeOps cancellation ordering, MP4 fast start /
  metadata / cancellation cleanup, PCM conversion, fake-backend finalize and segment concat, writer release / redo at the
  same path (`WriterReleaseTests`, with `FakeMediaOptions.SimulatedReleaseDelayMs` modelling a slow encoder teardown:
  unawaited release never posts to the caller's context, FinalizeAsync on a disposed writer, the 20 s release fallback,
  a release outliving the bridge), joins whose cancel waits for the native stop (`NativeOpsTests`).
- `Tests/DotNet~/run.sh` — the same tests under plain .NET 8 without Unity (Unity ignores `~` folders); compiles
  Api/Common/Internal/Fake with small Unity stand-ins.
- `core-tests/MediaJavaCheck/check.sh <android-all.jar>` — javac `--release 8 -Xlint:all -Werror` over the Java plugin
  against Android API 34 classes and a `UnityPlayer` stub, then three plain-JVM checks:
  - `YuvConverterCheck` — 72 layout/crop/rotation cases against an independent reference;
  - `AacPcmEncoderCheck` — the real `AacPcmEncoder` against a fake codec (`fakes/`): audio is never queued past the
    video end (VideoWriter and SegmentConcat driving patterns, PCM longer/equal/shorter than the video), plus a static
    guard against an audio look-ahead constant;
  - `VideoWriterReleaseCheck` — the real `VideoWriter` lifecycle against fake codec/muxer/surface/EGL classes
    (`writerfakes/`): `releaseAsync` reports only after the writer thread exited with codec and muxer released and its
    own unfinished file deleted, a late release never touches the output path or a newer writer's file, `open()`
    failures return only after the thread stopped, `open()` on a single-encoder fake device waits for a stopping writer's
    codec (never for open writers); key cases also run on the EGL renderer path (fake EGL14/GLES20/EGLExt), so the EGL
    teardown inside the release is covered.

  android-all.jar: e.g. Robolectric's `org.robolectric:android-all` from Maven.
