using System;
using System.Xml;

namespace Ballverse.Services.Editor
{
    /// <summary>
    /// Edits of a generated Gradle module's AndroidManifest.xml. Pure functions over the XML text, unit-tested.
    /// </summary>
    public static class AndroidManifestText
    {
        public const string AndroidNs = "http://schemas.android.com/apk/res/android";

        /// <summary>
        /// Adds <c>&lt;meta-data android:name="name" android:value="value"/&gt;</c> under &lt;application&gt; when no
        /// meta-data of that name exists; one that exists (set by the owner or a plugin) is left unchanged. Whitespace and
        /// the XML declaration are preserved.
        /// </summary>
        public static string EnsureMetaData(string manifest, string name, string value)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A name is required", nameof(name));
            if (value == null) throw new ArgumentNullException(nameof(value));
            var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            doc.LoadXml(manifest);
            var root = doc.DocumentElement;
            if (root == null || root.Name != "manifest") throw new FormatException("Not an Android manifest.");
            XmlElement application = null;
            foreach (XmlNode node in root.ChildNodes)
            {
                if (node is XmlElement e && e.Name == "application")
                {
                    application = e;
                    break;
                }
            }
            if (application == null)
            {
                application = doc.CreateElement("application");
                root.AppendChild(application);
            }
            foreach (XmlNode node in application.ChildNodes)
            {
                if (node is XmlElement e && e.Name == "meta-data" && e.GetAttribute("name", AndroidNs) == name) return manifest;
            }
            if (string.IsNullOrEmpty(root.GetAttribute("xmlns:android"))) root.SetAttribute("xmlns:android", AndroidNs);
            // A new line keeps the generated file readable; PreserveWhitespace keeps everything else as it was.
            application.AppendChild(doc.CreateWhitespace("\n    "));
            var meta = doc.CreateElement("meta-data");
            var nameAttr = doc.CreateAttribute("android", "name", AndroidNs);
            nameAttr.Value = name;
            meta.Attributes.Append(nameAttr);
            var valueAttr = doc.CreateAttribute("android", "value", AndroidNs);
            valueAttr.Value = value;
            meta.Attributes.Append(valueAttr);
            application.AppendChild(meta);
            return doc.OuterXml;
        }

        /// <summary>The value of meta-data <paramref name="name"/> under &lt;application&gt;, or null.</summary>
        public static string GetMetaData(string manifest, string name)
        {
            var doc = new XmlDocument { XmlResolver = null };
            doc.LoadXml(manifest);
            var nodes = doc.GetElementsByTagName("meta-data");
            foreach (XmlNode node in nodes)
            {
                if (node is XmlElement e && e.GetAttribute("name", AndroidNs) == name) return e.GetAttribute("value", AndroidNs);
            }
            return null;
        }
    }
}
