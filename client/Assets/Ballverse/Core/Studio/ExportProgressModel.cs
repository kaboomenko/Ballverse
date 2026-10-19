using System;
using System.Collections.Generic;

namespace Ballverse.Core.Studio
{
    /// <summary>Stage of a running export (spec §7.5 pipeline: preparation, frame loop, finalization).</summary>
    public enum ExportPhase
    {
        Preflight = 0,
        /// <summary>Waiting for the rewarded gate (spec §3.8 step 2).</summary>
        Gate = 1,
        /// <summary>Audio decode and offline mix (up to 10 % of the progress bar).</summary>
        Audio = 2,
        /// <summary>Frame loop: render, compose, read back, encode.</summary>
        Frames = 3,
        /// <summary>Join of the segments, AAC, fast start and verification.</summary>
        Finalize = 4,
        /// <summary>Saving to the gallery.</summary>
        Saving = 5,
        Done = 6,
    }

    /// <summary>
    /// Progress bar and remaining-time estimate of an export (spec §3.8 "процентом, оставшимся временем"). Weights: preparation
    /// (preflight, audio) 10 %, frames 85 %, finalization 5 %. The time estimate uses the frame rate over the last few seconds of
    /// rendering (pauses excluded by the caller not calling <see cref="OnFrames"/>), plus the finalization share; before enough
    /// frames are done it falls back to the caller's a-priori estimate (<see cref="Timeline.ExportSettings.EstimateExportTimeMs"/>).
    /// Pure: the caller passes a monotonic millisecond clock.
    /// </summary>
    public sealed class ExportProgressModel
    {
        public const double PrepareWeight = 0.10;
        public const double FramesWeight = 0.85;
        public const double FinalizeWeight = 0.05;
        /// <summary>Rate window, ms.</summary>
        public const long WindowMs = 3000;
        /// <summary>Frames needed before the measured rate replaces the a-priori estimate.</summary>
        public const int MinFramesForRate = 10;

        readonly int _frameCount;
        readonly long _aprioriMs;
        readonly Queue<KeyValuePair<long, int>> _samples = new Queue<KeyValuePair<long, int>>();
        int _framesDone;
        int _framesThisRun;

        /// <param name="frameCount">N, frames of the export plan.</param>
        /// <param name="framesAlreadyDone">Frames of segments finished before a resume.</param>
        /// <param name="aprioriTotalMs">Whole-export time estimate for the export sheet (spec §3.8 table), used until frames are measured.</param>
        public ExportProgressModel(int frameCount, int framesAlreadyDone, long aprioriTotalMs)
        {
            if (frameCount < 1) throw new ArgumentOutOfRangeException(nameof(frameCount));
            _frameCount = frameCount;
            _framesDone = Math.Max(0, Math.Min(frameCount, framesAlreadyDone));
            _aprioriMs = Math.Max(0, aprioriTotalMs);
            Phase = ExportPhase.Preflight;
        }

        public ExportPhase Phase { get; private set; }

        /// <summary>Progress inside the current non-frame phase, 0..1.</summary>
        public double PhaseFraction { get; private set; }

        public int FramesDone => _framesDone;
        public int FrameCount => _frameCount;

        /// <summary>Enters a phase (frames keep their count).</summary>
        public void SetPhase(ExportPhase phase, double fraction = 0)
        {
            Phase = phase;
            PhaseFraction = Clamp01(fraction);
        }

        /// <summary>Total frames encoded (or written into finished segments) so far at <paramref name="nowMs"/>.</summary>
        public void OnFrames(int framesDone, long nowMs)
        {
            int clamped = Math.Max(0, Math.Min(_frameCount, framesDone));
            if (clamped > _framesDone) _framesThisRun += clamped - _framesDone;
            _framesDone = clamped;
            _samples.Enqueue(new KeyValuePair<long, int>(nowMs, _framesDone));
            while (_samples.Count > 2 && nowMs - _samples.Peek().Key > WindowMs) _samples.Dequeue();
        }

        /// <summary>A pause or segment restart: the rate window starts over (frames may also go back to a segment start).</summary>
        public void ResetRate(int framesDone)
        {
            _samples.Clear();
            _framesDone = Math.Max(0, Math.Min(_frameCount, framesDone));
        }

        /// <summary>Overall progress 0..1 for the bar.</summary>
        public double Fraction
        {
            get
            {
                double frames = (double)_framesDone / _frameCount;
                switch (Phase)
                {
                    case ExportPhase.Preflight:
                    case ExportPhase.Gate:
                        return 0;
                    case ExportPhase.Audio:
                        return PrepareWeight * PhaseFraction;
                    case ExportPhase.Frames:
                        return PrepareWeight + FramesWeight * frames;
                    case ExportPhase.Finalize:
                        return PrepareWeight + FramesWeight + FinalizeWeight * PhaseFraction;
                    default:
                        return 1;
                }
            }
        }

        /// <summary>Measured frames per second over the window, or null before <see cref="MinFramesForRate"/> frames.</summary>
        public double? FramesPerSecond
        {
            get
            {
                if (_samples.Count < 2 || _framesThisRun < MinFramesForRate) return null;
                KeyValuePair<long, int> first = _samples.Peek();
                KeyValuePair<long, int> last = default(KeyValuePair<long, int>);
                foreach (var s in _samples) last = s;
                long dt = last.Key - first.Key;
                int df = last.Value - first.Value;
                if (dt <= 0 || df <= 0) return null;
                return df * 1000.0 / dt;
            }
        }

        /// <summary>Remaining time, ms; null when it cannot be estimated (no rate and no a-priori estimate).</summary>
        public long? RemainingMs
        {
            get
            {
                if (Phase == ExportPhase.Done) return 0;
                double? fps = FramesPerSecond;
                int left = _frameCount - _framesDone;
                if (fps.HasValue)
                {
                    double framesMs = left * 1000.0 / fps.Value;
                    double totalFramesMs = _frameCount * 1000.0 / fps.Value;
                    double finalizeMs = Phase == ExportPhase.Finalize
                        ? totalFramesMs * (FinalizeWeight / FramesWeight) * (1 - PhaseFraction)
                        : totalFramesMs * (FinalizeWeight / FramesWeight);
                    return (long)Math.Ceiling(framesMs + finalizeMs);
                }
                if (_aprioriMs <= 0) return null;
                return (long)Math.Ceiling(_aprioriMs * (1 - Fraction));
            }
        }

        static double Clamp01(double v)
        {
            if (double.IsNaN(v) || v < 0) return 0;
            return v > 1 ? 1 : v;
        }
    }
}
