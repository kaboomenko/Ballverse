using System.Collections.Generic;
using UnityEngine;

namespace Ballverse.Services
{
    /// <summary>
    /// The one place for ad unit ids and store keys. Create it with Assets → Create → Ballverse → Services Config and save it
    /// as <c>Resources/BallverseServicesConfig.asset</c>. The AdMob units here are used only by Store builds; Dev and
    /// OwnerTest builds always use Google's official test units (spec §7.9, <see cref="ServicesPolicy"/>). A Store build
    /// without real units runs with ads off (exports without ads, Р14) and the Store build check fails it.
    /// Nothing here is secret: ad unit ids and the Play licensing key are public identifiers.
    /// The AdMob app ids themselves go into the Google Mobile Ads settings asset (Assets → Google Mobile Ads → Settings).
    /// </summary>
    [CreateAssetMenu(menuName = "Ballverse/Services Config", fileName = "BallverseServicesConfig")]
    public sealed class ServicesConfig : ScriptableObject
    {
        public const string ResourcePath = "BallverseServicesConfig";

        [Header("AdMob ad units of Store builds (Dev / OwnerTest always use Google's test units)")]
        public string androidRewardedAdUnit = string.Empty;
        public string androidInterstitialAdUnit = string.Empty;
        public string iosRewardedAdUnit = string.Empty;
        public string iosInterstitialAdUnit = string.Empty;

        [Tooltip("AdMob test device ids (development).")]
        public List<string> adTestDeviceIds = new List<string>();

        [Header("UMP consent debugging (Dev and OwnerTest builds only)")]
        public bool umpForceEeaGeography;
        public List<string> umpTestDeviceHashedIds = new List<string>();

        [Header("Google Play")]
        [Tooltip("Base64 RSA public key: Play Console → Monetize → Monetization setup → Licensing.")]
        [TextArea(2, 6)]
        public string googlePlayPublicKeyBase64 = string.Empty;

        [Header("Dev / OwnerTest device builds")]
        [Tooltip("Fake store until the apps exist in the stores, then Sandbox (Unity IAP with license testers / sandbox accounts).")]
        public TestBuildStore testBuildStore = TestBuildStore.Fake;

        [Header("Editor")]
        public ServicesMode editorMode = ServicesMode.Fake;

        /// <summary>The asset from Resources, or an in-memory instance with the defaults.</summary>
        public static ServicesConfig LoadOrDefault()
        {
            var config = Resources.Load<ServicesConfig>(ResourcePath);
            return config != null ? config : CreateInstance<ServicesConfig>();
        }

        /// <summary>Configured ad units of the platform being built (Android ids in the editor); null when either is empty.</summary>
        public AdUnitIds CurrentPlatformAdUnits()
        {
#if UNITY_IOS
            return UnitsOrNull(iosRewardedAdUnit, iosInterstitialAdUnit);
#else
            return UnitsOrNull(androidRewardedAdUnit, androidInterstitialAdUnit);
#endif
        }

        /// <summary>Configured ad units of one platform; null when either is empty (no fallback to test units).</summary>
        public static AdUnitIds UnitsOrNull(string rewarded, string interstitial)
        {
            if (string.IsNullOrWhiteSpace(rewarded) || string.IsNullOrWhiteSpace(interstitial)) return null;
            return new AdUnitIds(rewarded.Trim(), interstitial.Trim());
        }
    }
}
