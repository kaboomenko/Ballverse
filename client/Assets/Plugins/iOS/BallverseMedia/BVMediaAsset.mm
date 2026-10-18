// Ballverse MediaBridge (iOS): AVAsset probe, audio decoding to PCM, saving to the Photos library.
#import "BVMediaCommon.h"
#import <AVFoundation/AVFoundation.h>
#import <CoreMedia/CoreMedia.h>
#import <Photos/Photos.h>

#include <atomic>
#include <cmath>
#include <cstdio>
#include <memory>
#include <vector>

// ---------------------------------------------------------------------------------------------------- helpers

static NSString* BVFourCC(FourCharCode code)
{
    char s[5] = {(char)((code >> 24) & 0xFF), (char)((code >> 16) & 0xFF), (char)((code >> 8) & 0xFF), (char)(code & 0xFF), 0};
    for (int i = 0; i < 4; i++) if (s[i] < 32 || s[i] > 126) s[i] = '?';
    return [NSString stringWithUTF8String:s];
}

int BVRotationOfTransform(CGAffineTransform t)
{
    double degrees = atan2(t.b, t.a) * 180.0 / M_PI;
    int d = (int)lround(degrees);
    d = ((d % 360) + 360) % 360;
    int snapped = (d + 45) / 90 * 90;
    return snapped == 360 ? 0 : snapped;
}

// "sdr" | "pq" | "hlg" | "dolbyvision"
NSString* BVTransferOfTrack(AVAssetTrack* track, int* bitDepth)
{
    NSString* result = @"sdr";
    int depth = 8;
    for (id desc in track.formatDescriptions)
    {
        CMFormatDescriptionRef fd = (__bridge CMFormatDescriptionRef)desc;
        FourCharCode subtype = CMFormatDescriptionGetMediaSubType(fd);
        CFTypeRef transfer = CMFormatDescriptionGetExtension(fd, kCMFormatDescriptionExtension_TransferFunction);
        if (transfer != NULL && CFGetTypeID(transfer) == CFStringGetTypeID())
        {
            if (CFEqual(transfer, kCMFormatDescriptionTransferFunction_ITU_R_2100_HLG)) result = @"hlg";
            else if (CFEqual(transfer, kCMFormatDescriptionTransferFunction_SMPTE_ST_2084_PQ)) result = @"pq";
        }
        if (subtype == 'dvh1' || subtype == 'dvhe' || subtype == 'dva1' || subtype == 'dvav') result = @"dolbyvision";
        CFTypeRef bits = CMFormatDescriptionGetExtension(fd, CFSTR("BitsPerComponent"));
        if (bits != NULL && CFGetTypeID(bits) == CFNumberGetTypeID())
        {
            int v = 0;
            CFNumberGetValue((CFNumberRef)bits, kCFNumberIntType, &v);
            if (v > 0) depth = v;
        }
    }
    if (![result isEqualToString:@"sdr"] && depth < 10) depth = 10;
    if (bitDepth != NULL) *bitDepth = depth;
    return result;
}

/// Loads the given asset keys synchronously (call from a background thread). Returns nil on success, else an error code.
NSString* BVLoadAssetKeys(AVAsset* asset, NSArray<NSString*>* keys, NSString** message)
{
    dispatch_semaphore_t done = dispatch_semaphore_create(0);
    [asset loadValuesAsynchronouslyForKeys:keys completionHandler:^{ dispatch_semaphore_signal(done); }];
    if (dispatch_semaphore_wait(done, dispatch_time(DISPATCH_TIME_NOW, (int64_t)30 * NSEC_PER_SEC)) != 0)
    {
        [asset cancelLoading];
        if (message) *message = @"timed out loading the asset";
        return BV_ERR_TIMEOUT;
    }
    for (NSString* key in keys)
    {
        NSError* error = nil;
        if ([asset statusOfValueForKey:key error:&error] != AVKeyValueStatusLoaded)
        {
            if (message) *message = error.localizedDescription ?: [NSString stringWithFormat:@"cannot load '%@'", key];
            return BV_ERR_CORRUPT;
        }
    }
    return nil;
}

// ---------------------------------------------------------------------------------------------------- probe

