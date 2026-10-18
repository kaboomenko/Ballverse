using System;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>
    /// Random access to the render frames of past ticks. Trails and camera smoothing are functions of the last few
    /// seconds of the stream (not of what happened to be rendered), so preview, scrubbing and export draw the same picture
    /// (spec §7.4 "превью = экспорт"). Implemented over a <see cref="RenderStreamReader"/> (studio, export) or by
    /// <see cref="RenderFrameRing"/> (live run).
    /// </summary>
    public interface IRenderFrameSource
    {
        /// <summary>Copies the frame of <paramref name="tick"/> into <paramref name="destination"/>; false when unavailable.</summary>
        bool TryReadFrame(int tick, RenderFrame destination);
    }

    /// <summary><see cref="IRenderFrameSource"/> over a finished render stream.</summary>
    public sealed class RenderStreamFrameSource : IRenderFrameSource
    {
        readonly RenderStreamReader _reader;

        public RenderStreamFrameSource(RenderStreamReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
        }

        public bool TryReadFrame(int tick, RenderFrame destination)
        {
            if (tick < 0 || tick >= _reader.TickCount) return false;
            _reader.ReadFrame(tick, destination);
            return true;
        }
    }

    /// <summary>
    /// The last <see cref="Capacity"/> frames of a live run, pushed once per simulation tick (copies, so the sim may reuse
    /// its frame). Allocation-free once every slot reached its peak body count.
    /// </summary>
    public sealed class RenderFrameRing : IRenderFrameSource
    {
        readonly RenderFrame[] _frames;
        readonly int[] _ticks;

        public RenderFrameRing(int capacity)
        {
            if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
            _frames = new RenderFrame[capacity];
            _ticks = new int[capacity];
            for (int i = 0; i < capacity; i++)
            {
                _frames[i] = new RenderFrame();
                _ticks[i] = -1;
            }
        }

        public int Capacity => _frames.Length;

        /// <summary>Newest pushed tick, or −1.</summary>
        public int LatestTick { get; private set; } = -1;

        /// <summary>Stores a copy of the frame of <paramref name="tick"/>. A tick at or before the latest one restarts the ring.</summary>
        public void Push(int tick, RenderFrame frame)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (tick < 0) throw new ArgumentOutOfRangeException(nameof(tick));
            if (tick <= LatestTick) Clear();
            int slot = tick % _frames.Length;
            _frames[slot].CopyFrom(frame);
            _ticks[slot] = tick;
            LatestTick = tick;
        }

        public void Clear()
        {
            for (int i = 0; i < _ticks.Length; i++) _ticks[i] = -1;
            LatestTick = -1;
        }

        public bool TryReadFrame(int tick, RenderFrame destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (tick < 0) return false;
            int slot = tick % _frames.Length;
            if (_ticks[slot] != tick) return false;
            destination.CopyFrom(_frames[slot]);
            return true;
        }
    }
}
