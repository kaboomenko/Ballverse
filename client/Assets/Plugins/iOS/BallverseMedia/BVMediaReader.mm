// Ballverse MediaBridge (iOS): sequential video frame reader (spec §7.5 step b).
// AVAssetReader + AVAssetReaderVideoCompositionOutput: the video composition applies preferredTransform (rotation),
// scales into the requested box and — with SDR BT.709 colour properties — tone-maps HDR (HLG/PQ/Dolby Vision) to SDR.
// Output BGRA pixel buffers are swizzled into the caller's RGBA32 buffer (top-down, tightly packed).
// The reader's time range ends at the END OF THE VIDEO TRACK (not the asset): a composition renders black frames where
// the track has no media (e.g. phone clips whose audio is slightly longer), and "after the last frame return the last
// frame" must return the real last frame, as on Android.
// Driven synchronously from one C# worker thread; not thread-safe.
#import "BVMediaCommon.h"
#import <Accelerate/Accelerate.h>
#import <AVFoundation/AVFoundation.h>
#import <CoreMedia/CoreMedia.h>
#import <CoreVideo/CoreVideo.h>

static const int64_t kSeekForwardThresholdUs = 3000000;

@interface BVVideoReader : NSObject
@property(nonatomic, readonly) int width;
@property(nonatomic, readonly) int height;
@property(nonatomic, readonly) BOOL toneMapped;
@property(nonatomic, readonly) int64_t durationUs;
@end

@implementation BVVideoReader
{
    AVURLAsset* _asset;
    AVAssetTrack* _track;
    AVMutableVideoComposition* _composition;
    AVAssetReader* _reader;
    AVAssetReaderVideoCompositionOutput* _output;
    CMSampleBufferRef _current;
    CMSampleBufferRef _next;
    int64_t _currentPts;
    int64_t _nextPts;
    BOOL _endOfStream;
    CMTime _videoEnd;
}

- (instancetype)initWithPath:(NSString*)path maxWidth:(int)maxWidth maxHeight:(int)maxHeight toneMap:(BOOL)toneMap
{
    if ((self = [super init]) == nil) return nil;
    if (![[NSFileManager defaultManager] fileExistsAtPath:path]) { BVSetLastError(BV_ERR_FILE_NOT_FOUND, path); return nil; }
    _asset = [AVURLAsset URLAssetWithURL:[NSURL fileURLWithPath:path] options:@{ AVURLAssetPreferPreciseDurationAndTimingKey : @YES }];
    NSString* message = nil;
    NSString* code = BVLoadAssetKeys(_asset, @[ @"tracks", @"duration", @"hasProtectedContent" ], &message);
    if (code != nil) { BVSetLastError(code, message); return nil; }
    if (_asset.hasProtectedContent) { BVSetLastError(BV_ERR_DRM_PROTECTED, @"the file is DRM protected"); return nil; }
    _track = [_asset tracksWithMediaType:AVMediaTypeVideo].firstObject;
    if (_track == nil) { BVSetLastError(BV_ERR_NO_VIDEO_TRACK, @"no video track"); return nil; }
    CMTime duration = _asset.duration;
    CMTimeRange trackRange = _track.timeRange;
    _videoEnd = CMTIMERANGE_IS_VALID(trackRange) && CMTIME_IS_NUMERIC(trackRange.duration) && CMTimeGetSeconds(trackRange.duration) > 0
        ? CMTimeRangeGetEnd(trackRange) : duration;
    if (CMTIME_IS_NUMERIC(duration) && CMTimeCompare(_videoEnd, duration) > 0) _videoEnd = duration;
    _durationUs = CMTIME_IS_NUMERIC(_videoEnd) ? (int64_t)llround(CMTimeGetSeconds(_videoEnd) * 1e6) : 0;

    CGSize natural = _track.naturalSize;
    CGAffineTransform t = _track.preferredTransform;
    CGRect displayed = CGRectApplyAffineTransform(CGRectMake(0, 0, natural.width, natural.height), t);
    int displayWidth = (int)lround(fabs(displayed.size.width));
    int displayHeight = (int)lround(fabs(displayed.size.height));
    if (displayWidth <= 0 || displayHeight <= 0) { BVSetLastError(BV_ERR_CORRUPT, @"invalid video size"); return nil; }
    int w = 0, h = 0;
    BVComputeOutputSize(displayWidth, displayHeight, maxWidth, maxHeight, &w, &h);
    _width = w;
    _height = h;

    // preferredTransform maps the coded frame into display space (possibly at a negative origin): rotate, move the
    // displayed rectangle to the origin, then scale it to exactly fill the output frame.
    CGAffineTransform transform = CGAffineTransformConcat(t, CGAffineTransformMakeTranslation(-CGRectGetMinX(displayed), -CGRectGetMinY(displayed)));
    transform = CGAffineTransformConcat(transform, CGAffineTransformMakeScale((CGFloat)w / displayWidth, (CGFloat)h / displayHeight));

    AVMutableVideoCompositionLayerInstruction* layer = [AVMutableVideoCompositionLayerInstruction videoCompositionLayerInstructionWithAssetTrack:_track];
    [layer setTransform:transform atTime:kCMTimeZero];
    AVMutableVideoCompositionInstruction* instruction = [AVMutableVideoCompositionInstruction videoCompositionInstruction];
    instruction.timeRange = CMTimeRangeMake(kCMTimeZero, duration);
    instruction.layerInstructions = @[ layer ];
    _composition = [AVMutableVideoComposition videoComposition];
    _composition.instructions = @[ instruction ];
    _composition.renderSize = CGSizeMake(w, h);
    float fps = _track.nominalFrameRate;
    if (fps <= 0 || fps > 60) fps = fps > 60 ? 60 : 30;
    _composition.frameDuration = CMTimeMakeWithSeconds(1.0 / fps, 600000);

    int bitDepth = 8;
    BOOL hdr = ![BVTransferOfTrack(_track, &bitDepth) isEqualToString:@"sdr"];
    if (toneMap)
    {
        // SDR BT.709 output colour space: AVFoundation converts (and tone-maps HDR) during composition.
        _composition.colorPrimaries = AVVideoColorPrimaries_ITU_R_709_2;
        _composition.colorTransferFunction = AVVideoTransferFunction_ITU_R_709_2;
        _composition.colorYCbCrMatrix = AVVideoYCbCrMatrix_ITU_R_709_2;
    }
    _toneMapped = hdr && toneMap;

    if (![self restartAt:0]) return nil;
    _next = [self decodeNext:&_nextPts];
    if (_next == NULL)
    {
        if (_reader.status != AVAssetReaderStatusFailed) BVSetLastError(BV_ERR_CORRUPT, @"no video frames");
        return nil;
    }
    return self;
}

