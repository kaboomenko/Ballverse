using System;
using System.Diagnostics;

namespace Ballverse.Services
{
    /// <summary>
    /// Time source of the services. Core policies take unix milliseconds as parameters (spec §7.4); the services read them
    /// here so tests can drive time with <see cref="ManualClock"/>.
    /// </summary>
    public interface IClock
    {
        /// <summary>Wall-clock time, unix milliseconds (persisted timestamps, Core ad policies, entitlement cache).</summary>
        long UnixTimeMs { get; }

        /// <summary>Monotonic milliseconds since an arbitrary origin (timeouts, backoff). Never goes backwards.</summary>
        long MonotonicMs { get; }
    }

    /// <summary>The device clock.</summary>
    public sealed class SystemClock : IClock
    {
        static readonly Stopwatch s_monotonic = Stopwatch.StartNew();

        public static readonly SystemClock Instance = new SystemClock();

        public long UnixTimeMs => Math.Max(0L, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        public long MonotonicMs => s_monotonic.ElapsedMilliseconds;
    }

    /// <summary>A clock moved by hand (tests, editor simulations).</summary>
    public sealed class ManualClock : IClock
    {
        public ManualClock(long unixTimeMs = 1_760_000_000_000L, long monotonicMs = 0L)
        {
            UnixTimeMs = unixTimeMs;
            MonotonicMs = monotonicMs;
        }

        public long UnixTimeMs { get; set; }

        public long MonotonicMs { get; set; }

        /// <summary>Moves both clocks forward by <paramref name="ms"/>.</summary>
        public void Advance(long ms)
        {
            if (ms < 0) throw new ArgumentOutOfRangeException(nameof(ms));
            UnixTimeMs += ms;
            MonotonicMs += ms;
        }
    }
}
