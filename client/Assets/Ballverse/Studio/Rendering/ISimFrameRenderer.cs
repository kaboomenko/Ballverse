using System.Collections.Generic;
using Ballverse.Core.RenderStream;
using UnityEngine;

namespace Ballverse.Studio
{
    /// <summary>Quality profile the simulation is drawn with (spec §7.7 table).</summary>
    public enum StudioRenderQuality
    {
        /// <summary>Studio preview: the device tier's realtime profile (the renderer may lower it dynamically).</summary>
        Preview = 0,
        /// <summary>Export: full bloom, full trails, anti-aliasing, scale 1 of the output, independent of the tier.</summary>
        Export = 1,
    }

    /// <summary>The clip a renderer is about to draw (see <see cref="ISimFrameRenderer.BeginClip"/>).</summary>
    public sealed class SimClipInfo
    {
        public SimClipInfo(string modeId, RenderStreamHeader header, int tickCount, IReadOnlyList<ShapeDefinition> shapes,
            IReadOnlyList<string> labels, string renderStreamPath, string eventsPath, string replayPath)
        {
            ModeId = modeId;
            Header = header;
            TickCount = tickCount;
            Shapes = shapes;
            Labels = labels;
            RenderStreamPath = renderStreamPath;
            EventsPath = eventsPath;
            ReplayPath = replayPath;
        }

        /// <summary>Mode id ("ring_escape", "geometry_bounce", "helix_drop").</summary>
        public string ModeId { get; }
        /// <summary>Render stream header: quantization, tick rate, grid.</summary>
        public RenderStreamHeader Header { get; }
        public int TickCount { get; }
        /// <summary>Shape table (index = shape id).</summary>
        public IReadOnlyList<ShapeDefinition> Shapes { get; }
        /// <summary>Label table (index = label id; index 0 = no label, null).</summary>
        public IReadOnlyList<string> Labels { get; }
        /// <summary>Absolute path of <c>clip.bvs</c>: a renderer that needs trail history may open its own reader on it.</summary>
        public string RenderStreamPath { get; }
        /// <summary>Absolute path of the render event log <c>clip.bve</c> (VFX), or null when the clip has none.</summary>
        public string EventsPath { get; }
        /// <summary>Absolute path of <c>clip.bvr</c>.</summary>
        public string ReplayPath { get; }
    }

    /// <summary>Which tick a frame shows (see <see cref="ISimFrameRenderer.SetFrameTime"/>).</summary>
    public readonly struct SimFrameTime
    {
        public SimFrameTime(int tick, bool frozen, int iteration, int discontinuity, long outputUs)
        {
            Tick = tick;
            Frozen = frozen;
            Iteration = iteration;
            Discontinuity = discontinuity;
            OutputUs = outputUs;
        }

        /// <summary>Render-stream tick of the frame.</summary>
        public int Tick { get; }
        /// <summary>The tick is held as a still (cover, freeze after the end, pre-roll): trails and particles should rest.</summary>
        public bool Frozen { get; }
        /// <summary>Loop / slow-mo replay repetition (0 = first pass).</summary>
        public int Iteration { get; }
        /// <summary>Changes whenever the shown tick does not follow the previous frame (seek, loop wrap, replay start): drop history.</summary>
        public int Discontinuity { get; }
        /// <summary>Output time of the frame, µs.</summary>
        public long OutputUs { get; }
    }

    /// <summary>
    /// Rendering seam between the studio and the simulation renderer (Ballverse.Render, another assembly the studio does not
    /// reference). The App implements it on top of <c>BallverseRenderer</c> and hands it to <see cref="StudioServices"/>; preview and
    /// export call the same instance kind, so both draw identical simulation frames (spec §7.5 principle).
    /// <para>Call order per clip: <see cref="BeginClip"/> → for each frame <see cref="SetFrameTime"/> then <see cref="Render"/> →
    /// <see cref="EndClip"/>. Main thread only. <see cref="Render"/> draws the whole 9:16 arena into <c>target</c> (an sRGB
    /// RGBA8 render texture sized by <c>SimSlotSizing</c>); the studio crops / letterboxes it into the slot.</para>
    /// <para>Determinism contract: the picture of a tick must not depend on which frames were drawn before it. The export
    /// redraws a segment from its first frame after a pause or an encoder retry, and a resumed export starts mid-clip; history
    /// effects (trails, event-seeded particles) must therefore be rebuilt from the stream for the shown tick — e.g. Ballverse.Render's
    /// <c>RenderStreamFrameSource</c> on <see cref="SimClipInfo.RenderStreamPath"/> with <see cref="SimFrameTime.Tick"/>, which reads
    /// the history ticks itself — rather than accumulated across calls. <see cref="SimFrameTime.Discontinuity"/> tells when the
    /// accumulated state no longer applies.</para>
    /// </summary>
    public interface ISimFrameRenderer
    {
        /// <summary>A clip is opened for preview or export (one clip per renderer instance at a time).</summary>
        void BeginClip(SimClipInfo clip);

        /// <summary>Timing of the next <see cref="Render"/> call (tick, still or playing, history discontinuity).</summary>
        void SetFrameTime(SimFrameTime time);

        /// <summary>Draws <paramref name="frame"/> (already decoded from the render stream) into <paramref name="target"/>.</summary>
        void Render(RenderFrame frame, IReadOnlyList<ShapeDefinition> shapes, IReadOnlyList<string> labels, RenderTexture target, StudioRenderQuality quality);

        /// <summary>The clip is closed; release per-clip resources.</summary>
        void EndClip();
    }
}
