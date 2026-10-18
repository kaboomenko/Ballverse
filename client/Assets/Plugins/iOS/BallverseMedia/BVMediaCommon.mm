// Ballverse MediaBridge (iOS): callbacks, cancellation registry, errors, share sheet, device signals.
#import "BVMediaCommon.h"
#import <AVFoundation/AVFoundation.h>

#include <cmath>
#include <string>

static BVResultCallback gResultCallback = NULL;
static BVProgressCallback gProgressCallback = NULL;
static NSMutableDictionary<NSNumber*, dispatch_block_t>* gCancelHandlers = nil;
static thread_local std::string gLastError;

static NSMutableDictionary<NSNumber*, dispatch_block_t>* CancelHandlers()
{
    static dispatch_once_t once;
    dispatch_once(&once, ^{ gCancelHandlers = [NSMutableDictionary dictionary]; });
    return gCancelHandlers;
}

static void Unregister(int64_t requestId)
{
    NSMutableDictionary* handlers = CancelHandlers();
    @synchronized(handlers) { [handlers removeObjectForKey:@(requestId)]; }
}

static void Complete(int64_t requestId, int32_t status, NSString* json)
{
    Unregister(requestId);
    BVResultCallback cb = gResultCallback;
    if (cb != NULL) cb(requestId, status, json != nil ? json.UTF8String : "");
}

void BVSucceed(int64_t requestId, NSDictionary* result)
{
    NSString* json = @"{}";
    if (result != nil)
    {
        NSData* data = [NSJSONSerialization dataWithJSONObject:result options:0 error:nil];
        if (data != nil) json = [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding];
    }
    Complete(requestId, BVStatusOk, json);
}

void BVFail(int64_t requestId, NSString* code, NSString* message)
{
    NSLog(@"[BallverseMedia] request %lld failed: %@ %@", (long long)requestId, code, message);
    NSDictionary* error = @{ @"code" : code ?: BV_ERR_IO, @"message" : message ?: @"" };
    NSData* data = [NSJSONSerialization dataWithJSONObject:error options:0 error:nil];
    NSString* json = data != nil ? [[NSString alloc] initWithData:data encoding:NSUTF8StringEncoding] : @"{}";
    Complete(requestId, BVStatusError, json);
}

void BVCancelled(int64_t requestId)
{
    Complete(requestId, BVStatusCancelled, @"");
}

void BVProgress(int64_t requestId, double progress)
{
    BVProgressCallback cb = gProgressCallback;
    if (cb != NULL) cb(requestId, progress);
}

void BVRegisterCancel(int64_t requestId, dispatch_block_t handler)
{
    NSMutableDictionary* handlers = CancelHandlers();
    @synchronized(handlers) { handlers[@(requestId)] = [handler copy]; }
}

void BVSetLastError(NSString* code, NSString* message)
{
    NSString* s = [NSString stringWithFormat:@"%@|%@", code ?: BV_ERR_IO, message ?: @""];
    gLastError = s.UTF8String ?: "";
    NSLog(@"[BallverseMedia] %@", s);
}

NSString* BVString(const char* utf8)
{
    if (utf8 == NULL) return @"";
    NSString* s = [NSString stringWithUTF8String:utf8];
    return s != nil ? s : @"";
}

UIViewController* BVTopViewController(void)
{
    UIWindow* window = nil;
    for (UIScene* scene in UIApplication.sharedApplication.connectedScenes)
    {
        if (![scene isKindOfClass:[UIWindowScene class]]) continue;
        for (UIWindow* w in ((UIWindowScene*)scene).windows)
        {
            if (w.isKeyWindow) { window = w; break; }
            if (window == nil) window = w;
        }
        if (window != nil && window.isKeyWindow) break;
    }
    UIViewController* vc = window.rootViewController;
    while (vc.presentedViewController != nil && !vc.presentedViewController.isBeingDismissed) vc = vc.presentedViewController;
    return vc;
}

int64_t BVFreeSpaceBytes(void)
{
    NSURL* home = [NSURL fileURLWithPath:NSHomeDirectory()];
    NSNumber* value = nil;
    if ([home getResourceValue:&value forKey:NSURLVolumeAvailableCapacityForImportantUsageKey error:nil] && value != nil)
        return value.longLongValue;
    NSDictionary* attrs = [[NSFileManager defaultManager] attributesOfFileSystemForPath:NSHomeDirectory() error:nil];
    NSNumber* free = attrs[NSFileSystemFreeSize];
    return free != nil ? free.longLongValue : -1;
}

