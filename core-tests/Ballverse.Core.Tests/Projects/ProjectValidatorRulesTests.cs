using System;
using System.Globalization;
using System.Linq;
using Ballverse.Core.Projects;
using Newtonsoft.Json.Linq;
using Xunit;
using static Ballverse.Core.Tests.Projects.ProjectTestUtil;

namespace Ballverse.Core.Tests.Projects
{
    /// <summary>
    /// Validator rules for the minimum reel duration, active slow-mo, studio settings added at schema 1, emoji-safe
    /// truncation, culture-invariant messages, file-index issue paths and the user volume source of truth.
    /// </summary>
    public class ProjectValidatorRulesTests
    {
        static SanitizeResult Run(Action<ProjectDocument> mutate)
        {
            var d = Golden();
            mutate(d);
            return ProjectValidator.Sanitize(d);
        }

        static void HasIssue(SanitizeResult r, string path, IssueCode code)
        {
            Assert.True(r.Issues.Any(i => i.Path == path && i.Code == code), "missing " + code + " at " + path + "; got:\n" + string.Join("\n", r.Issues));
        }

        // ---- §3.8 minimum duration 1 s in every duration mode ----

        static void AutoClip(ProjectDocument d, int trimIn, int trimOut, double speed)
        {
            d.Output.DurationMode = DurationMode.Auto;
            d.Clip.TrimInTick = trimIn;
            d.Clip.TrimOutTick = trimOut;
            d.Clip.Speed = speed;
            d.Clip.FinaleSlowMo.Seconds = 0;
        }

        [Theory]
        [InlineData(1.01, 990100L, true)]   // 60 ticks at one hundredth above 1x
        [InlineData(1.0, 1000000L, false)]
        [InlineData(0.99, 1010102L, false)]
        public void AutoReelBoundaryAtOneSecond(double speed, long expectedBeforeUs, bool repaired)
        {
            var d = Golden();
            AutoClip(d, 600, 660, speed);
            Assert.Equal(expectedBeforeUs, ProjectTiming.ReelDurationUs(d));
            var r = ProjectValidator.Sanitize(d);
            Assert.Equal(repaired, !r.IsClean);
            Assert.True(ProjectTiming.ReelDurationUs(r.Document) >= ProjectLimits.MinDurationUs);
            if (repaired)
            {
                HasIssue(r, "clip.trimOutTick", IssueCode.DurationTooShort);
                // The start moved earlier by the minimum needed (61 ticks at this speed); the end stayed.
                Assert.Equal(599, r.Document.Clip.TrimInTick);
                Assert.Equal(660, r.Document.Clip.TrimOutTick);
            }
        }

        [Fact]
        public void AutoReelFromOrdinaryUiInputIsLengthenedMinimally()
        {
            // 0.5 s of clip at 2x = 0.25 s of reel.
            var r = Run(d =>
            {
                d.UserVideo = null;
                AutoClip(d, 0, 30, 2.0);
            });
            HasIssue(r, "clip.trimOutTick", IssueCode.DurationTooShort);
            var c = r.Document.Clip;
            Assert.Equal(0, c.TrimInTick);
            Assert.Equal(120, c.TrimOutTick); // 2 s of clip at 2x
            Assert.Equal(ProjectLimits.MinDurationUs, ProjectTiming.ReelDurationUs(r.Document));
            Assert.True(ProjectTiming.ClipOutputUs(c.TrimInTick, c.TrimOutTick - 1, c.Speed, 0, 0.5) < ProjectLimits.MinDurationUs);
        }

        [Fact]
        public void AutoReelAtEndOfRunMovesStartEarlier()
        {
            int max = SimConstants.MaxDurationTicks;
            var r = Run(d => AutoClip(d, max - 10, max, 2.0));
            Assert.Equal(max - 120, r.Document.Clip.TrimInTick);
            Assert.Equal(max, r.Document.Clip.TrimOutTick);
        }

        [Fact]
        public void AutoReelWithSlowMoCountsTheSlowedFinale()
        {
            // Slow-mo 1 s at 0.25x: 15 ticks (0.25 s of clip) already last 1 s.
            var r = Run(d =>
            {
                AutoClip(d, 100, 105, 1.0);
                d.Clip.FinaleSlowMo.Seconds = 1;
                d.Clip.FinaleSlowMo.Factor = 0.25;
            });
            Assert.Equal(90, r.Document.Clip.TrimInTick);
            Assert.Equal(105, r.Document.Clip.TrimOutTick);
            Assert.Equal(ProjectLimits.MinDurationUs, ProjectTiming.ReelDurationUs(r.Document));
        }

