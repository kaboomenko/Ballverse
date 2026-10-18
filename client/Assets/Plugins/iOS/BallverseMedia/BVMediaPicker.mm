// Ballverse MediaBridge (iOS): system video picker + copy into the app sandbox (spec §3.2).
// iOS 14+: PHPickerViewController (out of process, no photo-library permission), original representation (no
// transcoding), NSItemProvider file representation (downloads from iCloud with progress, cancellable).
// iOS 13: UIImagePickerController with passthrough export (also permission-free since iOS 11).
#import "BVMediaCommon.h"
#import <AVFoundation/AVFoundation.h>
#import <PhotosUI/PhotosUI.h>

static NSString* const kMovieType = @"public.movie";
static const int64_t kSpaceReserveBytes = 50LL * 1024 * 1024;

@interface BVPickSession : NSObject <UIImagePickerControllerDelegate, UINavigationControllerDelegate, UIAdaptivePresentationControllerDelegate>
@property(nonatomic) int64_t requestId;
@property(nonatomic, copy) NSString* destinationDirectory;
@property(nonatomic, copy) NSString* baseName;
@property(nonatomic, weak) UIViewController* presented;
@property(nonatomic, strong) NSProgress* loadProgress;
@property(atomic) BOOL cancelled;
@property(atomic) BOOL completed;
@end

// The single active session (a picker is modal); also keeps the delegates alive (picker delegates are weak).
static BVPickSession* gActiveSession = nil;
static id gPhDelegate = nil;

static void BVEndSession(BVPickSession* session)
{
    dispatch_async(dispatch_get_main_queue(), ^{
        if (gActiveSession == session)
        {
            gActiveSession = nil;
            gPhDelegate = nil;
        }
    });
}

@implementation BVPickSession

- (void)stopObserving
{
    @synchronized(self)
    {
        if (self.loadProgress != nil)
        {
            @try { [self.loadProgress removeObserver:self forKeyPath:@"fractionCompleted"]; }
            @catch (NSException* e) { }
            self.loadProgress = nil;
        }
    }
}

- (void)finishWithPath:(NSString*)path
{
    [self stopObserving];
    if (self.completed) return;
    self.completed = YES;
    BVSucceed(self.requestId, @{ @"path" : path ?: @"" });
    BVEndSession(self);
}

- (void)failWithCode:(NSString*)code message:(NSString*)message
{
    [self stopObserving];
    if (self.completed) return;
    self.completed = YES;
    BVFail(self.requestId, code, message);
    BVEndSession(self);
}

- (void)finishCancelled
{
    [self stopObserving];
    if (self.completed) return;
    self.completed = YES;
    BVCancelled(self.requestId);
    BVEndSession(self);
}

// Copies the picked file (which the system deletes when the handler returns) to the destination.
- (void)copyFromURL:(NSURL*)source
{
    if (self.cancelled) { [self finishCancelled]; return; }
    NSFileManager* fm = [NSFileManager defaultManager];
    NSError* error = nil;
    if (![fm createDirectoryAtPath:self.destinationDirectory withIntermediateDirectories:YES attributes:nil error:&error])
    {
        [self failWithCode:BV_ERR_IO message:error.localizedDescription];
        return;
    }
    NSString* ext = source.pathExtension.lowercaseString;
    if (ext.length == 0) ext = @"mov";
    NSString* dest = [self.destinationDirectory stringByAppendingPathComponent:[self.baseName stringByAppendingPathExtension:ext]];
    NSNumber* size = [fm attributesOfItemAtPath:source.path error:nil][NSFileSize];
    int64_t free = BVFreeSpaceBytes();
    if (size != nil && free >= 0 && free < size.longLongValue + kSpaceReserveBytes)
    {
        [self failWithCode:BV_ERR_INSUFFICIENT_SPACE message:[NSString stringWithFormat:@"need %lld bytes, %lld free", size.longLongValue, free]];
        return;
    }
    [fm removeItemAtPath:dest error:nil];
    if (![fm copyItemAtURL:source toURL:[NSURL fileURLWithPath:dest] error:&error])
    {
        [fm removeItemAtPath:dest error:nil];
        [self failWithCode:BV_ERR_IO message:error.localizedDescription];
        return;
    }
    if (self.cancelled)
    {
        [fm removeItemAtPath:dest error:nil];
        [self finishCancelled];
        return;
    }
    BVProgress(self.requestId, 1.0);
    [self finishWithPath:dest];
}

- (void)observeValueForKeyPath:(NSString*)keyPath ofObject:(id)object change:(NSDictionary*)change context:(void*)context
{
    if ([keyPath isEqualToString:@"fractionCompleted"] && [object isKindOfClass:[NSProgress class]])
    {
        BVProgress(self.requestId, ((NSProgress*)object).fractionCompleted * 0.99);
    }
    else
    {
        [super observeValueForKeyPath:keyPath ofObject:object change:change context:context];
    }
}

