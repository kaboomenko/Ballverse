using System;
using System.Security;

namespace Ballverse.Services.Editor
{
    /// <summary>
    /// Text edits of an XML property list (the generated Xcode project's Info.plist). Pure string functions, so they compile
    /// on every build image without the iOS Xcode extension and are unit-tested.
    /// </summary>
    public static class InfoPlistText
    {
        /// <summary>True when <paramref name="key"/> is a key of the plist (any value).</summary>
        public static bool HasKey(string plist, string key)
        {
            if (plist == null) throw new ArgumentNullException(nameof(plist));
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A key is required", nameof(key));
            return plist.IndexOf("<key>" + key + "</key>", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Adds <c>&lt;key&gt;key&lt;/key&gt;&lt;string&gt;value&lt;/string&gt;</c> before the closing &lt;/dict&gt; of the
        /// root dictionary when the key is absent; a key that already exists (set by the owner or another plugin) is left
        /// unchanged. The value is XML-escaped.
        /// </summary>
        public static string EnsureString(string plist, string key, string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (HasKey(plist, key)) return plist;
            return InsertBeforeRootDictEnd(plist, "\t<key>" + key + "</key>\n\t<string>" + SecurityElement.Escape(value) + "</string>\n");
        }

        /// <summary>
        /// Adds <c>&lt;key&gt;key&lt;/key&gt;&lt;true/&gt;</c> (or <c>&lt;false/&gt;</c>) before the closing &lt;/dict&gt; of the root
        /// dictionary when the key is absent; an existing key is left unchanged.
        /// </summary>
        public static string EnsureBool(string plist, string key, bool value)
        {
            if (HasKey(plist, key)) return plist;
            return InsertBeforeRootDictEnd(plist, "\t<key>" + key + "</key>\n\t" + (value ? "<true/>" : "<false/>") + "\n");
        }

        static string InsertBeforeRootDictEnd(string plist, string insert)
        {
            int plistEnd = plist.LastIndexOf("</plist>", StringComparison.Ordinal);
            int dictEnd = plistEnd < 0 ? -1 : plist.LastIndexOf("</dict>", plistEnd, StringComparison.Ordinal);
            if (dictEnd < 0) throw new FormatException("Not an XML property list with a root <dict>.");
            return plist.Substring(0, dictEnd) + insert + plist.Substring(dictEnd);
        }
    }
}