        [Theory]
        [InlineData(999999L, true)]
        [InlineData(1000000L, false)]
        [InlineData(1000001L, false)]
        public void MatchVideoReelBoundaryAtOneSecond(long lengthUs, bool repaired)
        {
            var r = Run(d =>
            {
                d.Output.DurationMode = DurationMode.MatchVideo;
                d.UserVideo.TrimInUs = 5000000;
                d.UserVideo.TrimOutUs = 5000000 + lengthUs;
            });
            Assert.Equal(repaired, !r.IsClean);
            Assert.Equal(Math.Max(lengthUs, ProjectLimits.MinDurationUs), ProjectTiming.ReelDurationUs(r.Document));
            Assert.Equal(5000000 + lengthUs, r.Document.UserVideo.TrimOutUs); // end kept, start moved earlier
            if (repaired) HasIssue(r, "userVideo.trimOutUs", IssueCode.DurationTooShort);
        }

        [Fact]
        public void MatchVideoOfOneMicrosecondAtStartExtendsTheEnd()
        {
            var r = Run(d =>
            {
                d.Output.DurationMode = DurationMode.MatchVideo;
                d.UserVideo.TrimInUs = 0;
                d.UserVideo.TrimOutUs = 1;
            });
            Assert.Equal(0, r.Document.UserVideo.TrimInUs);
            Assert.Equal(ProjectLimits.MinDurationUs, r.Document.UserVideo.TrimOutUs);
            HasIssue(r, "userVideo.trimOutUs", IssueCode.DurationTooShort);
        }

        [Theory]
        [InlineData(999999L, 1000000L, true)]
        [InlineData(1000000L, 1000000L, false)]
        [InlineData(1000001L, 1000001L, false)]
        public void ManualReelBoundaryAtOneSecond(long input, long expected, bool repaired)
        {
            var r = Run(d =>
            {
                d.Output.DurationMode = DurationMode.Manual;
                d.Output.ManualDurationUs = input;
            });
            Assert.Equal(expected, r.Document.Output.ManualDurationUs);
            Assert.Equal(repaired, !r.IsClean);
        }

        [Fact]
        public void ShortVideoOutsideMatchVideoModeIsKept()
        {
            // In Auto the video only fills its slot; "if V is shorter than the reel" rules apply (spec §3.4).
            var r = Run(d =>
            {
                d.UserVideo.TrimInUs = 0;
                d.UserVideo.TrimOutUs = 1000;
            });
            Assert.True(r.IsClean, string.Join("\n", r.Issues));
            Assert.Equal(1000, r.Document.UserVideo.TrimOutUs);
        }

        // ---- §3.4 active slow-mo is 1-3 s ----

        [Theory]
        [InlineData(0.0, 0.0, false)]
        [InlineData(0.0001, 1.0, true)]
        [InlineData(0.5, 1.0, true)]
        [InlineData(0.999, 1.0, true)]
        [InlineData(1.0, 1.0, false)]
        [InlineData(2.5, 2.5, false)]
        public void ActiveSlowMoIsAtLeastOneSecond(double input, double expected, bool issue)
        {
            var r = Run(d => d.Clip.FinaleSlowMo.Seconds = input);
            Assert.Equal(expected, r.Document.Clip.FinaleSlowMo.Seconds);
            Assert.Equal(issue, !r.IsClean);
            if (issue) HasIssue(r, "clip.finaleSlowMo.seconds", IssueCode.OutOfRange);
        }

        // ---- studio settings added at schema 1 (§3.3-§3.6) ----

        [Fact]
        public void SimulationSlotAdjustmentsAreSanitized()
        {
            var r = Run(d =>
            {
                d.Clip.Fill = (SlotFill)7;
                d.Clip.Pan = new[] { 5.0, -5.0 };
                d.Clip.Zoom = 0.5;
                d.Clip.FitBackground = null;
            });
            Assert.Equal(SlotFill.Cover, r.Document.Clip.Fill);
            Assert.Equal(new[] { 1.0, -1.0 }, r.Document.Clip.Pan);
            Assert.Equal(1.0, r.Document.Clip.Zoom);
            Assert.Equal(FitBackgroundKind.Blur, r.Document.Clip.FitBackground.Kind);
            HasIssue(r, "clip.fill", IssueCode.InvalidValue);
            HasIssue(r, "clip.pan[0]", IssueCode.OutOfRange);
            HasIssue(r, "clip.zoom", IssueCode.OutOfRange);
            HasIssue(r, "clip.fitBackground", IssueCode.Missing);
            Assert.Equal(ProjectLimits.MaxZoom, Run(d => d.Clip.Zoom = 100).Document.Clip.Zoom);
            Assert.Equal(new[] { 0.0, 0.0 }, Run(d => d.Clip.Pan = null).Document.Clip.Pan);
        }

