using System;

namespace Ballverse.Core.Timeline
{
    /// <summary>What the video slot shows on one output frame (spec §3.4, §7.5 step b).</summary>
    public enum VideoSampleKind
    {
        /// <summary>Decode the source frame at <see cref="VideoSample.SourcePtsUs"/>; the video is playing.</summary>
        Frame = 0,
        /// <summary>Show the source frame at <see cref="VideoSample.SourcePtsUs"/> as a still.</summary>
        Frozen = 1,
        /// <summary>Black slot (<see cref="VideoAfterEnd.Black"/>).</summary>
        Black = 2,
        /// <summary>The video slot disappears and the simulation takes the whole frame (<see cref="VideoAfterEnd.ExpandSim"/>, layout only).</summary>
        ExpandSim = 3,
        /// <summary>The project has no user video.</summary>
        NoVideo = 4,
    }

    /// <summary>Result of <see cref="StudioTimeline.SampleVideo"/>.</summary>
    public readonly struct VideoSample : IEquatable<VideoSample>
    {
        public VideoSampleKind Kind { get; }
        /// <summary>Source presentation time, µs (meaningful for <see cref="VideoSampleKind.Frame"/> and <see cref="VideoSampleKind.Frozen"/>, else 0).</summary>
        public long SourcePtsUs { get; }
        /// <summary>0 for the first pass; k ≥ 1 for the k-th <see cref="VideoAfterEnd.Loop"/> repetition. Pts never decrease within one iteration.</summary>
        public int Iteration { get; }

        public VideoSample(VideoSampleKind kind, long sourcePtsUs, int iteration)
        {
            Kind = kind;
            SourcePtsUs = sourcePtsUs;
            Iteration = iteration;
        }

        public static VideoSample Frame(long sourcePtsUs, int iteration = 0)
        {
            return new VideoSample(VideoSampleKind.Frame, sourcePtsUs, iteration);
        }

        public static VideoSample Frozen(long sourcePtsUs)
        {
            return new VideoSample(VideoSampleKind.Frozen, sourcePtsUs, 0);
        }

        public static readonly VideoSample Black = new VideoSample(VideoSampleKind.Black, 0, 0);
        public static readonly VideoSample ExpandSim = new VideoSample(VideoSampleKind.ExpandSim, 0, 0);
        public static readonly VideoSample NoVideo = new VideoSample(VideoSampleKind.NoVideo, 0, 0);

        public bool Equals(VideoSample other)
        {
            return Kind == other.Kind && SourcePtsUs == other.SourcePtsUs && Iteration == other.Iteration;
        }

        public override bool Equals(object obj)
        {
            return obj is VideoSample other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Kind * 397 ^ SourcePtsUs.GetHashCode()) * 397 ^ Iteration;
            }
        }

        public static bool operator ==(VideoSample a, VideoSample b)
        {
            return a.Equals(b);
        }

        public static bool operator !=(VideoSample a, VideoSample b)
        {
            return !a.Equals(b);
        }

        public override string ToString()
        {
            return Kind + "(" + SourcePtsUs + ", it " + Iteration + ")";
        }
    }
}
