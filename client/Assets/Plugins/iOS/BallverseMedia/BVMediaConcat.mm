// Ballverse MediaBridge (iOS): segmented export — joins video-only MP4 segments without re-encoding and encodes the
// PCM mix into ONE AAC-LC track (spec §3.8, §7.5; C# IMediaBridge.ConcatSegmentsAsync).
// AVAssetReaderTrackOutput (passthrough, outputSettings nil) per segment → retimed sample buffers → AVAssetWriterInput
// (passthrough, sourceFormatHint of segment 0) + AAC AVAssetWriterInput fed from the raw f32le PCM file, MPEG-4 with
// shouldOptimizeForNetworkUse (moov first) and title/date metadata.
//
// Timing: samples of segment k are placed at pts − firstPts(k) + BVSegmentStartUs(frames before k, fps) (decode times
// shifted by the same amount, kept strictly increasing). Same formula as C# SegmentConcatOptions.SegmentStartUs.
// All segments must share the codec configuration (subtype, size, avcC): otherwise INVALID_STATE.
// Threading: runs on a global queue; cancellation via BVMedia_Cancel (registered before the work starts).
#import "BVMediaCommon.h"
#import <AVFoundation/AVFoundation.h>
#import <CoreMedia/CoreMedia.h>

#include <atomic>
#include <cstdio>
#include <memory>
#include <unistd.h>
#include <vector>

static const int64_t kConcatAudioLeadUs = 500000;
static const int64_t kConcatStallLimitUs = 10000000;
static const int kConcatAudioChunkFrames = 1024;

static int64_t BVSegmentStartUs(int64_t framesBefore, int32_t fps)
{
    return (framesBefore * 1000000 + fps - 1) / fps;
}

static NSData* BVAvcCOf(CMFormatDescriptionRef fd)
{
    CFTypeRef atoms = CMFormatDescriptionGetExtension(fd, kCMFormatDescriptionExtension_SampleDescriptionExtensionAtoms);
    if (atoms == NULL || CFGetTypeID(atoms) != CFDictionaryGetTypeID()) return nil;
    CFTypeRef avcC = CFDictionaryGetValue((CFDictionaryRef)atoms, CFSTR("avcC"));
    if (avcC == NULL || CFGetTypeID(avcC) != CFDataGetTypeID()) return nil;
    return (__bridge NSData*)avcC;
}

static BOOL BVSameVideoConfig(CMFormatDescriptionRef a, CMFormatDescriptionRef b)
{
    if (CMFormatDescriptionGetMediaSubType(a) != CMFormatDescriptionGetMediaSubType(b)) return NO;
    CMVideoDimensions da = CMVideoFormatDescriptionGetDimensions((CMVideoFormatDescriptionRef)a);
    CMVideoDimensions db = CMVideoFormatDescriptionGetDimensions((CMVideoFormatDescriptionRef)b);
    if (da.width != db.width || da.height != db.height) return NO;
    NSData* ca = BVAvcCOf(a);
    NSData* cb = BVAvcCOf(b);
    if (ca == nil || cb == nil) return ca == cb;
    return [ca isEqualToData:cb];
}

static BOOL BVIsSyncSample(CMSampleBufferRef sample)
{
    CFArrayRef attachments = CMSampleBufferGetSampleAttachmentsArray(sample, false);
    if (attachments == NULL || CFArrayGetCount(attachments) == 0) return YES;
    CFDictionaryRef dict = (CFDictionaryRef)CFArrayGetValueAtIndex(attachments, 0);
    CFTypeRef notSync = dict != NULL ? CFDictionaryGetValue(dict, kCMSampleAttachmentKey_NotSync) : NULL;
    return !(notSync != NULL && CFGetTypeID(notSync) == CFBooleanGetTypeID() && CFBooleanGetValue((CFBooleanRef)notSync));
}

