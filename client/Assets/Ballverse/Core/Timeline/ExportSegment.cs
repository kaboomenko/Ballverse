namespace Ballverse.Core.Timeline
{
    /// <summary>One 2-second export segment (spec §7.5 step f, §3.8 resume): frames [StartFrame, StartFrame + FrameCount), starting with an IDR frame.</summary>
    public readonly struct ExportSegment
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

        public override string ToString()
        {
            return "#" + Index + " [" + StartFrame + ", " + EndFrame + ")";
        }
    }
}
