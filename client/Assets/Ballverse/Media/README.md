# Ballverse.Media — MediaBridge (release 1)

Native video import / decode / encode / save / share behind `IMediaBridge` (spec §3.2, §3.8, §7.5).

| Backend | Where | Implementation |
|---|---|---|
| Android | `Android/` + `Assets/Plugins/Android/BallverseMedia/*.java` | Photo Picker (platform / Google Play services backport / `ACTION_OPEN_DOCUMENT`) in a transparent `PickerActivity`; MediaExtractor + MediaCodec → ImageReader (YUV_420_888 / P010) → `YuvConverter` (rotation, crop, scaling, BT.601/709/2020, HDR→SDR fallback); MediaCodec H.264 High (Baseline fallback) with an input surface fed through EGL (`lockHardwareCanvas` fallback) + AAC + MediaMuxer; MediaStore / legacy Movies save; `MediaFileProvider` share. |
| iOS | `Ios/` + `Assets/Plugins/iOS/BallverseMedia/*.mm` | PHPicker (UIImagePickerController on iOS 13), AVAssetReader + video composition (rotation, scaling, SDR tone mapping), AVAssetWriter + pixel-buffer adaptor (BGRA) + AAC, PHPhotoLibrary add-only save, UIActivityViewController, thermal/low-power/accessibility signals. |
| Fake | `Fake/` | Editor/desktop only: test-pattern video, PNG sequence + WAV output. **Not a codec.** |

`MediaBridgeFactory.Create()` picks the backend. Build integration is automatic: `Editor/AndroidManifestPatcher`
adds the activity, provider, permission (API ≤ 28) and `<queries>` to the generated Gradle project;
`Editor/IosBuildPostprocessor` links the frameworks, compiles the `.mm` files with ARC and adds the photo-library usage
strings to Info.plist (placeholder texts — localise before store submission).

## Data formats

- **Frames:** RGBA32, top-down, tightly packed (`width × 4` bytes per row), both directions. iOS swizzles to/from BGRA
  natively (vImage). Note for the compositor: `AsyncGPUReadback` returns rows in GPU memory order — bottom-up on OpenGL
  ES (`SystemInfo.graphicsUVStartsAtTop == false`), top-down on Vulkan/Metal — so flip on GL before appending.
- **Audio:** `PcmFile` = raw f32le, interleaved stereo, 48 kHz. Decoders produce it (Android resamples/downmixes in C#
  with a windowed-sinc `SincResampler`); writers encode it to AAC-LC 48 kHz (192 kbit/s by default).
- **MP4:** H.264 + AAC, `moov` at the front (iOS `shouldOptimizeForNetworkUse`; Android `Mp4FastStart` rewrite).

## Threading

- Every `IMediaBridge`, `IVideoReader`, `IVideoWriter` and `VideoFrame.Data` call is **main-thread only** (checked).
- Native completions (Java threads, GCD queues) are posted to the main thread via Unity's `SynchronizationContext`
  (`Internal/MainThread`); tasks complete and progress reports run on the main thread.
- Each reader owns one worker thread that drives the decoder synchronously (Android: JNI-attached). Writers encode on
  native threads (Android writer thread with its own EGL context; iOS serial dispatch queue).
- Cancellation completes the task immediately and tells native code to stop; partial files are deleted natively.

## Memory

- Readers: a pool of **3** frame buffers (`FramePool`); a `VideoFrame` holds one until disposed. Forward reads drop older
  prefetched frames; backward jumps drop all. Holding 3 frames stalls decoding (`ReadFrameAt` then times out).
- Writers: **3** frames in flight; `TryAppendVideoFrame` returns false when full, `AppendVideoFrame` waits (back-pressure).
  The caller's buffer is copied before the call returns (Android: one memcpy into a Java direct buffer; iOS: one swizzle
  into a pooled `CVPixelBuffer`).
- Native handles are released by `Dispose`; finalizers release leaked readers/writers on the main thread.

## Checks without Unity

- `core-tests/MediaJavaCheck/check.sh <android-all.jar>` — javac `--release 8 -Xlint:all -Werror` over the Java plugin
  against Android API 34 classes and a `UnityPlayer` stub.