extern "C" void BVMedia_Probe(int64_t requestId, const char* path)
{
    NSString* file = BVString(path);
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        @autoreleasepool
        {
            NSFileManager* fm = [NSFileManager defaultManager];
            if (![fm fileExistsAtPath:file]) { BVFail(requestId, BV_ERR_FILE_NOT_FOUND, file); return; }
            AVURLAsset* asset = [AVURLAsset URLAssetWithURL:[NSURL fileURLWithPath:file]
                                                    options:@{ AVURLAssetPreferPreciseDurationAndTimingKey : @YES }];
            NSString* message = nil;
            NSString* code = BVLoadAssetKeys(asset, @[ @"tracks", @"duration", @"hasProtectedContent" ], &message);
            if (code != nil) { BVFail(requestId, code, message); return; }
            if (asset.hasProtectedContent) { BVFail(requestId, BV_ERR_DRM_PROTECTED, @"the file is DRM protected"); return; }

            NSMutableDictionary* info = [NSMutableDictionary dictionary];
            NSNumber* size = [fm attributesOfItemAtPath:file error:nil][NSFileSize];
            info[@"fileSizeBytes"] = @(size.longLongValue);
            CMTime duration = asset.duration;
            info[@"durationUs"] = @(CMTIME_IS_NUMERIC(duration) ? (int64_t)llround(CMTimeGetSeconds(duration) * 1e6) : 0);

            AVAssetTrack* video = [asset tracksWithMediaType:AVMediaTypeVideo].firstObject;
            if (video != nil)
            {
                if (@available(iOS 13, *))
                {
                    if (!video.isDecodable) { BVFail(requestId, BV_ERR_UNSUPPORTED_FORMAT, @"the video codec cannot be decoded on this device"); return; }
                }
                CGSize natural = video.naturalSize;
                int bitDepth = 8;
                NSString* transfer = BVTransferOfTrack(video, &bitDepth);
                FourCharCode subtype = 0;
                if (video.formatDescriptions.count > 0) subtype = CMFormatDescriptionGetMediaSubType((__bridge CMFormatDescriptionRef)video.formatDescriptions.firstObject);
                info[@"hasVideo"] = @YES;
                info[@"width"] = @((int)lround(fabs(natural.width)));
                info[@"height"] = @((int)lround(fabs(natural.height)));
                info[@"rotation"] = @(BVRotationOfTransform(video.preferredTransform));
                info[@"frameRate"] = @(video.nominalFrameRate);
                info[@"videoCodec"] = BVFourCC(subtype);
                info[@"transfer"] = transfer;
                info[@"bitDepth"] = @(bitDepth);
            }
            else
            {
                info[@"hasVideo"] = @NO;
                info[@"width"] = @0;
                info[@"height"] = @0;
                info[@"rotation"] = @0;
                info[@"frameRate"] = @0;
                info[@"videoCodec"] = @"";
                info[@"transfer"] = @"sdr";
                info[@"bitDepth"] = @8;
            }

            AVAssetTrack* audio = [asset tracksWithMediaType:AVMediaTypeAudio].firstObject;
            BOOL hasAudio = NO;
            if (audio != nil && audio.formatDescriptions.count > 0)
            {
                BOOL decodable = YES;
                if (@available(iOS 13, *)) decodable = audio.isDecodable;
                CMAudioFormatDescriptionRef fd = (__bridge CMAudioFormatDescriptionRef)audio.formatDescriptions.firstObject;
                const AudioStreamBasicDescription* asbd = CMAudioFormatDescriptionGetStreamBasicDescription(fd);
                if (decodable && asbd != NULL)
                {
                    hasAudio = YES;
                    info[@"audioCodec"] = BVFourCC(asbd->mFormatID);
                    info[@"audioSampleRate"] = @((int)asbd->mSampleRate);
                    info[@"audioChannels"] = @((int)asbd->mChannelsPerFrame);
                }
            }
            if (!hasAudio)
            {
                info[@"audioCodec"] = @"";
                info[@"audioSampleRate"] = @0;
                info[@"audioChannels"] = @0;
            }
            info[@"hasAudio"] = @(hasAudio);
            if (video == nil && !hasAudio) { BVFail(requestId, BV_ERR_UNSUPPORTED_FORMAT, @"no decodable tracks"); return; }
            BVSucceed(requestId, info);
        }
    });
}

// ---------------------------------------------------------------------------------------------------- audio decode

