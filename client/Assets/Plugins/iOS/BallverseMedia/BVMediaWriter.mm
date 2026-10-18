// Ballverse MediaBridge (iOS): H.264 + AAC MP4 writer (spec §3.8).
// AVAssetWriter (MPEG-4, shouldOptimizeForNetworkUse = moov first) with an AVAssetWriterInputPixelBufferAdaptor
// (BGRA, IOSurface-backed pool) for H.264 High (auto level; Baseline fallback when High is rejected) and an AAC-LC
// 48 kHz stereo input fed from a raw f32le PCM file. Optional title/date metadata (spec §3.8; never location).
//
// Threading: BVWriter_Append runs on the caller (Unity main) thread: it waits for one of 3 in-flight slots, copies the
// RGBA frame into a pooled CVPixelBuffer (vImage RGBA→BGRA swizzle) and hands it to a private serial queue that
// appends it (pumping audio so AVAssetWriter can interleave). Finish/cancel run on the same queue, after pending frames.
#import "BVMediaCommon.h"
#import <Accelerate/Accelerate.h>
#import <AVFoundation/AVFoundation.h>
#import <CoreMedia/CoreMedia.h>
#import <CoreVideo/CoreVideo.h>

#include <atomic>
#include <cstdio>
#include <unistd.h>
#include <vector>

static const int kSlots = 3;
static const int kAudioChunkFrames = 1024;
static const int64_t kAudioLeadUs = 500000;
static const int64_t kStallLimitUs = 10000000;

@interface BVVideoWriter : NSObject
- (BOOL)appendRgba:(const uint8_t*)rgba rowBytes:(int)rowBytes pts:(int64_t)ptsUs timeoutMs:(int)timeoutMs result:(int*)result;
- (int)framesInFlight;
- (BOOL)isFinishing;
- (void)finishWithRequest:(int64_t)requestId;
- (void)cancel;
@end

@implementation BVVideoWriter
{
    NSString* _path;
    AVAssetWriter* _writer;
    AVAssetWriterInput* _videoInput;
    AVAssetWriterInput* _audioInput;
    AVAssetWriterInputPixelBufferAdaptor* _adaptor;
    dispatch_queue_t _queue;
    dispatch_semaphore_t _slots;
    std::atomic<int> _inFlight;
    std::atomic<bool> _failed;
    std::atomic<bool> _cancelled;
    std::atomic<bool> _finishing;
    NSString* _error; // guarded by @synchronized(self)
    int _width;
    int _height;
    int _fps;
    // queue-only state
    FILE* _pcm;
    int64_t _pcmFrames;
    int64_t _audioFrames;
    BOOL _audioDone;
    CMAudioFormatDescriptionRef _audioFormat;
    std::vector<uint8_t> _audioScratch;
    int64_t _lastPts;
    int _frames;
}

