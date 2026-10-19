using System;
using System.Text;

namespace Ballverse.Core.Studio
{
    /// <summary>
    /// Name of an exported video (spec §3.8 metadata: project title and date, never personal data or locations): <c>Ballverse_{name}_{yyyyMMdd_HHmmss}.mp4</c>
    /// with the project name reduced to ASCII letters, digits, '-' and '_' (other characters become '_', runs collapse, at most
    /// 40 characters); an empty result falls back to the mode id, then to "video". Safe as a single path segment on every
    /// platform and in <see cref="Projects.ProjectPaths.IsSafeMediaPath"/>.
    /// </summary>
    public static class ExportFileNaming
    {
        public const string Prefix = "Ballverse";
        public const string Extension = ".mp4";
        public const int MaxNameLength = 40;

        /// <param name="projectName">User-visible project name.</param>
        /// <param name="modeId">Mode id of the clip (fallback name).</param>
        /// <param name="nowIso">ISO-8601 timestamp of the export, e.g. "2026-10-18T12:06:00Z" (digits are taken in order).</param>
        public static string FileName(string projectName, string modeId, string nowIso)
        {
            string name = Slug(projectName);
            if (name.Length == 0) name = Slug(modeId);
            if (name.Length == 0) name = "video";
            return Prefix + "_" + name + "_" + Stamp(nowIso) + Extension;
        }

        /// <summary>ASCII slug of <paramref name="text"/> (see the class remarks).</summary>
        public static string Slug(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var sb = new StringBuilder(Math.Min(text.Length, MaxNameLength));
            bool lastUnderscore = true; // trims leading separators
            foreach (char c in text)
            {
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-';
                if (ok)
                {
                    sb.Append(c);
                    lastUnderscore = false;
                }
                else if (!lastUnderscore)
                {
                    sb.Append('_');
                    lastUnderscore = true;
                }
                if (sb.Length >= MaxNameLength) break;
            }
            while (sb.Length > 0 && (sb[sb.Length - 1] == '_' || sb[sb.Length - 1] == '-')) sb.Length--;
            return sb.ToString();
        }

        /// <summary>"yyyyMMdd_HHmmss" from the first 14 digits of an ISO timestamp (zero-padded when shorter).</summary>
        public static string Stamp(string nowIso)
        {
            var digits = new StringBuilder(14);
            if (nowIso != null)
            {
                foreach (char c in nowIso)
                {
                    if (c >= '0' && c <= '9') digits.Append(c);
                    if (digits.Length == 14) break;
                }
            }
            while (digits.Length < 14) digits.Append('0');
            return digits.ToString(0, 8) + "_" + digits.ToString(8, 6);
        }
    }
}
