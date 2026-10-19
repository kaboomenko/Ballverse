using System;
using Ballverse.Core.Layout;
using Ballverse.Core.Timeline;

namespace Ballverse.Core.Studio
{
    /// <summary>
    /// The two frame geometries of a project at one canvas size (spec §7.5 "one compositor"): the project's layout and its L4 form
    /// used while the video is in <see cref="VideoSampleKind.ExpandSim"/>, with the watermark kept where the layout put it — the
    /// same rule as <see cref="Integration.ExportFramePlanner"/>, for canvases that are not an export resolution (the preview).
    /// Immutable.
    /// </summary>
    public sealed class StudioFramePlans
    {
        StudioFramePlans(FrameRequest request, FramePlan layoutPlan, FramePlan expandSimPlan)
        {
            Request = request;
            LayoutPlan = layoutPlan;
            ExpandSimPlan = expandSimPlan;
        }

        public FrameRequest Request { get; }
        public FramePlan LayoutPlan { get; }
        public FramePlan ExpandSimPlan { get; }

        /// <summary>Composes both plans for <paramref name="request"/>.</summary>
        public static StudioFramePlans Compose(FrameRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            FramePlan plan = FrameComposer.Compose(request);
            FramePlan expand = plan;
            if (plan.Layout.HasVideo)
            {
                var l4 = new FrameRequest(request.CanvasWidth, request.CanvasHeight, request.Layout.WithType(LayoutType.L4),
                    request.VideoSourceSize, request.VideoContent, request.ShowWatermark, request.WatermarkLogoAspect, request.MaskRects,
                    request.TextBounds);
                FramePlan composed = FrameComposer.Compose(l4);
                expand = new FramePlan(composed.Layout, null, IntRect.Empty, plan.HasWatermark, plan.Watermark);
            }
            return new StudioFramePlans(request, plan, expand);
        }

        /// <summary>The plan to draw for a video sample.</summary>
        public FramePlan For(VideoSample video)
        {
            return video.Kind == VideoSampleKind.ExpandSim ? ExpandSimPlan : LayoutPlan;
        }

        /// <summary>True when the frame draws the video slot (layout has one and the video is not replaced by the expanded simulation).</summary>
        public static bool DrawsVideoSlot(FramePlan plan, VideoSample video)
        {
            return plan.Layout.HasVideo && video.Kind != VideoSampleKind.ExpandSim;
        }

        /// <summary>True when the frame needs a decoded source frame.</summary>
        public static bool NeedsVideoFrame(FramePlan plan, VideoSample video)
        {
            return DrawsVideoSlot(plan, video) && (video.Kind == VideoSampleKind.Frame || video.Kind == VideoSampleKind.Frozen);
        }
    }
}
