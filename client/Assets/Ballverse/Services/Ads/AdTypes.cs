using System;

namespace Ballverse.Services
{
    /// <summary>The two ad formats of release 1 (spec §5.3; no banners, no app-open ads).</summary>
    public enum AdFormat
    {
        Rewarded = 0,
        Interstitial = 1,
    }

    public enum AdsServiceState
    {
        NotInitialized = 0,
        Initializing = 1,
        Ready = 2,
        /// <summary>No ads SDK in this build, or initialization failed: every ad is "not ready" (exports run without ads, Р14).</summary>
        Unavailable = 3,
    }

    public enum AdLoadState
    {
        Idle = 0,
        Loading = 1,
        Loaded = 2,
        /// <summary>The last load failed; a retry is scheduled with backoff while the format is kept loaded.</summary>
        Failed = 3,
    }

    public enum RewardedShowResult
    {
        /// <summary>The SDK reported the reward and the ad has closed (RW2, RW6).</summary>
        Earned = 0,
        /// <summary>Closed before the reward (RW2).</summary>
        ClosedEarly = 1,
        /// <summary>The ad failed to present.</summary>
        FailedToShow = 2,
        /// <summary>No loaded ad.</summary>
        NotReady = 3,
    }

    public enum InterstitialShowResult
    {
        /// <summary>Presented and closed.</summary>
        Shown = 0,
        FailedToShow = 1,
        NotReady = 2,
    }

    /// <summary>An SDK error, reduced to what analytics may carry (spec §9.2 <c>error_code</c>).</summary>
    public readonly struct AdErrorInfo
    {
        public AdErrorInfo(int code, string message)
        {
            Code = code;
            Message = message ?? string.Empty;
        }

        public int Code { get; }
        public string Message { get; }

        /// <summary>Analytics form, e.g. <c>ad_3</c>.</summary>
        public string AnalyticsCode => "ad_" + Code.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public override string ToString()
        {
            return Code + ": " + Message;
        }
    }

    /// <summary>Impression-level revenue reported by the SDK (spec §9.1).</summary>
    public readonly struct AdRevenue
    {
        public AdRevenue(AdFormat format, long valueMicros, string currencyCode, int precision)
        {
            Format = format;
            ValueMicros = valueMicros;
            CurrencyCode = currencyCode ?? string.Empty;
            Precision = precision;
        }

        public AdFormat Format { get; }

        /// <summary>Value in micros of <see cref="CurrencyCode"/>.</summary>
        public long ValueMicros { get; }
        public string CurrencyCode { get; }
        public int Precision { get; }
    }

    /// <summary>Ad unit ids of one platform. Google's official test ids until the owner provides real ones.</summary>
    public sealed class AdUnitIds
    {
        // Google sample ad units that always serve test ads:
        // https://developers.google.com/admob/android/test-ads , https://developers.google.com/admob/ios/test-ads
        public const string TestAndroidRewarded = "ca-app-pub-3940256099942544/5224354917";
        public const string TestAndroidInterstitial = "ca-app-pub-3940256099942544/1033173712";
        public const string TestIosRewarded = "ca-app-pub-3940256099942544/1712485313";
        public const string TestIosInterstitial = "ca-app-pub-3940256099942544/4411468910";

        /// <summary>Google sample AdMob app ids; they go into the Google Mobile Ads settings asset, not into code paths.</summary>
        public const string TestAndroidAppId = "ca-app-pub-3940256099942544~3347511713";
        public const string TestIosAppId = "ca-app-pub-3940256099942544~1458002511";

        public AdUnitIds(string rewarded, string interstitial)
        {
            if (string.IsNullOrEmpty(rewarded)) throw new ArgumentException("rewarded ad unit id is empty", nameof(rewarded));
            if (string.IsNullOrEmpty(interstitial)) throw new ArgumentException("interstitial ad unit id is empty", nameof(interstitial));
            Rewarded = rewarded;
            Interstitial = interstitial;
        }

        public string Rewarded { get; }
        public string Interstitial { get; }

        public static AdUnitIds AndroidTest => new AdUnitIds(TestAndroidRewarded, TestAndroidInterstitial);
        public static AdUnitIds IosTest => new AdUnitIds(TestIosRewarded, TestIosInterstitial);

        public string For(AdFormat format)
        {
            return format == AdFormat.Rewarded ? Rewarded : Interstitial;
        }

        /// <summary>True when both ids are Google's test units (a release build should not ship them).</summary>
        public bool AreTestIds => Rewarded.StartsWith("ca-app-pub-3940256099942544/", StringComparison.Ordinal)
                                  && Interstitial.StartsWith("ca-app-pub-3940256099942544/", StringComparison.Ordinal);
    }
}