extern "C" void BVMedia_DecodeAudio(int64_t requestId, const char* path, const char* outputPath, int64_t startUs, int64_t durationUs)
{
    NSString* file = BVString(path);
    NSString* outFile = BVString(outputPath);
    auto cancelled = std::make_shared<std::atomic<bool>>(false);
    BVRegisterCancel(requestId, ^{ cancelled->store(true); });
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        @autoreleasepool
        {
            AVURLAsset* asset = [AVURLAsset URLAssetWithURL:[NSURL fileURLWithPath:file] options:@{ AVURLAssetPreferPreciseDurationAndTimingKey : @YES }];
            NSString* message = nil;
            NSString* code = BVLoadAssetKeys(asset, @[ @"tracks", @"duration" ], &message);
            if (code != nil) { BVFail(requestId, code, message); return; }
            AVAssetTrack* track = [asset tracksWithMediaType:AVMediaTypeAudio].firstObject;
            if (track == nil)
            {
                BVSucceed(requestId, @{ @"path" : outFile, @"sampleRate" : @0, @"channels" : @0, @"frames" : @0 });
                return;
            }
            NSError* error = nil;
            AVAssetReader* reader = [[AVAssetReader alloc] initWithAsset:asset error:&error];
            if (reader == nil) { BVFail(requestId, BV_ERR_DECODER_FAILED, error.localizedDescription); return; }
            CMTime start = CMTimeMake(startUs, 1000000);
            reader.timeRange = CMTimeRangeMake(start, durationUs > 0 ? CMTimeMake(durationUs, 1000000) : kCMTimePositiveInfinity);
            NSDictionary* settings = @{
                AVFormatIDKey : @(kAudioFormatLinearPCM),
                AVSampleRateKey : @48000,
                AVNumberOfChannelsKey : @2,
                AVLinearPCMBitDepthKey : @32,
                AVLinearPCMIsFloatKey : @YES,
                AVLinearPCMIsBigEndianKey : @NO,
                AVLinearPCMIsNonInterleaved : @NO,
            };
            AVAssetReaderAudioMixOutput* output = [AVAssetReaderAudioMixOutput assetReaderAudioMixOutputWithAudioTracks:@[ track ] audioSettings:settings];
            if (![reader canAddOutput:output]) { BVFail(requestId, BV_ERR_UNSUPPORTED_FORMAT, @"cannot decode the audio track"); return; }
            [reader addOutput:output];
            if (![reader startReading]) { BVFail(requestId, BV_ERR_DECODER_FAILED, reader.error.localizedDescription); return; }

            FILE* f = fopen(outFile.fileSystemRepresentation, "wb");
            if (f == NULL) { [reader cancelReading]; BVFail(requestId, BV_ERR_IO, @"cannot create the PCM file"); return; }
            const int64_t maxFrames = durationUs > 0 ? (durationUs * 48000 + 999999) / 1000000 : INT64_MAX;
            int64_t written = 0;
            bool first = true;
            bool ok = true;
            std::vector<uint8_t> scratch;
            while (written < maxFrames)
            {
                if (cancelled->load()) { ok = false; break; }
                CMSampleBufferRef sample = [output copyNextSampleBuffer];
                if (sample == NULL) break;
                if (first)
                {
                    // Leading silence when the audio track starts after the requested start.
                    first = false;
                    double lead = CMTimeGetSeconds(CMTimeSubtract(CMSampleBufferGetPresentationTimeStamp(sample), start));
                    int64_t silence = lead > 0 ? (int64_t)llround(lead * 48000) : 0;
                    if (silence > maxFrames) silence = maxFrames;
                    if (silence > 0)
                    {
                        std::vector<float> zeros((size_t)silence * 2, 0.0f);
                        fwrite(zeros.data(), sizeof(float), zeros.size(), f);
                        written += silence;
                    }
                }
                CMBlockBufferRef block = CMSampleBufferGetDataBuffer(sample);
                size_t length = block != NULL ? CMBlockBufferGetDataLength(block) : 0;
                if (length > 0)
                {
                    scratch.resize(length);
                    if (CMBlockBufferCopyDataBytes(block, 0, length, scratch.data()) == kCMBlockBufferNoErr)
                    {
                        int64_t frames = (int64_t)(length / 8);
                        if (frames > maxFrames - written) frames = maxFrames - written;
                        if (fwrite(scratch.data(), 8, (size_t)frames, f) != (size_t)frames) ok = false;
                        written += frames;
                    }
                }
                CFRelease(sample);
                if (!ok) break;
            }
            fclose(f);
            if (cancelled->load())
            {
                [reader cancelReading];
                [[NSFileManager defaultManager] removeItemAtPath:outFile error:nil];
                BVCancelled(requestId);
                return;
            }
            if (!ok || reader.status == AVAssetReaderStatusFailed)
            {
                [reader cancelReading];
                [[NSFileManager defaultManager] removeItemAtPath:outFile error:nil];
                BVFail(requestId, ok ? BV_ERR_DECODER_FAILED : BV_ERR_IO, reader.error.localizedDescription ?: @"audio decoding failed");
                return;
            }
            if (reader.status == AVAssetReaderStatusReading) [reader cancelReading];
            BVSucceed(requestId, @{ @"path" : outFile, @"sampleRate" : @48000, @"channels" : @2, @"frames" : @(written) });
        }
    });
}

// ---------------------------------------------------------------------------------------------------- gallery

@interface BVLegacySaveTarget : NSObject
@property(nonatomic) int64_t requestId;
- (void)video:(NSString*)videoPath didFinishSavingWithError:(NSError*)error contextInfo:(void*)contextInfo;
@end

static NSMutableSet* gLegacySaveTargets = nil;