        [Fact]
        public void FitBackgroundIsSanitized()
        {
            var r = Run(d =>
            {
                d.UserVideo.FitBackground.Kind = (FitBackgroundKind)9;
                d.UserVideo.FitBackground.Color = "red";
                d.Clip.FitBackground.Color = null;
            });
            Assert.Equal(FitBackgroundKind.Blur, r.Document.UserVideo.FitBackground.Kind);
            Assert.Equal(ProjectDefaults.FitBackgroundColor, r.Document.UserVideo.FitBackground.Color);
            Assert.Equal(ProjectDefaults.FitBackgroundColor, r.Document.Clip.FitBackground.Color);
            HasIssue(r, "userVideo.fitBackground.kind", IssueCode.InvalidValue);
            HasIssue(r, "userVideo.fitBackground.color", IssueCode.InvalidColor);
            HasIssue(r, "clip.fitBackground.color", IssueCode.InvalidColor);
            Assert.True(Run(d => d.UserVideo.FitBackground = new FitBackground { Kind = FitBackgroundKind.Theme, Color = "#11223344" }).IsClean);
        }

        [Theory]
        [InlineData(-1, 0, true)]
        [InlineData(0, 0, false)]
        [InlineData(16, 16, false)]
        [InlineData(17, 16, true)]
        public void PipBorderRange(int input, int expected, bool issue)
        {
            var r = Run(d =>
            {
                d.Layout.Type = LayoutType.L3;
                d.Layout.Pip = new PipSettings { Border = new PipBorder { Px = input, Color = "#FFFFFF" }, Shadow = false };
            });
            Assert.Equal(expected, r.Document.Layout.Pip.Border.Px);
            Assert.False(r.Document.Layout.Pip.Shadow);
            Assert.Equal(issue, !r.IsClean);
        }

        [Fact]
        public void PipBorderMissingOrBadColorIsRepaired()
        {
            var r = Run(d =>
            {
                d.Layout.Type = LayoutType.L5;
                d.Layout.Pip = new PipSettings { Border = null };
            });
            Assert.Equal(0, r.Document.Layout.Pip.Border.Px);
            HasIssue(r, "layout.pip.border", IssueCode.Missing);
            var r2 = Run(d =>
            {
                d.Layout.Type = LayoutType.L3;
                d.Layout.Pip = new PipSettings { Border = new PipBorder { Px = 2, Color = "#12" } };
            });
            Assert.Equal(ProjectDefaults.PipBorderColor, r2.Document.Layout.Pip.Border.Color);
            HasIssue(r2, "layout.pip.border.color", IssueCode.InvalidColor);
        }

        [Fact]
        public void TextAlignAndShadow()
        {
            var r = Run(d =>
            {
                d.Texts[0].Align = (TextAlign)5;
                d.Texts[0].Shadow = true;
            });
            Assert.Equal(TextAlign.Center, r.Document.Texts[0].Align);
            Assert.True(r.Document.Texts[0].Shadow);
            HasIssue(r, "texts[0].align", IssueCode.InvalidValue);
            Assert.True(Run(d => d.Texts[0].Align = TextAlign.Right).IsClean);
        }

        [Fact]
        public void CoverFrameStaysInsideTheReel()
        {
            var r = Run(d => d.Output.Cover = new CoverSettings { AtUs = -5, PutFirst = true });
            Assert.Equal(0, r.Document.Output.Cover.AtUs);
            Assert.True(r.Document.Output.Cover.PutFirst);
            HasIssue(r, "output.cover.atUs", IssueCode.OutOfRange);

            // Golden reel (Auto): 30 s of clip with a 2 s finale at 0.5x = 32 s.
            long reel = ProjectTiming.ReelDurationUs(Golden());
            Assert.Equal(32000000, reel);
            Assert.Equal(reel - 1, Run(d => d.Output.Cover.AtUs = reel).Document.Output.Cover.AtUs);
            Assert.True(Run(d => d.Output.Cover.AtUs = reel - 1).IsClean);

            var missing = Run(d => d.Output.Cover = null);
            Assert.NotNull(missing.Document.Output.Cover);
            HasIssue(missing, "output.cover", IssueCode.Missing);
        }

