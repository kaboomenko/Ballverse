using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ballverse.Services.Editor
{
    /// <summary>
    /// iOS terminates an app that asks for App Tracking Transparency without <c>NSUserTrackingUsageDescription</c> in
    /// Info.plist (spec §8.8 "ATT-описание"). The Google Mobile Ads plugin writes the key only when its settings field
    /// "User Tracking Usage Description" is filled, which it is not by default, so this build step adds the release-1 text
    /// when the key is still missing after the other plugins ran. A text set in the plugin settings (or by the owner) wins.
    /// The runtime bridge (<c>Plugins/iOS/BallverseTracking.mm</c>) also refuses to request ATT without the key.
    /// </summary>
    public sealed class IosTrackingUsageDescription : IPostprocessBuildWithReport
    {
        public const string Key = "NSUserTrackingUsageDescription";

        /// <summary>Spec §5.6 pre-prompt message («Реклама помогает Ballverse оставаться бесплатным…»), in English.</summary>
        public const string DefaultText =
            "Ads help keep Ballverse free. Allowing tracking lets ads be more relevant to you; the app works the same either way.";

        // After the Google Mobile Ads plist step (default order 0) and the app's own plist steps (100, 200).
        public int callbackOrder => 300;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;
            string plistPath = Path.Combine(report.summary.outputPath, "Info.plist");
            if (!File.Exists(plistPath))
            {
                Debug.LogWarning("[Ballverse] Info.plist not found at " + plistPath + "; " + Key + " not checked.");
                return;
            }
            string text = File.ReadAllText(plistPath);
            if (InfoPlistText.HasKey(text, Key)) return;
            File.WriteAllText(plistPath, InfoPlistText.EnsureString(text, Key, DefaultText), new UTF8Encoding(false));
            Debug.Log("[Ballverse] Added " + Key + " to Info.plist (ATT before the first ad, spec §5.6).");
        }
    }
}
