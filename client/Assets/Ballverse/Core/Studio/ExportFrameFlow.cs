using System;

namespace Ballverse.Core.Studio
{
    /// <summary>
    /// Bookkeeping of the export frame loop (spec §7.5 steps a–f): frames are rendered in order, read back asynchronously (at most
    /// <see cref="MaxInFlight"/> in flight — back-pressure) and appended to the segment writer in the same order; a segment is
    /// finished once all its frames were appended. A pause, an encoder retry or a resume restarts the current segment from its
    /// first frame (the half-written segment file is discarded). Pure, single-threaded; the Unity exporter calls the
    /// <c>On…</c> methods and asks <see cref="CanRender"/> / <see cref="SegmentComplete"/>.
    /// </summary>
    public sealed class ExportFrameFlow
    {
        /// <summary>Spec §7.5: at most 3 frames in flight between render and encoder.</summary>
        public const int DefaultMaxInFlight = 3;

        readonly int _frameCount;
        readonly int _segmentFrames;
        readonly int _maxInFlight;

        /// <param name="frameCount">N, frames of the export plan.</param>
        /// <param name="segmentFrames">Frames of a full 2-second segment.</param>
        /// <param name="completedSegments">Segments already finished (resume); the loop starts at the next one.</param>
        /// <param name="maxInFlight">Readbacks in flight (1–8).</param>
        public ExportFrameFlow(int frameCount, int segmentFrames, int completedSegments = 0, int maxInFlight = DefaultMaxInFlight)
        {
            if (frameCount < 1) throw new ArgumentOutOfRangeException(nameof(frameCount));
            if (segmentFrames < 1) throw new ArgumentOutOfRangeException(nameof(segmentFrames));
            if (maxInFlight < 1 || maxInFlight > 8) throw new ArgumentOutOfRangeException(nameof(maxInFlight));
            int segments = (frameCount + segmentFrames - 1) / segmentFrames;
            if (completedSegments < 0 || completedSegments > segments) throw new ArgumentOutOfRangeException(nameof(completedSegments));
            _frameCount = frameCount;
            _segmentFrames = segmentFrames;
            _maxInFlight = maxInFlight;
            SegmentCount = segments;
            Segment = completedSegments;
            ResetSegment();
        }

        public int FrameCount => _frameCount;
        public int SegmentCount { get; }
        public int MaxInFlight => _maxInFlight;

        /// <summary>Index of the segment being written (= <see cref="SegmentCount"/> when all are done).</summary>
        public int Segment { get; private set; }

        /// <summary>First frame of the current segment.</summary>
        public int SegmentStart => Math.Min(_frameCount, Segment * _segmentFrames);

        /// <summary>End (exclusive) of the current segment.</summary>
        public int SegmentEnd => Math.Min(_frameCount, SegmentStart + _segmentFrames);

        /// <summary>Next frame to render.</summary>
        public int NextRender { get; private set; }

        /// <summary>Next frame to hand to the writer (frames before it are appended).</summary>
        public int NextAppend { get; private set; }

        /// <summary>Frames rendered but not appended yet (readback or encoder queue).</summary>
        public int InFlight => NextRender - NextAppend;

        /// <summary>All segments finished.</summary>
        public bool IsDone => Segment >= SegmentCount;

        /// <summary>Frames inside finished segments plus frames appended in the current one (for the progress bar).</summary>
        public int FramesAppendedTotal => IsDone ? _frameCount : NextAppend;

        /// <summary>True when the next frame of the current segment may be rendered now (in-flight limit not reached).</summary>
        public bool CanRender => !IsDone && NextRender < SegmentEnd && InFlight < _maxInFlight;

        /// <summary>True when every frame of the current segment was appended: finalize its writer, then <see cref="OnSegmentFinished"/>.</summary>
        public bool SegmentComplete => !IsDone && NextAppend == SegmentEnd;

        /// <summary>True when the current frame to render starts its segment (a new segment writer must be open first).</summary>
        public bool AtSegmentStart => !IsDone && NextRender == SegmentStart;

        /// <summary>A frame was rendered and its readback requested; returns its index.</summary>
        public int OnRendered()
        {
            if (!CanRender) throw new InvalidOperationException("cannot render now (in-flight limit or segment end)");
            return NextRender++;
        }

        /// <summary>The oldest in-flight frame was accepted by the writer; returns its index.</summary>
        public int OnAppended()
        {
            if (InFlight <= 0) throw new InvalidOperationException("no frame in flight");
            return NextAppend++;
        }

        /// <summary>The current segment's file is finalized and recorded; moves to the next segment.</summary>
        public void OnSegmentFinished()
        {
            if (!SegmentComplete) throw new InvalidOperationException("segment not complete");
            Segment++;
            ResetSegment();
        }

        /// <summary>Discards the frames of the current segment (pause, encoder retry): it is rendered again from its first frame.</summary>
        public void RestartSegment()
        {
            if (IsDone) return;
            ResetSegment();
        }

        void ResetSegment()
        {
            NextRender = SegmentStart;
            NextAppend = SegmentStart;
        }
    }
}
