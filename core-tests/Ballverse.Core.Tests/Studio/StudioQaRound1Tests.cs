using System;
using System.Collections.Generic;
using System.Threading;
using Ballverse.Core.Audio;
using Ballverse.Core.Integration;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Tests.Audio;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Studio
{
    /// <summary>Regression tests for the Core parts of the studio QA round-1 fixes.</summary>
    public class StudioQaRound1Tests
    {
        static List<SoundEvent> ManyEvents(int count, int everyTicks)
        {
            var events = new List<SoundEvent>();
            for (int i = 0; i < count; i++)
                events.Add(new SoundEvent(i * everyTicks, (i % 3) == 0 ? SoundEventType.Hit : SoundEventType.Break, 1 + (i % 4), 2f + (i % 7), (i % 5) * 0.2f - 0.4f, 0f));
            return events;
        }

        [Fact]
        public void ChunkedEventRenderIsBitIdenticalToTheOfflineRender()
        {
            var clip = new ClipTiming(0, 600, 600, 100, 0, 50, SimAfterEnd.Freeze);
            var t = new StudioTimeline(clip, null, DurationMode.Auto, 60);
            ScheduledSoundEvent[] scheduled = ExportSoundSchedule.Build(t, ManyEvents(200, 3));
            int frames = (int)ExportSoundSchedule.AudioFrameCount(t);
            Assert.True(frames > 3 * StudioMixBuilder.ChunkFrames);

            var reference = new float[frames * 2];
            var mixer = new EventMixer(AudioTestUtil.SimplePack(), AudioTestUtil.FullBank(), null);
            mixer.RenderOffline(scheduled, reference);

            var chunked = new float[frames * 2];
            int missing = StudioMixBuilder.RenderEvents(AudioTestUtil.SimplePack(), AudioTestUtil.FullBank(), null, scheduled, chunked, CancellationToken.None);
            Assert.Equal(mixer.MissingSamples, missing);
            Assert.Equal(reference, chunked);
        }

        [Fact]
        public void CancelledMixStopsBeforeRendering()
        {
            var clip = new ClipTiming(0, 600, 600, 100, 0, 50, SimAfterEnd.Freeze);
            var t = new StudioTimeline(clip, null, DurationMode.Auto, 60);
            var cts = new CancellationTokenSource();
            cts.Cancel();
            var inputs = new StudioMixInputs
            {
                Timeline = t,
                SimEvents = ManyEvents(50, 5),
                Pack = AudioTestUtil.SimplePack(),
                Bank = AudioTestUtil.FullBank(),
            };
            Assert.Throws<OperationCanceledException>(() => StudioMixBuilder.Render(inputs, cts.Token));
            ScheduledSoundEvent[] scheduled = ExportSoundSchedule.Build(t, inputs.SimEvents);
            Assert.Throws<OperationCanceledException>(() =>
                StudioMixBuilder.RenderEvents(inputs.Pack, inputs.Bank, null, scheduled, new float[2 * 96000], cts.Token));
        }

        [Fact]
        public void AudioMixKeyIgnoresGeometryEdits()
        {
            ProjectDocument doc = ProjectDefaults.NewProject("p", "P", "2026-10-18T12:00:00Z", "ring_escape", 600);
            doc.UserVideo = UserVideoDefaults.Create("media/user.mp4", 10_000_000);
            StudioTimeline t = ProjectComposition.BuildTimeline(doc, 30, 600);
            string key = ProjectComposition.AudioMixKey(doc, t);

            doc.UserVideo.Zoom = 3;
            doc.UserVideo.Pan = new[] { 0.5, -0.2 };
            doc.UserVideo.Rotation = 90;
            doc.Clip.Zoom = 2;
            Assert.Equal(key, ProjectComposition.AudioMixKey(doc, t));

            doc.UserVideo.Volume = 0.5;
            string afterVolume = ProjectComposition.AudioMixKey(doc, t);
            Assert.NotEqual(key, afterVolume);
            doc.UserVideo.TrimInUs = 1_000_000;
            Assert.NotEqual(afterVolume, ProjectComposition.AudioMixKey(doc, t));
            Assert.NotEqual(ProjectComposition.AudioMixKey(doc, t), ProjectComposition.AudioMixKey(doc, ProjectComposition.BuildTimeline(doc, 60, 600)));
        }

        [Fact]
        public void StreamLongerThanTheLimitKeepsItsExportState()
        {
            Assert.Equal(SimConstants.MaxDurationTicks, ExportJob.EffectiveStreamTicks(SimConstants.MaxDurationTicks + 1));
            Assert.Equal(150, ExportJob.EffectiveStreamTicks(150));
            Assert.Equal(1, ExportJob.EffectiveStreamTicks(0));

            ProjectDocument doc = ProjectDefaults.NewProject("p3", "Full", "2026-10-18T12:00:00Z", "helix_drop", SimConstants.MaxDurationTicks);
            var req = new ExportJobRequest
            {
                JobId = "job9",
                Project = doc,
                Settings = new ExportSettings(ExportResolution.P720, 30),
                StreamTicks = SimConstants.MaxDurationTicks + 1,
                NowIso = "2026-10-18T12:06:00Z",
            };
            ExportResumeState s = ExportJob.CreateState(req);
            // What GpuExportFrameRenderer compares when it opens the clip (10801 ticks on disk).
            Assert.Equal(s.StreamTicks, ExportJob.EffectiveStreamTicks(SimConstants.MaxDurationTicks + 1));
        }

        [Fact]
        public void PreviewDecodeBoxOnlyGrowsWithZoomSteps()
        {
            SlotContent baseContent = SlotContent.Default;
            IntVec2 z1 = VideoDecodeSizing.PreviewBox(2160, 3840, baseContent);
            Assert.Equal(new IntVec2(540, 960), z1);
            // Pan never changes the box; zoom 1.0 .. 1.0 stays; 1.5 rounds up to 2.
            Assert.Equal(z1, VideoDecodeSizing.PreviewBox(2160, 3840, baseContent.WithPan(new FloatVec2(0.7f, -0.3f))));
            IntVec2 z15 = VideoDecodeSizing.PreviewBox(2160, 3840, baseContent.WithZoom(1.5f));
            IntVec2 z2 = VideoDecodeSizing.PreviewBox(2160, 3840, baseContent.WithZoom(2f));
            Assert.Equal(z2, z15);
            Assert.False(VideoDecodeSizing.Covers(z1, z15)); // zooming in past the step reopens once
            Assert.True(VideoDecodeSizing.Covers(z2, VideoDecodeSizing.PreviewBox(2160, 3840, baseContent.WithZoom(1.2f))));
            Assert.True(VideoDecodeSizing.Covers(z2, z1)); // pinching back out never reopens
            // Never larger than the source.
            IntVec2 z8 = VideoDecodeSizing.PreviewBox(2160, 3840, baseContent.WithZoom(8f));
            Assert.True(z8.X <= 2160 && z8.Y <= 3840);
            // A 1080p source is already decoded at its full size for a half-size preview at zoom 2.
            Assert.True(VideoDecodeSizing.Covers(VideoDecodeSizing.PreviewBox(1080, 1920, baseContent), VideoDecodeSizing.PreviewBox(1080, 1920, baseContent.WithZoom(2f))));
        }
    }
}

