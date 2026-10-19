using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ballverse.UI.Localization
{
    /// <summary>
    /// Key-based string lookup with a runtime language switch (spec §4.7). Lookup order: current language table →
    /// English table → a readable fallback (for <c>params.*</c> keys of modes that ship no strings, the id split into
    /// words) — missing keys are recorded in <see cref="MissingKeys"/> so tests and debug builds can report them.
    /// Pure C#; the Unity side only supplies table text (<see cref="ITableSource"/>).
    /// </summary>
    public sealed class Localizer
    {
        /// <summary>Supplies the JSON text of a language table, or null when the language is not shipped.</summary>
        public interface ITableSource
        {
            string LoadTableJson(string language);
        }

        readonly ITableSource _source;
        readonly Dictionary<string, StringTable> _tables = new Dictionary<string, StringTable>(StringComparer.Ordinal);
        readonly HashSet<string> _missing = new HashSet<string>(StringComparer.Ordinal);
        StringTable _current;
        StringTable _fallback;

        /// <summary>Raised after <see cref="SetLanguage"/> switched to another language.</summary>
        public event Action LanguageChanged;

        public string Language { get; private set; }
        /// <summary>Culture of the interface language (en-US / ru-RU).</summary>
        public CultureInfo Culture { get; private set; }
        /// <summary>
        /// Culture for numbers, dates and units (spec §4.7: "по локали устройства"): the device locale when the shell
        /// sets one (<see cref="SetFormatCulture"/>), otherwise <see cref="Culture"/>. Strings still follow
        /// <see cref="Language"/>.
        /// </summary>
        public CultureInfo FormatCulture => _formatCulture ?? Culture;

        CultureInfo _formatCulture;

        /// <summary>Device locale for numbers and dates (null = the interface language's culture).</summary>
        public void SetFormatCulture(CultureInfo culture)
        {
            _formatCulture = culture;
        }
        /// <summary>Keys that were requested but found in no table (sorted copy).</summary>
        public IReadOnlyList<string> MissingKeys
        {
            get
            {
                var list = new List<string>(_missing);
                list.Sort(StringComparer.Ordinal);
                return list;
            }
        }

        public Localizer(ITableSource source, string language)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _fallback = Load(LanguageResolver.English);
            Apply(language);
        }

        /// <summary>Switches the language (unsupported codes fall back to English). Returns true when it changed.</summary>
        public bool SetLanguage(string language)
        {
            string resolved = LanguageResolver.FromTag(language);
            if (resolved == Language) return false;
            Apply(resolved);
            LanguageChanged?.Invoke();
            return true;
        }

        public bool Has(string key)
        {
            return _current.Contains(key) || _fallback.Contains(key);
        }

        /// <summary>The string for <paramref name="key"/> (never null).</summary>
        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return "";
            if (_current.TryGet(key, out string s)) return s;
            if (_fallback.TryGet(key, out s)) return s;
            return Missing(key);
        }

        /// <summary>Formats the string for <paramref name="key"/> with the current culture.</summary>
        public string Format(string key, params object[] args)
        {
            string pattern = Get(key);
            if (args == null || args.Length == 0) return pattern;
            return SafeFormat(pattern, args);
        }

        /// <summary>Plural form for <paramref name="count"/>; the count is <c>{0}</c>, <paramref name="args"/> follow.</summary>
        public string Plural(string key, long count, params object[] args)
        {
            string pattern;
            var cat = PluralRules.Select(Language, count);
            if (!_current.TryGetPlural(key, cat, out pattern))
            {
                var fallbackCat = PluralRules.Select(LanguageResolver.English, count);
                if (!_fallback.TryGetPlural(key, fallbackCat, out pattern)) pattern = Missing(key);
            }
            var all = new object[1 + (args?.Length ?? 0)];
            all[0] = count;
            if (args != null) Array.Copy(args, 0, all, 1, args.Length);
            return SafeFormat(pattern, all);
        }

        public string Resolve(LocalizedText text)
        {
            if (text.Key == null) return text.Literal ?? "";
            if (text.Count.HasValue) return Plural(text.Key, text.Count.Value, text.Args);
            return text.Args == null ? Get(text.Key) : Format(text.Key, text.Args);
        }

        /// <summary>String for <paramref name="key"/> when it exists, else <paramref name="fallback"/> (no missing-key record).</summary>
        public string GetOrDefault(string key, string fallback)
        {
            if (key != null && _current.TryGet(key, out string s)) return s;
            if (key != null && _fallback.TryGet(key, out s)) return s;
            return fallback;
        }

        void Apply(string language)
        {
            Language = LanguageResolver.FromTag(language);
            _current = Load(Language);
            Culture = LanguageResolver.CultureFor(Language);
        }

        StringTable Load(string language)
        {
            if (_tables.TryGetValue(language, out var t)) return t;
            string json = _source.LoadTableJson(language);
            t = json == null ? new StringTable(language) : StringTable.Parse(language, json);
            _tables[language] = t;
            return t;
        }

        string SafeFormat(string pattern, object[] args)
        {
            try
            {
                return string.Format(FormatCulture, pattern, args);
            }
            catch (FormatException)
            {
                return pattern;
            }
        }

        string Missing(string key)
        {
            _missing.Add(key);
            return Humanize(key);
        }

        /// <summary>
        /// Readable stand-in for a missing key: the last dotted segment split at camelCase / underscores, first letter
        /// upper-case ("params.ballCount" → "Ball count"). Only reached when a table lacks a key.
        /// </summary>
        public static string Humanize(string key)
        {
            int dot = key.LastIndexOf('.');
            string id = dot >= 0 && dot < key.Length - 1 ? key.Substring(dot + 1) : key;
            var sb = new StringBuilder(id.Length + 4);
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                if (c == '_' || c == '-')
                {
                    if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                    continue;
                }
                if (char.IsUpper(c) && sb.Length > 0 && sb[sb.Length - 1] != ' ')
                {
                    sb.Append(' ');
                    sb.Append(char.ToLowerInvariant(c));
                    continue;
                }
                sb.Append(sb.Length == 0 ? char.ToUpperInvariant(c) : c);
            }
            return sb.ToString();
        }
    }
}
