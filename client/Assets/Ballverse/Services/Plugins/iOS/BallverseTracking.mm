// App Tracking Transparency and TCF consent bridge for Ballverse.Services (spec §5.6, §9.1).
// Statuses follow ATTrackingManagerAuthorizationStatus: 0 notDetermined, 1 restricted, 2 denied, 3 authorized;
// -1 means ATT does not exist (below iOS 14).
#import <Foundation/Foundation.h>
#import <AppTrackingTransparency/AppTrackingTransparency.h>
#include <stdlib.h>
#include <string.h>

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
        // iOS terminates an app that requests tracking without NSUserTrackingUsageDescription in Info.plist.
        // Without the text the request is skipped and the current (undetermined) status is reported.
        if ([[NSBundle mainBundle] objectForInfoDictionaryKey:@"NSUserTrackingUsageDescription"] == nil) {
            NSLog(@"[Ballverse] NSUserTrackingUsageDescription is missing from Info.plist; ATT request skipped.");
            if (callback != NULL) callback((int)[ATTrackingManager trackingAuthorizationStatus]);
            return;
        }
        dispatch_async(dispatch_get_main_queue(), ^{
            [ATTrackingManager requestTrackingAuthorizationWithCompletionHandler:^(ATTrackingManagerAuthorizationStatus status) {
                if (callback != NULL) callback((int)status);
            }];
        });
        return;
    }
    if (callback != NULL) callback(-1);
}

// IAB TCF v2 values written by the UMP SDK into NSUserDefaults.standardUserDefaults.
// 1 = GDPR applies, 0 = does not, -1 = not written or not a number.
int BallverseTcf_GetGdprApplies(void)
{
    id value = [[NSUserDefaults standardUserDefaults] objectForKey:@"IABTCF_gdprApplies"];
    if ([value isKindOfClass:[NSNumber class]]) return [(NSNumber *)value intValue];
    if ([value isKindOfClass:[NSString class]]) {
        if ([(NSString *)value isEqualToString:@"1"]) return 1;
        if ([(NSString *)value isEqualToString:@"0"]) return 0;
    }
    return -1;
}

// Purpose consent bit string, as a malloc'ed UTF-8 copy the managed marshaller frees; NULL when not written.
char *BallverseTcf_CopyPurposeConsents(void)
{
    id value = [[NSUserDefaults standardUserDefaults] objectForKey:@"IABTCF_PurposeConsents"];
    if (![value isKindOfClass:[NSString class]]) return NULL;
    const char *utf8 = [(NSString *)value UTF8String];
    if (utf8 == NULL) return NULL;
    size_t length = strlen(utf8);
    char *copy = (char *)malloc(length + 1);
    if (copy == NULL) return NULL;
    memcpy(copy, utf8, length + 1);
    return copy;
}

}