@implementation BVLegacySaveTarget
- (void)video:(NSString*)videoPath didFinishSavingWithError:(NSError*)error contextInfo:(void*)contextInfo
{
    if (error == nil)
        BVSucceed(self.requestId, @{ @"status" : @"saved", @"location" : @"", @"addedToAlbum" : @NO, @"message" : @"" });
    else if (error.code == -3310 || error.code == -3311 || error.code == 3311) // ALAssetsLibrary / PHPhotos "access denied"
        BVSucceed(self.requestId, @{ @"status" : @"permission_denied", @"location" : @"", @"addedToAlbum" : @NO, @"message" : error.localizedDescription ?: @"" });
    else
        BVSucceed(self.requestId, @{ @"status" : @"failed", @"location" : @"", @"addedToAlbum" : @NO, @"message" : error.localizedDescription ?: @"" });
    @synchronized(gLegacySaveTargets) { [gLegacySaveTargets removeObject:self]; }
}
@end

// Add-only save (spec §3.8: NSPhotoLibraryAddUsageDescription only). Add-only access cannot look up or create albums, so
// the video lands in the library (Recents) and addedToAlbum is NO on iOS (album pending an owner decision, see C#
// GallerySaveResult.AddedToAlbum). A cancel that arrives before performChanges starts prevents the save; once the
// system accepted the change it cannot be undone.
static void BVSaveWithPhotoKit(int64_t requestId, NSURL* url, std::shared_ptr<std::atomic<bool>> cancelled)
{
    if (cancelled->load()) { BVCancelled(requestId); return; }
    __block NSString* localId = @"";
    [[PHPhotoLibrary sharedPhotoLibrary] performChanges:^{
        PHAssetCreationRequest* request = [PHAssetCreationRequest creationRequestForAsset];
        PHAssetResourceCreationOptions* resourceOptions = [PHAssetResourceCreationOptions new];
        resourceOptions.shouldMoveFile = NO;
        [request addResourceWithType:PHAssetResourceTypeVideo fileURL:url options:resourceOptions];
        PHObjectPlaceholder* placeholder = request.placeholderForCreatedAsset;
        if (placeholder.localIdentifier != nil) localId = placeholder.localIdentifier;
    }
        completionHandler:^(BOOL success, NSError* error) {
            if (success)
                BVSucceed(requestId, @{ @"status" : @"saved", @"location" : localId, @"addedToAlbum" : @NO, @"message" : @"" });
            else if (cancelled->load())
                BVCancelled(requestId);
            else
                BVSucceed(requestId, @{ @"status" : @"failed", @"location" : @"", @"addedToAlbum" : @NO, @"message" : error.localizedDescription ?: @"save failed" });
        }];
}

extern "C" void BVMedia_SaveToGallery(int64_t requestId, const char* path, const char* album)
{
    NSString* file = BVString(path);
    (void)album; // iOS release 1: add-only access, no album (see BVSaveWithPhotoKit)
    NSURL* url = [NSURL fileURLWithPath:file];
    // Registered before anything asynchronous starts: C# issues BVMedia_Cancel only after this call returned.
    auto cancelled = std::make_shared<std::atomic<bool>>(false);
    BVRegisterCancel(requestId, ^{ cancelled->store(true); });
    if (@available(iOS 14, *))
    {
        [PHPhotoLibrary requestAuthorizationForAccessLevel:PHAccessLevelAddOnly handler:^(PHAuthorizationStatus status) {
            if (cancelled->load()) { BVCancelled(requestId); return; }
            if (status != PHAuthorizationStatusAuthorized && status != PHAuthorizationStatusLimited)
            {
                BVSucceed(requestId, @{ @"status" : @"permission_denied", @"location" : @"", @"addedToAlbum" : @NO, @"message" : @"photo library access denied" });
                return;
            }
            BVSaveWithPhotoKit(requestId, url, cancelled);
        }];
    }
    else
    {
        // iOS 13: UISaveVideoAtPathToSavedPhotosAlbum needs only the add-usage description.
        dispatch_async(dispatch_get_main_queue(), ^{
            if (cancelled->load()) { BVCancelled(requestId); return; }
            if (!UIVideoAtPathIsCompatibleWithSavedPhotosAlbum(file))
            {
                BVSucceed(requestId, @{ @"status" : @"failed", @"location" : @"", @"addedToAlbum" : @NO, @"message" : @"video not compatible with the photo library" });
                return;
            }
            static dispatch_once_t once;
            dispatch_once(&once, ^{ gLegacySaveTargets = [NSMutableSet set]; });
            BVLegacySaveTarget* target = [BVLegacySaveTarget new];
            target.requestId = requestId;
            @synchronized(gLegacySaveTargets) { [gLegacySaveTargets addObject:target]; }
            UISaveVideoAtPathToSavedPhotosAlbum(file, target, @selector(video:didFinishSavingWithError:contextInfo:), NULL);
        });
    }
}
