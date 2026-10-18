using System;
using System.Linq;
using Ballverse.Core.Projects;
using Xunit;
using static Ballverse.Core.Tests.Projects.ProjectTestUtil;

namespace Ballverse.Core.Tests.Projects
{
    /// <summary>
    /// The duration model matches the timeline's integer model (speed and slow-mo factor in hundredths, slow-mo in whole ticks,
    /// 0.1 s cover lead-in prepended), so the 180 s cap never cuts content and the project duration equals the export
    /// within one frame (spec §3.4, §3.8 acceptance).
    /// </summary>
    public class ProjectTimingTests
    {
        static void HasIssue(SanitizeResult r, string path, IssueCode code)
        {
            Assert.True(r.Issues.Any(i => i.Path == path && i.Code == code), "missing " + code + " at " + path + "; got:\n" + string.Join("\n", r.Issues));
        }

        static ProjectDocument FullRun(double speed, bool putFirst)
        {
            var d = Golden();
            d.Output.DurationMode = DurationMode.Auto;
            d.Output.Cover.PutFirst = putFirst;
            d.Clip.TrimInTick = 0;
            d.Clip.TrimOutTick = SimConstants.MaxDurationTicks;
            d.Clip.Speed = speed;
            d.Clip.FinaleSlowMo.Seconds = 0;
            return d;
        }

        // ---- reference: the timeline's frame count (StudioTimeline, spec §3.4/§7.5), without the [fps, 180·fps] clamp ----

        static int RefPercent(double ratio)
        {
            return (int)Math.Round(ratio * 100.0, MidpointRounding.AwayFromZero);
        }

        static long RefUnclampedFrames(ProjectDocument d, int fps)
        {
            long cover = d.Output.Cover.PutFirst ? fps / 10 : 0;
            long frames;
            switch (d.Output.DurationMode)
            {
                case DurationMode.MatchVideo:
                    frames = (d.UserVideo.TrimOutUs - d.UserVideo.TrimInUs - 1) * fps / 1000000 + 1;
                    break;
                case DurationMode.Manual:
                    frames = (d.Output.ManualDurationUs.Value * fps + 999999) / 1000000;
                    break;
                default:
                    long p = RefPercent(d.Clip.Speed), f = RefPercent(d.Clip.FinaleSlowMo.Factor);
                    long length = d.Clip.TrimOutTick - d.Clip.TrimInTick;
                    long s = Math.Min((long)Math.Round(d.Clip.FinaleSlowMo.Seconds * 60, MidpointRounding.AwayFromZero), length);
                    long units = (length - s) * 100 * f + s * 10000;
                    long ups = 60 * p * f;
                    frames = (units * fps + ups - 1) / ups;
                    break;
            }
            return frames + cover;
        }

        // ---- ProjectTiming ----

        [Fact]
        public void ClipOutputUsesHundredthsAndWholeTicks()
        {
            // Values the timeline renders (QA integration probe): 0.994 plays at 0.99x, 0.5049 at 0.5x.
            Assert.Equal(180723906, ProjectTiming.ClipOutputUs(0, 10735, 0.994, 0, 0.5));
            Assert.Equal(181733334, ProjectTiming.ClipOutputUs(0, 5452, 0.5049, 0, 0.5));
            // 1.009 s of slow-mo = 61 ticks: 539 ticks at 1x + 61 ticks at 0.5x, rounded up to whole µs.
            Assert.Equal(11016667, ProjectTiming.ClipOutputUs(0, 600, 1.0, 1.009, 0.5));
            // Factor 0.334 plays at 0.33.
            Assert.Equal(3030304, ProjectTiming.ClipOutputUs(0, 60, 1.0, 1, 0.334));
            Assert.Equal(99, ProjectTiming.PercentFromRatio(0.994));
            Assert.Equal(100, ProjectTiming.PercentFromRatio(1.005)); // binary 1.00499…, as the timeline rounds it
            Assert.Equal(0, ProjectTiming.PercentFromRatio(double.NaN));
            Assert.Equal(61, ProjectTiming.TicksFromSeconds(1.009));
            Assert.Equal(0, ProjectTiming.TicksFromSeconds(double.PositiveInfinity));
            Assert.Equal(0, ProjectTiming.ClipOutputUs(0, 60, 0.004, 0, 0.5)); // rounds to 0x
        }

        [Fact]
        public void ClipOutputSurvivesAbsurdUnsanitizedValues()
        {
            Assert.True(ProjectTiming.ClipOutputUs(0, int.MaxValue, 1e6, 1e6, 1e6) > 0);
            Assert.True(ProjectTiming.ClipOutputUs(int.MinValue, int.MaxValue, 0.01, 1e7, 0.01) > 0);
        }

