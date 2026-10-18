using System;
using System.Linq;
using Ballverse.Core.Projects;
using Xunit;
using static Ballverse.Core.Tests.Projects.ProjectTestUtil;

namespace Ballverse.Core.Tests.Projects
{
    /// <summary>
    /// User-video trims stay within the 10-min source (spec §3.2 "до 10 мин на входе"), and every sanitized project is a valid
    /// timeline input (checked against <see cref="TimelineInputMirror"/>, a copy of the Timeline module's constructor checks).
    /// </summary>
    public class SanitizedTimelineInputTests
    {
        const long S = 1000000L;

        static SanitizeResult Run(Action<ProjectDocument> mutate)
        {
            var d = Golden();
            mutate(d);
            return ProjectValidator.Sanitize(d);
        }

        static void HasIssue(SanitizeResult r, string path, IssueCode code)
        {
            Assert.True(r.Issues.Any(i => i.Path == path && i.Code == code && i.Severity == IssueSeverity.Warning),
                "missing " + code + " at " + path + "; got:\n" + string.Join("\n", r.Issues));
        }

        static void AssertTimelineAccepts(ProjectDocument d)
        {
            TimelineInputMirror.Validate(d, 30);
            TimelineInputMirror.Validate(d, 60);
        }

        [Fact]
        public void LimitMatchesTimeline()
        {
            Assert.Equal(TimelineInputMirror.MaxSourceDurationUs, ProjectLimits.MaxSourceVideoUs);
            Assert.Equal(600 * S, ProjectLimits.MaxSourceVideoUs);
            Assert.True(ProjectLimits.MaxOffsetUs <= TimelineInputMirror.MaxAbsOffsetUs);
            Assert.True(ProjectLimits.MaxDurationUs <= TimelineInputMirror.MaxManualDurationUs);
        }

        [Fact]
        public void MirrorRejectsTrimPastTenMinutes()
        {
            // Sanity check of the mirror itself: the QA case the timeline threw on.
            var d = Golden();
            d.UserVideo.TrimInUs = 500 * S;
            d.UserVideo.TrimOutUs = 620 * S;
            Assert.Throws<ArgumentOutOfRangeException>(() => TimelineInputMirror.Validate(d, 60));
        }

        [Fact]
        public void TrimEndPastSourceIsClampedAndFlagged()
        {
            // QA finding: a 120 s fragment ending at 620 s used to be accepted as clean.
            var r = Run(d =>
            {
                d.UserVideo.TrimInUs = 500 * S;
                d.UserVideo.TrimOutUs = 620 * S;
            });
            Assert.False(r.IsClean);
            HasIssue(r, "userVideo.trimOutUs", IssueCode.OutOfRange);
            Assert.Equal(500 * S, r.Document.UserVideo.TrimInUs);
            Assert.Equal(600 * S, r.Document.UserVideo.TrimOutUs);
            AssertTimelineAccepts(r.Document);
        }

        [Theory]
        [InlineData(420_000000L, 600_000000L, true)]   // a 180 s fragment ending exactly at 10 min
        [InlineData(599_000000L, 600_000000L, true)]
        [InlineData(0L, 600_000001L, false)]          // 1 µs past the source
        [InlineData(420_000000L, 600_000001L, false)]
        public void SourceLimitBoundary(long trimIn, long trimOut, bool clean)
        {
            var r = Run(d =>
            {
                d.UserVideo.TrimInUs = trimIn;
                d.UserVideo.TrimOutUs = trimOut;
            });
            Assert.Equal(clean, r.IsClean);
            Assert.True(r.Document.UserVideo.TrimOutUs <= ProjectLimits.MaxSourceVideoUs);
            if (!clean) HasIssue(r, "userVideo.trimOutUs", IssueCode.OutOfRange);
            AssertTimelineAccepts(r.Document);
        }

        [Fact]
        public void TrimStartPastSourceIsClamped()
        {
            var r = Run(d =>
            {
                d.UserVideo.TrimInUs = 700 * S;
                d.UserVideo.TrimOutUs = 720 * S;
            });
            HasIssue(r, "userVideo.trimInUs", IssueCode.OutOfRange);
            HasIssue(r, "userVideo.trimOutUs", IssueCode.OutOfRange);
            // Only the source's last microsecond is left; in Auto mode the video then freezes on it.
            Assert.Equal(600 * S - 1, r.Document.UserVideo.TrimInUs);
            Assert.Equal(600 * S, r.Document.UserVideo.TrimOutUs);
            AssertTimelineAccepts(r.Document);
            Assert.True(ProjectValidator.Sanitize(r.Document).IsClean);
        }

        [Fact]
        public void TrimPastSourceInMatchVideoModeKeepsOneSecond()
        {
            var r = Run(d =>
            {
                d.Output.DurationMode = DurationMode.MatchVideo;
                d.UserVideo.TrimInUs = 900 * S;
                d.UserVideo.TrimOutUs = 900 * S;
            });
            Assert.Equal(599 * S, r.Document.UserVideo.TrimInUs);
            Assert.Equal(600 * S, r.Document.UserVideo.TrimOutUs);
            HasIssue(r, "userVideo.trimOutUs", IssueCode.DurationTooShort);
            Assert.Equal(S, ProjectTiming.ReelDurationUs(r.Document));
            AssertTimelineAccepts(r.Document);
        }

