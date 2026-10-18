using System;

namespace Ballverse.Core.Timeline
{
    /// <summary>
    /// Timing of the simulation clip S on the studio timeline (spec §3.4): trims, speed, finale slow-mo and
    /// the after-end behaviour. Immutable; all values are exact integers.
    /// <para>
    /// Ticks are simulation ticks at <see cref="SimConstants.TickRate"/>. The trimmed clip is the half-open
    /// range [<see cref="TrimInTick"/>, <see cref="TrimOutTick"/>): <c>trimOutTick</c> is exclusive, so the last
    /// clip tick is <c>TrimOutTick - 1</c> (spec §7.4 project example: 0..1800 = 30 s).
    /// </para>
    /// <para>
    /// Speed and slow-mo factor are stored in hundredths (percent) so the frame mapping is an exact rational
    /// (decision: 1.0x = 100). The finale slow-mo length is stored in clip ticks (1/60 s resolution).
    /// </para>
    /// </summary>
    public sealed class ClipTiming
    {
        /// <summary>Lowest clip speed, 0.5x (spec §3.4).</summary>
        public const int MinSpeedPercent = 50;
        /// <summary>Highest clip speed, 2x (spec §3.4).</summary>
        public const int MaxSpeedPercent = 200;
        public const int NormalSpeedPercent = 100;
        /// <summary>Strongest finale slow-mo, 0.25x (spec §3.4).</summary>
        public const int MinSlowMoFactorPercent = 25;
        /// <summary>Weakest finale slow-mo, 0.5x (spec §3.4).</summary>
        public const int MaxSlowMoFactorPercent = 50;
        /// <summary>Shortest finale slow-mo: 1 s of clip time (spec §3.4 "последние 1–3 с"). 0 disables the slow-mo.</summary>
        public const int MinSlowMoTicks = 1 * SimConstants.TickRate;
        /// <summary>Longest finale slow-mo: 3 s of clip time (spec §3.4). 0 disables the slow-mo.</summary>
        public const int MaxSlowMoTicks = 3 * SimConstants.TickRate;
        /// <summary>
        /// <see cref="SimAfterEnd.SlowMoReplay"/> replays the last 2 s of the clip (or the whole clip if shorter)
        /// (decision, spec §3.4 "замедленный повтор финала").
        /// </summary>
        public const int SlowMoReplayTicks = 2 * SimConstants.TickRate;
        /// <summary>
        /// Fastest speed of <see cref="SimAfterEnd.SlowMoReplay"/>, percent of real time. The replay runs at
        /// min(0.5x, the slowest rate at which the replayed ticks were first shown) so it is never faster than the finale
        /// the viewer just saw (decision, see <see cref="StudioTimeline.SampleSim"/>).
        /// </summary>
        public const int SlowMoReplaySpeedPercent = 50;

        /// <summary>First clip tick (inclusive).</summary>
        public int TrimInTick { get; }
        /// <summary>End of the clip (exclusive).</summary>
        public int TrimOutTick { get; }
        /// <summary>
        /// Length of the render stream in ticks: ticks [0, AvailableTicks) can be drawn. <see cref="SimAfterEnd.ContinueSim"/>
        /// plays ticks past <see cref="TrimOutTick"/> up to this bound. At most <see cref="SimConstants.MaxDurationTicks"/>
        /// (one run is capped at 180 s, spec §2.1).
        /// </summary>
        public int AvailableTicks { get; }
        /// <summary>Clip speed in hundredths, 50..200 (spec §3.4).</summary>
        public int SpeedPercent { get; }
        /// <summary>Requested finale slow-mo length in clip ticks: 0 (off) or 60..180 (1–3 s, spec §3.4).</summary>
        public int SlowMoTicks { get; }
        /// <summary>Finale slow-mo factor in hundredths, 25..50, multiplied with <see cref="SpeedPercent"/>.</summary>
        public int SlowMoFactorPercent { get; }
        public SimAfterEnd AfterEnd { get; }

