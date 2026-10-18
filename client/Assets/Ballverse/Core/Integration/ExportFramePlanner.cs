using System;
using Ballverse.Core.Layout;
using Ballverse.Core.Timeline;

namespace Ballverse.Core.Integration
{
    /// <summary>
    /// Everything the Unity exporter needs for output frame i (spec §7.5 frame loop i = 0 … N−1, t = i / fps): what the
    /// simulation slot shows (render-stream tick, playing or frozen), what the video slot shows (source pts, frozen, black or
    /// gone), the composed geometry and the encoder bookkeeping. A plain struct: producing it allocates nothing.
    /// </summary>
    public readonly struct ExportFrame
    {
        public int Index { get; }
        /// <summary>Encoder presentation time, µs (<see cref="StudioTimeline.FrameTimeUs"/>).</summary>
        public long PtsUs { get; }
        /// <summary>Simulation slot: <see cref="SimSampleKind.Playing"/> or <see cref="SimSampleKind.Frozen"/> render-stream tick.</summary>
        public SimSample Sim { get; }
        /// <summary>Video slot: decode / freeze at a source pts, black, ExpandSim or no video.</summary>
        public VideoSample Video { get; }
        /// <summary>
        /// Geometry for this frame: the project's layout, or its L4 (simulation only) form while the video is in
        /// <see cref="VideoSampleKind.ExpandSim"/>. One of two plans precomputed by the planner (never null).
        /// </summary>
        public FramePlan Plan { get; }
        /// <summary>IDR frame (every 2 s, spec §3.8).</summary>
        public bool IsKeyframe { get; }
        /// <summary>2-second segment index (spec §7.5 resume).</summary>
        public int Segment { get; }
        /// <summary>First frame of its segment.</summary>
        public bool StartsSegment { get; }
        /// <summary>Part of the 0.1 s cover lead-in (spec §3.4).</summary>
        public bool IsCover { get; }

        public ExportFrame(int index, long ptsUs, SimSample sim, VideoSample video, FramePlan plan, bool isKeyframe, int segment,
            bool startsSegment, bool isCover)
        {
            Index = index;
            PtsUs = ptsUs;
            Sim = sim;
            Video = video;
            Plan = plan;
            IsKeyframe = isKeyframe;
            Segment = segment;
            StartsSegment = startsSegment;
            IsCover = isCover;
        }

        /// <summary>The video slot is drawn (the layout has one and the video is not replaced by the expanded simulation).</summary>
        public bool DrawsVideoSlot => Plan.Layout.HasVideo && Video.Kind != VideoSampleKind.ExpandSim;

        /// <summary>The video slot needs a decoded source frame at <see cref="VideoSample.SourcePtsUs"/>.</summary>
        public bool NeedsVideoFrame => DrawsVideoSlot && (Video.Kind == VideoSampleKind.Frame || Video.Kind == VideoSampleKind.Frozen);
    }

    /// <summary>
    /// Export plan helper joining Timeline, Layout and the export settings (spec §3.4, §3.8, §7.5): binds a compiled
    /// <see cref="StudioTimeline"/> to <see cref="ExportSettings"/> (<see cref="ExportPlan"/>), composes the frame geometry once
    /// (layout geometry does not depend on time, see <see cref="FrameComposer"/>) and then answers <see cref="GetFrame"/> for
    /// every frame without allocating.
    /// <para><see cref="VideoAfterEnd.ExpandSim"/> (layout open issue): while the timeline says ExpandSim the frame uses the
    /// layout's L4 form (simulation full frame). The watermark keeps the placement of the project's layout, so it never jumps
    /// corners mid-video when a PiP window disappears (its position stays clear: L4 only removes occupied areas).</para>
    /// Audio for the same export: <see cref="ExportSoundSchedule"/>. Immutable and thread-safe after construction.
    /// </summary>
    public sealed class ExportFramePlanner
    {
        readonly StudioTimeline _timeline;
        readonly ExportSettings _settings;
        readonly FramePlan _plan;
        readonly FramePlan _expandSimPlan;
        readonly int _gopFrames;
        readonly int _segmentFrames;

        /// <param name="timeline">Compiled timeline; its fps must equal the export fps.</param>
        /// <param name="settings">Resolution, fps and bitrates of the export.</param>
        /// <param name="request">Composition request of the project at the export canvas size (<see cref="OutputCanvas.ForExport"/>).</param>
        /// <exception cref="ArgumentException">fps or canvas size do not match the settings.</exception>
        public ExportFramePlanner(StudioTimeline timeline, ExportSettings settings, FrameRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            Plan = new ExportPlan(timeline, settings);
            if (request.CanvasWidth != settings.Width || request.CanvasHeight != settings.Height)
                throw new ArgumentException("Request canvas " + request.CanvasWidth + "x" + request.CanvasHeight + " differs from the export frame "
                    + settings.Width + "x" + settings.Height, nameof(request));
            _timeline = timeline;
            _settings = settings;
            _gopFrames = settings.GopFrames;
            _segmentFrames = settings.SegmentFrames;
            _plan = FrameComposer.Compose(request);
            if (_plan.Layout.HasVideo)
            {
                var l4 = new FrameRequest(request.CanvasWidth, request.CanvasHeight, request.Layout.WithType(LayoutType.L4),
                    request.VideoSourceSize, request.VideoContent, request.ShowWatermark, request.WatermarkLogoAspect, request.MaskRects,
                    request.TextBounds);
                FramePlan composed = FrameComposer.Compose(l4);
                _expandSimPlan = new FramePlan(composed.Layout, null, IntRect.Empty, _plan.HasWatermark, _plan.Watermark);
            }
            else
            {
                _expandSimPlan = _plan;
            }
        }

        /// <summary>The timeline bound to the export settings (frame count, segments, estimates, acceptance checks).</summary>
        public ExportPlan Plan { get; }

        /// <summary>N: frames to encode.</summary>
        public int FrameCount => _timeline.FrameCount;

        /// <summary>Geometry of the project's layout.</summary>
        public FramePlan LayoutPlan => _plan;

        /// <summary>Geometry while the video is in <see cref="VideoSampleKind.ExpandSim"/> (L4 form, same watermark).</summary>
        public FramePlan ExpandSimPlan => _expandSimPlan;

        /// <summary>Plan data of output frame <paramref name="frameIndex"/>; allocation-free.</summary>
        /// <exception cref="ArgumentOutOfRangeException">The index is outside [0, FrameCount).</exception>
        public ExportFrame GetFrame(int frameIndex)
        {
            if (frameIndex < 0 || frameIndex >= _timeline.FrameCount) throw new ArgumentOutOfRangeException(nameof(frameIndex));
            SimSample sim = _timeline.SampleSim(frameIndex);
            VideoSample video = _timeline.SampleVideo(frameIndex);
            FramePlan plan = video.Kind == VideoSampleKind.ExpandSim ? _expandSimPlan : _plan;
            int segment = frameIndex / _segmentFrames;
            return new ExportFrame(frameIndex, _timeline.FrameTimeUs(frameIndex), sim, video, plan, frameIndex % _gopFrames == 0, segment,
                frameIndex == segment * _segmentFrames, frameIndex < _timeline.CoverFrameCount);
        }

        /// <summary>The settings this planner was built for.</summary>
        public ExportSettings Settings => _settings;
    }
}
