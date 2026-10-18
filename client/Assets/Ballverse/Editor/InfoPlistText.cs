using System;
using System.Security;
using System.Text.RegularExpressions;

namespace Ballverse.Editor
{
    /// <summary>
    /// Text edits of an XML property list (the generated Xcode project's Info.plist), shared by the app's iOS build
    /// post-processors. Pure string functions, so they compile on every build image without the iOS Xcode extension
    /// (UnityEditor.iOS.Xcode is only present with iOS build support) and are unit-tested. New keys go before the
    /// closing &lt;/dict&gt; of the root dictionary, tab-indented like Unity's own Info.plist.
    /// </summary>
    public static class InfoPlistText
    {
        /// <summary>True when <paramref name="key"/> is a key of the plist (any value, any nesting level).</summary>
        public static bool HasKey(string plist, string key)
        {
            CheckArgs(plist, key);
            return plist.IndexOf("<key>" + key + "</key>", StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// Adds <c>&lt;key&gt;key&lt;/key&gt;&lt;string&gt;value&lt;/string&gt;</c> when the key is absent; a key that
        /// already exists (set by the owner or another plugin) is left unchanged. The value is XML-escaped.
        /// </summary>
        public static string EnsureString(string plist, string key, string value)
        {
            if (value == null) throw new ArgumentNullException(nameof(value));
            if (HasKey(plist, key)) return plist;
            return InsertIntoRootDict(plist, key, "<string>" + SecurityElement.Escape(value) + "</string>");
        }

        /// <summary>
        /// Sets a boolean key: replaces the value when the key exists with a boolean value, otherwise inserts it.
        /// Other content is unchanged.
        /// </summary>
        public static string SetBoolean(string plist, string key, bool value)
        {
            CheckArgs(plist, key);
            string literal = value ? "<true/>" : "<false/>";
            var existing = new Regex("(<key>" + Regex.Escape(key) + "</key>\\s*)<(true|false)\\s*/>", RegexOptions.CultureInvariant);
            Match match = existing.Match(plist);
            if (match.Success)
                return plist.Substring(0, match.Index) + match.Groups[1].Value + literal + plist.Substring(match.Index + match.Length);
            return InsertIntoRootDict(plist, key, literal);
        }

        static string InsertIntoRootDict(string plist, string key, string valueElement)
        {
            int plistEnd = plist.LastIndexOf("</plist>", StringComparison.Ordinal);
            int dictEnd = plistEnd < 0 ? -1 : plist.LastIndexOf("</dict>", plistEnd, StringComparison.Ordinal);
            if (dictEnd < 0) throw new FormatException("Not an XML property list with a root <dict>.");
            string insert = "\t<key>" + SecurityElement.Escape(key) + "</key>\n\t" + valueElement + "\n";
            return plist.Substring(0, dictEnd) + insert + plist.Substring(dictEnd);
        }

        static void CheckArgs(string plist, string key)
        {
            if (plist == null) throw new ArgumentNullException(nameof(plist));
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A key is required", nameof(key));
        }
    }
}
