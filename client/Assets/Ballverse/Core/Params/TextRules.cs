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

        /// <summary>True when <paramref name="s"/> holds a high or low surrogate that is not part of a pair.</summary>
        public static bool HasLoneSurrogate(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) i++;
                    else return true;
                }
                else if (char.IsLowSurrogate(c))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// <paramref name="s"/> with every unpaired surrogate replaced by U+FFFD, so the text survives UTF-8 encoding
        /// unchanged (replay paramsJson, spec §7.4; preset files). Returns the same instance when nothing changes.
        /// </summary>
        public static string ReplaceLoneSurrogates(string s)
        {
            if (!HasLoneSurrogate(s)) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                {
                    sb.Append(c).Append(s[i + 1]);
                    i++;
                }
                else if (char.IsSurrogate(c))
                {
                    sb.Append('\uFFFD');
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
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