@interface BVSegmentConcat : NSObject
- (instancetype)initWithRequest:(int64_t)requestId segments:(NSArray<NSString*>*)segments pcm:(NSString*)pcmPath out:(NSString*)outPath
                            fps:(int32_t)fps audioBitrate:(int32_t)audioBitrate title:(NSString*)title creation:(NSString*)creation
                      cancelled:(std::shared_ptr<std::atomic<bool>>)cancelled;
- (void)run;
@end

@implementation BVSegmentConcat
{
    int64_t _requestId;
    NSArray<NSString*>* _segments;
    NSString* _pcmPath;
    NSString* _outPath;
    int32_t _fps;
    int32_t _audioBitrate;
    NSString* _title;
    NSString* _creation;
    std::shared_ptr<std::atomic<bool>> _cancelled;
    AVAssetWriter* _writer;
    AVAssetWriterInput* _videoInput;
    AVAssetWriterInput* _audioInput;
    CMAudioFormatDescriptionRef _audioFormat;
    FILE* _pcm;
    int64_t _pcmFrames;
    int64_t _audioFrames;
    BOOL _audioDone;
    std::vector<uint8_t> _audioScratch;
}

- (instancetype)initWithRequest:(int64_t)requestId segments:(NSArray<NSString*>*)segments pcm:(NSString*)pcmPath out:(NSString*)outPath
                            fps:(int32_t)fps audioBitrate:(int32_t)audioBitrate title:(NSString*)title creation:(NSString*)creation
                      cancelled:(std::shared_ptr<std::atomic<bool>>)cancelled
{
    if ((self = [super init]) == nil) return nil;
    _requestId = requestId;
    _segments = [segments copy];
    _pcmPath = [pcmPath copy];
    _outPath = [outPath copy];
    _fps = fps;
    _audioBitrate = audioBitrate;
    _title = [title copy];
    _creation = [creation copy];
    _cancelled = cancelled;
    return self;
}

- (void)dealloc
{
    if (_pcm != NULL) fclose(_pcm);
    if (_audioFormat != NULL) CFRelease(_audioFormat);
}

- (void)abort
{
    if (_writer != nil && _writer.status == AVAssetWriterStatusWriting) [_writer cancelWriting];
    [[NSFileManager defaultManager] removeItemAtPath:_outPath error:nil];
}

- (void)fail:(NSString*)code message:(NSString*)message
{
    [self abort];
    if (_cancelled->load()) BVCancelled(_requestId);
    else BVFail(_requestId, code, message);
}

- (BOOL)checkCancelled
{
    if (!_cancelled->load()) return NO;
    [self abort];
    BVCancelled(_requestId);
    return YES;
}

// Loads a segment; returns its asset (keep it: AVAssetTrack.asset is weak) and the video track, or nil + error.
- (AVURLAsset*)loadSegment:(NSString*)path track:(AVAssetTrack**)trackOut error:(NSString**)code message:(NSString**)message
{
    *trackOut = nil;
    if (![[NSFileManager defaultManager] fileExistsAtPath:path])
    {
        *code = BV_ERR_FILE_NOT_FOUND;
        *message = [NSString stringWithFormat:@"segment not found: %@", path];
        return nil;
    }
    AVURLAsset* asset = [AVURLAsset URLAssetWithURL:[NSURL fileURLWithPath:path] options:@{ AVURLAssetPreferPreciseDurationAndTimingKey : @YES }];
    NSString* loadCode = BVLoadAssetKeys(asset, @[ @"tracks" ], message);
    if (loadCode != nil) { *code = loadCode; return nil; }
    AVAssetTrack* track = [asset tracksWithMediaType:AVMediaTypeVideo].firstObject;
    if (track == nil || track.formatDescriptions.count == 0)
    {
        *code = BV_ERR_NO_VIDEO_TRACK;
        *message = [NSString stringWithFormat:@"segment has no video track: %@", path];
        return nil;
    }
    *trackOut = track;
    return asset;
}