- (instancetype)initWithPath:(NSString*)path width:(int)width height:(int)height fps:(int)fps bitrate:(int)bitrate
                    keyframe:(int)keyframeSeconds audioPath:(NSString*)audioPath audioBitrate:(int)audioBitrate
                       title:(NSString*)title creationDate:(NSString*)creationDate
{
    if ((self = [super init]) == nil) return nil;
    _path = [path copy];
    _width = width;
    _height = height;
    _fps = fps;
    _lastPts = -1;
    _inFlight = 0;
    _failed = false;
    _cancelled = false;
    _finishing = false;
    _queue = dispatch_queue_create("com.ballverse.media.writer", DISPATCH_QUEUE_SERIAL);
    _slots = dispatch_semaphore_create(kSlots);

    NSFileManager* fm = [NSFileManager defaultManager];
    [fm createDirectoryAtPath:[path stringByDeletingLastPathComponent] withIntermediateDirectories:YES attributes:nil error:nil];
    [fm removeItemAtPath:path error:nil];
    NSError* error = nil;
    _writer = [[AVAssetWriter alloc] initWithURL:[NSURL fileURLWithPath:path] fileType:AVFileTypeMPEG4 error:&error];
    if (_writer == nil) { BVSetLastError(BV_ERR_IO, error.localizedDescription); return nil; }
    _writer.shouldOptimizeForNetworkUse = YES;
    _writer.metadata = BVMetadataItems(title, creationDate);

    NSDictionary* color = @{
        AVVideoColorPrimariesKey : AVVideoColorPrimaries_ITU_R_709_2,
        AVVideoTransferFunctionKey : AVVideoTransferFunction_ITU_R_709_2,
        AVVideoYCbCrMatrixKey : AVVideoYCbCrMatrix_ITU_R_709_2,
    };
    NSMutableDictionary* compression = [@{
        AVVideoAverageBitRateKey : @(bitrate),
        AVVideoMaxKeyFrameIntervalKey : @(fps * keyframeSeconds),
        AVVideoMaxKeyFrameIntervalDurationKey : @(keyframeSeconds),
        AVVideoExpectedSourceFrameRateKey : @(fps),
        AVVideoProfileLevelKey : AVVideoProfileLevelH264HighAutoLevel,
        AVVideoH264EntropyModeKey : AVVideoH264EntropyModeCABAC,
    } mutableCopy];
    NSMutableDictionary* video = [@{
        AVVideoCodecKey : AVVideoCodecTypeH264,
        AVVideoWidthKey : @(width),
        AVVideoHeightKey : @(height),
        AVVideoColorPropertiesKey : color,
        AVVideoCompressionPropertiesKey : compression,
    } mutableCopy];
    if (![_writer canApplyOutputSettings:video forMediaType:AVMediaTypeVideo])
    {
        compression[AVVideoProfileLevelKey] = AVVideoProfileLevelH264BaselineAutoLevel;
        [compression removeObjectForKey:AVVideoH264EntropyModeKey];
        video[AVVideoCompressionPropertiesKey] = compression;
        if (![_writer canApplyOutputSettings:video forMediaType:AVMediaTypeVideo])
        {
            BVSetLastError(BV_ERR_ENCODER_FAILED, [NSString stringWithFormat:@"H.264 %dx%d@%d not supported", width, height, fps]);
            return nil;
        }
    }
    _videoInput = [AVAssetWriterInput assetWriterInputWithMediaType:AVMediaTypeVideo outputSettings:video];
    _videoInput.expectsMediaDataInRealTime = NO;
    NSDictionary* attributes = @{
        (id)kCVPixelBufferPixelFormatTypeKey : @(kCVPixelFormatType_32BGRA),
        (id)kCVPixelBufferWidthKey : @(width),
        (id)kCVPixelBufferHeightKey : @(height),
        (id)kCVPixelBufferIOSurfacePropertiesKey : @{},
    };
    _adaptor = [AVAssetWriterInputPixelBufferAdaptor assetWriterInputPixelBufferAdaptorWithAssetWriterInput:_videoInput sourcePixelBufferAttributes:attributes];
    if (![_writer canAddInput:_videoInput]) { BVSetLastError(BV_ERR_ENCODER_FAILED, @"cannot add the video input"); return nil; }
    [_writer addInput:_videoInput];

    if (audioPath.length > 0)
    {
        NSNumber* size = [fm attributesOfItemAtPath:audioPath error:nil][NSFileSize];
        _pcmFrames = size.longLongValue / 8;
        if (_pcmFrames > 0)
        {
            AudioChannelLayout layout = {};
            layout.mChannelLayoutTag = kAudioChannelLayoutTag_Stereo;
            NSDictionary* audio = @{
                AVFormatIDKey : @(kAudioFormatMPEG4AAC),
                AVSampleRateKey : @48000,
                AVNumberOfChannelsKey : @2,
                AVEncoderBitRateKey : @(audioBitrate),
                AVChannelLayoutKey : [NSData dataWithBytes:&layout length:sizeof(layout)],
            };
            _audioInput = [AVAssetWriterInput assetWriterInputWithMediaType:AVMediaTypeAudio outputSettings:audio];
            _audioInput.expectsMediaDataInRealTime = NO;
            if (![_writer canAddInput:_audioInput]) { BVSetLastError(BV_ERR_ENCODER_FAILED, @"cannot add the AAC input"); return nil; }
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
                BVSetLastError(BV_ERR_ENCODER_FAILED, @"cannot describe the PCM input");
                return nil;
            }
            _pcm = fopen(audioPath.fileSystemRepresentation, "rb");
            if (_pcm == NULL) { BVSetLastError(BV_ERR_FILE_NOT_FOUND, audioPath); return nil; }
            _audioScratch.resize((size_t)kAudioChunkFrames * 8);
        }
    }

    if (![_writer startWriting])
    {
        BVSetLastError(BV_ERR_ENCODER_FAILED, _writer.error.localizedDescription ?: @"startWriting failed");
        return nil;
    }
    [_writer startSessionAtSourceTime:kCMTimeZero];
    return self;
}

