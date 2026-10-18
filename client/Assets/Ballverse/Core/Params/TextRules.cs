using System.Text;

namespace Ballverse.Core.Params
{
    /// <summary>String length rules shared by string lists and preset names: lengths count Unicode code points.</summary>
    internal static class TextRules
    {
        /// <summary>Number of code points (a surrogate pair counts once; a lone surrogate counts once).</summary>
        public static int CodePointCount(string s)
        {
            int n = 0;
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
                n++;
            }
            return n;
        }

        /// <summary>First <paramref name="maxCodePoints"/> code points of <paramref name="s"/>; never splits a surrogate pair.</summary>
        public static string Truncate(string s, int maxCodePoints)
        {
            int n = 0;
            int i = 0;
            while (i < s.Length && n < maxCodePoints)
            {
                if (char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i += 2;
                else i++;
                n++;
            }
            return i >= s.Length ? s : s.Substring(0, i);
        }

        /// <summary>Ids: ASCII letter first, then letters, digits or '_' (camelCase or snake_case), max 64.</summary>
        public static bool IsValidParamId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 64) return false;
            if (!IsAsciiLetter(id[0])) return false;
            for (int i = 1; i < id.Length; i++)
            {
                char c = id[i];
                if (!IsAsciiLetter(c) && !(c >= '0' && c <= '9') && c != '_') return false;
            }
            return true;
        }

        public static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        public static string Quote(string s)
        {
            return s == null ? "null" : new StringBuilder(s.Length + 2).Append('\'').Append(s).Append('\'').ToString();
        }
    }
}
