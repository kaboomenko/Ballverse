using System;

namespace Ballverse.Core.Config
{
    /// <summary>
    /// Target VBR bitrates in kbps per export resolution and frame rate (spec §3.8).
    /// Remote key <c>export_bitrates</c>: JSON object with keys "720p30", "720p60", "1080p30", "1080p60".
    /// </summary>
    public sealed class ExportBitrates
    {
        /// <summary>Spec §3.8: 1080p60 16 Mbit/s, 1080p30 10, 720p60 10, 720p30 6.</summary>
        public static readonly ExportBitrates Defaults = new ExportBitrates(6000, 10000, 10000, 16000);

        public int P720Fps30Kbps { get; }
        public int P720Fps60Kbps { get; }
        public int P1080Fps30Kbps { get; }
        public int P1080Fps60Kbps { get; }

        /// <exception cref="ArgumentOutOfRangeException">A value is outside 1000–50000 kbps.</exception>
        public ExportBitrates(int p720Fps30Kbps, int p720Fps60Kbps, int p1080Fps30Kbps, int p1080Fps60Kbps)
        {
            P720Fps30Kbps = Check(p720Fps30Kbps, nameof(p720Fps30Kbps));
            P720Fps60Kbps = Check(p720Fps60Kbps, nameof(p720Fps60Kbps));
            P1080Fps30Kbps = Check(p1080Fps30Kbps, nameof(p1080Fps30Kbps));
            P1080Fps60Kbps = Check(p1080Fps60Kbps, nameof(p1080Fps60Kbps));
        }

        /// <summary>Bitrate for a resolution and frame rate (30 or 60).</summary>
        /// <exception cref="ArgumentOutOfRangeException">Unknown resolution or fps other than 30/60.</exception>
        public int GetKbps(ExportResolution resolution, int fps)
        {
            if (!RemoteConfigLimits.IsValidFps(fps)) throw new ArgumentOutOfRangeException(nameof(fps));
            switch (resolution)
            {
                case ExportResolution.P720: return fps == 60 ? P720Fps60Kbps : P720Fps30Kbps;
                case ExportResolution.P1080: return fps == 60 ? P1080Fps60Kbps : P1080Fps30Kbps;
                default: throw new ArgumentOutOfRangeException(nameof(resolution));
            }
        }

        /// <summary>JSON sub-key of a combination, e.g. "1080p60".</summary>
        public static string KeyOf(ExportResolution resolution, int fps)
        {
            return ((int)resolution).ToString(System.Globalization.CultureInfo.InvariantCulture) + "p"
                + fps.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        static int Check(int kbps, string name)
        {
            if (kbps < RemoteConfigLimits.BitrateKbpsMin || kbps > RemoteConfigLimits.BitrateKbpsMax)
                throw new ArgumentOutOfRangeException(name, kbps, "bitrate must be 1000–50000 kbps");
            return kbps;
        }
    }
}
