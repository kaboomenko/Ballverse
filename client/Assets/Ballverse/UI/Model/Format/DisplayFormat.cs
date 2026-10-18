using System;
using System.Globalization;
using Ballverse.UI.Localization;

namespace Ballverse.UI.Format
{
    /// <summary>Times, durations and sizes as shown in the interface (spec §4.7: numbers by locale). Pure.</summary>
    public static class DisplayFormat
    {
        /// <summary>Timeline clock "m:ss.f" (tenths) or "m:ss".</summary>
        public static string Clock(long micros, bool tenths)
        {
            if (micros < 0) micros = 0;
            long totalTenths = micros / 100000L;
            long totalSeconds = totalTenths / 10;
            long minutes = totalSeconds / 60;
            long seconds = totalSeconds % 60;
            string s = minutes.ToString(CultureInfo.InvariantCulture) + ":" + seconds.ToString("00", CultureInfo.InvariantCulture);
            return tenths ? s + "." + (totalTenths % 10).ToString(CultureInfo.InvariantCulture) : s;
        }

        /// <summary>Duration in milliseconds as "m:ss" (cards, result).</summary>
        public static string DurationMs(long ms)
        {
            return Clock(ms * 1000L, false);
        }

        /// <summary>Approximate remaining time ("~ 25 s" / "~ 2 min") via the localized patterns.</summary>
        public static string Remaining(long ms, Localizer localizer)
        {
            if (localizer == null) throw new ArgumentNullException(nameof(localizer));
            if (ms < 0) ms = 0;
            long seconds = (ms + 999) / 1000;
            if (seconds < 60) return localizer.Format("format.remaining_seconds", seconds);
            long minutes = (seconds + 30) / 60;
            return localizer.Format("format.remaining_minutes", minutes);
        }

        /// <summary>File size with a localized unit (KB / MB / GB, 1024 based).</summary>
        public static string Bytes(long bytes, Localizer localizer)
        {
            if (localizer == null) throw new ArgumentNullException(nameof(localizer));
            if (bytes < 0) bytes = 0;
            const double k = 1024.0;
            if (bytes < k * k) return localizer.Format("format.size_kb", Math.Max(1, Math.Round(bytes / k)).ToString("0", localizer.Culture));
            if (bytes < k * k * k) return localizer.Format("format.size_mb", (bytes / (k * k)).ToString(bytes < 10 * k * k ? "0.0" : "0", localizer.Culture));
            return localizer.Format("format.size_gb", (bytes / (k * k * k)).ToString("0.0", localizer.Culture));
        }

        /// <summary>Short date of the interface culture.</summary>
        public static string ShortDate(DateTime local, Localizer localizer)
        {
            if (localizer == null) throw new ArgumentNullException(nameof(localizer));
            return local.ToString("d", localizer.Culture);
        }

        /// <summary>Speed multiplier "1×", "0.5×".</summary>
        public static string Multiplier(double value, Localizer localizer)
        {
            if (localizer == null) throw new ArgumentNullException(nameof(localizer));
            string number = value.ToString(Math.Abs(value - Math.Round(value)) < 1e-6 ? "0" : "0.##", localizer.Culture);
            return localizer.Format("unit.times", number);
        }
    }
}
