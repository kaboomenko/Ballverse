using System;
using System.Collections.Generic;

namespace Ballverse.UI.Localization
{
    /// <summary>Interface language choice in Settings (spec §4.3, §4.7).</summary>
    public enum LanguagePreference
    {
        /// <summary>Follow the device language when it is supported, otherwise English.</summary>
        System = 0,
        English = 1,
        Russian = 2,
    }

    /// <summary>Maps the Settings choice and the device language to a shipped table (spec §4.7: EN + RU in 1.0).</summary>
    public static class LanguageResolver
    {
        public const string English = "en";
        public const string Russian = "ru";

        /// <summary>Shipped interface languages; the first one is the fallback.</summary>
        public static readonly IReadOnlyList<string> Supported = Array.AsReadOnly(new[] { English, Russian });

        /// <param name="systemLanguageTag">Device language as a BCP 47 tag or bare code ("ru-RU", "ru", "en"); may be null.</param>
        public static string Resolve(LanguagePreference preference, string systemLanguageTag)
        {
            switch (preference)
            {
                case LanguagePreference.English: return English;
                case LanguagePreference.Russian: return Russian;
                default: return FromTag(systemLanguageTag);
            }
        }

        /// <summary>Shipped language for a device tag; unsupported or empty tags give English.</summary>
        public static string FromTag(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return English;
            int sep = tag.IndexOfAny(new[] { '-', '_' });
            string code = (sep >= 0 ? tag.Substring(0, sep) : tag).ToLowerInvariant();
            foreach (string s in Supported)
                if (s == code) return s;
            return English;
        }

        /// <summary>Culture used for numbers, dates and units of a shipped language.</summary>
        public static System.Globalization.CultureInfo CultureFor(string language)
        {
            try
            {
                return System.Globalization.CultureInfo.GetCultureInfo(language == Russian ? "ru-RU" : "en-US");
            }
            catch (System.Globalization.CultureNotFoundException)
            {
                // IL2CPP builds with stripped globalization data: fall back to invariant formatting.
                return System.Globalization.CultureInfo.InvariantCulture;
            }
        }
    }
}