NSArray<AVMetadataItem*>* BVMetadataItems(NSString* title, NSString* creationIso8601)
{
    NSMutableArray<AVMetadataItem*>* items = [NSMutableArray array];
    if (title.length > 0)
    {
        AVMutableMetadataItem* item = [AVMutableMetadataItem metadataItem];
        item.keySpace = AVMetadataKeySpaceCommon;
        item.key = AVMetadataCommonKeyTitle;
        item.value = title;
        [items addObject:item];
    }
    if (creationIso8601.length > 0)
    {
        AVMutableMetadataItem* item = [AVMutableMetadataItem metadataItem];
        item.keySpace = AVMetadataKeySpaceCommon;
        item.key = AVMetadataCommonKeyCreationDate;
        item.value = creationIso8601;
        [items addObject:item];
    }
    return items;
}

void BVComputeOutputSize(int displayWidth, int displayHeight, int maxWidth, int maxHeight, int* outWidth, int* outHeight)
{
    double scale = 1.0;
    if (maxWidth > 0) scale = fmin(scale, (double)maxWidth / displayWidth);
    if (maxHeight > 0) scale = fmin(scale, (double)maxHeight / displayHeight);
    int w = (int)floor(displayWidth * scale + 0.5) & ~1;
    int h = (int)floor(displayHeight * scale + 0.5) & ~1;
    *outWidth = w < 2 ? 2 : w;
    *outHeight = h < 2 ? 2 : h;
}

typedef struct
{
    int32_t thermal;
    int32_t lowPower;
    int32_t reduceMotion;
    int32_t reduceTransparency;
    int64_t freeSpaceBytes;
} BVSignals;

extern "C" {

void BVMedia_SetCallbacks(BVResultCallback onResult, BVProgressCallback onProgress)
{
    gResultCallback = onResult;
    gProgressCallback = onProgress;
}

void BVMedia_Cancel(int64_t requestId)
{
    dispatch_block_t handler = nil;
    NSMutableDictionary* handlers = CancelHandlers();
    @synchronized(handlers)
    {
        handler = handlers[@(requestId)];
        [handlers removeObjectForKey:@(requestId)];
    }
    if (handler != nil) handler();
}

int32_t BVMedia_CopyLastError(uint8_t* buffer, int32_t capacity)
{
    if (buffer == NULL || capacity <= 0) return 0;
    int32_t n = (int32_t)gLastError.size();
    if (n > capacity) n = capacity;
    memcpy(buffer, gLastError.data(), (size_t)n);
    return n;
}

void BVMedia_GetSignals(BVSignals* out)
{
    if (out == NULL) return;
    NSProcessInfo* info = [NSProcessInfo processInfo];
    switch (info.thermalState)
    {
        case NSProcessInfoThermalStateNominal: out->thermal = 0; break;
        case NSProcessInfoThermalStateFair: out->thermal = 1; break;
        case NSProcessInfoThermalStateSerious: out->thermal = 2; break;
        case NSProcessInfoThermalStateCritical: out->thermal = 3; break;
        default: out->thermal = -1; break;
    }
    out->lowPower = info.isLowPowerModeEnabled ? 1 : 0;
    out->reduceMotion = UIAccessibilityIsReduceMotionEnabled() ? 1 : 0;
    out->reduceTransparency = UIAccessibilityIsReduceTransparencyEnabled() ? 1 : 0;
    out->freeSpaceBytes = BVFreeSpaceBytes();
}

void BVMedia_Share(const char* path, const char* caption)
{
    NSString* file = BVString(path);
    NSString* text = BVString(caption);
    dispatch_async(dispatch_get_main_queue(), ^{
        UIViewController* top = BVTopViewController();
        if (top == nil) { NSLog(@"[BallverseMedia] share: no view controller"); return; }
        NSMutableArray* items = [NSMutableArray arrayWithObject:[NSURL fileURLWithPath:file]];
        if (text.length > 0) [items addObject:text];
        UIActivityViewController* sheet = [[UIActivityViewController alloc] initWithActivityItems:items applicationActivities:nil];
        UIPopoverPresentationController* popover = sheet.popoverPresentationController;
        if (popover != nil)
        {
            // iPad: anchor the popover to the centre of the screen.
            popover.sourceView = top.view;
            popover.sourceRect = CGRectMake(CGRectGetMidX(top.view.bounds), CGRectGetMidY(top.view.bounds), 1, 1);
            popover.permittedArrowDirections = 0;
        }
        [top presentViewController:sheet animated:YES completion:nil];
    });
}

} // extern "C"
