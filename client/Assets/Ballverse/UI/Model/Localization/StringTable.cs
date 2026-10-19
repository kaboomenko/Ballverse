using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Ballverse.UI.Localization
{
    /// <summary>
    /// One language's string table (spec §4.7). File format: a flat JSON object of key → string, or key → plural
    /// object with CLDR categories (<c>zero</c>, <c>one</c>, <c>two</c>, <c>few</c>, <c>many</c>, <c>other</c>;
    /// <c>other</c> is required). Keys are dotted ids such as <c>gallery.title</c>; values may hold
    /// <see cref="string.Format(IFormatProvider, string, object[])"/> placeholders <c>{0}</c>, <c>{1}</c>.
    /// Keys starting with <c>_</c> are comments and ignored. Pure C#: no UnityEngine.
    /// </summary>
    public sealed class StringTable
    {
        readonly Dictionary<string, string> _strings = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, Dictionary<PluralCategory, string>> _plurals =
            new Dictionary<string, Dictionary<PluralCategory, string>>(StringComparer.Ordinal);
        readonly List<string> _problems = new List<string>();

        /// <summary>Language code, e.g. "en", "ru".</summary>
        public string Language { get; }
        /// <summary>Entries that could not be read (wrong shape); the rest of the table still loads.</summary>
        public IReadOnlyList<string> Problems => _problems;
        public int Count => _strings.Count + _plurals.Count;

        public StringTable(string language)
        {
            Language = language ?? throw new ArgumentNullException(nameof(language));
        }

        /// <summary>
        /// Parses a table. Malformed JSON throws <see cref="FormatException"/>; individual bad entries are skipped and
        /// listed in <see cref="Problems"/>.
        /// </summary>
        public static StringTable Parse(string language, string json)
        {
            var table = new StringTable(language);
            if (string.IsNullOrEmpty(json)) return table;
            JObject root;
            try
            {
                using (var reader = new JsonTextReader(new System.IO.StringReader(json)) { DateParseHandling = DateParseHandling.None })
                {
                    root = JObject.Load(reader);
                }
            }
            catch (JsonException e)
            {
                throw new FormatException("String table '" + language + "' is not a JSON object: " + e.Message, e);
            }
            foreach (var prop in root.Properties())
            {
                string key = prop.Name;
                if (key.Length == 0 || key[0] == '_') continue;
                var value = prop.Value;
                if (value.Type == JTokenType.String)
                {
                    table._strings[key] = (string)value;
                }
                else if (value.Type == JTokenType.Object)
                {
                    var forms = new Dictionary<PluralCategory, string>();
                    bool ok = true;
                    foreach (var form in ((JObject)value).Properties())
                    {
                        if (!PluralRules.TryParseCategory(form.Name, out PluralCategory cat) || form.Value.Type != JTokenType.String)
                        {
                            ok = false;
                            break;
                        }
                        forms[cat] = (string)form.Value;
                    }
                    if (ok && forms.ContainsKey(PluralCategory.Other)) table._plurals[key] = forms;
                    else table._problems.Add(key + ": plural entries need string forms of known categories including 'other'");
                }
                else
                {
                    table._problems.Add(key + ": value must be a string or a plural object");
                }
            }
            return table;
        }

        public bool Contains(string key)
        {
            return key != null && (_strings.ContainsKey(key) || _plurals.ContainsKey(key));
        }

        public bool TryGet(string key, out string value)
        {
            if (key != null && _strings.TryGetValue(key, out value)) return true;
            if (key != null && _plurals.TryGetValue(key, out var forms))
            {
                value = forms[PluralCategory.Other];
                return true;
            }
            value = null;
            return false;
        }

        /// <summary>Plural form for <paramref name="category"/>, falling back to <c>other</c>; plain strings serve every count.</summary>
        public bool TryGetPlural(string key, PluralCategory category, out string value)
        {
            if (key != null && _plurals.TryGetValue(key, out var forms))
            {
                if (!forms.TryGetValue(category, out value)) value = forms[PluralCategory.Other];
                return true;
            }
            return TryGet(key, out value);
        }

        /// <summary>All keys, sorted ordinally (diagnostics and tests).</summary>
        public IReadOnlyList<string> Keys()
        {
            var keys = new List<string>(_strings.Keys);
            keys.AddRange(_plurals.Keys);
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }

        /// <summary>Adds or replaces a plain string (tests and runtime overrides).</summary>
        public void Set(string key, string value)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("Key is required", nameof(key));
            _plurals.Remove(key);
            _strings[key] = value ?? "";
        }
    }
}
