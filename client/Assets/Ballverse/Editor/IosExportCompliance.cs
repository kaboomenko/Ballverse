using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Ballverse.Editor
{
    /// <summary>
    /// Writes <c>ITSAppUsesNonExemptEncryption = NO</c> into the generated Xcode project's Info.plist (spec §8.8: the app
    /// only uses standard HTTPS), so TestFlight and App Store uploads do not stop at the export-compliance question.
    /// <para>The plist is edited as text with <see cref="InfoPlistText"/>, so this compiles and runs on every build image
    /// without the iOS Xcode extension; it runs after the media plugin's Info.plist step (order 100).</para>
    /// </summary>
    public sealed class IosExportCompliance : IPostprocessBuildWithReport
    {
        public const string EncryptionKey = "ITSAppUsesNonExemptEncryption";

        public int callbackOrder => 200;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;
            PatchInfoPlistAt(report.summary.outputPath);
        }

        /// <summary>Sets the key in <c>&lt;xcodeProjectPath&gt;/Info.plist</c>; rewrites the file only when it changes.</summary>
        public static void PatchInfoPlistAt(string xcodeProjectPath)
        {
            if (string.IsNullOrEmpty(xcodeProjectPath)) throw new ArgumentException("The Xcode project path is required.", nameof(xcodeProjectPath));
            string plistPath = Path.Combine(xcodeProjectPath, "Info.plist");
            if (!File.Exists(plistPath))
                throw new BuildFailedException("[Ballverse] Info.plist not found at " + plistPath + "; cannot set " + EncryptionKey + ".");
            string text = File.ReadAllText(plistPath);
            string patched = InfoPlistText.SetBoolean(text, EncryptionKey, false);
            if (!string.Equals(text, patched, StringComparison.Ordinal))
                File.WriteAllText(plistPath, patched, new UTF8Encoding(false));
        }
    }
}
