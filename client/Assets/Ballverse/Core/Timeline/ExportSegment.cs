using System;

namespace Ballverse.Core.Timeline
{
    /// <summary>One 2-second export segment (spec §7.5 step f, §3.8 resume): frames [StartFrame, StartFrame + FrameCount), starting with an IDR frame.</summary>
    public readonly struct ExportSegment : IEquatable<ExportSegment>
    {
        public int Index { get; }
        public int StartFrame { get; }
        public int FrameCount { get; }

        /// <summary>Exclusive end frame.</summary>
        public int EndFrame => StartFrame + FrameCount;

        public ExportSegment(int index, int startFrame, int frameCount)
        {
            Index = index;
            StartFrame = startFrame;
            FrameCount = frameCount;
        }

        public bool Equals(ExportSegment other)
        {
            return Index == other.Index && StartFrame == other.StartFrame && FrameCount == other.FrameCount;
        }

        public override bool Equals(object obj)
        {
            return obj is ExportSegment other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (Index * 397 ^ StartFrame) * 397 ^ FrameCount;
            }
        }

        public static bool operator ==(ExportSegment a, ExportSegment b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(ExportSegment a, ExportSegment b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return "#" + Index + " [" + StartFrame + ", " + EndFrame + ")";
        }
    }
}
