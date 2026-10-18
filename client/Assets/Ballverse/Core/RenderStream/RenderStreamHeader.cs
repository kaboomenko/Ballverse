using System;
using System.Text;

namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Header of a .bvs render stream (spec §7.4 "Поток рендера: формат"): tick rate, keyframe interval,
    /// quantization parameters and the mode that produced the stream. Immutable.
    /// </summary>
    public sealed class RenderStreamHeader
    {
        /// <summary>Format version written by this code. Readers reject other versions.</summary>
        public const int CurrentFormatVersion = 1;
        /// <summary>Default keyframe interval: one block per second at 60 Hz (spec §7.4 "блоки по 1 с").</summary>
        public const int DefaultBlockTicks = 60;
        public const int MaxTickRate = 1000;
        public const int MaxBlockTicks = 3600;
        /// <summary>Maximum UTF-8 byte length of <see cref="ModeId"/>.</summary>
        public const int MaxModeIdBytes = 64;

        /// <summary>Simulation ticks per second (spec §7.4 rule 1: 60).</summary>
        public int TickRate { get; }
        /// <summary>Ticks per block; every block starts with a keyframe. Defaults to one second.</summary>
        public int BlockTicks { get; }
        public RenderQuantization Quantization { get; }
        /// <summary>Stable mode string id (e.g. "ring_escape", see <see cref="ModeInfo.Id"/>); may be empty.</summary>
        public string ModeId { get; }

        public RenderStreamHeader(string modeId)
            : this(modeId, SimConstants.TickRate, DefaultBlockTicks, RenderQuantization.Default)
        {
        }

        public RenderStreamHeader(string modeId, int tickRate, int blockTicks, RenderQuantization quantization)
        {
            if (modeId == null) throw new ArgumentNullException(nameof(modeId));
            if (Encoding.UTF8.GetByteCount(modeId) > MaxModeIdBytes) throw new ArgumentException("modeId longer than " + MaxModeIdBytes + " UTF-8 bytes", nameof(modeId));
            if (tickRate < 1 || tickRate > MaxTickRate) throw new ArgumentOutOfRangeException(nameof(tickRate));
            if (blockTicks < 1 || blockTicks > MaxBlockTicks) throw new ArgumentOutOfRangeException(nameof(blockTicks));
            ModeId = modeId;
            TickRate = tickRate;
            BlockTicks = blockTicks;
            Quantization = quantization ?? throw new ArgumentNullException(nameof(quantization));
        }
    }
}
