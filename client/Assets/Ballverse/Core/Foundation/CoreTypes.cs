namespace Ballverse.Core
{
    /// <summary>Device performance tier (spec §8.1).</summary>
    public enum DeviceTier
    {
        A = 0,
        B = 1,
        C = 2,
    }

    /// <summary>Global simulation constants (spec §2.1, §7.4).</summary>
    public static class SimConstants
    {
        /// <summary>Fixed physics step rate. Tick = one step of 1/60 s.</summary>
        public const int TickRate = 60;
        /// <summary>Hard cap of one run and of an exported video, seconds.</summary>
        public const int MaxDurationSeconds = 180;
        public const int MaxDurationTicks = MaxDurationSeconds * TickRate;
        /// <summary>State hash cadence in the replay HASH chunk.</summary>
        public const int HashIntervalTicks = 60;
        public const float ArenaWidth = 9f;
        public const float ArenaHeight = 16f;
        public const float SafeZoneWidth = 8f;
        public const float SafeZoneHeight = 12f;
    }

    // Studio enums shared by the project model (spec §7.4) and timeline math (spec §3.4).
    // Kept in Foundation so both modules use one definition.

    /// <summary>Split-screen layouts (spec §3.3).</summary>
    public enum LayoutType
    {
        /// <summary>Simulation top, video bottom.</summary>
        L1 = 1,
        /// <summary>Left / right.</summary>
        L2 = 2,
        /// <summary>Simulation full frame, video in a picture-in-picture window.</summary>
        L3 = 3,
        /// <summary>Simulation only.</summary>
        L4 = 4,
        /// <summary>Video full frame, simulation in a window.</summary>
        L5 = 5,
    }

    public enum SlotFill
    {
        Cover = 0,
        Fit = 1,
    }

    /// <summary>What the simulation slot shows after the clip ends while the video still plays (spec §3.4).</summary>
    public enum SimAfterEnd
    {
        Freeze = 0,
        SlowMoReplay = 1,
        Loop = 2,
        /// <summary>Keep simulating after the finale (requires the render stream to extend past the clip).</summary>
        ContinueSim = 3,
    }

    /// <summary>What the video slot shows after the user video ends while the clip still plays (spec §3.4).</summary>
    public enum VideoAfterEnd
    {
        Freeze = 0,
        Loop = 1,
        Black = 2,
        /// <summary>The simulation takes the whole frame.</summary>
        ExpandSim = 3,
    }

    /// <summary>How the total video duration is chosen (spec §3.4).</summary>
    public enum DurationMode
    {
        /// <summary>Equals the trimmed clip length.</summary>
        Auto = 0,
        /// <summary>Equals the trimmed user video length.</summary>
        MatchVideo = 1,
        Manual = 2,
    }

    public enum ExportResolution
    {
        P720 = 720,
        P1080 = 1080,
    }
}
