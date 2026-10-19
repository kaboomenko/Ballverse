using System;
using Ballverse.Core.Audio;

namespace Ballverse.Core.Modes
{
    /// <summary>
    /// Mode-side priority for the mixer's overall limit (spec §2.7: at most 40 sounds per second). The mixer refuses a sound
    /// when 40 sounds were accepted in the last second, whatever their importance, so in a crowded scene a stream of hits can
    /// leave no room for a break or a finale. A mode that wants its key sounds heard counts what it emits over a sliding window
    /// of <see cref="WindowTicks"/> ticks and emits a lower-priority sound only while the count is below that priority's cap
    /// (e.g. hits ≤ 32, breaks ≤ 39, finale ≤ 40). Because the mixer counts only sounds it accepted, never more than the mode
    /// emitted, a sound the budget lets through at cap 40 is never refused by the mixer's 40/s window at playback speed 1× or
    /// slower (the export, and the live view at timeScale ≤ 1). At faster playback the output second holds more ticks, so the
    /// mixer can still refuse; true priority there needs the Audio owner (open issue).
    /// <para>
    /// Dropped sounds must be dropped from the sound list and the EVNT records alike (the export rebuilds sound from EVNT), and
    /// the decision must not depend on which sinks are attached. The budget depends only on the sequence of (tick, cap) calls,
    /// so it is deterministic and re-simulation reproduces it. Allocation-free after construction.
    /// </para>
    /// </summary>
    public sealed class ModeSoundBudget
    {
        /// <summary>The mixer's window in ticks: 1 s (<see cref="AudioConstants.RateWindowFrames"/> = 60 ticks × 800 frames).</summary>
        public const int WindowTicks = SimConstants.TickRate;

        /// <summary>The mixer's overall cap per window.</summary>
        public const int MaxPerWindow = AudioConstants.MaxSoundsPerWindow;

        readonly int[] _perTick = new int[WindowTicks];
        int _sum;
        int _tick = int.MinValue;

        /// <summary>Sounds emitted in the window ending at the last tick passed in.</summary>
        public int Count => _sum;

        /// <summary>
        /// True (and counts the sound) when fewer than <paramref name="cap"/> sounds were emitted in ticks
        /// <c>tick − 59 … tick</c>. Ticks must not decrease (an earlier tick is treated as the latest one seen).
        /// </summary>
        public bool TryTake(int tick, int cap)
        {
            if (cap > MaxPerWindow) cap = MaxPerWindow;
            Advance(tick);
            if (_sum >= cap) return false;
            _perTick[Slot(_tick)]++;
            _sum++;
            return true;
        }

        public void Reset()
        {
            Array.Clear(_perTick, 0, _perTick.Length);
            _sum = 0;
            _tick = int.MinValue;
        }

        void Advance(int tick)
        {
            if (_tick == int.MinValue)
            {
                _tick = tick;
                return;
            }
            if (tick <= _tick) return;
            if ((long)tick - _tick >= WindowTicks)
            {
                Array.Clear(_perTick, 0, _perTick.Length);
                _sum = 0;
            }
            else
            {
                for (int t = _tick + 1; t <= tick; t++)
                {
                    int slot = Slot(t);
                    _sum -= _perTick[slot];
                    _perTick[slot] = 0;
                }
            }
            _tick = tick;
        }

        static int Slot(int tick)
        {
            int r = tick % WindowTicks;
            return r < 0 ? r + WindowTicks : r;
        }
    }
}