- (void)dealloc
{
    [self releaseSamples];
    [_reader cancelReading];
}

- (void)releaseSamples
{
    if (_current != NULL) { CFRelease(_current); _current = NULL; }
    if (_next != NULL) { CFRelease(_next); _next = NULL; }
}

- (BOOL)restartAt:(int64_t)ptsUs
{
    [self releaseSamples];
    [_reader cancelReading];
    _reader = nil;
    _output = nil;
    _endOfStream = NO;
    NSError* error = nil;
    _reader = [[AVAssetReader alloc] initWithAsset:_asset error:&error];
    if (_reader == nil) { BVSetLastError(BV_ERR_DECODER_FAILED, error.localizedDescription); return NO; }
    // Start at ptsUs (clamped so the range never starts at or after the video end), stop at the video track's end.
    if (_durationUs > 0 && ptsUs >= _durationUs) ptsUs = MAX((int64_t)0, _durationUs - 2000000);
    CMTime start = ptsUs > 0 ? CMTimeMake(ptsUs, 1000000) : kCMTimeZero;
    if (CMTIME_IS_NUMERIC(_videoEnd) && CMTimeCompare(_videoEnd, start) > 0) _reader.timeRange = CMTimeRangeFromTimeToTime(start, _videoEnd);
    else if (ptsUs > 0) _reader.timeRange = CMTimeRangeMake(start, kCMTimePositiveInfinity);
    NSDictionary* settings = @{ (id)kCVPixelBufferPixelFormatTypeKey : @(kCVPixelFormatType_32BGRA) };
    _output = [AVAssetReaderVideoCompositionOutput assetReaderVideoCompositionOutputWithVideoTracks:@[ _track ] videoSettings:settings];
    _output.videoComposition = _composition;
    _output.alwaysCopiesSampleData = NO;
    if (![_reader canAddOutput:_output]) { BVSetLastError(BV_ERR_UNSUPPORTED_FORMAT, @"cannot decode this video"); return NO; }
    [_reader addOutput:_output];
    if (![_reader startReading])
    {
        BVSetLastError(BV_ERR_DECODER_FAILED, _reader.error.localizedDescription ?: @"startReading failed");
        return NO;
    }
    return YES;
}

- (CMSampleBufferRef)decodeNext:(int64_t*)ptsUs
{
    if (_endOfStream) return NULL;
    while (true)
    {
        CMSampleBufferRef sample = [_output copyNextSampleBuffer];
        if (sample == NULL)
        {
            _endOfStream = YES;
            if (_reader.status == AVAssetReaderStatusFailed)
            {
                NSError* e = _reader.error;
                BVSetLastError([e.domain isEqualToString:AVFoundationErrorDomain] && e.code == AVErrorDecoderNotFound ? BV_ERR_UNSUPPORTED_FORMAT : BV_ERR_DECODER_FAILED,
                               e.localizedDescription ?: @"decoding failed");
            }
            return NULL;
        }
        if (CMSampleBufferGetImageBuffer(sample) == NULL) { CFRelease(sample); continue; }
        *ptsUs = (int64_t)llround(CMTimeGetSeconds(CMSampleBufferGetPresentationTimeStamp(sample)) * 1e6);
        return sample;
    }
}