        [Theory]
        [InlineData(long.MaxValue, long.MaxValue)]
        [InlineData(long.MinValue, long.MaxValue)]
        [InlineData(long.MaxValue, long.MinValue)]
        [InlineData(0L, long.MaxValue)]
        public void ExtremeTrimsBecomeValidTimelineInput(long trimIn, long trimOut)
        {
            foreach (DurationMode mode in Enum.GetValues(typeof(DurationMode)))
            {
                var r = Run(d =>
                {
                    d.Output.DurationMode = mode;
                    d.Output.Cover.PutFirst = true;
                    d.UserVideo.TrimInUs = trimIn;
                    d.UserVideo.TrimOutUs = trimOut;
                    d.UserVideo.OffsetUs = trimIn;
                });
                Assert.False(r.HasErrors);
                AssertTimelineAccepts(r.Document);
                Assert.True(ProjectValidator.Sanitize(r.Document).IsClean);
            }
        }

        [Fact]
        public void FragmentStillCappedAt180sInsideTheSource()
        {
            var r = Run(d =>
            {
                d.UserVideo.TrimInUs = 100 * S;
                d.UserVideo.TrimOutUs = 1000 * S;
            });
            HasIssue(r, "userVideo.trimOutUs", IssueCode.OutOfRange);
            HasIssue(r, "userVideo.trimOutUs", IssueCode.DurationTooLong);
            Assert.Equal(280 * S, r.Document.UserVideo.TrimOutUs);
            AssertTimelineAccepts(r.Document);
        }

        [Fact]
        public void GoldenProjectIsValidTimelineInput()
        {
            AssertTimelineAccepts(ProjectValidator.Sanitize(Golden()).Document);
        }

        // ---- integration-style property ----

        static long WildUs(ref Rng r)
        {
            switch (r.NextInt(0, 8))
            {
                case 0: return long.MinValue;
                case 1: return long.MaxValue;
                case 2: return -(long)r.NextInt(0, 1000000) * 1000;
                case 3: return 600 * S + r.NextInt(-2, 3); // around the source limit
                case 4: return (long)r.NextInt(0, 2000000) * 1000 + r.NextInt(0, 1000); // up to ~33 min
                default: return (long)r.NextInt(0, 700000) * 1000;
            }
        }

        static int WildTick(ref Rng r)
        {
            switch (r.NextInt(0, 6))
            {
                case 0: return int.MinValue;
                case 1: return int.MaxValue;
                case 2: return r.NextInt(-100, 100);
                default: return r.NextInt(-10, SimConstants.MaxDurationTicks + 200);
            }
        }

        static double WildRatio(ref Rng r)
        {
            switch (r.NextInt(0, 8))
            {
                case 0: return double.NaN;
                case 1: return double.NegativeInfinity;
                case 2: return -r.NextDouble();
                case 3: return r.NextDouble() * 1e6;
                default: return r.NextDouble() * 4;
            }
        }

        /// <summary>
        /// Property: whatever timing values a project.json holds, the sanitized document is accepted by every timeline
        /// constructor (clip, user video, spec) at both frame rates, in every duration mode, with and without the cover.
        /// </summary>
        [Fact]
        public void EverySanitizedProjectIsValidTimelineInput()
        {
            var rng = Rng.FromSeed(0x71AE_11FEUL, 3);
            for (int n = 0; n < 1000; n++)
            {
                var d = Golden();
                d.Output.DurationMode = (DurationMode)rng.NextInt(-1, 4);
                d.Output.ManualDurationUs = rng.NextInt(0, 4) == 0 ? (long?)null : WildUs(ref rng);
                d.Output.Cover = rng.NextInt(0, 8) == 0 ? null : new CoverSettings { AtUs = WildUs(ref rng), PutFirst = rng.NextBool() };
                d.Clip.TrimInTick = WildTick(ref rng);
                d.Clip.TrimOutTick = WildTick(ref rng);
                d.Clip.Speed = WildRatio(ref rng);
                d.Clip.FinaleSlowMo = rng.NextInt(0, 8) == 0 ? null : new FinaleSlowMo { Seconds = WildRatio(ref rng), Factor = WildRatio(ref rng) };
                d.Clip.AfterEnd = (SimAfterEnd)rng.NextInt(-1, 5);
                if (rng.NextInt(0, 5) == 0)
                {
                    d.UserVideo = null;
                }
                else
                {
                    d.UserVideo.TrimInUs = WildUs(ref rng);
                    d.UserVideo.TrimOutUs = rng.NextBool() ? WildUs(ref rng) : d.UserVideo.TrimInUs + WildUs(ref rng) / 4;
                    d.UserVideo.OffsetUs = WildUs(ref rng);
                    d.UserVideo.AfterEnd = (VideoAfterEnd)rng.NextInt(-1, 4);
                }

                var r = ProjectValidator.Sanitize(d);
                Assert.False(r.HasErrors, "case " + n + ":\n" + string.Join("\n", r.Issues));
                try
                {
                    AssertTimelineAccepts(r.Document);
                }
                catch (ArgumentOutOfRangeException e)
                {
                    throw new Xunit.Sdk.XunitException("case " + n + ": timeline rejects " + e.ParamName + " = " + e.ActualValue + "\n" + ProjectJson.Serialize(r.Document));
                }
                long reel = ProjectTiming.ReelDurationUs(r.Document);
                Assert.InRange(reel, ProjectLimits.MinDurationUs, ProjectLimits.MaxDurationUs);
                Assert.True(ProjectValidator.Sanitize(r.Document).IsClean, "case " + n + " not idempotent");
            }
        }
    }
}
