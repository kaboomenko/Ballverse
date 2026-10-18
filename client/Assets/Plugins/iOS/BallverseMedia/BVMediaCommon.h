// Ballverse MediaBridge (iOS) — shared declarations of the Objective-C++ plugin.
// C ABI consumed by Assets/Ballverse/Media/Ios/IosNative.cs via [DllImport("__Internal")].
// Requires ARC (-fobjc-arc is added per file by Ballverse.Media.Editor.IosBuildPostprocessor).
#pragma once

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#include <stdint.h>

#if !__has_feature(objc_arc)
#error "BallverseMedia requires ARC (-fobjc-arc); see Ballverse.Media.Editor.IosBuildPostprocessor"
#endif

typedef void (*BVResultCallback)(int64_t requestId, int32_t status, const char* json);
typedef void (*BVProgressCallback)(int64_t requestId, double progress);

enum
{
    BVStatusOk = 0,
    BVStatusCancelled = 1,
    BVStatusError = 2,
};

// Error codes shared with C# MediaException.ParseCode and the Android plugin.
#define BV_ERR_NOT_SUPPORTED @"NOT_SUPPORTED"
#define BV_ERR_FILE_NOT_FOUND @"FILE_NOT_FOUND"
#define BV_ERR_IO @"IO"
#define BV_ERR_INSUFFICIENT_SPACE @"INSUFFICIENT_SPACE"
#define BV_ERR_PERMISSION_DENIED @"PERMISSION_DENIED"
#define BV_ERR_UNSUPPORTED_FORMAT @"UNSUPPORTED_FORMAT"
#define BV_ERR_DRM_PROTECTED @"DRM_PROTECTED"
#define BV_ERR_CORRUPT @"CORRUPT"
#define BV_ERR_NO_VIDEO_TRACK @"NO_VIDEO_TRACK"
#define BV_ERR_DECODER_FAILED @"DECODER_FAILED"
#define BV_ERR_ENCODER_FAILED @"ENCODER_FAILED"
#define BV_ERR_MUXER_FAILED @"MUXER_FAILED"
#define BV_ERR_TIMEOUT @"TIMEOUT"
#define BV_ERR_BUSY @"BUSY"
#define BV_ERR_INVALID_STATE @"INVALID_STATE"
#define BV_ERR_DOWNLOAD @"DOWNLOAD"

// Completion helpers (any thread). Each unregisters the request's cancel handler.
void BVSucceed(int64_t requestId, NSDictionary* result);
void BVFail(int64_t requestId, NSString* code, NSString* message);
void BVCancelled(int64_t requestId);
void BVProgress(int64_t requestId, double progress);

// Cancellation registry: BVMedia_Cancel(requestId) runs (once) the handler registered for the request.
void BVRegisterCancel(int64_t requestId, dispatch_block_t handler);

// Thread-local "CODE|message" for synchronous calls (read by C# through BVMedia_CopyLastError).
void BVSetLastError(NSString* code, NSString* message);

NSString* BVString(const char* utf8);
// Topmost presented view controller of the key window (main thread only).
UIViewController* BVTopViewController(void);
// Free bytes for "important usage" on the app volume, -1 if unknown.
int64_t BVFreeSpaceBytes(void);
// Clockwise display rotation (0/90/180/270) of a track's preferredTransform.
int BVRotationOfTransform(CGAffineTransform transform);
// "sdr" | "pq" | "hlg" | "dolbyvision" of a video track (and its bit depth).
@class AVAssetTrack;
@class AVAsset;
NSString* BVTransferOfTrack(AVAssetTrack* track, int* bitDepth);
// Loads asset keys synchronously (background threads only). nil on success, else an error code (+ message).
NSString* BVLoadAssetKeys(AVAsset* asset, NSArray<NSString*>* keys, NSString** message);
// Title / creation-date metadata items for AVAssetWriter.metadata (empty strings are skipped; never location).
@class AVMetadataItem;
NSArray<AVMetadataItem*>* BVMetadataItems(NSString* title, NSString* creationIso8601);
// Output frame size rule shared with C# VideoReaderOptions.ComputeOutputSize.
void BVComputeOutputSize(int displayWidth, int displayHeight, int maxWidth, int maxHeight, int* outWidth, int* outHeight);