        [Fact]
        public void ReelIncludesCoverLeadIn()
        {
            var d = Golden(); // 30 s clip + 2 s at 0.5x = 32 s
            Assert.Equal(32000000, ProjectTiming.ContentDurationUs(d));
            Assert.Equal(32000000, ProjectTiming.ReelDurationUs(d));
            d.Output.Cover.PutFirst = true;
            Assert.Equal(32000000, ProjectTiming.ContentDurationUs(d));
            Assert.Equal(32100000, ProjectTiming.ReelDurationUs(d));
            Assert.Equal(179900000, ProjectTiming.MaxContentUs(d));
            d.Output.Cover = null;
            Assert.Equal(32000000, ProjectTiming.ReelDurationUs(d));
        }

        // ---- validator: rounding ----

        [Fact]
        public void SanitizerRoundsSpeedFactorAndSlowMoLikeTheTimeline()
        {
            var d = Golden();
            d.Clip.Speed = 1.234;
            d.Clip.FinaleSlowMo.Seconds = 1.009;
            d.Clip.FinaleSlowMo.Factor = 0.334;
            var r = ProjectValidator.Sanitize(d);
            Assert.Equal(1.23, r.Document.Clip.Speed);
            Assert.Equal(61 / 60.0, r.Document.Clip.FinaleSlowMo.Seconds);
            Assert.Equal(0.33, r.Document.Clip.FinaleSlowMo.Factor);
            HasIssue(r, "clip.speed", IssueCode.OutOfRange);
            HasIssue(r, "clip.finaleSlowMo.seconds", IssueCode.OutOfRange);
            HasIssue(r, "clip.finaleSlowMo.factor", IssueCode.OutOfRange);
            Assert.True(ProjectValidator.Sanitize(r.Document).IsClean);
            Assert.True(ProjectValidator.Sanitize(Golden()).IsClean);
        }

        // ---- validator: 180 s cap with the cover lead-in and rounding ----

        [Fact]
        public void FullRunWithoutCoverStaysWhole()
        {
            var r = ProjectValidator.Sanitize(FullRun(1.0, false));
            Assert.True(r.IsClean, string.Join("\n", r.Issues));
            Assert.Equal(SimConstants.MaxDurationTicks, r.Document.Clip.TrimOutTick);
            Assert.Equal(180000000, ProjectTiming.ReelDurationUs(r.Document));
        }

        [Fact]
        public void FullRunWithCoverLeadInLeavesRoomForIt()
        {
            var r = ProjectValidator.Sanitize(FullRun(1.0, true));
            HasIssue(r, "clip.trimOutTick", IssueCode.DurationTooLong);
            Assert.Equal(10794, r.Document.Clip.TrimOutTick); // 179.9 s of content
            Assert.Equal(179900000, ProjectTiming.ContentDurationUs(r.Document));
            Assert.Equal(180000000, ProjectTiming.ReelDurationUs(r.Document));
            Assert.Equal(10800, RefUnclampedFrames(r.Document, 60)); // timeline: 6 cover + 10794 clip frames, nothing cut
            Assert.True(ProjectValidator.Sanitize(r.Document).IsClean);
        }

        [Theory]
        [InlineData(0.994, 0.99, 10692)]   // 10692 ticks / 59.4 = 180 s exactly
        [InlineData(0.5049, 0.5, 5400)]
        public void FullRunAtUnroundedSpeedIsCappedOnTheRenderedSpeed(double speed, double rendered, int expectedTrimOut)
        {
            var r = ProjectValidator.Sanitize(FullRun(speed, false));
            Assert.Equal(rendered, r.Document.Clip.Speed);
            Assert.Equal(expectedTrimOut, r.Document.Clip.TrimOutTick);
            Assert.True(ProjectTiming.ReelDurationUs(r.Document) <= ProjectLimits.MaxDurationUs);
            Assert.True(ProjectTiming.ClipOutputUs(0, expectedTrimOut + 1, rendered, 0, 0.5) > ProjectLimits.MaxDurationUs);
            Assert.True(RefUnclampedFrames(r.Document, 60) <= 10800);
            Assert.True(RefUnclampedFrames(r.Document, 30) <= 5400);
        }

        [Fact]
        public void ManualDurationLeavesRoomForCover()
        {
            var d = Golden();
            d.Output.DurationMode = DurationMode.Manual;
            d.Output.ManualDurationUs = ProjectLimits.MaxDurationUs;
            Assert.True(ProjectValidator.Sanitize(d).IsClean);
            d.Output.Cover.PutFirst = true;
            var r = ProjectValidator.Sanitize(d);
            HasIssue(r, "output.manualDurationUs", IssueCode.OutOfRange);
            Assert.Equal(179900000, r.Document.Output.ManualDurationUs);
            Assert.Equal(180000000, ProjectTiming.ReelDurationUs(r.Document));
        }

