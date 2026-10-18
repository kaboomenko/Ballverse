using System.Linq;
using System.Xml;
using Ballverse.App;
using Ballverse.Editor;
using NUnit.Framework;

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
            string patched = IosExportCompliance.SetBoolean(UnityInfoPlist, IosExportCompliance.EncryptionKey, false);
            Assert.AreEqual(patched, IosExportCompliance.SetBoolean(patched, IosExportCompliance.EncryptionKey, false), "idempotent");
            StringAssert.Contains("<!DOCTYPE plist", patched);

            var doc = new XmlDocument { XmlResolver = null };
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
            using (var reader = XmlReader.Create(new System.IO.StringReader(patched), settings)) doc.Load(reader);
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
            string patched = IosExportCompliance.SetBoolean(withTrue, IosExportCompliance.EncryptionKey, false);
            StringAssert.Contains("<key>ITSAppUsesNonExemptEncryption</key>\n\t<false/>", patched);
            StringAssert.DoesNotContain("<true/>", patched);
            Assert.Throws<System.FormatException>(() => IosExportCompliance.SetBoolean("<plist/>", "K", false));
        }
    }
}