namespace Ballverse.Core.Tests.Studio
{
    public class ThumbnailPlanTests
    {
        [Fact]
        public void SamplesSitAtTheCentresOfEqualParts()
        {
            Assert.Equal(new[] { 75, 225, 375, 525 }, ThumbnailPlan.Ticks(600, 4));
            Assert.Equal(new[] { 0, 1, 1 }, ThumbnailPlan.Ticks(2, 3));
            Assert.Empty(ThumbnailPlan.Ticks(0, 4));
            Assert.Empty(ThumbnailPlan.Ticks(10, 0));
            Assert.Equal(new long[] { 1_250_000, 3_750_000, 6_250_000, 8_750_000 }, ThumbnailPlan.Times(10_000_000, 4));
            Assert.Empty(ThumbnailPlan.Times(0, 4));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => ThumbnailPlan.Ticks(10, -1));
            Assert.Equal(170, ThumbnailPlan.HeightFor(96, 9, 16));
            Assert.Equal(54, ThumbnailPlan.HeightFor(96, 1920, 1080));
            Assert.Equal(96, ThumbnailPlan.HeightFor(96, 0, 0));
        }
    }
}

namespace Ballverse.Core.Tests.Studio
{
    public class ImportPolicyTests
    {
        [Fact]
        public void LargeFileIsMoreThanAQuarterOfTheFreeSpaceBeforeTheCopy()
        {
            const long GB = 1L << 30;
            Assert.False(ImportPolicy.IsLargeFile(2 * GB, 10 * GB, -1));      // 20 %
            Assert.True(ImportPolicy.IsLargeFile(3 * GB, 10 * GB, -1));       // 30 %
            // Only the after-copy figure: 2 GB copied leaving 8 GB = 20 % of the 10 GB before (QA: was flagged at > 20 %).
            Assert.False(ImportPolicy.IsLargeFile(2 * GB, -1, 8 * GB));
            Assert.True(ImportPolicy.IsLargeFile(3 * GB, -1, 7 * GB));
            Assert.False(ImportPolicy.IsLargeFile(3 * GB, -1, -1));
            Assert.False(ImportPolicy.IsLargeFile(0, 10, 10));
        }
    }
}

