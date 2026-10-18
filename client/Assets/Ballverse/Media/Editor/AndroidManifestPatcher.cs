#if UNITY_ANDROID
using System.IO;
using System.Xml;
using UnityEditor.Android;
using UnityEngine;

namespace Ballverse.Media.Editor
{
    /// <summary>
    /// Adds the MediaBridge manifest entries to the generated unityLibrary manifest (merged into the app by Gradle), so no
    /// hand-maintained main AndroidManifest.xml is needed and other plugins' manifests stay untouched:
    /// <list type="bullet">
    /// <item>PickerActivity (transparent host for the Photo Picker and the legacy storage permission request);</item>
    /// <item>MediaFileProvider (read-only share provider, authority ${applicationId}.ballverse.media.share);</item>
    /// <item>WRITE_EXTERNAL_STORAGE with maxSdkVersion 28 (gallery save on Android 8–9 only);</item>
    /// <item>&lt;queries&gt; for the Google Play services Photo Picker backport and ACTION_SEND targets;</item>
    /// <item>the ModuleDependencies service that asks Google Play services to install the Photo Picker backport.</item>
    /// </list>
    /// The patch is idempotent (entries are matched by android:name).
    /// It also appends R8/ProGuard keep rules for the plugin classes to unityLibrary's <c>proguard-unity.txt</c> (a
    /// consumer rules file of the library): C# reaches <c>com.ballverse.media.*</c> only by name through JNI and
    /// AndroidJavaProxy, so a minified release build would otherwise strip or rename them.
    /// </summary>
    public sealed class AndroidManifestPatcher : IPostGenerateGradleAndroidProject
    {
        const string AndroidNs = "http://schemas.android.com/apk/res/android";
        const string ToolsNs = "http://schemas.android.com/tools";

        public int callbackOrder => 100;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            string manifest = Path.Combine(path, "src", "main", "AndroidManifest.xml");
            if (!File.Exists(manifest))
            {
                Debug.LogError("[Media] unityLibrary manifest not found at " + manifest + "; MediaBridge Android entries not added.");
                return;
            }
            Patch(manifest);
            AddKeepRules(Path.Combine(path, "proguard-unity.txt"));
        }

        internal const string KeepRulesMarker = "# Ballverse.Media keep rules";

        /// <summary>The keep rules appended to the library's consumer ProGuard file.</summary>
        internal const string KeepRules =
            KeepRulesMarker + "\n" +
            "# Classes and members used by C# through JNI (AndroidJavaClass/AndroidJavaObject) and AndroidJavaProxy.\n" +
            "-keep class com.ballverse.media.** { *; }\n" +
            "-keep interface com.ballverse.media.** { *; }\n";

        /// <summary>Appends <see cref="KeepRules"/> once (idempotent).</summary>
        public static void AddKeepRules(string proguardFile)
        {
            if (!File.Exists(proguardFile))
            {
                Debug.LogWarning("[Media] " + proguardFile + " not found; if minification is enabled add: " + KeepRules);
                return;
            }
            string text = File.ReadAllText(proguardFile);
            if (text.Contains(KeepRulesMarker)) return;
            File.AppendAllText(proguardFile, (text.Length > 0 && !text.EndsWith("\n") ? "\n" : string.Empty) + KeepRules);
        }

