using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core.Audio;
using Ballverse.Core.Integration;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;
using Ballverse.Core.Studio;
using Ballverse.Core.Tests.Audio;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Studio
{
    public class ProjectCompositionTests
    {
        static ProjectDocument Doc(int clipTicks = 600)
        {
            return ProjectDefaults.NewProject("p1", "P", "2026-10-18T12:00:00Z", "ring_escape", clipTicks);
        }

        [Fact]
        public void DefaultProjectGivesAutoTimelineOfTheClip()
        {
            StudioTimeline t = ProjectComposition.BuildTimeline(Doc(600), 60, 600);
            Assert.Equal(600, t.FrameCount);
            Assert.Null(t.Spec.Video);
            Assert.Equal(DurationMode.Auto, t.Spec.DurationMode);
            Assert.Equal(300, ProjectComposition.BuildTimeline(Doc(600), 30, 600).FrameCount);
        }

        [Fact]
        public void TrimIsClampedIntoAShorterStream()
        {
            ProjectDocument d = Doc(600);
            d.Clip.TrimInTick = 100;
            d.Clip.TrimOutTick = 600;
            ClipTiming c = ProjectComposition.BuildClipTiming(d.Clip, 400);
            Assert.Equal(100, c.TrimInTick);
            Assert.Equal(400, c.TrimOutTick);
            d.Clip.TrimInTick = 500;
            c = ProjectComposition.BuildClipTiming(d.Clip, 400);
            Assert.Equal(399, c.TrimInTick);
            Assert.Equal(400, c.TrimOutTick);
            c = ProjectComposition.BuildClipTiming(d.Clip, 0);
            Assert.Equal(1, c.AvailableTicks);
        }

        [Fact]
        public void SpeedAndSlowMoAreRoundedAndClamped()
        {
            ProjectDocument d = Doc();
            d.Clip.Speed = 0.1;
            d.Clip.FinaleSlowMo = new FinaleSlowMo { Seconds = 2, Factor = 0.5 };
            ClipTiming c = ProjectComposition.BuildClipTiming(d.Clip, 600);
            Assert.Equal(ClipTiming.MinSpeedPercent, c.SpeedPercent);
            Assert.Equal(120, c.SlowMoTicks);
            Assert.Equal(50, c.SlowMoFactorPercent);
            d.Clip.Speed = double.NaN;
            d.Clip.FinaleSlowMo = new FinaleSlowMo { Seconds = 0, Factor = 0.1 };
            c = ProjectComposition.BuildClipTiming(d.Clip, 600);
            Assert.Equal(100, c.SpeedPercent);
            Assert.Equal(0, c.SlowMoTicks);
        }

        [Fact]
        public void VideoTimingAndLayoutFollowTheUserVideo()
        {
            ProjectDocument d = Doc();
            Assert.Null(ProjectComposition.BuildVideoTiming(null));
            Assert.Equal(LayoutType.L4, ProjectComposition.BuildLayoutSpec(d.Layout, false).Type);
            d.UserVideo = new UserVideoSettings { TrimInUs = 1000000, TrimOutUs = 6000000, OffsetUs = -500000, Rotation = 100, Mirror = true, Fill = SlotFill.Fit, Zoom = 2 };
            UserVideoTiming v = ProjectComposition.BuildVideoTiming(d.UserVideo);
            Assert.Equal(5000000, v.LengthUs);
            Assert.Equal(-500000, v.OffsetUs);
            Assert.Equal(LayoutType.L1, ProjectComposition.BuildLayoutSpec(d.Layout, true).Type);
            SlotContent content = ProjectComposition.BuildVideoContent(d.UserVideo);
            Assert.Equal(90, content.Rotation);
            Assert.True(content.Mirror);
            Assert.Equal(SlotFill.Fit, content.Fill);
            Assert.Equal(2f, content.Zoom);
            d.UserVideo.TrimOutUs = d.UserVideo.TrimInUs;
            Assert.Null(ProjectComposition.BuildVideoTiming(d.UserVideo));
        }

        [Fact]
        public void PipAndCircleShapeMapToTheLayoutSpec()
        {
            var l = new LayoutSettings { Type = LayoutType.L3, Pip = new PipSettings { Shape = PipShape.Circle, SizeFraction = 0.4, X = 0.3, Y = 0.7 } };
            LayoutSpec spec = ProjectComposition.BuildLayoutSpec(l, true);
            Assert.Equal(SlotShape.Circle, spec.Pip.Shape);
            Assert.Equal(0.4f, spec.Pip.SizeFraction, 4);
            Assert.Equal(0.3f, spec.Pip.Center.X, 4);
        }

        [Fact]
        public void FrameRequestUsesTheCanvasAndWatermarkFlag()
        {
            ProjectDocument d = Doc();
            FrameRequest r = ProjectComposition.BuildFrameRequest(d, 720, 1280, new IntVec2(1920, 1080), true, null, null);
            Assert.Equal(720, r.CanvasWidth);
            Assert.False(r.HasVideoSource); // no user video in the project
            FramePlan plan = FrameComposer.Compose(r);
            Assert.True(plan.HasWatermark);
            Assert.False(plan.Layout.HasVideo);
        }

        [Fact]
        public void MixSettingsMapGainsMusicAndFlags()
        {
            var a = new AudioSettings { Sim = 1.5, User = 0, Normalize = false, DuckMusicUnderVoice = false, Music = new MusicSettings { Track = "t", Volume = 0.5, StartUs = 1000000, FadeInUs = 9000000, FadeOutUs = 500000 } };
            var s = ProjectComposition.BuildMixSettings(a, true);
            Assert.Equal(1.5f, s.SimGain);
            Assert.False(s.UserEnabled);
            Assert.False(s.Normalize);
            Assert.False(s.DuckMusicUnderUser);
            Assert.Equal(0.5f, s.MusicGain);
            Assert.Equal(-48000, s.MusicStartFrame);
            Assert.Equal(2f, s.MusicFadeInSeconds, 4);
            Assert.Equal(0.5f, s.MusicFadeOutSeconds, 4);
            Assert.Equal(0f, ProjectComposition.BuildMixSettings(new AudioSettings(), false).MusicGain);
            Assert.False(ProjectComposition.BuildMixSettings(new AudioSettings(), false).UserEnabled);
        }

        [Fact]
        public void ExportSettingsPreferExplicitThenStoredThenTier()
        {
            var output = new OutputSettings { Resolution = 720, Fps = 30 };
            ExportSettings s = ProjectComposition.ResolveExportSettings(output, DeviceTier.A);
            Assert.Equal(ExportResolution.P720, s.Resolution);
            Assert.Equal(30, s.Fps);
            s = ProjectComposition.ResolveExportSettings(output, DeviceTier.A, null, ExportResolution.P1080, 60);
            Assert.Equal(1920, s.Height);
            Assert.Equal(60, s.Fps);
            s = ProjectComposition.ResolveExportSettings(new OutputSettings(), DeviceTier.C);
            Assert.Equal(ExportResolution.P1080, s.Resolution);
            Assert.Equal(ExportSettings.DefaultFps(DeviceTier.C), s.Fps);
            Assert.Equal(ExportResolution.P1080, ProjectComposition.ResolutionOf(999));
            Assert.Equal(270, ProjectComposition.SnapRotation(-90));
        }
    }

    public class SimSlotSizingTests
    {
        [Fact]
        public void FullFrameSlotGetsOneTexelPerPixel()
        {
            SimSlotRender r = SimSlotSizing.Compute(1080, 1920, SlotContent.Default);
            Assert.Equal(new IntVec2(1080, 1920), r.TargetSize);
            Assert.False(r.Mapping.HasLetterbox);
        }

        [Fact]
        public void WideSlotCoverCropsAndSizesByWidth()
        {
            SimSlotRender r = SimSlotSizing.Compute(1080, 960, SlotContent.Default); // L1 half
            Assert.Equal(1080, r.TargetSize.X);
            Assert.Equal(1920, r.TargetSize.Y);
            Assert.True(r.Mapping.SourceCrop.Height < 1920f);
        }

        [Fact]
        public void ZoomNeverGrowsTheWholeArenaTargetAndTinySlotsKeepMinimum()
        {
            // QA round 1: zoom 3 rendered 2304x4096 (37.7 MB + the renderer's MSAA scene at that size).
            foreach (float z in new[] { 1f, 2f, 3f, 8f })
            {
                SimSlotRender zoomed = SimSlotSizing.Compute(1080, 1920, new SlotContent(0, false, SlotFill.Cover, FloatVec2.Zero, z), 4096);
                Assert.Equal(new IntVec2(1080, 1920), zoomed.TargetSize);
                Assert.False(zoomed.IsViewport);
                Assert.Equal(1080f / z, zoomed.Mapping.SourceCrop.Width, 1); // the zoom magnifies the picture
            }
            SimSlotRender capped = SimSlotSizing.Compute(1080, 1920, SlotContent.Default, 1024);
            Assert.Equal(1024, capped.TargetSize.Y);
            SimSlotRender tiny = SimSlotSizing.Compute(2, 2, SlotContent.Default);
            Assert.True(tiny.TargetSize.X >= SimSlotSizing.MinSide && tiny.TargetSize.Y >= SimSlotSizing.MinSide);
            Assert.Throws<ArgumentOutOfRangeException>(() => SimSlotSizing.Compute(0, 10, SlotContent.Default));
        }

        [Fact]
        public void ViewportRendersOnlyTheVisibleWindowAtSlotResolution()
        {
            var zoom2 = new SlotContent(0, false, SlotFill.Cover, new FloatVec2(1f, 0f), 2f);
            SimSlotRender r = SimSlotSizing.Compute(1080, 1920, zoom2, 4096, viewport: true);
            Assert.Equal(new IntVec2(1080, 1920), r.TargetSize);
            Assert.True(r.IsViewport);
            Assert.Equal(0.5f, r.Viewport.Width, 3);
            Assert.Equal(0.5f, r.Viewport.Height, 3);
            Assert.Equal(0f, r.Viewport.X, 3); // pan +1 moves the picture right: the left half shows
            Assert.Equal(0.25f, r.Viewport.Y, 3);
            Assert.False(r.Mapping.HasLetterbox);
            Assert.Equal(1f, r.Mapping.Scale, 3); // one texel per output pixel

            SimSlotRender band = SimSlotSizing.Compute(1080, 960, SlotContent.Default, 4096, viewport: true); // L1 half
            Assert.Equal(new IntVec2(1080, 960), band.TargetSize);
            Assert.Equal(0.5f, band.Viewport.Height, 3);

            var fit = new SlotContent(0, true, SlotFill.Fit, FloatVec2.Zero, 1f);
            SimSlotRender letterbox = SimSlotSizing.Compute(1080, 960, fit, 4096, viewport: true);
            Assert.False(letterbox.IsViewport);
            Assert.True(letterbox.Mapping.HasLetterbox);
            Assert.Equal(SimSlotSizing.Compute(1080, 960, fit).Mapping.Destination, letterbox.Mapping.Destination);
            Assert.Equal(960, letterbox.TargetSize.Y);
        }
    }

    public class UserAudioPlacementTests
    {
        static StudioTimeline Timeline(long offsetUs, VideoAfterEnd afterEnd, int clipTicks = 600, long videoLenUs = 4000000, DurationMode mode = DurationMode.Auto)
        {
            var clip = new ClipTiming(0, clipTicks, clipTicks, 100, 0, 50, SimAfterEnd.Freeze);
            var video = new UserVideoTiming(0, videoLenUs, offsetUs, afterEnd);
            return new StudioTimeline(clip, video, mode, 60);
        }

        [Fact]
        public void VideoStartingWithTheClipPlaysFromFrameZero()
        {
            StudioTimeline t = Timeline(0, VideoAfterEnd.Freeze);
            var runs = UserAudioPlacement.ComputeRuns(t, 48000 * 4, 48000 * 10);
            Assert.Single(runs);
            Assert.Equal(0, runs[0].OutputFrame);
            Assert.Equal(0, runs[0].SourceFrame);
            Assert.Equal(48000 * 4, runs[0].Frames);
        }

        [Fact]
        public void LateVideoStartsLaterAndEarlyVideoSkipsItsHead()
        {
            var late = UserAudioPlacement.ComputeRuns(Timeline(1000000, VideoAfterEnd.Freeze), 48000 * 4, 48000 * 10);
            Assert.Equal(48000, late[0].OutputFrame);
            Assert.Equal(0, late[0].SourceFrame);
            var early = UserAudioPlacement.ComputeRuns(Timeline(-1000000, VideoAfterEnd.Freeze), 48000 * 4, 48000 * 10);
            Assert.Equal(0, early[0].OutputFrame);
            Assert.Equal(48000, early[0].SourceFrame);
            Assert.Equal(48000 * 3, early[0].Frames);
        }

        [Fact]
        public void LoopRepeatsTheTrimmedRange()
        {
            var runs = UserAudioPlacement.ComputeRuns(Timeline(0, VideoAfterEnd.Loop), 48000 * 4, 48000 * 10);
            Assert.Equal(3, runs.Count);
            Assert.Equal(48000 * 8, runs[2].OutputFrame);
            Assert.Equal(48000 * 2, runs[2].Frames);
        }

        [Fact]
        public void ShortDecodedAudioLeavesSilence()
        {
            var runs = UserAudioPlacement.ComputeRuns(Timeline(0, VideoAfterEnd.Loop), 48000, 48000 * 10);
            Assert.Equal(3, runs.Count);
            Assert.Equal(48000 * 4, runs[1].OutputFrame);
            Assert.Equal(48000, runs[1].Frames);
        }

        [Fact]
        public void PlaceCopiesSamples()
        {
            StudioTimeline t = Timeline(1000000, VideoAfterEnd.Freeze);
            var src = new float[2 * 48000 * 4];
            for (int i = 0; i < src.Length; i++) src[i] = 0.25f;
            var output = new float[2 * 48000 * 10];
            output[0] = 9f;
            UserAudioPlacement.Place(t, src, output);
            Assert.Equal(0f, output[0]);
            Assert.Equal(0f, output[2 * 47999]);
            Assert.Equal(0.25f, output[2 * 48000]);
            Assert.Equal(0.25f, output[2 * (48000 * 5 - 1) + 1]);
            Assert.Equal(0f, output[2 * 48000 * 5]);
            Assert.Empty(UserAudioPlacement.ComputeRuns(new StudioTimeline(new ClipTiming(0, 60, 60, 100, 0, 50, SimAfterEnd.Freeze), null, DurationMode.Auto, 60), 10, 10));
        }
    }

    public class StudioMixBuilderTests
    {
        [Fact]
        public void MixHasTheTimelineLengthAndCarriesUserAudio()
        {
            var clip = new ClipTiming(0, 120, 120, 100, 0, 50, SimAfterEnd.Freeze);
            var t = new StudioTimeline(clip, new UserVideoTiming(0, 2000000), DurationMode.Auto, 30);
            var user = new float[2 * 96000];
            for (int i = 0; i < user.Length; i++) user[i] = (float)Math.Sin(i * 0.01) * 0.3f;
            var inputs = new StudioMixInputs
            {
                Timeline = t,
                UserAudio = new ArrayPcmSource(user),
                Settings = new Ballverse.Core.Audio.Mastering.TrackMixSettings { Normalize = false },
            };
            StudioMixResult r = StudioMixBuilder.Render(inputs);
            Assert.Equal(ExportSoundSchedule.AudioFrameCount(t), r.FrameCount);
            double energy = 0;
            foreach (float v in r.Samples) energy += v * v;
            Assert.True(energy > 0);
        }

        [Fact]
        public void SimEventsAreRenderedWithThePack()
        {
            var clip = new ClipTiming(0, 120, 120, 100, 0, 50, SimAfterEnd.Freeze);
            var t = new StudioTimeline(clip, null, DurationMode.Auto, 60);
            var events = new List<SoundEvent> { new SoundEvent(10, SoundEventType.Hit, 1, 5f, 0.5f, 0f) };
            var inputs = new StudioMixInputs
            {
                Timeline = t,
                SimEvents = events,
                Pack = AudioTestUtil.SimplePack(),
                Bank = AudioTestUtil.FullBank(),
                Settings = new Ballverse.Core.Audio.Mastering.TrackMixSettings { Normalize = false },
            };
            StudioMixResult r = StudioMixBuilder.Render(inputs);
            Assert.Equal(0, r.MissingSamples);
            Assert.Equal(0f, r.Samples[2 * 7000]); // before tick 10 (8000 frames)
            bool sound = false;
            for (int i = 2 * 8000; i < 2 * 12000; i++) sound |= r.Samples[i] != 0f;
            Assert.True(sound);
            Assert.Throws<ArgumentException>(() => StudioMixBuilder.Render(new StudioMixInputs()));
        }
    }

    public class RenderStreamSoundBodySourceTests
    {
        [Fact]
        public void SizeAndPanComeFromTheFrameOfTheEventTick()
        {
            var q = RenderQuantization.Default;
            var ms = new MemoryStream();
            var writer = new RenderStreamWriter(ms, new RenderStreamHeader("ring_escape"), true);
            int circle = writer.DefineShape(ShapeDefinition.Circle);
            int capsule = writer.DefineShape(ShapeDefinition.Capsule(q.QuantizeScale(2f), q.QuantizeScale(0.5f)));
            for (int tick = 0; tick < 10; tick++)
            {
                var f = new RenderFrame();
                f.AddBody(new BodyState(1, circle, q.QuantizePosition(-4.5f + tick * 0.5f), 0, 0, q.QuantizeScale(0.25f), 0, BodyFlags.None));
                if (tick >= 5) f.AddBody(new BodyState(2, capsule, q.QuantizePosition(9f), 0, 0, q.UnitScale, 0, BodyFlags.None));
                writer.AppendFrame(tick, f);
            }
            writer.Finish();
            ms.Position = 0;
            using (var reader = new RenderStreamReader(ms))
            {
                var src = new RenderStreamSoundBodySource(reader);
                Assert.True(src.TryGetBody(new ReplayEvent(0, ReplayEventType.Hit, 1, 0, 0), out float size, out float pan));
                Assert.Equal(0.5f, size, 2);
                Assert.Equal(-1f, pan, 2);
                Assert.True(src.TryGetBody(new ReplayEvent(9, ReplayEventType.Hit, 1, 0, 0), out _, out pan));
                Assert.Equal(0f, pan, 2);
                Assert.False(src.TryGetBody(new ReplayEvent(2, ReplayEventType.Hit, 2, 0, 0), out size, out pan));
                Assert.Equal(RenderStreamSoundBodySource.DefaultSize, size);
                Assert.True(src.TryGetBody(new ReplayEvent(500, ReplayEventType.Hit, 2, 0, 0), out size, out pan)); // clamped to the last tick
                Assert.Equal(2.5f, size, 2);
                Assert.Equal(1f, pan);
            }
        }
    }

    public class RunRetentionTests
    {
        static ClipEntry Run(string id, long stream) => new ClipEntry(new ClipRecord { Id = id, ModeId = "m", DurationTicks = 1 }, "runs/" + id, true, stream > 0, false, 1, stream);

        [Fact]
        public void DeletesOldRunsAndEvictsStreamsOverBudget()
        {
            var runs = new List<ClipEntry> { Run("a", 300), Run("b", 300), Run("c", 0), Run("d", 300), Run("e", 300), Run("f", 300) };
            RunRetentionPlan plan = RunRetention.Decide(runs, 5, 700);
            Assert.Equal(new[] { "f" }, plan.DeleteRuns);
            Assert.Equal(new[] { "d", "e" }, plan.EvictStreams);
            Assert.True(RunRetention.Decide(new List<ClipEntry>(), 5, 1).IsEmpty);
            Assert.Throws<ArgumentOutOfRangeException>(() => RunRetention.Decide(runs, 0, 1));
        }
    }
}
