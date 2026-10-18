using Ballverse.Core.Ads;

namespace Ballverse.Services
{
    /// <summary>snake_case analytics values for the ad enums (spec §9.2 <c>trigger</c>, <c>reason</c>).</summary>
    public static class AdAnalyticsNames
    {
        public static string Trigger(NaturalBreakKind kind)
        {
            switch (kind)
            {
                case NaturalBreakKind.Retry: return "retry";
                case NaturalBreakKind.Generate: return "generate";
                case NaturalBreakKind.BackFromResult: return "back";
                case NaturalBreakKind.ToGallery: return "gallery";
                case NaturalBreakKind.ToVideo: return "to_video";
                default: return "other";
            }
        }

        public static string Reason(InterstitialSuppressReason reason)
        {
            switch (reason)
            {
                case InterstitialSuppressReason.Entitlement: return "entitlement";
                case InterstitialSuppressReason.ToVideo: return "to_video";
                case InterstitialSuppressReason.InStudio: return "in_studio";
                case InterstitialSuppressReason.Exporting: return "exporting";
                case InterstitialSuppressReason.PurchaseFlow: return "purchase_flow";
                case InterstitialSuppressReason.RewardedQuietPeriod: return "rewarded_quiet_period";
                case InterstitialSuppressReason.MinInterval: return "min_interval";
                case InterstitialSuppressReason.AdNotReady: return "ad_not_ready";
                default: return "none";
            }
        }
    }
}
