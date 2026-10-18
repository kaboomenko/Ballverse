using System;
using UnityEditor;

namespace Ballverse.Editor
{
    public enum BuildKind
    {
        /// <summary>Development APK for sideloading on a test phone (debug keystore unless a keystore is passed).</summary>
        AndroidApk = 0,
        /// <summary>Release Android App Bundle for Google Play (needs an upload keystore to be accepted by Play).</summary>
        AndroidAab = 1,
        /// <summary>Xcode project; signing and the actual iPhone build happen in Xcode or the macOS CI job.</summary>
        IosXcode = 2,
    }

    /// <summary>Decisions <see cref="BuildScript"/> takes from the build kind and command line, kept free of side effects so EditMode tests cover them.</summary>
    public static class BuildPolicy
    {
        /// <summary>"-ballverseDevelopment true|false": development or release player (see <see cref="IsDevelopment"/>).</summary>
        public const string DevelopmentOption = "ballverseDevelopment";

        /// <summary>"-ballverseAllowTestAds true": lets a release build ship Google's sample AdMob app id (test ads only).</summary>
        public const string AllowTestAdsOption = "ballverseAllowTestAds";

        /// <summary>Google's test publisher id, shared by every sample app id and test ad unit id.</summary>
        public const string GoogleTestPublisherId = "3940256099942544";

        /// <summary>Google's public sample app ids (developers.google.com/admob/android/test-ads, /ios/test-ads).</summary>
        public const string SampleAndroidAppId = "ca-app-pub-" + GoogleTestPublisherId + "~3347511713";
        public const string SampleIosAppId = "ca-app-pub-" + GoogleTestPublisherId + "~1458002511";

        /// <summary>True when <paramref name="id"/> belongs to Google's test publisher (sample app id or test ad unit id).</summary>
        public static bool IsGoogleSampleId(string id) =>
            id != null && id.IndexOf(GoogleTestPublisherId, StringComparison.Ordinal) >= 0;

        /// <summary>
        /// Development or release player. An explicit -ballverseDevelopment wins; otherwise the APK and the Xcode project
        /// are development builds (installed on test phones), except an Xcode project signed with a Distribution
        /// profile (App Store/TestFlight or Ad Hoc), and the App Bundle is a release build.
        /// </summary>
        public static bool IsDevelopment(BuildKind kind, BuildArgs args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            bool? explicitValue = args.GetBool(DevelopmentOption);
            if (explicitValue.HasValue) return explicitValue.Value;
            switch (kind)
            {
                case BuildKind.AndroidApk: return true;
                case BuildKind.AndroidAab: return false;
                default:
                    return !string.Equals(args.Get("iosProvisioningProfileType"), "Distribution", StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Spec §8.9: test ad ids must not reach a release build. Returns why a release build for
        /// <paramref name="target"/> may not use <paramref name="ids"/>, or null when it may (development build, ads
        /// plugin absent, real id, or test ads explicitly allowed).
        /// </summary>
        public static string ReleaseAdIdsProblem(BuildTarget target, bool development, bool allowTestAds, (string android, string ios)? ids)
        {
            if (development || allowTestAds || !ids.HasValue) return null;
            string id = target == BuildTarget.iOS ? ids.Value.ios : ids.Value.android;
            if (!string.IsNullOrWhiteSpace(id) && !IsGoogleSampleId(id)) return null;
            return "Release " + target + " build refused: the AdMob app id is " +
                   (string.IsNullOrWhiteSpace(id) ? "empty" : "Google's sample id (" + id + ")") +
                   ". Enter the real id in Assets > Google Mobile Ads > Settings, build a development player " +
                   "(-" + DevelopmentOption + " true), or pass -" + AllowTestAdsOption + " true to ship test ads knowingly (spec §8.9).";
        }
    }
}
