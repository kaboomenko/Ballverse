using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ballverse.Services.Editor
{
    /// <summary>
    /// Spec §9.1: analytics starts collecting only after the user's UMP decision. The native Firebase SDK starts with the
    /// process and, by default, collects automatic events (first_open, session_start) with every Consent Mode type granted
    /// before any C# runs; <c>SetAnalyticsCollectionEnabled</c> and <c>SetConsent</c> from <c>ServicesStartup</c> come only
    /// after Firebase's asynchronous dependency check. So the builds ship Firebase's documented defaults — collection off and
    /// all four consent types denied — and the services turn them on after the decision
    /// (<c>ServicesStartup.ApplyConsentMode</c>).
    /// <list type="bullet">
    /// <item>Android: <c>&lt;meta-data&gt;</c> in the unityLibrary manifest (merged into the app manifest).</item>
    /// <item>iOS: Boolean keys in the generated Info.plist.</item>
    /// </list>
    /// Values the owner or another plugin already set are kept. The keys are harmless while Firebase is not in the build
    /// (release 1), so the step always runs and nothing depends on remembering it when <c>BALLVERSE_FIREBASE</c> is turned on.
    /// </summary>
    public sealed class FirebaseConsentDefaults : IPostGenerateGradleAndroidProject, IPostprocessBuildWithReport
    {
        /// <summary>Android manifest meta-data names and their values.</summary>
        public static readonly string[,] AndroidMetaData =
        {
            { "firebase_analytics_collection_enabled", "false" },
            { "google_analytics_default_allow_analytics_storage", "false" },
            { "google_analytics_default_allow_ad_storage", "false" },
            { "google_analytics_default_allow_ad_user_data", "false" },
            { "google_analytics_default_allow_ad_personalization_signals", "false" },
        };

        /// <summary>iOS Info.plist keys, all written as Boolean NO.</summary>
        public static readonly string[] IosPlistKeys =
        {
            "FIREBASE_ANALYTICS_COLLECTION_ENABLED",
            "GOOGLE_ANALYTICS_DEFAULT_ALLOW_ANALYTICS_STORAGE",
            "GOOGLE_ANALYTICS_DEFAULT_ALLOW_AD_STORAGE",
            "GOOGLE_ANALYTICS_DEFAULT_ALLOW_AD_USER_DATA",
            "GOOGLE_ANALYTICS_DEFAULT_ALLOW_AD_PERSONALIZATION_SIGNALS",
        };

        // After the media plugin's unityLibrary patch (100), the backup rules (200) and the ATT text (300).
        public int callbackOrder => 310;

        /// <summary>The manifest text with every default of <see cref="AndroidMetaData"/> that is not set yet.</summary>
        public static string ApplyToManifest(string manifest)
        {
            for (int i = 0; i < AndroidMetaData.GetLength(0); i++)
                manifest = AndroidManifestText.EnsureMetaData(manifest, AndroidMetaData[i, 0], AndroidMetaData[i, 1]);
            return manifest;
        }

        /// <summary>The plist text with every key of <see cref="IosPlistKeys"/> that is not set yet, as NO.</summary>
        public static string ApplyToPlist(string plist)
        {
            foreach (string key in IosPlistKeys) plist = InfoPlistText.EnsureBool(plist, key, false);
            return plist;
        }

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifestPath = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifestPath))
            {
                Debug.LogWarning("[Ballverse] unityLibrary manifest not found at " + manifestPath + "; Firebase consent defaults not set.");
                return;
            }
            string text = File.ReadAllText(manifestPath);
            string updated = ApplyToManifest(text);
            if (updated == text) return;
            File.WriteAllText(manifestPath, updated, new UTF8Encoding(false));
            Debug.Log("[Ballverse] Firebase analytics collection and consent default to off until the UMP decision (spec §9.1).");
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;
            string plistPath = Path.Combine(report.summary.outputPath, "Info.plist");
            if (!File.Exists(plistPath))
            {
                Debug.LogWarning("[Ballverse] Info.plist not found at " + plistPath + "; Firebase consent defaults not set.");
                return;
            }
            string text = File.ReadAllText(plistPath);
            string updated = ApplyToPlist(text);
            if (updated == text) return;
            File.WriteAllText(plistPath, updated, new UTF8Encoding(false));
            Debug.Log("[Ballverse] Firebase analytics collection and consent default to off until the UMP decision (spec §9.1).");
        }
    }
}