        /// <summary>Trimmed clip length in ticks (always ≥ 1).</summary>
        public int LengthTicks => TrimOutTick - TrimInTick;

        /// <summary>
        /// Slow-mo length actually applied: <see cref="SlowMoTicks"/> clamped to the clip length, so a slow-mo longer
        /// than the clip slows down the whole clip.
        /// </summary>
        public int EffectiveSlowMoTicks => Math.Min(SlowMoTicks, LengthTicks);

        /// <summary>Last tick of the trimmed clip (shown by <see cref="SimAfterEnd.Freeze"/>).</summary>
        public int LastTick => TrimOutTick - 1;

        public ClipTiming(
            int trimInTick,
            int trimOutTick,
            int availableTicks,
            int speedPercent = NormalSpeedPercent,
            int slowMoTicks = 0,
            int slowMoFactorPercent = MaxSlowMoFactorPercent,
            SimAfterEnd afterEnd = SimAfterEnd.Freeze)
        {
            if (availableTicks < 1 || availableTicks > SimConstants.MaxDurationTicks)
                throw new ArgumentOutOfRangeException(nameof(availableTicks), availableTicks, "Render stream length must be 1..MaxDurationTicks.");
            if (trimInTick < 0)
                throw new ArgumentOutOfRangeException(nameof(trimInTick), trimInTick, "trimInTick must be >= 0.");
            if (trimOutTick <= trimInTick || trimOutTick > availableTicks)
                throw new ArgumentOutOfRangeException(nameof(trimOutTick), trimOutTick, "trimOutTick must be in (trimInTick, availableTicks].");
            if (speedPercent < MinSpeedPercent || speedPercent > MaxSpeedPercent)
                throw new ArgumentOutOfRangeException(nameof(speedPercent), speedPercent, "Speed must be 0.5x..2x (spec §3.4).");
            if (slowMoTicks != 0 && (slowMoTicks < MinSlowMoTicks || slowMoTicks > MaxSlowMoTicks))
                throw new ArgumentOutOfRangeException(nameof(slowMoTicks), slowMoTicks, "Finale slow-mo must be 0 (off) or 1..3 s (spec §3.4).");
            if (slowMoFactorPercent < MinSlowMoFactorPercent || slowMoFactorPercent > MaxSlowMoFactorPercent)
                throw new ArgumentOutOfRangeException(nameof(slowMoFactorPercent), slowMoFactorPercent, "Slow-mo factor must be 0.25..0.5 (spec §3.4).");
            if (!Enum.IsDefined(typeof(SimAfterEnd), afterEnd))
                throw new ArgumentOutOfRangeException(nameof(afterEnd), afterEnd, "Unknown SimAfterEnd.");

            TrimInTick = trimInTick;
            TrimOutTick = trimOutTick;
            AvailableTicks = availableTicks;
            SpeedPercent = speedPercent;
            SlowMoTicks = slowMoTicks;
            SlowMoFactorPercent = slowMoFactorPercent;
            AfterEnd = afterEnd;
        }

        /// <summary>
        /// Converts a ratio such as 1.25 (project JSON "speed", "factor") to hundredths, rounding half away from zero.
        /// Range checking is left to the constructor.
        /// </summary>
        public static int PercentFromRatio(double ratio)
        {
            if (double.IsNaN(ratio) || double.IsInfinity(ratio)) throw new ArgumentOutOfRangeException(nameof(ratio));
            return (int)Math.Round(ratio * 100.0, MidpointRounding.AwayFromZero);
        }

        /// <summary>Converts seconds (project JSON "finaleSlowMo.seconds") to ticks, rounding half away from zero.</summary>
        public static int TicksFromSeconds(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new ArgumentOutOfRangeException(nameof(seconds));
            return (int)Math.Round(seconds * SimConstants.TickRate, MidpointRounding.AwayFromZero);
        }
    }
}
