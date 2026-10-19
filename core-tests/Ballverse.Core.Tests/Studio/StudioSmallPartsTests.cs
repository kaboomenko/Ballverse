using System;
using Ballverse.Core.Audio;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Studio
{
    public class StudioFilesAndNamingTests
    {
        [Fact]
        public void PathsAreSafeAndPadded()
        {
            Assert.Equal("seg_0007.mp4", StudioFiles.SegmentFileName(7));
            Assert.Equal("j1/segments/seg_0012.mp4", StudioFiles.SegmentPath("j1", 12));
            Assert.Equal("j1/segments/seg_0012_r2.mp4", StudioFiles.SegmentPath("j1", 12, 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => StudioFiles.SegmentFileName(0, -1));
            Assert.Equal("j1/mix.pcm", StudioFiles.JobFile("j1", StudioFiles.MixPcmFile));
            Assert.Equal("runs/r", StudioFiles.RunDir("r"));
            Assert.Equal("clips/c", StudioFiles.ClipDir("c"));
            Assert.Throws<ArgumentException>(() => StudioFiles.RunDir("a/b"));
            Assert.Throws<ArgumentException>(() => StudioFiles.JobFile("j", "../x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => StudioFiles.SegmentFileName(-1));
        }

        [Fact]
        public void ExportNamesAreAsciiSlugs()
        {
            Assert.Equal("Ballverse_Ring_Escape_12_20261018_120600.mp4", ExportFileNaming.FileName("Ring Escape 12", "ring_escape", "2026-10-18T12:06:00Z"));
            Assert.Equal("Ballverse_ring_escape_20261018_120600.mp4", ExportFileNaming.FileName("Кто выживет?", "ring_escape", "2026-10-18T12:06:00Z"));
            Assert.Equal("Ballverse_video_00000000_000000.mp4", ExportFileNaming.FileName("", null, null));
            Assert.Equal("a_b-c", ExportFileNaming.Slug("  a /// b-c__ "));
            Assert.Equal(ExportFileNaming.MaxNameLength, ExportFileNaming.Slug(new string('x', 100)).Length);
            Assert.True(ProjectPaths.IsSafeMediaPath("exports/" + ExportFileNaming.FileName("A.B", "m", "2026")));
        }
    }

    public class ExportProgressModelTests
    {
        [Fact]
        public void PhasesMapToTheBar()
        {
            var p = new ExportProgressModel(1000, 0, 30000);
            Assert.Equal(0, p.Fraction);
            p.SetPhase(ExportPhase.Audio, 0.5);
            Assert.Equal(0.05, p.Fraction, 6);
            p.SetPhase(ExportPhase.Frames);
            p.OnFrames(500, 0);
            Assert.Equal(0.10 + 0.85 * 0.5, p.Fraction, 6);
            p.SetPhase(ExportPhase.Finalize, 0.5);
            Assert.Equal(0.975, p.Fraction, 6);
            p.SetPhase(ExportPhase.Done);
            Assert.Equal(1, p.Fraction);
            Assert.Equal(0, p.RemainingMs);
        }

        [Fact]
        public void RemainingTimeUsesTheMeasuredRateAfterEnoughFrames()
        {
            var p = new ExportProgressModel(600, 0, 60000);
            p.SetPhase(ExportPhase.Frames);
            Assert.Equal((long)Math.Ceiling(60000 * (1 - 0.10)), p.RemainingMs); // a-priori
            for (int i = 0; i <= 10; i++) p.OnFrames(i * 6, i * 100); // 60 fps of export
            Assert.NotNull(p.FramesPerSecond);
            Assert.Equal(60, p.FramesPerSecond.Value, 3);
            long expected = (long)Math.Ceiling((600 - 60) * 1000.0 / 60 + 600 * 1000.0 / 60 * (0.05 / 0.85));
            Assert.Equal(expected, p.RemainingMs);
        }

        [Fact]
        public void ResumeCountsDoneFramesAndResetRateClearsWindow()
        {
            var p = new ExportProgressModel(600, 240, 0);
            Assert.Equal(240, p.FramesDone);
            p.SetPhase(ExportPhase.Frames);
            Assert.Null(p.RemainingMs);
            p.OnFrames(242, 0);
            p.OnFrames(245, 100);
            Assert.Null(p.FramesPerSecond); // fewer than 10 frames this run
            p.ResetRate(240);
            Assert.Equal(240, p.FramesDone);
            Assert.Throws<ArgumentOutOfRangeException>(() => new ExportProgressModel(0, 0, 0));
        }
    }

    public class PlaybackClockTests
    {
        [Fact]
        public void PlaysPausesAndStopsAtTheEnd()
        {
            var c = new PlaybackClock(1000000, 60);
            c.Advance(100000);
            Assert.Equal(0, c.PositionUs); // paused
            c.Play();
            Assert.Equal(100000, c.Advance(100000));
            c.Pause();
            Assert.Equal(100000, c.Advance(100000));
            c.Play();
            c.Advance(5000000);
            Assert.Equal(999999, c.PositionUs);
            Assert.False(c.IsPlaying);
            Assert.True(c.ReachedEnd);
            int disc = c.Discontinuity;
            c.Play();
            Assert.Equal(0, c.PositionUs);
            Assert.Equal(disc + 1, c.Discontinuity);
        }

        [Fact]
        public void LoopWrapsAndCountsDiscontinuities()
        {
            var c = new PlaybackClock(1000000, 30) { Loop = true };
            c.Play();
            c.Advance(1200000);
            Assert.Equal(200000, c.PositionUs);
            Assert.Equal(1, c.Discontinuity);
            Assert.True(c.IsPlaying);
        }

        [Fact]
        public void FollowsAudioBySlewingOrSnapping()
        {
            var c = new PlaybackClock(10000000, 60);
            c.Play();
            c.Seek(1000000);
            // predicted 1016667, audio 1008000: drift 8667 < snap (2 frames = 33334) → slews by 1/8
            long p = c.Advance(16667, 1008000);
            Assert.Equal(1016667 - 8667 / 8, p);
            // big drift snaps
            Assert.Equal(3000000, c.Advance(16667, 3000000));
        }

        [Fact]
        public void SeekClampsAndDurationChangesClampPosition()
        {
            var c = new PlaybackClock(1000000, 60);
            c.Seek(-5);
            Assert.Equal(0, c.PositionUs);
            c.Seek(5000000);
            Assert.Equal(999999, c.PositionUs);
            c.SetDuration(500000, 30);
            Assert.Equal(499999, c.PositionUs);
            Assert.Throws<ArgumentOutOfRangeException>(() => c.SetDuration(0, 30));
            Assert.Throws<ArgumentOutOfRangeException>(() => c.SetDuration(1, 25));
        }
    }

    public class PcmPlaybackCursorTests
    {
        static float[] Ramp(int frames)
        {
            var b = new float[frames * 2];
            for (int i = 0; i < frames; i++)
            {
                b[2 * i] = i;
                b[2 * i + 1] = -i;
            }
            return b;
        }

        [Fact]
        public void StoppedCursorOutputsSilence()
        {
            var c = new PcmPlaybackCursor();
            c.SetBuffer(Ramp(1000));
            var data = new float[64];
            Assert.Equal(0, c.Read(data, 2, 48000));
            Assert.All(data, v => Assert.Equal(0f, v));
            Assert.Equal(0, c.PositionFrames);
        }

        [Fact]
        public void PlaysAtSourceRateAfterTheFadeIn()
        {
            var c = new PcmPlaybackCursor();
            c.SetBuffer(Ramp(10000));
            c.Playing = true;
            var data = new float[2 * 1000];
            Assert.Equal(1000, c.Read(data, 2, 48000));
            // after the 240-frame fade the samples equal the source
            Assert.Equal(500f, data[2 * 500]);
            Assert.Equal(-500f, data[2 * 500 + 1]);
            Assert.True(Math.Abs(data[2 * 10]) < 10f); // faded in
            Assert.Equal(1000, c.PositionFrames);
            Assert.Equal(1000 * 1000000L / 48000, c.PositionUs);
        }

        [Fact]
        public void ResamplesMonoAndExtraChannels()
        {
            var c = new PcmPlaybackCursor();
            c.SetBuffer(Ramp(10000));
            c.Playing = true;
            var mono = new float[500];
            c.Read(mono, 1, 24000); // 2 source frames per output frame
            Assert.Equal(1000, c.PositionFrames);
            Assert.Equal(0f, mono[400]); // (L + R) / 2 of the ramp = 0
            var quad = new float[4 * 400];
            c.Read(quad, 4, 48000);
            Assert.Equal(0f, quad[4 * 300 + 2]);
            Assert.Equal(0f, quad[4 * 300 + 3]);
            Assert.Equal(1300f, quad[4 * 300]);
        }

        [Fact]
        public void SeekJumpsAndEndIsSilent()
        {
            var c = new PcmPlaybackCursor();
            c.SetBuffer(Ramp(1000));
            c.Seek(900);
            Assert.Equal(900, c.PositionFrames);
            c.Playing = true;
            var data = new float[2 * 300];
            int produced = c.Read(data, 2, 48000);
            Assert.Equal(100, produced);
            Assert.Equal(1000, c.PositionFrames);
            c.SeekUs(10000); // 480 frames
            Assert.Equal(480, c.PositionFrames);
            Assert.Throws<ArgumentException>(() => c.SetBuffer(new float[3]));
            Assert.Equal(0, c.Read(null, 2, 48000));
            Assert.Equal(AudioConstants.SampleRate, 48000);
        }
    }

    public class TextLayerTimingTests
    {
        static TextLayer Layer(string anim, long inUs = 1000000, long outUs = 3000000) => new TextLayer { Id = "t1", Text = "Hello", Anim = anim, InUs = inUs, OutUs = outUs };

        [Fact]
        public void VisibilityIsHalfOpen()
        {
            TextLayer l = Layer(null);
            Assert.False(TextLayerTiming.Evaluate(l, 999999, 5).Visible);
            Assert.True(TextLayerTiming.Evaluate(l, 1000000, 5).Visible);
            Assert.True(TextLayerTiming.Evaluate(l, 2999999, 5).Visible);
            Assert.False(TextLayerTiming.Evaluate(l, 3000000, 5).Visible);
            Assert.False(TextLayerTiming.IsVisible(null, 0));
        }

        [Fact]
        public void AnimationsStartAndSettle()
        {
            Assert.Equal(TextAnimKind.Pop, TextLayerTiming.ParseAnim(" POP "));
            Assert.Equal(TextAnimKind.Fade, TextLayerTiming.ParseAnim("appear"));
            Assert.Equal(TextAnimKind.None, TextLayerTiming.ParseAnim("spin"));
            TextLayerState fade = TextLayerTiming.Evaluate(Layer("fade"), 1200000, 5);
            Assert.Equal(0.5f, fade.Alpha, 3);
            TextLayerState pop0 = TextLayerTiming.Evaluate(Layer("pop"), 1000000, 5);
            Assert.Equal(0.6f, pop0.Scale, 3);
            float maxScale = 0;
            for (long t = 1000000; t < 1400000; t += 10000) maxScale = Math.Max(maxScale, TextLayerTiming.Evaluate(Layer("pop"), t, 5).Scale);
            Assert.InRange(maxScale, 1.02f, 1.06f);
            Assert.Equal(1f, TextLayerTiming.Evaluate(Layer("pop"), 1500000, 5).Scale, 4);
            Assert.Equal(60f, TextLayerTiming.Evaluate(Layer("slide"), 1000000, 5).OffsetYRef, 3);
            Assert.Equal(0f, TextLayerTiming.Evaluate(Layer("slide"), 1500000, 5).OffsetYRef, 3);
            Assert.Equal(-80f, TextLayerTiming.Evaluate(Layer("bounce"), 1000000, 5).OffsetYRef, 3);
            Assert.Equal(0f, TextLayerTiming.Evaluate(Layer("bounce"), 1800000, 5).OffsetYRef, 3);
        }

        [Fact]
        public void TypewriterRevealsCharacters()
        {
            TextLayer l = Layer("typewriter");
            Assert.Equal(1, TextLayerTiming.Evaluate(l, 1000000, 5).VisibleCharacters);
            Assert.Equal(3, TextLayerTiming.Evaluate(l, 1100000, 5).VisibleCharacters);
            Assert.Equal(-1, TextLayerTiming.Evaluate(l, 1250000, 5).VisibleCharacters);
            // 100 chars: at most 2 s in total → 20 ms each
            Assert.Equal(51, TextLayerTiming.Evaluate(l, 2000000, 100).VisibleCharacters);
        }

        [Fact]
        public void CounterPlaceholderResolves()
        {
            Assert.Equal("Осталось: 7", TextLayerTiming.ResolveText("Осталось: {n}", 7));
            Assert.Equal("Осталось: ", TextLayerTiming.ResolveText("Осталось: {n}", null));
            Assert.Equal("plain", TextLayerTiming.ResolveText("plain", 3));
            Assert.Equal(string.Empty, TextLayerTiming.ResolveText(null, 3));
            Assert.True(TextLayerTiming.UsesCounter("{n} left"));
            Assert.False(TextLayerTiming.UsesCounter("left"));
        }
    }
}
