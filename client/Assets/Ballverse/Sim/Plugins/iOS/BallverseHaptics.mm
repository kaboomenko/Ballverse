// Short impact haptic for live simulations (spec §2.7): UIImpactFeedbackGenerator with an intensity (iOS 13+; the
// release-1 minimum is iOS 16). Called from Ballverse.Sim.UnityHapticsDevice on the main thread.
#import <UIKit/UIKit.h>

static UIImpactFeedbackGenerator *s_ballverseImpact = nil;

extern "C" void BallverseHaptics_Impact(float intensity)
{
    if (s_ballverseImpact == nil)
    {
        s_ballverseImpact = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
        [s_ballverseImpact prepare];
    }
    CGFloat value = intensity < 0.0f ? 0.0f : (intensity > 1.0f ? 1.0f : intensity);
    [s_ballverseImpact impactOccurredWithIntensity:value];
    [s_ballverseImpact prepare];
}
