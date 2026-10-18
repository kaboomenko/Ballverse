using System;
using Ballverse.Platform;
using UnityEditor;

namespace Ballverse.Editor
{
    public enum BuildKind
    {
        /// <summary>APK for sideloading on a test phone (debug keystore unless a keystore is passed).</summary>
        AndroidApk = 0,
        /// <summary>Android App Bundle for Google Play (needs an upload keystore to be accepted by Play).</summary>
        AndroidAab = 1,
        /// <summary>Xcode project; signing and the actual iPhone build happen in Xcode or the macOS CI job.</summary>
        IosXcode = 2,
    }

    /// <summary>Decisions <see cref="BuildScript"/> takes from the build kind and command line, kept free of side effects so EditMode tests cover them.</summary>
    public static class BuildPolicy
    {
        /// <summary>"-ballverseVariant Dev|OwnerTest|Store": the build variant (spec §7.9, see <see cref="ResolveVariant"/>).</summary>
        public const string VariantOption = "ballverseVariant";

        /// <summary>Google's test publisher id, shared by every sample app id and test ad unit id.</summary>
        public const string GoogleTestPublisherId = "3940256099942544";

        /// <summary>Google's public sample app ids (developers.google.com/admob/android/test-ads, /ios/test-ads).</summary>
        public const string SampleAndroidAppId = "ca-app-pub-" + GoogleTestPublisherId + "~3347511713";
        public const string SampleIosAppId = "ca-app-pub-" + GoogleTestPublisherId + "~1458002511";

        /// <summary>True when <paramref name="id"/> belongs to Google's test publisher (sample app id or test ad unit id).</summary>
        public static bool IsGoogleSampleId(string id) =>
            id != null && id.IndexOf(GoogleTestPublisherId, StringComparison.Ordinal) >= 0;

        /// <summary>Variant used when -ballverseVariant is absent: OwnerTest for the APK and the Xcode project (the owner's phones), Store for the App Bundle.</summary>
        public static BuildVariant DefaultVariant(BuildKind kind) =>
            kind == BuildKind.AndroidAab ? BuildVariant.Store : BuildVariant.OwnerTest;

        /// <summary>
        /// The build variant: an explicit -ballverseVariant wins, otherwise <see cref="DefaultVariant"/>.
        /// Throws <see cref="ArgumentException"/> for an unknown value, so a typo never silently builds another variant.
        /// </summary>
        public static BuildVariant ResolveVariant(BuildKind kind, BuildArgs args)
        {
            if (args == null) throw new ArgumentNullException(nameof(args));
            string value = args.Get(VariantOption);
            if (value == null) return DefaultVariant(kind);
            if (BuildVariants.TryParse(value, out BuildVariant variant)) return variant;
            throw new ArgumentException("Unknown -" + VariantOption + " '" + value + "'; use Dev, OwnerTest or Store.", nameof(args));
        }

        /// <summary>Unity build options of a variant: Dev is a Development Build (debug optimisation); OwnerTest and Store are release players.</summary>
        public static BuildOptions OptionsFor(BuildVariant variant) =>
            variant == BuildVariant.Dev ? BuildOptions.Development : BuildOptions.None;

        /// <summary>Extra scripting symbols of a variant (BuildPlayerOptions.extraScriptingDefines): exactly its own symbol.</summary>
        public static string[] ScriptingDefinesFor(BuildVariant variant) => new[] { BuildVariants.SymbolOf(variant) };

        /// <summary>
        /// Spec §8.9: a Store build must carry the real AdMob app id. Returns why a <paramref name="variant"/> build for
        /// <paramref name="target"/> may not use <paramref name="ids"/>, or null when it may (Dev/OwnerTest, which use
        /// Google's test ids by design; ads plugin absent; real id).
        /// </summary>
        public static string AdIdsProblem(BuildTarget target, BuildVariant variant, (string android, string ios)? ids)
        {
            if (BuildVariants.AllowsTestServices(variant) || !ids.HasValue) return null;
            string id = target == BuildTarget.iOS ? ids.Value.ios : ids.Value.android;
            if (!string.IsNullOrWhiteSpace(id) && !IsGoogleSampleId(id)) return null;
            return "Store " + target + " build refused: the AdMob app id is " +
                   (string.IsNullOrWhiteSpace(id) ? "empty" : "Google's sample id (" + id + ")") +
                   ". Enter the real id in Assets > Google Mobile Ads > Settings, or build the OwnerTest variant " +
                   "(-" + VariantOption + " OwnerTest), which uses Google's test ads (spec §7.9, §8.9).";
        }
    }
}