- (int64_t)readFrameAt:(int64_t)target into:(uint8_t*)destination capacity:(int)capacity
{
    if ((int64_t)capacity < (int64_t)_width * _height * 4) { BVSetLastError(BV_ERR_INVALID_STATE, @"destination too small"); return INT64_MIN; }
    BOOL backward = _current != NULL && target < _currentPts;
    int64_t anchor = _current != NULL ? _currentPts : (_next != NULL ? _nextPts : 0);
    BOOL farForward = target - anchor > kSeekForwardThresholdUs && (_next == NULL || target >= _nextPts);
    if (backward || farForward)
    {
        if (![self restartAt:target]) return INT64_MIN;
    }
    for (int attempt = 0; attempt < 2; attempt++)
    {
        while (true)
        {
            if (_next == NULL && !_endOfStream)
            {
                _next = [self decodeNext:&_nextPts];
                if (_next == NULL && _reader.status == AVAssetReaderStatusFailed) return INT64_MIN;
            }
            if (_next == NULL) break;
            if (_nextPts <= target)
            {
                if (_current != NULL) CFRelease(_current);
                _current = _next;
                _currentPts = _nextPts;
                _next = NULL;
                continue;
            }
            break;
        }
        if (_current != NULL || _next != NULL || target <= 0) break;
        // Restarted at or past the end: re-read the last two seconds and keep the final frame.
        if (![self restartAt:MAX((int64_t)0, MIN(target, _durationUs) - 2000000)]) return INT64_MIN;
    }
    CMSampleBufferRef frame = _current != NULL ? _current : _next;
    int64_t pts = _current != NULL ? _currentPts : _nextPts;
    if (frame == NULL) { BVSetLastError(BV_ERR_CORRUPT, @"no decodable frames"); return INT64_MIN; }
    if (![self copyFrame:frame into:destination]) return INT64_MIN;
    return pts;
}

- (BOOL)copyFrame:(CMSampleBufferRef)sample into:(uint8_t*)destination
{
    CVImageBufferRef image = CMSampleBufferGetImageBuffer(sample);
    if (image == NULL) { BVSetLastError(BV_ERR_DECODER_FAILED, @"sample without image"); return NO; }
    if (CVPixelBufferLockBaseAddress(image, kCVPixelBufferLock_ReadOnly) != kCVReturnSuccess)
    {
        BVSetLastError(BV_ERR_DECODER_FAILED, @"cannot lock pixel buffer");
        return NO;
    }
    size_t w = CVPixelBufferGetWidth(image);
    size_t h = CVPixelBufferGetHeight(image);
    vImage_Buffer src = {CVPixelBufferGetBaseAddress(image), (vImagePixelCount)MIN(h, (size_t)_height), (vImagePixelCount)MIN(w, (size_t)_width), CVPixelBufferGetBytesPerRow(image)};
    vImage_Buffer dst = {destination, (vImagePixelCount)_height, (vImagePixelCount)_width, (size_t)_width * 4};
    if (w < (size_t)_width || h < (size_t)_height) memset(destination, 0, (size_t)_width * _height * 4);
    dst.height = src.height;
    dst.width = src.width;
    const uint8_t bgraToRgba[4] = {2, 1, 0, 3};
    vImage_Error err = vImagePermuteChannels_ARGB8888(&src, &dst, bgraToRgba, kvImageNoFlags);
    CVPixelBufferUnlockBaseAddress(image, kCVPixelBufferLock_ReadOnly);
    if (err != kvImageNoError) { BVSetLastError(BV_ERR_DECODER_FAILED, [NSString stringWithFormat:@"vImage error %ld", (long)err]); return NO; }
    return YES;
}

@end

extern "C" {

void* BVReader_Open(const char* path, int32_t maxWidth, int32_t maxHeight, int32_t toneMapToSdr,
                    int32_t* width, int32_t* height, int32_t* toneMapped, int64_t* durationUs)
{
    @autoreleasepool
    {
        BVVideoReader* reader = [[BVVideoReader alloc] initWithPath:BVString(path) maxWidth:maxWidth maxHeight:maxHeight toneMap:toneMapToSdr != 0];
        if (reader == nil) return NULL;
        if (width) *width = reader.width;
        if (height) *height = reader.height;
        if (toneMapped) *toneMapped = reader.toneMapped ? 1 : 0;
        if (durationUs) *durationUs = reader.durationUs;
        return (void*)CFBridgingRetain(reader);
    }
}

int64_t BVReader_Read(void* handle, int64_t ptsUs, uint8_t* destination, int32_t capacity)
{
    if (handle == NULL || destination == NULL) { BVSetLastError(BV_ERR_INVALID_STATE, @"reader closed"); return INT64_MIN; }
    @autoreleasepool
    {
        BVVideoReader* reader = (__bridge BVVideoReader*)handle;
        return [reader readFrameAt:ptsUs into:destination capacity:capacity];
    }
}

void BVReader_Close(void* handle)
{
    if (handle == NULL) return;
    @autoreleasepool
    {
        // Transfers ownership back to ARC; the reader is released at the end of this scope.
        (void)CFBridgingRelease(handle);
    }
}

} // extern "C"