        [Fact]
        public void MatchVideoLeavesRoomForCover()
        {
            var d = Golden();
            d.Output.DurationMode = DurationMode.MatchVideo;
            d.UserVideo.TrimInUs = 5000000;
            d.UserVideo.TrimOutUs = 185000000;
            Assert.True(ProjectValidator.Sanitize(d).IsClean);
            d.Output.Cover.PutFirst = true;
            var r = ProjectValidator.Sanitize(d);
            HasIssue(r, "userVideo.trimOutUs", IssueCode.DurationTooLong);
            Assert.Equal(5000000, r.Document.UserVideo.TrimInUs);
            Assert.Equal(184900000, r.Document.UserVideo.TrimOutUs);
            Assert.Equal(180000000, ProjectTiming.ReelDurationUs(r.Document));
            Assert.True(ProjectValidator.Sanitize(r.Document).IsClean);
        }

        [Fact]
        public void CoverFrameIsClampedToTheContent()
        {
            var d = Golden();
            d.Output.Cover.PutFirst = true;
            d.Output.Cover.AtUs = 32050000; // inside the reel (32.1 s) but past the content (32 s)
            var r = ProjectValidator.Sanitize(d);
            HasIssue(r, "output.cover.atUs", IssueCode.OutOfRange);
            Assert.Equal(31999999, r.Document.Output.Cover.AtUs);
        }

        [Fact]
        public void ProjectSummaryDurationIncludesCover()
        {
            var store = new InMemoryFileStore();
            var repo = new ProjectRepository(store);
            var d = Doc("p1");
            d.Output.Cover.PutFirst = true;
            repo.Save(d);
            Assert.Equal(32100000, repo.List().Projects.Single().DurationUs);
        }

        /// <summary>
        /// Property: after sanitizing, the timeline renders the reel without hitting the 180 s clamp (nothing is cut) and the
        /// project duration equals the timeline duration within one frame, in every duration mode, with and without the cover.
        /// </summary>
        [Fact]
        public void SanitizedReelMatchesTimelineWithinOneFrame()
        {
            var rng = Rng.FromSeed(0x7131E, 4);
            for (int n = 0; n < 3000; n++)
            {
                var d = Golden();
                d.Output.DurationMode = (DurationMode)rng.NextInt(0, 3);
                d.Output.Cover.PutFirst = rng.NextBool();
                d.Output.ManualDurationUs = rng.NextInt(0, 4) == 0 ? (long?)null : rng.NextInt(0, 200000) * 1000L + rng.NextInt(0, 1000);
                d.Clip.Speed = 0.4 + rng.NextDouble() * 1.7;
                d.Clip.FinaleSlowMo.Seconds = rng.NextInt(0, 3) == 0 ? 0 : rng.NextDouble() * 3.5;
                d.Clip.FinaleSlowMo.Factor = 0.2 + rng.NextDouble() * 0.35;
                d.Clip.TrimInTick = rng.NextInt(0, SimConstants.MaxDurationTicks);
                d.Clip.TrimOutTick = rng.NextInt(0, SimConstants.MaxDurationTicks + 1);
                d.UserVideo.TrimInUs = rng.NextInt(0, 60000) * 1000L;
                d.UserVideo.TrimOutUs = d.UserVideo.TrimInUs + rng.NextInt(0, 200000) * 1000L + rng.NextInt(0, 1000);

                var r = ProjectValidator.Sanitize(d);
                var s = r.Document;
                Assert.False(r.HasErrors);
                Assert.Equal(RefPercent(s.Clip.Speed) / 100.0, s.Clip.Speed);
                long reel = ProjectTiming.ReelDurationUs(s);
                Assert.InRange(reel, ProjectLimits.MinDurationUs, ProjectLimits.MaxDurationUs);
                foreach (int fps in new[] { 30, 60 })
                {
                    long frames = RefUnclampedFrames(s, fps);
                    Assert.True(frames <= 180L * fps, "case " + n + " fps " + fps + ": timeline would cut " + (frames - 180L * fps) + " frames");
                    Assert.True(frames >= fps);
                    long timelineUs = (2L * frames * 1000000 + fps) / (2L * fps);
                    Assert.True(Math.Abs(timelineUs - reel) <= 1000000 / fps,
                        "case " + n + " fps " + fps + ": project " + reel + " us vs timeline " + timelineUs + " us");
                }
                Assert.True(ProjectValidator.Sanitize(s).IsClean, "case " + n + " not idempotent");
            }
        }
    }
}
