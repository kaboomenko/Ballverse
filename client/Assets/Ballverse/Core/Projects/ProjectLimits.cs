namespace Ballverse.Core.Projects
{
    /// <summary>Value ranges enforced by <see cref="ProjectValidator"/> (spec §3.3-§3.6, §3.8, §3.9).</summary>
    public static class ProjectLimits
    {
        public const long MicrosPerSecond = 1000000L;

        /// <summary>Hard cap of the reel and of a user video fragment (spec §3.4, §3.2): 180 s.</summary>
        public const long MaxDurationUs = SimConstants.MaxDurationSeconds * MicrosPerSecond;
        /// <summary>Shortest export (spec §3.8 "Длительность 1-180 с").</summary>
        public const long MinDurationUs = 1 * MicrosPerSecond;

        // §3.3 layout
        public const double MinRatio = 0.30;
        public const double MaxRatio = 0.70;
        public const int MinDividerPx = 0;
        public const int MaxDividerPx = 16;
        public const int MinCornerRadius = 0;
        public const int MaxCornerRadius = 48;
        public const int MinPadding = 0;
        public const int MaxPadding = 48;
        public const double MinPipSize = 0.20;
        public const double MaxPipSize = 0.60;
        /// <summary>PiP frame width cap; the spec gives no range, the divider's 16 px is reused.</summary>
        public const int MinPipBorderPx = 0;
        public const int MaxPipBorderPx = 16;
        /// <summary>Zoom has no spec maximum; 8x is the chosen sensible cap.</summary>
        public const double MinZoom = 1.0;
        public const double MaxZoom = 8.0;
        public const double MinPan = -1.0;
        public const double MaxPan = 1.0;

        // §3.4 timeline
        public const double MinSpeed = 0.5;
        public const double MaxSpeed = 2.0;
        /// <summary>0 means "slow-mo off".</summary>
        public const double MinSlowMoSeconds = 0.0;
        /// <summary>Shortest active finale slow-mo (spec §3.4 "последние 1-3 с"); values in (0, 1) are raised to it.</summary>
        public const double MinActiveSlowMoSeconds = 1.0;
        public const double MaxSlowMoSeconds = 3.0;
        public const double MinSlowMoFactor = 0.25;
        public const double MaxSlowMoFactor = 0.5;
        /// <summary>Video track shift relative to the clip, ±180 s.</summary>
        public const long MaxOffsetUs = MaxDurationUs;

        // §3.5 texts
        public const int MaxTextLayers = 5;
        public const int MinTextSize = 12;
        public const int MaxTextSize = 160;
        public const int MinStrokePx = 0;
        public const int MaxStrokePx = 8;
        /// <summary>Chosen cap for one text layer (the spec gives none).</summary>
        public const int MaxTextLength = 200;

        // §3.6 audio
        public const double MinVolume = 0.0;
        public const double MaxVolume = 2.0;
        public const long MaxMusicFadeUs = 2 * MicrosPerSecond;

        // §3.9 project
        /// <summary>Chosen cap for a project name.</summary>
        public const int MaxNameLength = 100;
        /// <summary>Chosen cap of the export history; the oldest records are dropped first.</summary>
        public const int MaxExportRecords = 100;
        /// <summary>Max length of ids (project, text layer) and of tokens (font, template, mode id…).</summary>
        public const int MaxIdLength = 64;
        /// <summary>Max length of a relative path inside the project folder.</summary>
        public const int MaxPathLength = 255;

        /// <summary>The only aspect of 1.0 (spec §3.8: landscape and 1:1 come in 1.1).</summary>
        public const string Aspect916 = "9:16";
    }
}