namespace Ballverse.Core.Tests.Studio
{
    /// <summary>
    /// Static guard for the duplicated ExpandSim rule (QA round 1): <see cref="StudioFramePlans.Compose"/> (preview, any canvas)
    /// and Core.Integration <see cref="Ballverse.Core.Integration.ExportFramePlanner"/> (export canvases only; another module)
    /// must produce identical geometry at every export canvas and layout.
    /// </summary>
    public class FramePlanParityTests
    {
        static void SameGeometry(FramePlan a, FramePlan b)
        {
            Assert.Equal(a.Layout.EffectiveType, b.Layout.EffectiveType);
            Assert.Equal(a.Layout.Simulation.Rect, b.Layout.Simulation.Rect);
            Assert.Equal(a.Layout.HasVideo, b.Layout.HasVideo);
            if (a.Layout.HasVideo) Assert.Equal(a.Layout.Video.Rect, b.Layout.Video.Rect);
            Assert.Equal(a.Layout.HasDivider, b.Layout.HasDivider);
            Assert.Equal(a.HasWatermark, b.HasWatermark);
            Assert.Equal(a.Watermark.Rect, b.Watermark.Rect);
            Assert.Equal(a.Watermark.Corner, b.Watermark.Corner);
            Assert.Equal(a.HasVideoMapping, b.HasVideoMapping);
            Assert.Equal(a.VideoDestination, b.VideoDestination);
        }

        [Theory]
        [InlineData(LayoutType.L1, ExportResolution.P1080)]
        [InlineData(LayoutType.L2, ExportResolution.P720)]
        [InlineData(LayoutType.L3, ExportResolution.P1080)]
        [InlineData(LayoutType.L4, ExportResolution.P720)]
        public void PreviewPlansMatchTheExportPlanner(LayoutType type, ExportResolution resolution)
        {
            ProjectDocument d = ProjectDefaults.NewProject("p1", "P", "2026-10-18T12:00:00Z", "ring_escape", 600);
            d.UserVideo = UserVideoDefaults.Create("media/user.mp4", 5_000_000);
            d.Layout.Type = type;
            d.Layout.Pip = new PipSettings();
            var settings = new ExportSettings(resolution, 30);
            FrameRequest r = ProjectComposition.BuildFrameRequest(d, settings.Width, settings.Height, new IntVec2(1080, 1920), true, null, null);
            StudioFramePlans preview = StudioFramePlans.Compose(r);
            var export = new Ballverse.Core.Integration.ExportFramePlanner(ProjectComposition.BuildTimeline(d, 30, 600), settings, r);
            SameGeometry(preview.LayoutPlan, export.LayoutPlan);
            SameGeometry(preview.ExpandSimPlan, export.ExpandSimPlan);
        }
    }
}