- (BOOL)setUpWithHint:(CMFormatDescriptionRef)hint
{
    NSFileManager* fm = [NSFileManager defaultManager];
    [fm createDirectoryAtPath:[_outPath stringByDeletingLastPathComponent] withIntermediateDirectories:YES attributes:nil error:nil];
    [fm removeItemAtPath:_outPath error:nil];
    NSError* error = nil;
    _writer = [[AVAssetWriter alloc] initWithURL:[NSURL fileURLWithPath:_outPath] fileType:AVFileTypeMPEG4 error:&error];
    if (_writer == nil) { [self fail:BV_ERR_IO message:error.localizedDescription ?: @"cannot create the output"]; return NO; }
    _writer.shouldOptimizeForNetworkUse = YES;
    _writer.metadata = BVMetadataItems(_title, _creation);

    _videoInput = [[AVAssetWriterInput alloc] initWithMediaType:AVMediaTypeVideo outputSettings:nil sourceFormatHint:hint];
    _videoInput.expectsMediaDataInRealTime = NO;
    if (![_writer canAddInput:_videoInput]) { [self fail:BV_ERR_MUXER_FAILED message:@"cannot add the passthrough video input"]; return NO; }
    [_writer addInput:_videoInput];

    if (_pcmPath.length > 0)
    {
        NSNumber* size = [fm attributesOfItemAtPath:_pcmPath error:nil][NSFileSize];
        _pcmFrames = size.longLongValue / 8;
    }
    if (_pcmFrames > 0)
    {
        AudioChannelLayout layout = {};
        layout.mChannelLayoutTag = kAudioChannelLayoutTag_Stereo;
        NSDictionary* audio = @{
            AVFormatIDKey : @(kAudioFormatMPEG4AAC),
            AVSampleRateKey : @48000,
            AVNumberOfChannelsKey : @2,
            AVEncoderBitRateKey : @(_audioBitrate),
            AVChannelLayoutKey : [NSData dataWithBytes:&layout length:sizeof(layout)],
        };
        _audioInput = [AVAssetWriterInput assetWriterInputWithMediaType:AVMediaTypeAudio outputSettings:audio];
        _audioInput.expectsMediaDataInRealTime = NO;
        if (![_writer canAddInput:_audioInput]) { [self fail:BV_ERR_ENCODER_FAILED message:@"cannot add the AAC input"]; return NO; }
        [_writer addInput:_audioInput];
        AudioStreamBasicDescription asbd = {};
        asbd.mSampleRate = 48000;
        asbd.mFormatID = kAudioFormatLinearPCM;
        asbd.mFormatFlags = kAudioFormatFlagIsFloat | kAudioFormatFlagIsPacked;
        asbd.mBytesPerPacket = 8;
        asbd.mFramesPerPacket = 1;
        asbd.mBytesPerFrame = 8;
        asbd.mChannelsPerFrame = 2;
        asbd.mBitsPerChannel = 32;
        if (CMAudioFormatDescriptionCreate(kCFAllocatorDefault, &asbd, 0, NULL, 0, NULL, NULL, &_audioFormat) != noErr)
        {
            [self fail:BV_ERR_ENCODER_FAILED message:@"cannot describe the PCM input"];
            return NO;
        }
        _pcm = fopen(_pcmPath.fileSystemRepresentation, "rb");
        if (_pcm == NULL) { [self fail:BV_ERR_FILE_NOT_FOUND message:_pcmPath]; return NO; }
        _audioScratch.resize((size_t)kConcatAudioChunkFrames * 8);
    }
    if (![_writer startWriting])
    {
        [self fail:BV_ERR_MUXER_FAILED message:_writer.error.localizedDescription ?: @"startWriting failed"];
        return NO;
    }
    [_writer startSessionAtSourceTime:kCMTimeZero];
    return YES;
}

