// App Tracking Transparency bridge for Ballverse.Services (spec §5.6).
// Statuses follow ATTrackingManagerAuthorizationStatus: 0 notDetermined, 1 restricted, 2 denied, 3 authorized;
// -1 means ATT does not exist (below iOS 14).
#import <Foundation/Foundation.h>
#import <AppTrackingTransparency/AppTrackingTransparency.h>

typedef void (*BallverseAttCallback)(int status);

extern "C" {

int BallverseAtt_GetStatus(void)
{
    if (@available(iOS 14, *)) {
        return (int)[ATTrackingManager trackingAuthorizationStatus];
    }
    return -1;
}

void BallverseAtt_Request(BallverseAttCallback callback)
{
    if (@available(iOS 14, *)) {
        dispatch_async(dispatch_get_main_queue(), ^{
            [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {
                if (callback != NULL) callback((int)status);
            }];
        });
        return;
    }
    if (callback != NULL) callback(-1);
}

}
