using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;

namespace Ballverse.Editor
{
    /// <summary>
    /// Writes <c>ITSAppUsesNonExemptEncryption = NO</c> into the generated Xcode project's Info.plist (spec §8.8: the app
    /// only uses standard HTTPS), so TestFlight and App Store uploads do not stop at the export-compliance question.
    /// <para>The plist is edited as text (one key), so this compiles and runs on every build image without the iOS
    /// Xcode extension; it runs after the media plugin's Info.plist step (order 100).</para>
    /// </summary>
    public sealed class IosExportCompliance : IPostprocessBuildWithReport
    {
        public const string EncryptionKey = "ITSAppUsesNonExemptEncryption";

        public int callbackOrder => 200;

        public void OnPostprocessBuild(BuildReport report)
        {
            if (report.summary.platform != BuildTarget.iOS) return;
            string plistPath = Path.Combine(report.summary.outputPath, "Info.plist");
            if (!File.Exists(plistPath))
                throw new BuildFailedException("[Ballverse] Info.plist not found at " + plistPath + "; cannot set " + EncryptionKey + ".");
            string text = File.ReadAllText(plistPath);
            string patched = SetBoolean(text, EncryptionKey, false);
            if (!string.Equals(text, patched, StringComparison.Ordinal))
                File.WriteAllText(plistPath, patched, new UTF8Encoding(false));
        }

        /// <summary>
        /// Sets a boolean key in the top-level &lt;dict&gt; of an XML property list: replaces the value when the key exists,
        /// otherwise inserts the key before the closing &lt;/dict&gt; of the root dictionary. Other content is unchanged.
        /// </summary>
        public static string SetBoolean(string plist, string key, bool value)
        {
            if (plist == null) throw new ArgumentNullException(nameof(plist));
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A key is required", nameof(key));
            string literal = value ? "<true/>" : "<false/>";

            var existing = new Regex("(<key>" + Regex.Escape(key) + "</key>\\s*)<(true|false)\\s*/>", RegexOptions.CultureInvariant);
            Match match = existing.Match(plist);
            if (match.Success)
                return plist.Substring(0, match.Index) + match.Groups[1].Value + literal + plist.Substring(match.Index + match.Length);

            int plistEnd = plist.LastIndexOf("</plist>", StringComparison.Ordinal);
            int dictEnd = plistEnd < 0 ? -1 : plist.LastIndexOf("</dict>", plistEnd, StringComparison.Ordinal);
            if (dictEnd < 0) throw new FormatException("Not an XML property list with a root <dict>.");
            string insert = "\t<key>" + key + "</key>\n\t" + literal + "\n";
            return plist.Substring(0, dictEnd) + insert + plist.Substring(dictEnd);
        }
    }
}