// Appends PCM chunks while the audio input is ready, up to untilUs / limitFrames. Returns YES if any were appended.
// Same feeding scheme as BVVideoWriter (kept separate so the single-file writer stays untouched).
- (BOOL)pumpAudioUntil:(int64_t)untilUs limitFrames:(int64_t)limitFrames
{
    if (_audioInput == nil || _audioDone) return NO;
    BOOL any = NO;
    while (_audioInput.readyForMoreMediaData && _audioFrames * 1000000 / 48000 < untilUs)
    {
        int64_t n = kConcatAudioChunkFrames;
        if (n > _pcmFrames - _audioFrames) n = _pcmFrames - _audioFrames;
        if (n > limitFrames - _audioFrames) n = limitFrames - _audioFrames;
        if (n > 0) n = (int64_t)fread(_audioScratch.data(), 8, (size_t)n, _pcm);
        if (n <= 0)
        {
            [_audioInput markAsFinished];
            _audioDone = YES;
            return any;
        }
        size_t bytes = (size_t)n * 8;
        CMBlockBufferRef block = NULL;
        CMSampleBufferRef sample = NULL;
        OSStatus status = CMBlockBufferCreateWithMemoryBlock(kCFAllocatorDefault, NULL, bytes, kCFAllocatorDefault, NULL, 0, bytes,
                                                             kCMBlockBufferAssureMemoryNowFlag, &block);
        if (status == kCMBlockBufferNoErr) status = CMBlockBufferReplaceDataBytes(_audioScratch.data(), block, 0, bytes);
        if (status == noErr)
            status = CMAudioSampleBufferCreateReadyWithPacketDescriptions(kCFAllocatorDefault, block, _audioFormat, (CMItemCount)n,
                                                                          CMTimeMake(_audioFrames, 48000), NULL, &sample);
        BOOL ok = status == noErr && sample != NULL && [_audioInput appendSampleBuffer:sample];
        if (sample != NULL) CFRelease(sample);
        if (block != NULL) CFRelease(block);
        if (!ok)
        {
            _audioDone = YES;
            return NO;
        }
        _audioFrames += n;
        any = YES;
    }
    return any;
}

// Waits until the video input accepts data (feeding audio meanwhile so the writer can interleave). NO on failure/cancel.
- (BOOL)waitForVideoInput:(int64_t)ptsUs
{
    int64_t waitedUs = 0;
    while (!_videoInput.readyForMoreMediaData)
    {
        if (_writer.status == AVAssetWriterStatusFailed || _cancelled->load()) return NO;
        if (![self pumpAudioUntil:ptsUs + 2 * kConcatAudioLeadUs limitFrames:INT64_MAX])
        {
            usleep(2000);
            waitedUs += 2000;
            if (waitedUs > kConcatStallLimitUs) return NO;
        }
    }
    return YES;
}

