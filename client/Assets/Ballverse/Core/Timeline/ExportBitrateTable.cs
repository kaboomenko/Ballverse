using System;
using Newtonsoft.Json;

namespace Ballverse.Core.Timeline
{
    /// <summary>
    /// Target VBR bitrates of the exported file (spec §3.8): 1080p60 16 Mbit/s, 1080p30 10, 720p60 10, 720p30 6;
    /// audio AAC-LC 192 kbit/s. Immutable; <see cref="WithOverrides"/> applies Remote Config values.
    /// </summary>
    public sealed class ExportBitrateTable
    {
        public const long DefaultVideo1080p60Bps = 16000000L;
        public const long DefaultVideo1080p30Bps = 10000000L;
        public const long DefaultVideo720p60Bps = 10000000L;
        public const long DefaultVideo720p30Bps = 6000000L;
        public const long DefaultAudioBps = 192000L;

        /// <summary>Lowest accepted bitrate (any stream), bit/s. Guards against broken remote values.</summary>
        public const long MinBps = 32000L;
        /// <summary>Highest accepted bitrate (any stream), bit/s (H.264 level 4.2 High allows 62.5 Mbit/s).</summary>
        public const long MaxBps = 62500000L;

        /// <summary>The §3.8 table.</summary>
        public static readonly ExportBitrateTable Default = new ExportBitrateTable(
            DefaultVideo1080p60Bps, DefaultVideo1080p30Bps, DefaultVideo720p60Bps, DefaultVideo720p30Bps, DefaultAudioBps);

        public long Video1080p60Bps { get; }
        public long Video1080p30Bps { get; }
        public long Video720p60Bps { get; }
        public long Video720p30Bps { get; }
        public long AudioBps { get; }

        public ExportBitrateTable(long video1080p60Bps, long video1080p30Bps, long video720p60Bps, long video720p30Bps, long audioBps)
        {
            Video1080p60Bps = Check(video1080p60Bps, nameof(video1080p60Bps));
            Video1080p30Bps = Check(video1080p30Bps, nameof(video1080p30Bps));
            Video720p60Bps = Check(video720p60Bps, nameof(video720p60Bps));
            Video720p30Bps = Check(video720p30Bps, nameof(video720p30Bps));
            AudioBps = Check(audioBps, nameof(audioBps));
        }

        /// <summary>Video bitrate for a resolution and frame rate (bit/s).</summary>
        public long GetVideoBps(ExportResolution resolution, int fps)
        {
            OutputFps.Validate(fps, nameof(fps));
            switch (resolution)
            {
                case ExportResolution.P1080:
                    return fps == OutputFps.Fps60 ? Video1080p60Bps : Video1080p30Bps;
                case ExportResolution.P720:
                    return fps == OutputFps.Fps60 ? Video720p60Bps : Video720p30Bps;
                default:
                    throw new ArgumentOutOfRangeException(nameof(resolution), resolution, "Unknown ExportResolution.");
            }
        }

        /// <summary>
        /// Returns a table with the non-null override fields applied. Throws <see cref="ArgumentOutOfRangeException"/>
        /// if an override is outside [<see cref="MinBps"/>, <see cref="MaxBps"/>]; null overrides return this table.
        /// </summary>
        public ExportBitrateTable WithOverrides(ExportBitrateOverrides overrides)
        {
            if (overrides == null) return this;
            return new ExportBitrateTable(
                overrides.Video1080p60Bps ?? Video1080p60Bps,
                overrides.Video1080p30Bps ?? Video1080p30Bps,
                overrides.Video720p60Bps ?? Video720p60Bps,
                overrides.Video720p30Bps ?? Video720p30Bps,
                overrides.AudioBps ?? AudioBps);
        }

        /// <summary>
        /// Applies a Remote Config JSON payload (<see cref="ExportBitrateOverrides"/>) onto <see cref="Default"/>.
        /// Malformed JSON or out-of-range values return false and the default table, so bad remote data never blocks export.
        /// </summary>
        public static bool TryFromRemoteConfig(string json, out ExportBitrateTable table)
        {
            table = Default;
            if (string.IsNullOrEmpty(json)) return false;
            try
            {
                table = Default.WithOverrides(ExportBitrateOverrides.FromJson(json));
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
            catch (ArgumentOutOfRangeException)
            {
                return false;
            }
        }

        static long Check(long bps, string name)
        {
            if (bps < MinBps || bps > MaxBps)
                throw new ArgumentOutOfRangeException(name, bps, "Bitrate out of range.");
            return bps;
        }
    }
}
