using Ballverse.UI.Components;
using Ballverse.UI.Navigation;

namespace Ballverse.UI.Screens
{
    /// <summary>
    /// Own explanation before the iOS App Tracking Transparency dialog (spec §5.6: shown before the first ad, not at
    /// launch; refusing does not limit anything). One "Continue" button leads to the system dialog.
    /// </summary>
    public sealed class TrackingPrePromptScreen : ScreenController
    {
        public override ScreenId Id => ScreenId.TrackingPrePrompt;
        protected override string Layout => "TrackingPrePrompt";

        protected override void OnCreate()
        {
            Tap.On(Root, "continue", Continue);
        }

        public override bool HandleBack()
        {
            Continue();
            return true;
        }

        void Continue()
        {
            if (Ctx.Navigator.Current == ScreenId.TrackingPrePrompt) Ctx.Navigator.Pop();
            Ctx.ViewModels.TrackingConsent?.ContinueToSystemPrompt();
        }
    }
}