        /// <summary>Patches a manifest file in place.</summary>
        public static void Patch(string manifestPath)
        {
            var doc = new XmlDocument();
            doc.Load(manifestPath);
            XmlElement root = doc.DocumentElement;
            if (root == null || root.Name != "manifest") throw new IOException("not an Android manifest: " + manifestPath);
            if (!root.HasAttribute("xmlns:android")) root.SetAttribute("xmlns:android", AndroidNs);
            if (!root.HasAttribute("xmlns:tools")) root.SetAttribute("xmlns:tools", ToolsNs);

            XmlElement app = root["application"];
            if (app == null)
            {
                app = doc.CreateElement("application");
                root.AppendChild(app);
            }

            XmlElement permission = Ensure(doc, root, "uses-permission", "android.permission.WRITE_EXTERNAL_STORAGE");
            SetAndroid(permission, "maxSdkVersion", "28");

            XmlElement queries = root["queries"];
            if (queries == null)
            {
                queries = doc.CreateElement("queries");
                root.AppendChild(queries);
            }
            EnsureQueryIntent(doc, queries, "androidx.activity.result.contract.action.PICK_IMAGES", null);
            EnsureQueryIntent(doc, queries, "android.intent.action.SEND", "video/*");

            XmlElement activity = Ensure(doc, app, "activity", "com.ballverse.media.PickerActivity");
            SetAndroid(activity, "exported", "false");
            SetAndroid(activity, "theme", "@android:style/Theme.Translucent.NoTitleBar");
            SetAndroid(activity, "excludeFromRecents", "true");
            SetAndroid(activity, "configChanges",
                "mcc|mnc|locale|touchscreen|keyboard|keyboardHidden|navigation|orientation|screenLayout|uiMode|screenSize|smallestScreenSize|fontScale|layoutDirection|density");

            XmlElement provider = Ensure(doc, app, "provider", "com.ballverse.media.MediaFileProvider");
            SetAndroid(provider, "authorities", "${applicationId}.ballverse.media.share");
            SetAndroid(provider, "exported", "false");
            SetAndroid(provider, "grantUriPermissions", "true");

            // Google Play services Photo Picker backport (Android 4.4–10 devices without the platform picker).
            XmlElement service = Ensure(doc, app, "service", "com.google.android.gms.metadata.ModuleDependencies");
            SetAndroid(service, "enabled", "false");
            SetAndroid(service, "exported", "false");
            service.SetAttribute("ignore", ToolsNs, "MissingClass");
            if (service["intent-filter"] == null)
            {
                XmlElement filter = doc.CreateElement("intent-filter");
                XmlElement action = doc.CreateElement("action");
                SetAndroid(action, "name", "com.google.android.gms.metadata.MODULE_DEPENDENCIES");
                filter.AppendChild(action);
                service.AppendChild(filter);
                XmlElement meta = doc.CreateElement("meta-data");
                SetAndroid(meta, "name", "photopicker_activity:0:required");
                SetAndroid(meta, "value", "");
                service.AppendChild(meta);
            }

            doc.Save(manifestPath);
        }

        static XmlElement Ensure(XmlDocument doc, XmlElement parent, string tag, string name)
        {
            foreach (XmlNode node in parent.ChildNodes)
            {
                if (node is XmlElement e && e.Name == tag && e.GetAttribute("name", AndroidNs) == name) return e;
            }
            XmlElement created = doc.CreateElement(tag);
            SetAndroid(created, "name", name);
            parent.AppendChild(created);
            return created;
        }

        static void EnsureQueryIntent(XmlDocument doc, XmlElement queries, string action, string mimeType)
        {
            foreach (XmlNode node in queries.ChildNodes)
            {
                if (!(node is XmlElement intent) || intent.Name != "intent") continue;
                XmlElement a = intent["action"];
                if (a != null && a.GetAttribute("name", AndroidNs) == action) return;
            }
            XmlElement created = doc.CreateElement("intent");
            XmlElement actionElement = doc.CreateElement("action");
            SetAndroid(actionElement, "name", action);
            created.AppendChild(actionElement);
            if (mimeType != null)
            {
                XmlElement data = doc.CreateElement("data");
                SetAndroid(data, "mimeType", mimeType);
                created.AppendChild(data);
            }
            queries.AppendChild(created);
        }

        static void SetAndroid(XmlElement e, string attribute, string value)
        {
            e.SetAttribute(attribute, AndroidNs, value);
        }
    }
}
#endif