- (void)run
{
    NSString* code = nil;
    NSString* message = nil;
    AVAssetTrack* firstTrack = nil;
    AVURLAsset* firstAsset = [self loadSegment:_segments.firstObject track:&firstTrack error:&code message:&message];
    if (firstAsset == nil) { [self fail:code message:message]; return; }
    CMFormatDescriptionRef hint = (__bridge CMFormatDescriptionRef)firstTrack.formatDescriptions.firstObject;
    if (![self setUpWithHint:hint]) return;

    int64_t total = 0;
    CMTime lastDts = kCMTimeInvalid;
    for (NSUInteger k = 0; k < _segments.count; k++)
    {
        if ([self checkCancelled]) return;
        AVAssetTrack* track = firstTrack;
        AVURLAsset* asset = k == 0 ? firstAsset : [self loadSegment:_segments[k] track:&track error:&code message:&message];
        if (asset == nil) { [self fail:code message:message]; return; }
        if (!BVSameVideoConfig(hint, (__bridge CMFormatDescriptionRef)track.formatDescriptions.firstObject))
        {
            [self fail:BV_ERR_INVALID_STATE message:[NSString stringWithFormat:@"segment %lu has a different codec configuration than segment 0", (unsigned long)k]];
            return;
        }
        NSError* error = nil;
        AVAssetReader* reader = [[AVAssetReader alloc] initWithAsset:asset error:&error];
        if (reader == nil) { [self fail:BV_ERR_CORRUPT message:error.localizedDescription ?: @"cannot read a segment"]; return; }
        AVAssetReaderTrackOutput* output = [AVAssetReaderTrackOutput assetReaderTrackOutputWithTrack:track outputSettings:nil];
        output.alwaysCopiesSampleData = NO;
        if (![reader canAddOutput:output]) { [self fail:BV_ERR_CORRUPT message:@"cannot read the segment's video samples"]; return; }
        [reader addOutput:output];
        if (![reader startReading]) { [self fail:BV_ERR_CORRUPT message:reader.error.localizedDescription ?: @"startReading failed"]; return; }

        CMTime base = CMTimeMake(BVSegmentStartUs(total, _fps), 1000000);
        CMTime first = kCMTimeInvalid;
        int64_t count = 0;
        while (true)
        {
            @autoreleasepool
            {
                if (_cancelled->load()) { [reader cancelReading]; [self checkCancelled]; return; }
                CMSampleBufferRef sample = [output copyNextSampleBuffer];
                if (sample == NULL) break;
                CMItemCount n = CMSampleBufferGetNumSamples(sample);
                if (n <= 0) { CFRelease(sample); continue; }
                CMItemCount timingCount = 0;
                CMSampleBufferGetSampleTimingInfoArray(sample, 0, NULL, &timingCount);
                std::vector<CMSampleTimingInfo> timing((size_t)(timingCount > 0 ? timingCount : 1));
                if (timingCount <= 0 || CMSampleBufferGetSampleTimingInfoArray(sample, timingCount, timing.data(), &timingCount) != noErr)
                {
                    CFRelease(sample);
                    [reader cancelReading];
                    [self fail:BV_ERR_CORRUPT message:@"segment sample without timing"];
                    return;
                }
                if (CMTIME_IS_INVALID(first))
                {
                    if (!BVIsSyncSample(sample))
                    {
                        CFRelease(sample);
                        [reader cancelReading];
                        [self fail:BV_ERR_INVALID_STATE message:[NSString stringWithFormat:@"segment %lu does not start with a sync frame", (unsigned long)k]];
                        return;
                    }
                    first = timing[0].presentationTimeStamp;
                }
                for (CMItemCount i = 0; i < timingCount; i++)
                {
                    CMTime pts = CMTimeAdd(base, CMTimeSubtract(timing[i].presentationTimeStamp, first));
                    timing[i].presentationTimeStamp = pts;
                    if (CMTIME_IS_VALID(timing[i].decodeTimeStamp))
                    {
                        CMTime dts = CMTimeAdd(base, CMTimeSubtract(timing[i].decodeTimeStamp, first));
                        if (CMTIME_IS_VALID(lastDts) && CMTimeCompare(dts, lastDts) <= 0) dts = CMTimeAdd(lastDts, CMTimeMake(1, 1000000));
                        if (CMTimeCompare(dts, pts) > 0) dts = pts;
                        timing[i].decodeTimeStamp = dts;
                        lastDts = dts;
                    }
                }
                CMSampleBufferRef retimed = NULL;
                OSStatus status = CMSampleBufferCreateCopyWithNewTiming(kCFAllocatorDefault, sample, timingCount, timing.data(), &retimed);
                CFRelease(sample);
                if (status != noErr || retimed == NULL)
                {
                    [reader cancelReading];
                    [self fail:BV_ERR_MUXER_FAILED message:[NSString stringWithFormat:@"cannot retime a sample (%d)", (int)status]];
                    return;
                }
                int64_t ptsUs = (int64_t)llround(CMTimeGetSeconds(timing[0].presentationTimeStamp) * 1e6);
                BOOL ok = [self waitForVideoInput:ptsUs] && [_videoInput appendSampleBuffer:retimed];
                CFRelease(retimed);
                if (!ok)
                {
                    [reader cancelReading];
                    if ([self checkCancelled]) return;
                    [self fail:BV_ERR_MUXER_FAILED message:_writer.error.localizedDescription ?: @"cannot append a video sample"];
                    return;
                }
                [self pumpAudioUntil:ptsUs + kConcatAudioLeadUs limitFrames:INT64_MAX];
                count += n;
            }
        }
        if (reader.status == AVAssetReaderStatusFailed)
        {
            [self fail:BV_ERR_CORRUPT message:reader.error.localizedDescription ?: @"cannot read a segment"];
            return;
        }
        if (count == 0) { [self fail:BV_ERR_CORRUPT message:[NSString stringWithFormat:@"segment %lu has no video samples", (unsigned long)k]]; return; }
        total += count;
        BVProgress(_requestId, (double)(k + 1) / (double)(_segments.count + 1));
    }

    int64_t endUs = BVSegmentStartUs(total, _fps);
    [_videoInput markAsFinished];
    if (_audioInput != nil)
    {
        int64_t limit = (endUs * 48000 + 999999) / 1000000;
        int64_t waitedUs = 0;
        while (!_audioDone)
        {
            if ([self checkCancelled]) return;
            if (_writer.status == AVAssetWriterStatusFailed) break;
            if ([self pumpAudioUntil:INT64_MAX limitFrames:limit]) { waitedUs = 0; continue; }
            if (_audioDone) break;
            usleep(2000);
            waitedUs += 2000;
            if (waitedUs > kConcatStallLimitUs) { [self fail:BV_ERR_TIMEOUT message:@"audio input stalled"]; return; }
        }
    }
    if ([self checkCancelled]) return;
    if (_writer.status == AVAssetWriterStatusFailed)
    {
        [self fail:BV_ERR_ENCODER_FAILED message:_writer.error.localizedDescription ?: @"writer failed"];
        return;
    }
    [_writer endSessionAtSourceTime:CMTimeMake(endUs, 1000000)];
    int64_t frames = total;
    [_writer finishWritingWithCompletionHandler:^{
        if (self->_writer.status == AVAssetWriterStatusCompleted && !self->_cancelled->load())
        {
            BVSucceed(self->_requestId, @{ @"frames" : @(frames), @"durationUs" : @(endUs) });
            return;
        }
        [[NSFileManager defaultManager] removeItemAtPath:self->_outPath error:nil];
        if (self->_cancelled->load()) BVCancelled(self->_requestId);
        else BVFail(self->_requestId, BV_ERR_MUXER_FAILED, self->_writer.error.localizedDescription ?: @"finishWriting failed");
    }];
}