- (void)dealloc
{
    if (_pcm != NULL) fclose(_pcm);
    if (_audioFormat != NULL) CFRelease(_audioFormat);
}

- (void)failWith:(NSString*)code message:(NSString*)message
{
    @synchronized(self)
    {
        if (_error == nil) _error = [NSString stringWithFormat:@"%@|%@", code, message ?: @""];
    }
    _failed = true;
    NSLog(@"[BallverseMedia] writer failed: %@ %@", code, message);
}

- (NSString*)errorString
{
    @synchronized(self) { return _error ?: [NSString stringWithFormat:@"%@|unknown", BV_ERR_ENCODER_FAILED]; }
}

- (void)setLastErrorFromState
{
    NSString* e = [self errorString];
    NSRange bar = [e rangeOfString:@"|"];
    if (bar.location == NSNotFound) BVSetLastError(BV_ERR_ENCODER_FAILED, e);
    else BVSetLastError([e substringToIndex:bar.location], [e substringFromIndex:bar.location + 1]);
}

- (int)framesInFlight
{
    return _inFlight.load();
}

- (BOOL)isFinishing
{
    return _finishing.load() ? YES : NO;
}

// Caller thread. result: 1 appended, 0 no slot within timeout, -1 failure (thread-local error set).
- (BOOL)appendRgba:(const uint8_t*)rgba rowBytes:(int)rowBytes pts:(int64_t)ptsUs timeoutMs:(int)timeoutMs result:(int*)result
{
    *result = -1;
    if (_failed.load() || _cancelled.load() || _finishing.load())
    {
        if (_failed.load()) [self setLastErrorFromState];
        else BVSetLastError(BV_ERR_INVALID_STATE, @"writer is closed");
        return NO;
    }
    dispatch_time_t deadline = timeoutMs > 0 ? dispatch_time(DISPATCH_TIME_NOW, (int64_t)timeoutMs * NSEC_PER_MSEC) : DISPATCH_TIME_NOW;
    if (dispatch_semaphore_wait(_slots, deadline) != 0)
    {
        *result = 0;
        return NO;
    }
    CVPixelBufferPoolRef pool = _adaptor.pixelBufferPool;
    CVPixelBufferRef buffer = NULL;
    if (pool == NULL || CVPixelBufferPoolCreatePixelBuffer(kCFAllocatorDefault, pool, &buffer) != kCVReturnSuccess || buffer == NULL)
    {
        dispatch_semaphore_signal(_slots);
        [self failWith:BV_ERR_ENCODER_FAILED message:@"no pixel buffer from the adaptor pool"];
        [self setLastErrorFromState];
        return NO;
    }
    CVPixelBufferLockBaseAddress(buffer, 0);
    vImage_Buffer src = {(void*)rgba, (vImagePixelCount)_height, (vImagePixelCount)_width, (size_t)rowBytes};
    vImage_Buffer dst = {CVPixelBufferGetBaseAddress(buffer), (vImagePixelCount)_height, (vImagePixelCount)_width, CVPixelBufferGetBytesPerRow(buffer)};
    const uint8_t rgbaToBgra[4] = {2, 1, 0, 3};
    vImage_Error err = vImagePermuteChannels_ARGB8888(&src, &dst, rgbaToBgra, kvImageNoFlags);
    CVPixelBufferUnlockBaseAddress(buffer, 0);
    if (err != kvImageNoError)
    {
        CVPixelBufferRelease(buffer);
        dispatch_semaphore_signal(_slots);
        [self failWith:BV_ERR_ENCODER_FAILED message:[NSString stringWithFormat:@"vImage error %ld", (long)err]];
        [self setLastErrorFromState];
        return NO;
    }
    _inFlight++;
    dispatch_async(_queue, ^{
        [self appendOnQueue:buffer pts:ptsUs];
        CVPixelBufferRelease(buffer);
        self->_inFlight--;
        dispatch_semaphore_signal(self->_slots);
    });
    *result = 1;
    return YES;
}

