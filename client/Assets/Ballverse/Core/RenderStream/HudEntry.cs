using System;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// One HUD value of a render frame (score, timer, counters…), keyed by a small mode-defined id (spec §7.4 "состояние HUD").
    /// </summary>
    public struct HudEntry : IEquatable<HudEntry>
    {
        /// <summary>Largest allowed <see cref="Key"/>.</summary>
        public const int MaxKey = 65535;

        /// <summary>
        /// Largest number of HUD entries in one frame. The spec's HUD is small (score, timer, a few counters, §7.4);
        /// the cap bounds the reader's per-frame merge cost, because unchanged HUD entries cost no bytes in a delta frame.
        /// Keys may still be sparse anywhere in [0, <see cref="MaxKey"/>].
        /// </summary>
        public const int MaxEntriesPerFrame = 256;

        /// <summary>HUD slot id in [0, <see cref="MaxKey"/>]; unique within a frame.</summary>
        public int Key;
        public int Value;

        public HudEntry(int key, int value)
        {
            Key = key;
            Value = value;
        }

        public bool Equals(HudEntry other)
        {
            return Key == other.Key && Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is HudEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (Key * 397) ^ Value;
        }

        public static bool operator ==(HudEntry a, HudEntry b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(HudEntry a, HudEntry b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return "hud[" + Key + "]=" + Value;
        }
    }
}
