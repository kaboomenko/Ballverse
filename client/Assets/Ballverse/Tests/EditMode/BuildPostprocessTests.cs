using System;
using System.IO;
using System.Linq;
using System.Xml;
using Ballverse.App;
using Ballverse.Editor;
using NUnit.Framework;
using UnityEditor.Build;

namespace Ballverse.Tests
{
    /// <summary>Android backup rules (spec §3.9) and the iOS export-compliance key (spec §8.8) written by build post-processors.</summary>
    public sealed class BuildPostprocessTests
    {
        const string AndroidNs = "http://schemas.android.com/apk/res/android";
        const string ToolsNs = "http://schemas.android.com/tools";

        static string[] IncludedPaths(string xml, string sectionXPath)
        {
            var doc = new XmlDocument();
            doc.LoadXml(xml);
            return doc.SelectNodes(sectionXPath + "/include").Cast<XmlElement>()
                .Select(e => e.GetAttribute("domain") + ":" + e.GetAttribute("path"))
                .ToArray();
        }

        static readonly string[] ExpectedIncludes =
        {
            "file:" + AppPaths.SettingsDirName, "file:" + AppPaths.PresetsDirName,
            "external:" + AppPaths.SettingsDirName, "external:" + AppPaths.PresetsDirName,
        };

        [Test]
        public void BackupRulesIncludeOnlySettingsAndPresets()
        {
            CollectionAssert.AreEquivalent(ExpectedIncludes, IncludedPaths(AndroidBackupRules.FullBackupContentXml(), "/full-backup-content"));
            string rules = AndroidBackupRules.DataExtractionRulesXml();
            CollectionAssert.AreEquivalent(ExpectedIncludes, IncludedPaths(rules, "/data-extraction-rules/cloud-backup"));
            CollectionAssert.AreEquivalent(ExpectedIncludes, IncludedPaths(rules, "/data-extraction-rules/device-transfer"));

            foreach (string xml in new[] { AndroidBackupRules.FullBackupContentXml(), rules })
            {
                StringAssert.DoesNotContain(AppPaths.ProjectsDirName, xml);
                StringAssert.DoesNotContain(AppPaths.LogsDirName, xml);
                StringAssert.DoesNotContain("<exclude", xml);
            }
        }

        [Test]
        public void ManifestPatchPointsTheApplicationAtTheRulesAndIsIdempotent()
        {
            var doc = new XmlDocument();
            doc.LoadXml(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>" +
                "<manifest xmlns:android=\"" + AndroidNs + "\" package=\"com.ballverse.app\">" +
                "<application android:label=\"@string/app_name\" android:allowBackup=\"false\" xmlns:tools=\"" + ToolsNs + "\" tools:replace=\"android:label\" />" +
                "</manifest>");

            AndroidBackupRules.PatchManifest(doc);
            AndroidBackupRules.PatchManifest(doc);

            XmlElement app = doc.DocumentElement["application"];
            Assert.AreEqual("true", app.GetAttribute("allowBackup", AndroidNs));
            Assert.AreEqual("@xml/" + AndroidBackupRules.FullBackupContentName, app.GetAttribute("fullBackupContent", AndroidNs));
            Assert.AreEqual("@xml/" + AndroidBackupRules.DataExtractionRulesName, app.GetAttribute("dataExtractionRules", AndroidNs));
            Assert.AreEqual("@string/app_name", app.GetAttribute("label", AndroidNs));
            CollectionAssert.AreEqual(
                new[] { "android:label", "android:allowBackup", "android:fullBackupContent", "android:dataExtractionRules" },
                app.GetAttribute("replace", ToolsNs).Split(','));
            Assert.AreEqual(1, doc.DocumentElement.GetElementsByTagName("application").Count);

            // Round trip: attributes keep the android/tools prefixes.
            string xml = doc.OuterXml;
            StringAssert.Contains("android:fullBackupContent=", xml);
            StringAssert.Contains("tools:replace=", xml);
        }

        [Test]
        public void ManifestPatchCreatesTheApplicationElementWhenMissing()
        {
            var doc = new XmlDocument();
            doc.LoadXml("<manifest package=\"com.ballverse.app\"></manifest>");
            AndroidBackupRules.PatchManifest(doc);
            XmlElement app = doc.DocumentElement["application"];
            Assert.IsNotNull(app);
            Assert.AreEqual("@xml/" + AndroidBackupRules.DataExtractionRulesName, app.GetAttribute("dataExtractionRules", AndroidNs));

            var reloaded = new XmlDocument();
            reloaded.LoadXml(doc.OuterXml);
            Assert.AreEqual("true", reloaded.DocumentElement["application"].GetAttribute("allowBackup", AndroidNs));
        }

