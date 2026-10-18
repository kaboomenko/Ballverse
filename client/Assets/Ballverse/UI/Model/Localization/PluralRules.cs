using System;

namespace Ballverse.UI.Localization
{
    /// <summary>CLDR plural categories.</summary>
    public enum PluralCategory
    {
        Zero = 0,
        One = 1,
        Two = 2,
        Few = 3,
        Many = 4,
        Other = 5,
    }

    /// <summary>Cardinal plural rules for the shipped languages (CLDR): English and Russian.</summary>
    public static class PluralRules
    {
        public static PluralCategory Select(string language, long n)
        {
            long abs = n < 0 ? -n : n;
            if (string.Equals(language, "ru", StringComparison.Ordinal))
            {
                long mod10 = abs % 10;
                long mod100 = abs % 100;
                if (mod10 == 1 && mod100 != 11) return PluralCategory.One;
                if (mod10 >= 2 && mod10 <= 4 && (mod100 < 12 || mod100 > 14)) return PluralCategory.Few;
                return PluralCategory.Many;
            }
            return abs == 1 ? PluralCategory.One : PluralCategory.Other;
        }

        public static bool TryParseCategory(string name, out PluralCategory category)
        {
            switch (name)
            {
                case "zero": category = PluralCategory.Zero; return true;
                case "one": category = PluralCategory.One; return true;
                case "two": category = PluralCategory.Two; return true;
                case "few": category = PluralCategory.Few; return true;
                case "many": category = PluralCategory.Many; return true;
                case "other": category = PluralCategory.Other; return true;
                default: category = PluralCategory.Other; return false;
            }
        }
    }
}
