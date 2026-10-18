using System;
using System.Globalization;

namespace Ballverse.Media
{
    /// <summary>
    /// Container metadata of an exported MP4 (spec §3.8 "Метаданные": project title and date; never location or
    /// personal data). Written as <c>moov/udta</c> title (<c>©nam</c>) and date (<c>©day</c>, ISO 8601 UTC): natively by
    /// AVAssetWriter on iOS, by the C# MP4 post-processing (<see cref="Mp4FastStart"/>) elsewhere. Immutable.
    /// </summary>
    public sealed class Mp4Metadata
    {
        /// <summary>Longest title kept (characters); longer titles are cut.</summary>
        public const int MaxTitleLength = 255;

        /// <summary>Project title, or null for none. Control characters are replaced by spaces.</summary>
        public string Title { get; }

        /// <summary>Creation date (UTC), or null for none.</summary>
        public DateTime? CreationTimeUtc { get; }

        public Mp4Metadata(string title, DateTime? creationTimeUtc)
        {
            Title = CleanTitle(title);
            if (creationTimeUtc.HasValue)
            {
                DateTime t = creationTimeUtc.Value;
                if (t.Kind == DateTimeKind.Local) t = t.ToUniversalTime();
                else if (t.Kind == DateTimeKind.Unspecified) t = DateTime.SpecifyKind(t, DateTimeKind.Utc);
                CreationTimeUtc = new DateTime(t.Ticks - t.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
            }
        }

        /// <summary>True when there is nothing to write.</summary>
        public bool IsEmpty => Title == null && !CreationTimeUtc.HasValue;

        /// <summary>The date as written into the file ("2026-10-18T09:30:00Z"), or null.</summary>
        public string CreationTimeIso8601 =>
            CreationTimeUtc?.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

        static string CleanTitle(string title)
        {
            if (title == null) return null;
            char[] chars = title.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++) if (char.IsControl(chars[i])) chars[i] = ' ';
            string s = new string(chars).Trim();
            if (s.Length > MaxTitleLength)
            {
                int cut = MaxTitleLength;
                if (char.IsHighSurrogate(s[cut - 1])) cut--; // never split a surrogate pair
                s = s.Substring(0, cut);
            }
            return s.Length == 0 ? null : s;
        }
    }
}
