using System;
using System.Collections.Generic;
using Ballverse.Core.Audio;
using Ballverse.Core.Timeline;

namespace Ballverse.Core.Studio
{
    /// <summary>Random-access reader of interleaved stereo 48 kHz float PCM (a decoded user track in memory or in a PcmFile).</summary>
    public interface IPcmSource
    {
        /// <summary>Frames available.</summary>
        long FrameCount { get; }

        /// <summary>Copies frames starting at <paramref name="frame"/> into <paramref name="destination"/> (interleaved, even length);
        /// returns the frames copied (fewer at the end).</summary>
        int Read(long frame, Span<float> destination);
    }

    /// <summary><see cref="IPcmSource"/> over an interleaved stereo array.</summary>
    public sealed class ArrayPcmSource : IPcmSource
    {
        readonly float[] _samples;

        public ArrayPcmSource(float[] interleavedStereo)
        {
            _samples = interleavedStereo ?? throw new ArgumentNullException(nameof(interleavedStereo));
            if ((_samples.Length & 1) != 0) throw new ArgumentException("interleaved stereo length must be even", nameof(interleavedStereo));
        }

        public long FrameCount => _samples.Length / 2;

        public int Read(long frame, Span<float> destination)
        {
            if (frame < 0 || frame >= FrameCount) return 0;
            int n = (int)Math.Min(destination.Length / 2, FrameCount - frame);
            new ReadOnlySpan<float>(_samples, (int)(frame * 2), n * 2).CopyTo(destination);
            return n;
        }
    }

    /// <summary>One contiguous copy of source frames into the output (see <see cref="UserAudioPlacement.ComputeRuns"/>).</summary>
    public readonly struct PcmRun
    {
        public long OutputFrame { get; }
        public long SourceFrame { get; }
        public long Frames { get; }

        public PcmRun(long outputFrame, long sourceFrame, long frames)
        {
            OutputFrame = outputFrame;
            SourceFrame = sourceFrame;
            Frames = frames;
        }
    }

    /// <summary>
    /// Places the user video's sound on the output timeline (spec §3.6 "Звук видео пользователя", §3.7 "в экспорте — 0"): the
    /// track <see cref="Audio.Mastering.TrackMixer"/> expects starts at output frame 0, while the video itself starts at
    /// <see cref="StudioTimeline.VideoStartUs"/> (offset, cover lead-in, MatchVideo origin) and plays its trimmed range at 1×.
    /// The rules follow the picture of <see cref="StudioTimeline.SampleVideo"/>:
    /// <list type="bullet">
    /// <item>before the content (cover lead-in) and before the video starts (frozen first frame): silence;</item>
    /// <item>while the video plays: source audio from <c>trimIn + (t − videoStart)</c>;</item>
    /// <item>after its end: <see cref="VideoAfterEnd.Loop"/> repeats the trimmed range with the picture (period = trimmed length);
    /// Freeze, Black and ExpandSim are silent (a still picture has no sound).</item>
    /// </list>
    /// Sample positions are whole 48 kHz frames: output frame n ↔ t = n / 48000 s; the video start is floor(µs · 0.048).
    /// </summary>
    public static class UserAudioPlacement
    {
        const int ChunkFrames = 8192;

        /// <summary>
        /// The copies that place a source of <paramref name="sourceFrames"/> frames (decoded from <see cref="UserVideoTiming.TrimInUs"/>)
        /// into an output of <paramref name="outputFrames"/> frames, in output order. Empty without a video.
        /// </summary>
        public static IReadOnlyList<PcmRun> ComputeRuns(StudioTimeline timeline, long sourceFrames, long outputFrames)
        {
            if (timeline == null) throw new ArgumentNullException(nameof(timeline));
            var runs = new List<PcmRun>();
            UserVideoTiming video = timeline.Spec.Video;
            if (video == null || outputFrames <= 0) return runs;
            long period = AudioConstants.MicrosToFrames(video.LengthUs);
            long length = Math.Min(sourceFrames, period);
            if (length <= 0 || period <= 0) return runs;
            long contentStart = AudioConstants.MicrosToFrames(timeline.ContentStartUs);
            long videoStart = FloorFrames(timeline.VideoStartUs);
            bool loop = video.AfterEnd == VideoAfterEnd.Loop;
            long n = Math.Max(contentStart, Math.Max(0, videoStart));
            while (n < outputFrames)
            {
                long local = n - videoStart;
                long within = local;
                if (local >= period)
                {
                    if (!loop) break;
                    within = local % period;
                }
                if (within >= length)
                {
                    // Past the decoded audio inside this repetition (a source shorter than the trim): silence until the next one.
                    if (!loop) break;
                    n += period - within;
                    continue;
                }
                long run = Math.Min(outputFrames - n, length - within);
                runs.Add(new PcmRun(n, within, run));
                n += run;
            }
            return runs;
        }

        /// <summary>Writes the placed user track for the whole output into <paramref name="output"/> (interleaved stereo, cleared first).</summary>
        public static void Place(StudioTimeline timeline, IPcmSource source, Span<float> output)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if ((output.Length & 1) != 0) throw new ArgumentException("interleaved stereo length must be even", nameof(output));
            output.Clear();
            foreach (PcmRun r in ComputeRuns(timeline, source.FrameCount, output.Length / 2))
            {
                long done = 0;
                while (done < r.Frames)
                {
                    int want = (int)Math.Min(ChunkFrames, r.Frames - done);
                    Span<float> dst = output.Slice((int)((r.OutputFrame + done) * 2), want * 2);
                    int got = source.Read(r.SourceFrame + done, dst);
                    if (got <= 0) break;
                    done += got;
                }
            }
        }

        /// <summary>Array convenience of <see cref="Place(StudioTimeline, IPcmSource, Span{float})"/>.</summary>
        public static void Place(StudioTimeline timeline, float[] source, Span<float> output)
        {
            Place(timeline, new ArrayPcmSource(source ?? throw new ArgumentNullException(nameof(source))), output);
        }

        /// <summary>floor(µs · 48000 / 10⁶) for any sign.</summary>
        public static long FloorFrames(long micros)
        {
            return AudioConstants.MicrosToFrames(micros);
        }
    }
}