        [Fact]
        public void NewSettingsHaveSpecDefaults()
        {
            var d = ProjectDefaults.NewProject("p", "n", Now, "ring_escape");
            Assert.True(d.Audio.DuckMusicUnderVoice); // §3.6 "Голос важнее", default on
            Assert.Equal(SlotFill.Cover, d.Clip.Fill);  // §3.3 Cover by default
            Assert.Equal(1.0, d.Clip.Zoom);
            Assert.Equal(new[] { 0.0, 0.0 }, d.Clip.Pan);
            Assert.Equal(FitBackgroundKind.Blur, d.Clip.FitBackground.Kind);
            Assert.False(d.Output.Cover.PutFirst);       // §3.4: 1.0 uses the first frame
            Assert.Equal(0, d.Output.Cover.AtUs);
            var t = new TextLayer();
            Assert.Equal(TextAlign.Center, t.Align);
            Assert.False(t.Shadow);
            var pip = new PipSettings();
            Assert.Equal(0, pip.Border.Px);
            Assert.True(pip.Shadow);
            Assert.True(ProjectValidator.Sanitize(d).IsClean);
        }

        // ---- user volume: audio.user is the source of truth ----

        [Fact]
        public void ContradictoryUserVolumesAreReconciled()
        {
            var r = Run(d =>
            {
                d.Audio.User = 0.0;
                d.UserVideo.Volume = 2.0;
            });
            Assert.Equal(0.0, r.Document.Audio.User);
            Assert.Equal(0.0, r.Document.UserVideo.Volume);
            HasIssue(r, "userVideo.volume", IssueCode.InvalidValue);
            Assert.True(Run(d => d.UserVideo = null).IsClean);
            Assert.True(Run(d => d.Audio.User = d.UserVideo.Volume = 1.5).IsClean);
        }

        [Fact]
        public void UserVolumeIsClampedBeforeMirroring()
        {
            var r = Run(d =>
            {
                d.Audio.User = 5.0;
                d.UserVideo.Volume = 1.0;
            });
            Assert.Equal(2.0, r.Document.Audio.User);
            Assert.Equal(2.0, r.Document.UserVideo.Volume);
            Assert.True(ProjectValidator.Sanitize(r.Document).IsClean);
        }

        // ---- §3.5 emoji are never cut in half by the text cap ----

        static string Truncated(string text)
        {
            return Run(d => d.Texts[0].Text = text).Document.Texts[0].Text;
        }

        [Fact]
        public void FlagStraddlingTheCapIsDroppedWhole()
        {
            const string us = "\U0001F1FA\U0001F1F8";
            Assert.Equal(new string('a', 198), Truncated(new string('a', 198) + us));
            Assert.Equal(new string('a', 196) + us, Truncated(new string('a', 196) + us)); // fits exactly
            // A complete flag before the cut stays: the regional indicators pair from the start of the run.
            const string fr = "\U0001F1EB\U0001F1F7";
            Assert.Equal(new string('a', 195) + us, Truncated(new string('a', 195) + us + fr));
            Assert.Equal(new string('a', 193) + us, Truncated(new string('a', 193) + us + fr)); // the half of 🇫🇷 that fits is dropped
        }

        [Fact]
        public void ZwjSequenceStraddlingTheCapIsDroppedWhole()
        {
            const string family = "\U0001F468‍\U0001F469‍\U0001F467";
            string t = Truncated(new string('a', 197) + family);
            Assert.Equal(new string('a', 197), t);
            t = Truncated(new string('a', 194) + family);
            Assert.Equal(new string('a', 194), t); // cut after the second ZWJ
            Assert.Equal(new string('a', 192) + family, Truncated(new string('a', 192) + family));
        }

        [Fact]
        public void ModifierAndKeycapSequencesAreDroppedWhole()
        {
            Assert.Equal(new string('a', 198), Truncated(new string('a', 198) + "\U0001F44D\U0001F3FD")); // thumbs up + skin tone
            Assert.Equal(new string('a', 199), Truncated(new string('a', 199) + "1️⃣"));        // keycap 1
            Assert.Equal(new string('a', 199), Truncated(new string('a', 199) + "❤️"));          // red heart + VS16
            Assert.Equal(new string('a', 199), Truncated(new string('a', 199) + "é"));               // e + combining acute
        }

