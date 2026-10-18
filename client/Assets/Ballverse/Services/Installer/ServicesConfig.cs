using System.Collections.Generic;
using UnityEngine;

namespace Ballverse.Services
{
    /// <summary>
    /// The one place for ad unit ids and store keys. Create it with Assets → Create → Ballverse → Services Config and save it
    /// as <c>Resources/BallverseServicesConfig.asset</c>. Without the asset, Google's official test ad units are used.
    /// Nothing here is secret: ad unit ids and the Play licensing key are public identifiers.
    /// The AdMob app ids themselves go into the Google Mobile Ads settings asset (Assets → Google Mobile Ads → Settings).
    /// </summary>
    [CreateAssetMenu(menuName = "Ballverse/Services Config", fileName = "BallverseServicesConfig")]
    public sealed class ServicesConfig : ScriptableObject
    {
        public const string ResourcePath = "BallverseServicesConfig";

        [Header("AdMob ad units (Google test units until the real ones are provided)")]
        public string androidRewardedAdUnit = AdUnitIds.TestAndroidRewarded;
        public string androidInterstitialAdUnit = AdUnitIds.TestAndroidInterstitial;
        public string iosRewardedAdUnit = AdUnitIds.TestIosRewarded;
        public string iosInterstitialAdUnit = AdUnitIds.TestIosInterstitial;

        [Tooltip("AdMob test device ids (development).")]
        public List<string> adTestDeviceIds = new List<string>();

        [Header("UMP consent debugging (development builds only)")]
        public bool umpForceEeaGeography;
        public List<string> umpTestDeviceHashedIds = new List<string>();

        [Header("Google Play")]
        [Tooltip("Base64 RSA public key: Play Console → Monetize → Monetization setup → Licensing.")]
        [TextArea(2, 6)]
        public string googlePlayPublicKeyBase64 = string.Empty;

        [Header("Editor")]
        public ServicesMode editorMode = ServicesMode.Fake;

        /// <summary>The asset from Resources, or an in-memory instance with the defaults.</summary>
        public static ServicesConfig LoadOrDefault()
        {
            var config = Resources.Load<ServicesConfig>(ResourcePath);
            return config != null ? config : CreateInstance<ServicesConfig>();
        }

        /// <summary>Ad units of the platform being built (Android ids in the editor).</summary>
        public AdUnitIds CurrentPlatformAdUnits()
        {
#if UNITY_IOS
            return new AdUnitIds(Or(iosRewardedAdUnit, AdUnitIds.TestIosRewarded), Or(iosInterstitialAdUnit, AdUnitIds.TestIosInterstitial));
#else
            return new AdUnitIds(Or(androidRewardedAdUnit, AdUnitIds.TestAndroidRewarded), Or(androidInterstitialAdUnit, AdUnitIds.TestAndroidInterstitial));
#endif
        }

        static string Or(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        }
    }
}
