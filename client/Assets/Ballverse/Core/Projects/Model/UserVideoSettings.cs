using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace Ballverse.Core.Projects
{
    /// <summary><c>userVideo</c> section of project.json (spec §7.4, §3.2-§3.4, §3.6).</summary>
    public sealed class UserVideoSettings
    {
        /// <summary>Copy of the user video, relative to the project folder (e.g. "media/user.mp4").</summary>
        public string File { get; set; } = ProjectPaths.DefaultUserVideoFile;

        /// <summary>Trim start in the source video, microseconds.</summary>
        public long TrimInUs { get; set; }

        /// <summary>Trim end in the source video (exclusive), microseconds; greater than <see cref="TrimInUs"/>.</summary>
        public long TrimOutUs { get; set; }

        /// <summary>Shift of the video track relative to the clip, microseconds (spec §3.4); may be negative.</summary>
        public long OffsetUs { get; set; }

        /// <summary>Rotation in degrees: 0, 90, 180 or 270 (spec §3.3).</summary>
        public int Rotation { get; set; }

        /// <summary>Horizontal mirror (front camera).</summary>
        public bool Mirror { get; set; }

        /// <summary>Slot fill mode (spec §3.3).</summary>
        [JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
        public SlotFill Fill { get; set; } = SlotFill.Cover;

        /// <summary>Pan offset [x, y], each in -1..1 (fraction of the slot).</summary>
        public double[] Pan { get; set; } = { 0.0, 0.0 };

        /// <summary>Zoom factor, 1..<see cref="ProjectLimits.MaxZoom"/>.</summary>
        public double Zoom { get; set; } = 1.0;

        /// <summary>User audio volume, 0-2.0 (spec §3.6: 0-200%).</summary>
        public double Volume { get; set; } = 1.0;

        /// <summary>What the video slot shows when the video is shorter than the reel (spec §3.4).</summary>
        [JsonConverter(typeof(StringEnumConverter), typeof(CamelCaseNamingStrategy))]
        public VideoAfterEnd AfterEnd { get; set; } = VideoAfterEnd.Freeze;
    }
}