        [Fact]
        public void TruncationNeverEndsInsideASequenceProperty()
        {
            string[] pieces =
            {
                "a", "й", "\U0001F525", "\U0001F1FA\U0001F1F8", "\U0001F1FA", "\U0001F468‍\U0001F469‍\U0001F467",
                "\U0001F44D\U0001F3FD", "1️⃣", "❤️", "\U0001F3F4\U000E0067\U000E0062\U000E0073\U000E0063\U000E0074\U000E007F",
            };
            var rng = Rng.FromSeed(0xE1101, 2);
            for (int iter = 0; iter < 150; iter++)
            {
                var sb = new System.Text.StringBuilder();
                while (sb.Length < 230) sb.Append(pieces[rng.NextInt(0, pieces.Length)]);
                string source = sb.ToString();
                string t = Truncated(source);
                Assert.True(t.Length <= ProjectLimits.MaxTextLength);
                Assert.StartsWith(t, source, StringComparison.Ordinal);
                // The cut lands on a piece boundary of the source unless the cut piece was a lone regional indicator.
                char last = t[t.Length - 1];
                Assert.NotEqual('‍', last);
                Assert.False(char.IsHighSurrogate(last));
                char after = source[t.Length];
                Assert.False(after == '‍' || after == '️' || after == '⃣', "cut before an extender at " + t.Length);
                if (char.IsHighSurrogate(after))
                {
                    int cp = char.ConvertToUtf32(source, t.Length);
                    Assert.False(cp >= 0x1F3FB && cp <= 0x1F3FF, "cut before a skin tone");
                    Assert.False(cp >= 0xE0020 && cp <= 0xE007F, "cut inside a tag sequence");
                }
                // Idempotent.
                Assert.Equal(t, Truncated(t));
            }
        }

        [Fact]
        public void NameTruncationAlsoKeepsEmojiWhole()
        {
            var r = Run(d => d.Name = new string('n', 98) + "\U0001F1FA\U0001F1F8");
            Assert.Equal(new string('n', 98), r.Document.Name);
        }

        // ---- culture-invariant messages ----

        [Fact]
        public void IssueMessagesDoNotDependOnCulture()
        {
            var d = Golden();
            d.UserVideo.OffsetUs = long.MinValue;
            d.SchemaVersion = -3;
            var prev = CultureInfo.CurrentCulture;
            string[] invariant, custom;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
                invariant = ProjectValidator.Sanitize(d).Issues.Select(i => i.ToString()).ToArray();
                var minus = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                minus.NumberFormat.NegativeSign = "−"; // as sv-SE under ICU
                CultureInfo.CurrentCulture = minus;
                custom = ProjectValidator.Sanitize(d).Issues.Select(i => i.ToString()).ToArray();
            }
            finally
            {
                CultureInfo.CurrentCulture = prev;
            }
            Assert.Equal(invariant, custom);
            Assert.Contains(invariant, m => m.Contains("Clamped to -180000000..180000000."));
            Assert.Contains(invariant, m => m.Contains("got -3;"));
        }

        [Fact]
        public void MigratorMessagesDoNotDependOnCulture()
        {
            var prev = CultureInfo.CurrentCulture;
            try
            {
                var minus = (CultureInfo)CultureInfo.InvariantCulture.Clone();
                minus.NumberFormat.NegativeSign = "−";
                CultureInfo.CurrentCulture = minus;
                var r = ProjectMigrator.Default.Migrate(ProjectJson.ParseObject("{\"schemaVersion\":-7}"));
                Assert.Contains("-7", r.Message);
            }
            finally
            {
                CultureInfo.CurrentCulture = prev;
            }
        }

        // ---- issue paths use the index in the file ----

        [Fact]
        public void TextIssuePathsUseTheFileIndex()
        {
            var o = ProjectJson.ParseObject(ReadData("project_v1.json"));
            var t = (JObject)o["texts"][0].DeepClone();
            t["size"] = 999;
            var t2 = (JObject)t.DeepClone();
            t2["id"] = "t1"; // duplicate
            o["texts"] = new JArray(JValue.CreateNull(), t, JValue.CreateNull(), t2);
            var r = ProjectValidator.Sanitize(ProjectJson.ToDocument(o));
            HasIssue(r, "texts[0]", IssueCode.InvalidValue);
            HasIssue(r, "texts[1].size", IssueCode.OutOfRange);
            HasIssue(r, "texts[2]", IssueCode.InvalidValue);
            HasIssue(r, "texts[3].id", IssueCode.Duplicate);
            HasIssue(r, "texts[3].size", IssueCode.OutOfRange);
            Assert.DoesNotContain(r.Issues, i => i.Path == "texts[0].size");
            Assert.Equal(2, r.Document.Texts.Count);
        }
    }
}