@end

extern "C" void BVMedia_ConcatSegments(int64_t requestId, const char* segmentsNewlineSeparated, const char* pcmPath, const char* outputPath,
                                       int32_t fps, int32_t audioBitrate, const char* title, const char* creationIso8601)
{
    NSMutableArray<NSString*>* segments = [NSMutableArray array];
    for (NSString* line in [BVString(segmentsNewlineSeparated) componentsSeparatedByString:@"\n"])
    {
        if (line.length > 0) [segments addObject:line];
    }
    if (segments.count == 0 || fps <= 0)
    {
        BVFail(requestId, BV_ERR_INVALID_STATE, segments.count == 0 ? @"no segments" : @"fps must be positive");
        return;
    }
    // Registered before any work starts, so a cancel from C# (always issued after this call returns) is never lost.
    auto cancelled = std::make_shared<std::atomic<bool>>(false);
    BVRegisterCancel(requestId, ^{ cancelled->store(true); });
    BVSegmentConcat* job = [[BVSegmentConcat alloc] initWithRequest:requestId segments:segments pcm:BVString(pcmPath) out:BVString(outputPath)
                                                                fps:fps audioBitrate:audioBitrate title:BVString(title)
                                                           creation:BVString(creationIso8601) cancelled:cancelled];
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{
        @autoreleasepool
        {
            [job run];
        }
    });
}
