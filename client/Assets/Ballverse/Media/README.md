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

1. Per 2-second segment (`ExportPlan.GetSegments()`): `OpenWriter(segmentPath, VideoWriterOptions.FromExportSettings(settings, null))`
   (video only), append its frames with pts = `FramePtsUs(i) − FramePtsUs(segment.StartFrame)`, `FinalizeAsync`, then
   persist the export state (Core). A segment interrupted by backgrounding is discarded (dispose the writer) and redone.
2. `ConcatSegmentsAsync(segmentResultPaths, pcmMixPath, outFile, SegmentConcatOptions.FromExportSettings(settings, metadata), ct)`:
   remux without re-encoding, the PCM mix becomes one AAC track, moov at the front, title/date written. The result's
   `FrameCount` must equal `ExportPlan.FrameCount`.

## Data formats

- **Frames:** RGBA32, top-down, tightly packed (`width × 4` bytes per row), both directions. iOS swizzles to/from BGRA
  natively (vImage). Note for the compositor: `AsyncGPUReadback` returns rows in GPU memory order — bottom-up on OpenGL
  ES (`SystemInfo.graphicsUVStartsAtTop == false`), top-down on Vulkan/Metal — so flip on GL before appending.
- **Audio:** `PcmFile` = raw f32le, interleaved stereo, 48 kHz. Decoders produce it (Android resamples/downmixes in C#
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
  native cancel never precedes its start; an already-cancelled token does not start native work at all.

## Memory

- Readers: a pool of **3** frame buffers (`FramePool`); a `VideoFrame` holds one until disposed. Forward reads drop older
  prefetched frames; backward jumps drop all. Holding up to 2 frames never blocks a read (speculative prefetches give
  their buffer back to a requested time); holding 3 frames stalls decoding (`ReadFrameAt` then times out).
- Writers: **3** frames in flight; `TryAppendVideoFrame` returns false when full, `AppendVideoFrame` waits (back-pressure).
  The caller's buffer is copied before the call returns (Android: one memcpy into a Java direct buffer; iOS: one swizzle
  into a pooled `CVPixelBuffer`).
- Native handles are released by `Dispose`; finalizers release leaked readers/writers on the main thread.

## Tests and checks

- `Tests/EditMode` — NUnit EditMode tests (assembly `Ballverse.Media.Tests`, run by the Unity test workflow): reader
  frame selection, pool-starvation regressions, random read stress, NativeOps cancellation ordering, MP4 fast start /
  metadata / cancellation cleanup, PCM conversion, fake-backend finalize and segment concat.
- `Tests/DotNet~/run.sh` — the same tests under plain .NET 8 without Unity (Unity ignores `~` folders); compiles
  Api/Common/Internal/Fake with small Unity stand-ins.
- `core-tests/MediaJavaCheck/check.sh <android-all.jar>` — javac `--release 8 -Xlint:all -Werror` over the Java plugin
  against Android API 34 classes and a `UnityPlayer` stub, then `YuvConverterCheck` (72 layout/crop/rotation cases
  against an independent reference). android-all.jar: e.g. Robolectric's `org.robolectric:android-all` from Maven.