- (void)loadFromItemProvider:(NSItemProvider*)provider
{
    if (![provider hasItemConformingToTypeIdentifier:kMovieType])
    {
        [self failWithCode:BV_ERR_UNSUPPORTED_FORMAT message:@"the picked item is not a movie"];
        return;
    }
    __weak BVPickSession* weakSelf = self;
    NSProgress* progress = [provider loadFileRepresentationForTypeIdentifier:kMovieType completionHandler:^(NSURL* url, NSError* error) {
        BVPickSession* strongSelf = weakSelf;
        if (strongSelf == nil) return;
        [strongSelf stopObserving];
        if (strongSelf.cancelled) { [strongSelf finishCancelled]; return; }
        if (url == nil)
        {
            if ([error.domain isEqualToString:NSCocoaErrorDomain] && error.code == NSUserCancelledError) [strongSelf finishCancelled];
            else [strongSelf failWithCode:BV_ERR_DOWNLOAD message:error.localizedDescription ?: @"cannot load the picked video"];
            return;
        }
        [strongSelf copyFromURL:url];
    }];
    if (progress != nil)
    {
        @synchronized(self)
        {
            if (!self.completed)
            {
                self.loadProgress = progress;
                [progress addObserver:self forKeyPath:@"fractionCompleted" options:NSKeyValueObservingOptionNew context:NULL];
            }
        }
    }
}

- (void)cancel
{
    self.cancelled = YES;
    [self.loadProgress cancel];
    // finishCancelled below stops observing; the provider's completion handler then sees `cancelled`.
    UIViewController* presented = self.presented;
    if (presented != nil && presented.presentingViewController != nil) [presented dismissViewControllerAnimated:YES completion:nil];
    [self finishCancelled];
}

// UIImagePickerController (iOS 13)

- (void)imagePickerController:(UIImagePickerController*)picker didFinishPickingMediaWithInfo:(NSDictionary<UIImagePickerControllerInfoKey, id>*)info
{
    NSURL* url = info[UIImagePickerControllerMediaURL];
    [picker dismissViewControllerAnimated:YES completion:nil];
    if (url == nil) { [self finishWithPath:@""]; return; }
    dispatch_async(dispatch_get_global_queue(QOS_CLASS_USER_INITIATED, 0), ^{ [self copyFromURL:url]; });
}

- (void)imagePickerControllerDidCancel:(UIImagePickerController*)picker
{
    [picker dismissViewControllerAnimated:YES completion:nil];
    [self finishWithPath:@""];
}

// Swipe-to-dismiss

- (void)presentationControllerDidDismiss:(UIPresentationController*)presentationController
{
    // Only user-initiated dismissals (swipe down) arrive here; programmatic ones after a pick do not.
    [self finishWithPath:@""];
}

@end

API_AVAILABLE(ios(14))
@interface BVPhPickerDelegate : NSObject <PHPickerViewControllerDelegate>
@property(nonatomic, strong) BVPickSession* session;
@end

@implementation BVPhPickerDelegate

- (void)picker:(PHPickerViewController*)picker didFinishPicking:(NSArray<PHPickerResult*>*)results
{
    BVPickSession* session = self.session;
    [picker dismissViewControllerAnimated:YES completion:nil];
    if (results.count == 0) { [session finishWithPath:@""]; return; }
    [session loadFromItemProvider:results.firstObject.itemProvider];
}

@end

extern "C" void BVMedia_PickVideo(int64_t requestId, const char* destinationDirectory, const char* baseName)
{
    NSString* dir = BVString(destinationDirectory);
    NSString* name = BVString(baseName);
    dispatch_async(dispatch_get_main_queue(), ^{
        if (gActiveSession != nil && !gActiveSession.completed)
        {
            BVFail(requestId, BV_ERR_BUSY, @"a picker is already open");
            return;
        }
        UIViewController* top = BVTopViewController();
        if (top == nil)
        {
            BVFail(requestId, BV_ERR_INVALID_STATE, @"no view controller to present the picker");
            return;
        }
        BVPickSession* session = [BVPickSession new];
        session.requestId = requestId;
        session.destinationDirectory = dir;
        session.baseName = name;
        gActiveSession = session;
        __weak BVPickSession* weakSession = session;
        BVRegisterCancel(requestId, ^{
            dispatch_async(dispatch_get_main_queue(), ^{ [weakSession cancel]; });
        });

        UIViewController* picker = nil;
        if (@available(iOS 14, *))
        {
            PHPickerConfiguration* config = [[PHPickerConfiguration alloc] init];
            config.filter = [PHPickerFilter videosFilter];
            config.selectionLimit = 1;
            config.preferredAssetRepresentationMode = PHPickerConfigurationAssetRepresentationModeCurrent;
            PHPickerViewController* ph = [[PHPickerViewController alloc] initWithConfiguration:config];
            BVPhPickerDelegate* delegate = [BVPhPickerDelegate new];
            delegate.session = session;
            gPhDelegate = delegate;
            ph.delegate = delegate;
            picker = ph;
        }
        else
        {
            UIImagePickerController* ip = [[UIImagePickerController alloc] init];
            ip.sourceType = UIImagePickerControllerSourceTypePhotoLibrary;
            ip.mediaTypes = @[ kMovieType ];
            ip.videoExportPreset = AVAssetExportPresetPassthrough;
            ip.delegate = session;
            // UIImagePickerController does not report swipe-to-dismiss as a cancel; observe the presentation instead.
            ip.presentationController.delegate = session;
            picker = ip;
        }
        session.presented = picker;
        [top presentViewController:picker animated:YES completion:nil];
    });
}