// Queue only.
- (void)appendOnQueue:(CVPixelBufferRef)buffer pts:(int64_t)ptsUs
{
    if (_failed.load() || _cancelled.load()) return;
    int64_t waitedUs = 0;
    while (!_videoInput.readyForMoreMediaData)
    {
        if (_writer.status == AVAssetWriterStatusFailed)
        {
            [self failWith:BV_ERR_ENCODER_FAILED message:_writer.error.localizedDescription];
            return;
        }
        if (_cancelled.load()) return;
        if (![self pumpAudioUntil:ptsUs + 2 * kAudioLeadUs limitFrames:INT64_MAX])
        {
            usleep(2000);
            waitedUs += 2000;
            if (waitedUs > kStallLimitUs) { [self failWith:BV_ERR_TIMEOUT message:@"video input stalled"]; return; }
        }
    }
    if (![_adaptor appendPixelBuffer:buffer withPresentationTime:CMTimeMake(ptsUs, 1000000)])
    {
        [self failWith:BV_ERR_ENCODER_FAILED message:_writer.error.localizedDescription ?: @"appendPixelBuffer failed"];
        return;
    }
    _lastPts = ptsUs;
    _frames++;
    [self pumpAudioUntil:ptsUs + kAudioLeadUs limitFrames:INT64_MAX];
}

// Queue only. Appends PCM chunks while the audio input is ready, up to untilUs / limitFrames. Returns YES if any were appended.
- (BOOL)pumpAudioUntil:(int64_t)untilUs limitFrames:(int64_t)limitFrames
{
    if (_audioInput == nil || _audioDone) return NO;
    BOOL any = NO;
    while (_audioInput.readyForMoreMediaData && _audioFrames * 1000000 / 48000 < untilUs)
    {
        int64_t n = kAudioChunkFrames;
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
            [self failWith:BV_ERR_ENCODER_FAILED message:_writer.error.localizedDescription ?: @"cannot append audio"];
            _audioDone = YES;
            return any;
        }
        _audioFrames += n;
        any = YES;
    }
    return any;
}

- (void)finishWithRequest:(int64_t)requestId
{
    _finishing = true;
    BVRegisterCancel(requestId, ^{ [self cancel]; });
    dispatch_async(_queue, ^{
        if (self->_cancelled.load()) { BVCancelled(requestId); return; }
        if (self->_failed.load() || self->_frames == 0)
        {
            NSString* e = self->_frames == 0 && !self->_failed.load() ? [NSString stringWithFormat:@"%@|no frames", BV_ERR_INVALID_STATE] : [self errorString];
            [self->_writer cancelWriting];
            [[NSFileManager defaultManager] removeItemAtPath:self->_path error:nil];
            NSRange bar = [e rangeOfString:@"|"];
            BVFail(requestId, bar.location == NSNotFound ? BV_ERR_ENCODER_FAILED : [e substringToIndex:bar.location],
                   bar.location == NSNotFound ? e : [e substringFromIndex:bar.location + 1]);
            return;
        }
        int64_t endUs = self->_lastPts + (1000000 + self->_fps - 1) / self->_fps;
        // Video first: once it is marked finished the writer no longer holds audio back for interleaving.
        [self->_videoInput markAsFinished];
        if (self->_audioInput != nil)
        {
            int64_t limit = (endUs * 48000 + 999999) / 1000000;
            int64_t waitedUs = 0;
            while (!self->_audioDone && !self->_cancelled.load() && !self->_failed.load())
            {
                if (![self pumpAudioUntil:INT64_MAX limitFrames:limit])
                {
                    if (self->_audioDone) break;
                    usleep(2000);
                    waitedUs += 2000;
                    if (waitedUs > kStallLimitUs) { [self failWith:BV_ERR_TIMEOUT message:@"audio input stalled"]; break; }
                }
                else
                {
                    waitedUs = 0;
                }
            }
        }
        if (self->_cancelled.load()) { BVCancelled(requestId); return; }
        if (self->_failed.load())
        {
            [self->_writer cancelWriting];
            [[NSFileManager defaultManager] removeItemAtPath:self->_path error:nil];
            NSString* e = [self errorString];
            NSRange bar = [e rangeOfString:@"|"];
            BVFail(requestId, bar.location == NSNotFound ? BV_ERR_ENCODER_FAILED : [e substringToIndex:bar.location],
                   bar.location == NSNotFound ? e : [e substringFromIndex:bar.location + 1]);
            return;
        }
        [self->_writer endSessionAtSourceTime:CMTimeMake(endUs, 1000000)];
        int frames = self->_frames;
        [self->_writer finishWritingWithCompletionHandler:^{
            if (self->_writer.status == AVAssetWriterStatusCompleted)
            {
                BVSucceed(requestId, @{ @"frames" : @(frames), @"durationUs" : @(endUs) });
            }
            else
            {
                [[NSFileManager defaultManager] removeItemAtPath:self->_path error:nil];
                if (self->_cancelled.load()) BVCancelled(requestId);
                else BVFail(requestId, BV_ERR_MUXER_FAILED, self->_writer.error.localizedDescription ?: @"finishWriting failed");
            }
        }];
    });
}