        const string UnityInfoPlist =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">\n" +
            "<plist version=\"1.0\">\n<dict>\n" +
            "\t<key>CFBundleIdentifier</key>\n\t<string>com.ballverse.app</string>\n" +
            "\t<key>UIRequiredDeviceCapabilities</key>\n\t<array>\n\t\t<string>arm64</string>\n\t</array>\n" +
            "\t<key>NSAppTransportSecurity</key>\n\t<dict>\n\t\t<key>NSAllowsArbitraryLoads</key>\n\t\t<false/>\n\t</dict>\n" +
            "</dict>\n</plist>\n";

        [Test]
        public void ExportComplianceKeyIsAddedToTheRootDictOnce()
        {
            string patched = InfoPlistText.SetBoolean(UnityInfoPlist, IosExportCompliance.EncryptionKey, false);
            Assert.AreEqual(patched, InfoPlistText.SetBoolean(patched, IosExportCompliance.EncryptionKey, false), "idempotent");
            StringAssert.Contains("<!DOCTYPE plist", patched);

            var doc = new XmlDocument { XmlResolver = null };
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(patched), settings)) doc.Load(reader);
            XmlNode rootDict = doc.SelectSingleNode("/plist/dict");
            XmlNode key = rootDict.SelectSingleNode("key[text()='" + IosExportCompliance.EncryptionKey + "']");
            Assert.IsNotNull(key, "key is in the root dict");
            Assert.AreEqual("false", key.NextSibling is XmlElement e ? e.Name : null);
            Assert.AreEqual(1, doc.SelectNodes("//key[text()='" + IosExportCompliance.EncryptionKey + "']").Count);
            Assert.IsNotNull(rootDict.SelectSingleNode("key[text()='NSAppTransportSecurity']"), "other keys are kept");
        }

        [Test]
        public void ExportComplianceKeyThatIsTrueIsSwitchedToFalse()
        {
            string withTrue = UnityInfoPlist.Replace("</dict>\n</plist>", "\t<key>ITSAppUsesNonExemptEncryption</key>\n\t<true/>\n</dict>\n</plist>");
            string patched = InfoPlistText.SetBoolean(withTrue, IosExportCompliance.EncryptionKey, false);
            StringAssert.Contains("<key>ITSAppUsesNonExemptEncryption</key>\n\t<false/>", patched);
            StringAssert.DoesNotContain("<true/>", patched);
            Assert.Throws<FormatException>(() => InfoPlistText.SetBoolean("<plist/>", "K", false));
        }

        [Test]
        public void EnsureStringAddsAnAbsentKeyEscapedAndKeepsAnExistingOne()
        {
            string patched = InfoPlistText.EnsureString(UnityInfoPlist, "NSUserTrackingUsageDescription", "Ads & you <3");
            Assert.IsTrue(InfoPlistText.HasKey(patched, "NSUserTrackingUsageDescription"));
            StringAssert.Contains("<string>Ads &amp; you &lt;3</string>", patched);
            Assert.AreEqual(patched, InfoPlistText.EnsureString(patched, "NSUserTrackingUsageDescription", "other"), "an existing key wins");
            Assert.IsFalse(InfoPlistText.HasKey(UnityInfoPlist, "NSUserTrackingUsageDescription"));
            Assert.Throws<FormatException>(() => InfoPlistText.EnsureString("<plist></plist>", "K", "v"));

            // String and boolean edits compose: both keys land in the root dict and the document stays valid XML.
            string both = InfoPlistText.SetBoolean(patched, IosExportCompliance.EncryptionKey, false);
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            var doc = new XmlDocument { XmlResolver = null };
            using (var reader = XmlReader.Create(new StringReader(both), settings)) doc.Load(reader);
            Assert.IsNotNull(doc.SelectSingleNode("/plist/dict/key[text()='NSUserTrackingUsageDescription']"));
            Assert.IsNotNull(doc.SelectSingleNode("/plist/dict/key[text()='" + IosExportCompliance.EncryptionKey + "']"));
        }

        // ---- File-system entry points of the post-processors, on temporary copies of Unity's output layout ----

        string _temp;

        [SetUp]
        public void CreateTempFolder()
        {
            _temp = Path.Combine(Path.GetTempPath(), "BallversePostprocess-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temp);
        }

        [TearDown]
        public void DeleteTempFolder()
        {
            if (_temp != null && Directory.Exists(_temp)) Directory.Delete(_temp, true);
        }

        const string LauncherManifest =
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" +
            "<manifest xmlns:android=\"" + AndroidNs + "\" xmlns:tools=\"" + ToolsNs + "\">\n" +
            "  <application android:label=\"@string/app_name\" android:icon=\"@mipmap/app_icon\" />\n" +
            "</manifest>\n";

        /// <summary>Unity's Gradle export: &lt;root&gt;/unityLibrary (the path Unity passes) and &lt;root&gt;/launcher.</summary>
        string CreateGradleProject(bool withLauncher)
        {
            string unityLibrary = Path.Combine(_temp, "unityLibrary");
            Directory.CreateDirectory(Path.Combine(unityLibrary, "src", "main"));
            if (withLauncher)
            {
                string main = Path.Combine(_temp, "launcher", "src", "main");
                Directory.CreateDirectory(main);
                File.WriteAllText(Path.Combine(main, "AndroidManifest.xml"), LauncherManifest);
            }
            return unityLibrary;
        }

        void AssertLauncherPatched()
        {
            string main = Path.Combine(_temp, "launcher", "src", "main");
            string xmlDir = Path.Combine(main, "res", "xml");
            Assert.AreEqual(AndroidBackupRules.FullBackupContentXml(), File.ReadAllText(Path.Combine(xmlDir, AndroidBackupRules.FullBackupContentName + ".xml")));
            Assert.AreEqual(AndroidBackupRules.DataExtractionRulesXml(), File.ReadAllText(Path.Combine(xmlDir, AndroidBackupRules.DataExtractionRulesName + ".xml")));

            var doc = new XmlDocument();
            doc.Load(Path.Combine(main, "AndroidManifest.xml"));
            XmlElement app = doc.DocumentElement["application"];
            Assert.AreEqual("true", app.GetAttribute("allowBackup", AndroidNs));
            Assert.AreEqual("@xml/" + AndroidBackupRules.FullBackupContentName, app.GetAttribute("fullBackupContent", AndroidNs));
            Assert.AreEqual("@xml/" + AndroidBackupRules.DataExtractionRulesName, app.GetAttribute("dataExtractionRules", AndroidNs));
            Assert.AreEqual("@mipmap/app_icon", app.GetAttribute("icon", AndroidNs), "other attributes are kept");
            Assert.AreEqual(1, doc.DocumentElement.GetElementsByTagName("application").Count);
            Assert.IsFalse(Directory.Exists(Path.Combine(_temp, "unityLibrary", "src", "main", "res", "xml")), "the library module is not touched");
        }

        [Test]
        public void GradleCallbackWritesTheRulesIntoTheLauncherModule()
        {
            string unityLibrary = CreateGradleProject(withLauncher: true);
            new AndroidBackupRules().OnPostGenerateGradleAndroidProject(unityLibrary);
            AssertLauncherPatched();
            string once = File.ReadAllText(Path.Combine(_temp, "launcher", "src", "main", "AndroidManifest.xml"));

            // Unity may pass the path with a trailing separator; a second run is idempotent.
            new AndroidBackupRules().OnPostGenerateGradleAndroidProject(unityLibrary + Path.DirectorySeparatorChar);
            AssertLauncherPatched();
            Assert.AreEqual(once, File.ReadAllText(Path.Combine(_temp, "launcher", "src", "main", "AndroidManifest.xml")));
        }

        [Test]
        public void GradleCallbackFailsTheBuildWithoutALauncherManifest()
        {
            string unityLibrary = CreateGradleProject(withLauncher: false);
            var e = Assert.Throws<BuildFailedException>(() => new AndroidBackupRules().OnPostGenerateGradleAndroidProject(unityLibrary));
            StringAssert.Contains("Launcher manifest not found", e.Message);
        }

        [Test]
        public void InfoPlistIsPatchedInTheXcodeProjectFolder()
        {
            string plistPath = Path.Combine(_temp, "Info.plist");
            File.WriteAllText(plistPath, UnityInfoPlist);
            IosExportCompliance.PatchInfoPlistAt(_temp);
            string patched = File.ReadAllText(plistPath);
            StringAssert.Contains("<key>" + IosExportCompliance.EncryptionKey + "</key>\n\t<false/>", patched);
            StringAssert.Contains("<key>CFBundleIdentifier</key>", patched);

            // Back-date to a whole second and read the stored value back: file systems keep coarser
            // timestamps than DateTime ticks (CI's overlay fs), so compare against what was actually stored.
            DateTime written = File.GetLastWriteTimeUtc(plistPath);
            DateTime backDated = new DateTime(written.Ticks - written.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc).AddMinutes(-5);
            File.SetLastWriteTimeUtc(plistPath, backDated);
            DateTime stored = File.GetLastWriteTimeUtc(plistPath);
            IosExportCompliance.PatchInfoPlistAt(_temp + Path.DirectorySeparatorChar);
            Assert.AreEqual(patched, File.ReadAllText(plistPath));
            Assert.AreEqual(stored, File.GetLastWriteTimeUtc(plistPath), "an unchanged plist is not rewritten");

            Assert.Throws<BuildFailedException>(() => IosExportCompliance.PatchInfoPlistAt(Path.Combine(_temp, "missing")));
        }
    }
}
