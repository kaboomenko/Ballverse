using System;
using Ballverse.Core.Layout;
using Ballverse.Core.Timeline;

namespace Ballverse.Core.Studio
{
    /// <summary>
    /// Internal resolution of the studio preview (spec §3.7 "предпросмотр понижает внутреннее разрешение, а не FPS", §8.2 P5):
    /// starts at full size (or one step down on tier C at 1080p60, <see cref="PreviewPolicy.ShouldReduceInternalResolution"/>),
    /// steps down when frames are missed for a while and back up after a long stable stretch. The output file is never
    /// affected. Pure; the caller reports every presented frame with its real duration.
    /// </summary>
    public sealed class PreviewQualityGovernor
    {
        /// <summary>Scale steps of the preview canvas.</summary>
        public static readonly float[] Steps = { 1f, 0.85f, 0.7f, 0.55f };
        /// <summary>Slow time needed before stepping down, ms.</summary>
        public const long DownAfterMs = 1500;
        /// <summary>Stable time needed before stepping up, ms.</summary>
        public const long UpAfterMs = 6000;

        readonly long _frameUs;
        long _slowUs;
        long _fastUs;
        double _avgUs;

        /// <param name="fps">Preview frame rate (= export fps).</param>
        /// <param name="startReduced">Start one step down (tier C, 1080p60).</param>
        public PreviewQualityGovernor(int fps, bool startReduced)
        {
            OutputFps.Validate(fps, nameof(fps));
            _frameUs = PreviewPolicy.MaxAvDriftUs(fps);
            StepIndex = startReduced ? 1 : 0;
        }

        public int StepIndex { get; private set; }

        /// <summary>Current scale of the preview canvas (1 = full).</summary>
        public float Scale => Steps[StepIndex];

        /// <summary>Incremented whenever <see cref="Scale"/> changes (the preview reallocates its targets).</summary>
        public int Version { get; private set; }

        /// <summary>Reports one presented frame that took <paramref name="frameUs"/> of real time. Returns true when the scale changed.</summary>
        public bool OnFrame(long frameUs)
        {
            if (frameUs <= 0) return false;
            if (frameUs > 250000) frameUs = 250000; // a hitch (app switch, GC) is not a trend
            _avgUs = _avgUs <= 0 ? frameUs : _avgUs + (frameUs - _avgUs) * 0.1;
            bool slow = _avgUs > _frameUs * 1.2;
            bool fast = _avgUs < _frameUs * 0.75;
            _slowUs = slow ? _slowUs + frameUs : 0;
            _fastUs = fast ? _fastUs + frameUs : 0;
            if (_slowUs >= DownAfterMs * 1000 && StepIndex < Steps.Length - 1)
            {
                StepIndex++;
                return Changed();
            }
            if (_fastUs >= UpAfterMs * 1000 && StepIndex > 0)
            {
                StepIndex--;
                return Changed();
            }
            return false;
        }

        /// <summary>Preview canvas for a display area of <paramref name="baseCanvas"/> (already 9:16) at the current scale: even sides, at least 8 px.</summary>
        public IntVec2 ScaleCanvas(IntVec2 baseCanvas)
        {
            return ScaleCanvas(baseCanvas, Scale);
        }

        /// <summary>Scales a 9:16 canvas, keeping the 9:16 shape (<see cref="OutputCanvas.FitPreview"/>) and even sides.</summary>
        public static IntVec2 ScaleCanvas(IntVec2 baseCanvas, float scale)
        {
            if (baseCanvas.X < OutputCanvas.MinSide || baseCanvas.Y < OutputCanvas.MinSide) throw new ArgumentOutOfRangeException(nameof(baseCanvas));
            int w = Math.Max(OutputCanvas.MinSide, (int)Math.Round(baseCanvas.X * (double)scale) & ~1);
            int h = Math.Max(OutputCanvas.MinSide, (int)Math.Round(baseCanvas.Y * (double)scale) & ~1);
            IntVec2 fit = OutputCanvas.FitPreview(w, h);
            return new IntVec2(Math.Max(OutputCanvas.MinSide, fit.X & ~1), Math.Max(OutputCanvas.MinSide, fit.Y & ~1));
        }

        bool Changed()
        {
            _slowUs = 0;
            _fastUs = 0;
            _avgUs = 0;
            Version++;
            return true;
        }
    }
}