- (void)cancel
{
    if (_cancelled.exchange(true)) return;
    dispatch_async(_queue, ^{
        if (self->_writer.status == AVAssetWriterStatusWriting) [self->_writer cancelWriting];
        [[NSFileManager defaultManager] removeItemAtPath:self->_path error:nil];
    });
}

@end

extern "C" {

void* BVWriter_Open(const char* path, int32_t width, int32_t height, int32_t fps, int32_t bitrate, int32_t keyframeIntervalSeconds,
                    const char* audioPcmPath, int32_t audioBitrate, const char* title, const char* creationIso8601)
{
    @autoreleasepool
    {
        BVVideoWriter* writer = [[BVVideoWriter alloc] initWithPath:BVString(path) width:width height:height fps:fps bitrate:bitrate
                                                           keyframe:keyframeIntervalSeconds audioPath:BVString(audioPcmPath) audioBitrate:audioBitrate
                                                              title:BVString(title) creationDate:BVString(creationIso8601)];
        if (writer == nil)
        {
            [[NSFileManager defaultManager] removeItemAtPath:BVString(path) error:nil];
            return NULL;
        }
        return (void*)CFBridgingRetain(writer);
    }
}

int32_t BVWriter_Append(void* handle, const uint8_t* rgba, int32_t rowBytes, int64_t ptsUs, int32_t timeoutMs)
{
    if (handle == NULL || rgba == NULL) { BVSetLastError(BV_ERR_INVALID_STATE, @"writer closed"); return -1; }
    @autoreleasepool
    {
        int result = -1;
        [(__bridge BVVideoWriter*)handle appendRgba:rgba rowBytes:rowBytes pts:ptsUs timeoutMs:timeoutMs result:&result];
        return result;
    }
}

int32_t BVWriter_FramesInFlight(void* handle)
{
    return handle == NULL ? 0 : [(__bridge BVVideoWriter*)handle framesInFlight];
}

void BVWriter_Finish(void* handle, int64_t requestId)
{
    if (handle == NULL) { BVFail(requestId, BV_ERR_INVALID_STATE, @"writer closed"); return; }
    [(__bridge BVVideoWriter*)handle finishWithRequest:requestId];
}

void BVWriter_Cancel(void* handle)
{
    if (handle != NULL) [(__bridge BVVideoWriter*)handle cancel];
}

// Releases the handle. An unfinished writer is cancelled (partial file deleted). Pending queue blocks keep the object
// alive until they have run.
void BVWriter_Close(void* handle)
{
    if (handle == NULL) return;
    @autoreleasepool
    {
        BVVideoWriter* writer = (BVVideoWriter*)CFBridgingRelease(handle);
        if (![writer isFinishing]) [writer cancel];
        writer = nil;
    }
}

} // extern "C"
