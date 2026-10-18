using System;
using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Projects;
using Xunit;
using static Ballverse.Core.Tests.Projects.ProjectTestUtil;

namespace Ballverse.Core.Tests.Projects
{
    public class ProjectValidatorTests
    {
        static SanitizeResult Run(Action<ProjectDocument> mutate, string expectedId = null)
        {
            var d = Golden();
            mutate(d);
            return ProjectValidator.Sanitize(d, expectedId);
        }

        static void HasIssue(SanitizeResult r, string path, IssueCode code, IssueSeverity severity = IssueSeverity.Warning)
        {
            Assert.Contains(r.Issues, i => i.Path == path && i.Code == code && i.Severity == severity);
        }

        [Fact]
        public void DoesNotModifyInput()
        {
            var d = Golden();
            d.Layout.Ratio = 5;
            var r = ProjectValidator.Sanitize(d);
            Assert.Equal(5, d.Layout.Ratio);
            Assert.Equal(0.70, r.Document.Layout.Ratio);
            Assert.NotSame(d, r.Document);
        }

        // ---- §3.3 layout ----

        [Theory]
        [InlineData(0.29, 0.30, true)]
        [InlineData(0.30, 0.30, false)]
        [InlineData(0.70, 0.70, false)]
        [InlineData(0.71, 0.70, true)]
        [InlineData(-3, 0.30, true)]
        public void RatioRange(double input, double expected, bool issue)
        {
            var r = Run(d => d.Layout.Ratio = input);
            Assert.Equal(expected, r.Document.Layout.Ratio);
            Assert.Equal(issue, !r.IsClean);
            if (issue) HasIssue(r, "layout.ratio", IssueCode.OutOfRange);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void NonFiniteRatioUsesDefault(double input)
        {
            var r = Run(d => d.Layout.Ratio = input);
            Assert.Equal(0.5, r.Document.Layout.Ratio);
            HasIssue(r, "layout.ratio", IssueCode.InvalidValue);
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(0, 0)]
        [InlineData(16, 16)]
        [InlineData(17, 16)]
        public void DividerRange(int input, int expected)
        {
            Assert.Equal(expected, Run(d => d.Layout.Divider.Px = input).Document.Layout.Divider.Px);
        }

        [Theory]
        [InlineData(-5, 0)]
        [InlineData(48, 48)]
        [InlineData(49, 48)]
        public void CornerRadiusAndPaddingRange(int input, int expected)
        {
            var r = Run(d =>
            {
                d.Layout.CornerRadius = input;
                d.Layout.Padding = input;
            });
            Assert.Equal(expected, r.Document.Layout.CornerRadius);
            Assert.Equal(expected, r.Document.Layout.Padding);
        }

        [Theory]
        [InlineData(0.1, 0.20)]
        [InlineData(0.2, 0.20)]
        [InlineData(0.6, 0.60)]
        [InlineData(0.9, 0.60)]
        public void PipSizeRange(double input, double expected)
        {
            var r = Run(d =>
            {
                d.Layout.Type = LayoutType.L3;
                d.Layout.Pip = new PipSettings { SizeFraction = input };
            });
            Assert.Equal(expected, r.Document.Layout.Pip.SizeFraction);
        }

        [Fact]
        public void PipPositionClamped()
        {
            var r = Run(d => d.Layout.Pip = new PipSettings { X = -1, Y = 2, Shape = (PipShape)9 });
            Assert.Equal(0.0, r.Document.Layout.Pip.X);
            Assert.Equal(1.0, r.Document.Layout.Pip.Y);
            Assert.Equal(PipShape.RoundedRect, r.Document.Layout.Pip.Shape);
        }

        [Theory]
        [InlineData(LayoutType.L3)]
        [InlineData(LayoutType.L5)]
        public void PipLayoutWithoutPipGetsDefault(LayoutType type)
        {
            var r = Run(d => d.Layout.Type = type);
            Assert.NotNull(r.Document.Layout.Pip);
            HasIssue(r, "layout.pip", IssueCode.Missing);
        }

        [Fact]
        public void UnknownLayoutTypeAndMissingSectionsUseDefaults()
        {
            var r = Run(d =>
            {
                d.Layout.Type = (LayoutType)42;
                d.Layout.Divider = null;
            });
            Assert.Equal(LayoutType.L1, r.Document.Layout.Type);
            Assert.Equal(6, r.Document.Layout.Divider.Px);
            HasIssue(r, "layout.type", IssueCode.InvalidValue);
            HasIssue(r, "layout.divider", IssueCode.Missing);

            var r2 = Run(d => d.Layout = null);
            Assert.NotNull(r2.Document.Layout);
            HasIssue(r2, "layout", IssueCode.Missing);
        }

        // ---- colors ----

        [Theory]
        [InlineData("#FFFFFF", true)]
        [InlineData("#ffffff", true)]
        [InlineData("#FFFFFF33", true)]
        [InlineData("#0aB9c3dE", true)]
        [InlineData("FFFFFF", false)]
        [InlineData("#FFF", false)]
        [InlineData("#FFFFF", false)]
        [InlineData("#FFFFFFF", false)]
        [InlineData("#GGGGGG", false)]
        [InlineData("#FFFFFF333", false)]
        [InlineData("red", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        [InlineData(" #FFFFFF", false)]
        public void ColorFormat(string color, bool ok)
        {
            Assert.Equal(ok, ProjectValidator.IsColor(color));
        }

        [Fact]
        public void InvalidColorsReplacedByDefaults()
        {
            var r = Run(d =>
            {
                d.Layout.Divider.Color = "blue";
                d.Texts[0].Fill = "#12";
                d.Texts[0].Stroke.Color = null;
            });
            Assert.Equal("#FFFFFF33", r.Document.Layout.Divider.Color);
            Assert.Equal("#FFFFFF", r.Document.Texts[0].Fill);
            Assert.Equal("#000000", r.Document.Texts[0].Stroke.Color);
            HasIssue(r, "layout.divider.color", IssueCode.InvalidColor);
            HasIssue(r, "texts[0].fill", IssueCode.InvalidColor);
            HasIssue(r, "texts[0].stroke.color", IssueCode.InvalidColor);
        }

        // ---- §3.4 clip ----

        [Theory]
        [InlineData(0.4, 0.5)]
        [InlineData(0.5, 0.5)]
        [InlineData(2.0, 2.0)]
        [InlineData(2.5, 2.0)]
        public void SpeedRange(double input, double expected)
        {
            Assert.Equal(expected, Run(d => d.Clip.Speed = input).Document.Clip.Speed);
        }

        [Theory]
        [InlineData(-1, 0.0)]
        [InlineData(0, 0.0)]
        [InlineData(3, 3.0)]
        [InlineData(3.5, 3.0)]
        public void SlowMoSecondsRange(double input, double expected)
        {
            Assert.Equal(expected, Run(d => d.Clip.FinaleSlowMo.Seconds = input).Document.Clip.FinaleSlowMo.Seconds);
        }

        [Theory]
        [InlineData(0.1, 0.25)]
        [InlineData(0.25, 0.25)]
        [InlineData(0.5, 0.5)]
        [InlineData(1.0, 0.5)]
        public void SlowMoFactorRange(double input, double expected)
        {
            Assert.Equal(expected, Run(d => d.Clip.FinaleSlowMo.Factor = input).Document.Clip.FinaleSlowMo.Factor);
        }

        [Fact]
        public void MissingSlowMoIsOff()
        {
            var r = Run(d => d.Clip.FinaleSlowMo = null);
            Assert.Equal(0.0, r.Document.Clip.FinaleSlowMo.Seconds);
            HasIssue(r, "clip.finaleSlowMo", IssueCode.Missing);
        }

        [Fact]
        public void InvertedClipTrimIsSwapped()
        {
            var r = Run(d =>
            {
                d.Clip.TrimInTick = 900;
                d.Clip.TrimOutTick = 300;
            });
            Assert.Equal(300, r.Document.Clip.TrimInTick);
            Assert.Equal(900, r.Document.Clip.TrimOutTick);
            HasIssue(r, "clip.trimInTick", IssueCode.InvalidRange);
        }

        [Fact]
        public void EmptyClipTrimIsWidened()
        {
            var r = Run(d => d.Clip.TrimInTick = d.Clip.TrimOutTick = 600);
            Assert.Equal(600, r.Document.Clip.TrimInTick);
            Assert.Equal(601, r.Document.Clip.TrimOutTick);
        }

        [Fact]
        public void ClipTrimClampedToRunCap()
        {
            var r = Run(d =>
            {
                d.Clip.TrimInTick = -10;
                d.Clip.TrimOutTick = 999999;
            });
            Assert.Equal(0, r.Document.Clip.TrimInTick);
            Assert.True(r.Document.Clip.TrimOutTick <= SimConstants.MaxDurationTicks);
        }

        [Fact]
        public void ClipTrimAtMaxEdge()
        {
            var r = Run(d => d.Clip.TrimInTick = d.Clip.TrimOutTick = SimConstants.MaxDurationTicks);
            Assert.True(r.Document.Clip.TrimInTick < r.Document.Clip.TrimOutTick);
            Assert.Equal(SimConstants.MaxDurationTicks, r.Document.Clip.TrimOutTick);
        }

        [Fact]
        public void AutoDurationOver180sShortensClip()
        {
            // Full 180 s run at 0.5x speed would be 360 s.
            var r = Run(d =>
            {
                d.Clip.TrimInTick = 0;
                d.Clip.TrimOutTick = SimConstants.MaxDurationTicks;
                d.Clip.Speed = 0.5;
                d.Clip.FinaleSlowMo.Seconds = 3;
                d.Clip.FinaleSlowMo.Factor = 0.25;
            });
            HasIssue(r, "clip.trimOutTick", IssueCode.DurationTooLong);
            long us = ProjectTiming.ClipOutputUs(r.Document.Clip);
            Assert.True(us <= ProjectLimits.MaxDurationUs);
            var c = r.Document.Clip;
            // Maximal: one more tick would exceed the cap.
            Assert.True(ProjectTiming.ClipOutputUs(c.TrimInTick, c.TrimOutTick + 1, c.Speed, 3, 0.25) > ProjectLimits.MaxDurationUs);
        }

        [Fact]
        public void AutoDurationAtExactly180sIsAccepted()
        {
            var r = Run(d =>
            {
                d.Clip.TrimOutTick = SimConstants.MaxDurationTicks;
                d.Clip.FinaleSlowMo.Seconds = 0;
            });
            Assert.True(r.IsClean, string.Join("\n", r.Issues));
            Assert.Equal(ProjectLimits.MaxDurationUs, ProjectTiming.ReelDurationUs(r.Document));
        }

        [Fact]
        public void ClipMissingIsError()
        {
            var r = Run(d => d.Clip = null);
            Assert.True(r.HasErrors);
            HasIssue(r, "clip", IssueCode.Missing, IssueSeverity.Error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("../ring_escape")]
        [InlineData("ring escape")]
        public void BadModeIdIsError(string modeId)
        {
            var r = Run(d => d.Clip.ModeId = modeId);
            HasIssue(r, "clip.modeId", IssueCode.InvalidId, IssueSeverity.Error);
        }

        [Fact]
        public void UnknownButSafeModeIdIsAccepted()
        {
            Assert.True(Run(d => d.Clip.ModeId = "future_mode_99").IsClean);
        }

        [Fact]
        public void UnknownEnumsReset()
        {
            var r = Run(d =>
            {
                d.Clip.AfterEnd = (SimAfterEnd)77;
                d.UserVideo.AfterEnd = (VideoAfterEnd)(-1);
                d.UserVideo.Fill = (SlotFill)5;
                d.Texts[0].Plate = (TextPlate)8;
                d.Output.DurationMode = (DurationMode)9;
            });
            Assert.Equal(SimAfterEnd.Freeze, r.Document.Clip.AfterEnd);
            Assert.Equal(VideoAfterEnd.Freeze, r.Document.UserVideo.AfterEnd);
            Assert.Equal(SlotFill.Cover, r.Document.UserVideo.Fill);
            Assert.Equal(TextPlate.None, r.Document.Texts[0].Plate);
            Assert.Equal(DurationMode.Auto, r.Document.Output.DurationMode);
        }

        [Fact]
        public void NumericEnumFromJsonIsSanitized()
        {
            var o = ProjectJson.ParseObject(ReadData("project_v1.json"));
            o["clip"]["afterEnd"] = 12;
            var r = ProjectValidator.Sanitize(ProjectJson.ToDocument(o));
            Assert.Equal(SimAfterEnd.Freeze, r.Document.Clip.AfterEnd);
            HasIssue(r, "clip.afterEnd", IssueCode.InvalidValue);
        }

        [Fact]
        public void InvalidOptionalTokensCleared()
        {
            var r = Run(d =>
            {
                d.Clip.SoundPack = "../../x";
                d.Clip.Melody = "a b";
                d.Texts[0].Template = "<script>";
                d.Texts[0].Anim = "";
                d.Texts[0].Font = "/system/fonts/x.ttf";
            });
            Assert.Null(r.Document.Clip.SoundPack);
            Assert.Null(r.Document.Clip.Melody);
            Assert.Null(r.Document.Texts[0].Template);
            Assert.Null(r.Document.Texts[0].Anim);
            Assert.Equal(ProjectDefaults.Font, r.Document.Texts[0].Font);
        }

        // ---- paths (security) ----

        [Theory]
        [InlineData("../other/clip.bvr")]
        [InlineData("/data/data/app/clip.bvr")]
        [InlineData("..\\clip.bvr")]
        [InlineData("C:/clip.bvr")]
        [InlineData("file:///clip.bvr")]
        public void UnsafeReplayPathReset(string path)
        {
            var r = Run(d => d.Clip.Replay = path);
            Assert.Equal("clip.bvr", r.Document.Clip.Replay);
            HasIssue(r, "clip.replay", IssueCode.UnsafePath);
        }

        [Theory]
        [InlineData("../../../../etc/passwd")]
        [InlineData("/sdcard/DCIM/x.mp4")]
        [InlineData("media\\user.mp4")]
        [InlineData("media/../../p2/media/user.mp4")]
        [InlineData(null)]
        public void UnsafeVideoPathReset(string path)
        {
            var r = Run(d => d.UserVideo.File = path);
            Assert.Equal(ProjectPaths.DefaultUserVideoFile, r.Document.UserVideo.File);
            Assert.False(r.IsClean);
        }

        [Fact]
        public void UnsafeExportFileCleared()
        {
            var r = Run(d => d.Exports[0].File = "/storage/emulated/0/Movies/x.mp4");
            Assert.Null(r.Document.Exports[0].File);
            HasIssue(r, "exports[0].file", IssueCode.UnsafePath);
            Assert.Equal("exports/a.mp4", Run(d => d.Exports[0].File = "exports/a.mp4").Document.Exports[0].File);
        }

        [Fact]
        public void UnsafeIdWithoutExpectedIdIsError()
        {
            var r = Run(d => d.Id = "../../victim");
            HasIssue(r, "id", IssueCode.InvalidId, IssueSeverity.Error);
        }

        [Fact]
        public void UnsafeOrMismatchedIdReplacedByFolderId()
        {
            var r = Run(d => d.Id = "../../victim", "folder1");
            Assert.False(r.HasErrors);
            Assert.Equal("folder1", r.Document.Id);
            var r2 = Run(d => d.Id = "other", "folder1");
            Assert.Equal("folder1", r2.Document.Id);
            HasIssue(r2, "id", IssueCode.InvalidId);
            Assert.True(Run(d => d.Id = "folder1", "folder1").IsClean);
        }

        // ---- user video ----

        [Theory]
        [InlineData(0, 0, false)]
        [InlineData(90, 90, false)]
        [InlineData(180, 180, false)]
        [InlineData(270, 270, false)]
        [InlineData(360, 0, true)]
        [InlineData(-90, 270, true)]
        [InlineData(450, 90, true)]
        [InlineData(45, 0, true)]
        [InlineData(91, 0, true)]
        public void Rotation(int input, int expected, bool issue)
        {
            var r = Run(d => d.UserVideo.Rotation = input);
            Assert.Equal(expected, r.Document.UserVideo.Rotation);
            Assert.Equal(issue, !r.IsClean);
        }

        [Theory]
        [InlineData(0.5, 1.0)]
        [InlineData(1.0, 1.0)]
        [InlineData(8.0, 8.0)]
        [InlineData(100.0, 8.0)]
        public void ZoomRange(double input, double expected)
        {
            Assert.Equal(expected, Run(d => d.UserVideo.Zoom = input).Document.UserVideo.Zoom);
        }

        [Fact]
        public void PanShapeAndRange()
        {
            Assert.Equal(new[] { -1.0, 1.0 }, Run(d => d.UserVideo.Pan = new[] { -5.0, 5.0 }).Document.UserVideo.Pan);
            Assert.Equal(new[] { 0.0, 0.0 }, Run(d => d.UserVideo.Pan = new[] { 1.0 }).Document.UserVideo.Pan);
            Assert.Equal(new[] { 0.0, 0.0 }, Run(d => d.UserVideo.Pan = null).Document.UserVideo.Pan);
            Assert.Equal(new[] { 0.0, 0.3 }, Run(d => d.UserVideo.Pan = new[] { double.NaN, 0.3 }).Document.UserVideo.Pan);
        }

        [Fact]
        public void VideoTrimRepairs()
        {
            var inverted = Run(d =>
            {
                d.UserVideo.TrimInUs = 5000000;
                d.UserVideo.TrimOutUs = 2000000;
            }).Document.UserVideo;
            Assert.Equal(2000000, inverted.TrimInUs);
            Assert.Equal(5000000, inverted.TrimOutUs);

            var empty = Run(d => d.UserVideo.TrimInUs = d.UserVideo.TrimOutUs = 7000000).Document.UserVideo;
            Assert.Equal(7000000 + ProjectLimits.MinDurationUs, empty.TrimOutUs);

            var negative = Run(d => d.UserVideo.TrimInUs = -1).Document.UserVideo;
            Assert.Equal(0, negative.TrimInUs);
        }

        [Fact]
        public void VideoFragmentCappedAt180s()
        {
            var r = Run(d =>
            {
                d.UserVideo.TrimInUs = 10000000;
                d.UserVideo.TrimOutUs = 600000000;
            });
            Assert.Equal(10000000 + ProjectLimits.MaxDurationUs, r.Document.UserVideo.TrimOutUs);
            HasIssue(r, "userVideo.trimOutUs", IssueCode.DurationTooLong);
            Assert.True(Run(d => d.UserVideo.TrimOutUs = ProjectLimits.MaxDurationUs).IsClean);
        }

        [Fact]
        public void OffsetClamped()
        {
            Assert.Equal(-ProjectLimits.MaxOffsetUs, Run(d => d.UserVideo.OffsetUs = long.MinValue).Document.UserVideo.OffsetUs);
            Assert.Equal(-250000, Run(d => d.UserVideo.OffsetUs = -250000).Document.UserVideo.OffsetUs);
        }

        // ---- §3.5 texts ----

        [Fact]
        public void AtMostFiveTexts()
        {
            var r = Run(d =>
            {
                for (int i = 2; i <= 7; i++)
                {
                    var t = ProjectJson.Clone(d).Texts[0];
                    t.Id = "t" + i;
                    d.Texts.Add(t);
                }
            });
            Assert.Equal(5, r.Document.Texts.Count);
            Assert.Equal(new[] { "t1", "t2", "t3", "t4", "t5" }, r.Document.Texts.Select(t => t.Id));
            HasIssue(r, "texts[5]", IssueCode.TooMany);
        }

        [Theory]
        [InlineData(11, 12)]
        [InlineData(12, 12)]
        [InlineData(160, 160)]
        [InlineData(161, 160)]
        public void TextSizeRange(int input, int expected)
        {
            Assert.Equal(expected, Run(d => d.Texts[0].Size = input).Document.Texts[0].Size);
        }

        [Theory]
        [InlineData(-1, 0)]
        [InlineData(0, 0)]
        [InlineData(8, 8)]
        [InlineData(9, 8)]
        public void StrokeRange(int input, int expected)
        {
            Assert.Equal(expected, Run(d => d.Texts[0].Stroke.Px = input).Document.Texts[0].Stroke.Px);
        }

        [Fact]
        public void TextIdsRegeneratedWhenDuplicateOrUnsafe()
        {
            var r = Run(d =>
            {
                var a = ProjectJson.Clone(d).Texts[0];
                var b = ProjectJson.Clone(d).Texts[0];
                var c = ProjectJson.Clone(d).Texts[0];
                a.Id = "t1"; // duplicate of the first
                b.Id = "../x";
                c.Id = "t3";
                d.Texts.Add(a);
                d.Texts.Add(b);
                d.Texts.Add(c);
                d.Texts.Add(null);
            });
            Assert.Equal(new[] { "t1", "t2", "t4", "t3" }, r.Document.Texts.Select(t => t.Id));
            HasIssue(r, "texts[1].id", IssueCode.Duplicate);
            HasIssue(r, "texts[2].id", IssueCode.InvalidId);
            HasIssue(r, "texts[4]", IssueCode.InvalidValue);
        }

        [Fact]
        public void TextTimingRepaired()
        {
            var inverted = Run(d =>
            {
                d.Texts[0].InUs = 5000000;
                d.Texts[0].OutUs = 1000000;
            }).Document.Texts[0];
            Assert.Equal(1000000, inverted.InUs);
            Assert.Equal(5000000, inverted.OutUs);

            var empty = Run(d => d.Texts[0].InUs = d.Texts[0].OutUs = 2000000).Document.Texts[0];
            Assert.Equal(3000000, empty.OutUs);

            var late = Run(d =>
            {
                d.Texts[0].InUs = ProjectLimits.MaxDurationUs + 5;
                d.Texts[0].OutUs = ProjectLimits.MaxDurationUs + 10;
            }).Document.Texts[0];
            Assert.True(late.InUs < late.OutUs);
            Assert.Equal(ProjectLimits.MaxDurationUs, late.OutUs);
        }

        [Fact]
        public void TextContentCleaned()
        {
            var r = Run(d => d.Texts[0].Text = "a\u0000b\tc\nd\u007F");
            Assert.Equal("abc\nd", r.Document.Texts[0].Text);
            Assert.Equal("", Run(d => d.Texts[0].Text = null).Document.Texts[0].Text);

            string longText = new string('x', 199) + "\U0001F600"; // emoji would straddle the 200-char cap
            Assert.Equal(new string('x', 199), Run(d => d.Texts[0].Text = longText).Document.Texts[0].Text);
            Assert.Equal("ab", Run(d => d.Texts[0].Text = "a\uD800b").Document.Texts[0].Text);
            Assert.True(Run(d => d.Texts[0].Text = "Пиши 👇\nсвой цвет").IsClean);
        }

        [Fact]
        public void TextPosClamped()
        {
            Assert.Equal(new[] { 0.0, 1.0 }, Run(d => d.Texts[0].Pos = new[] { -1.0, 3.0 }).Document.Texts[0].Pos);
            Assert.Equal(new[] { 0.5, 0.5 }, Run(d => d.Texts[0].Pos = new double[3]).Document.Texts[0].Pos);
        }

        [Fact]
        public void NullTextsAndStrokeAllowed()
        {
            var r = Run(d => d.Texts[0].Stroke = null);
            Assert.True(r.IsClean);
            var r2 = Run(d => d.Texts = null);
            Assert.Empty(r2.Document.Texts);
            HasIssue(r2, "texts", IssueCode.Missing);
        }

        // ---- §3.6 audio ----

        [Theory]
        [InlineData(-0.1, 0.0)]
        [InlineData(0.0, 0.0)]
        [InlineData(2.0, 2.0)]
        [InlineData(2.01, 2.0)]
        public void VolumeRanges(double input, double expected)
        {
            var r = Run(d =>
            {
                d.Audio.Sim = input;
                d.Audio.User = input;
                d.UserVideo.Volume = input;
                d.Audio.Music = new MusicSettings { Track = "lofi_01", Volume = input };
            });
            Assert.Equal(expected, r.Document.Audio.Sim);
            Assert.Equal(expected, r.Document.Audio.User);
            Assert.Equal(expected, r.Document.UserVideo.Volume);
            Assert.Equal(expected, r.Document.Audio.Music.Volume);
        }

        [Fact]
        public void MusicSanitized()
        {
            var r = Run(d => d.Audio.Music = new MusicSettings { Track = "lofi_01", StartUs = -5, FadeInUs = 3000000, FadeOutUs = -1 });
            var m = r.Document.Audio.Music;
            Assert.Equal(0, m.StartUs);
            Assert.Equal(ProjectLimits.MaxMusicFadeUs, m.FadeInUs);
            Assert.Equal(0, m.FadeOutUs);
            Assert.Null(Run(d => d.Audio.Music = new MusicSettings { Track = "../x.mp3" }).Document.Audio.Music);
            var noAudio = Run(d => d.Audio = null);
            Assert.Equal(1.0, noAudio.Document.Audio.Sim);
        }

        // ---- output / duration ----

        [Fact]
        public void ManualDurationClamped()
        {
            var r = Run(d =>
            {
                d.Output.DurationMode = DurationMode.Manual;
                d.Output.ManualDurationUs = 500 * ProjectLimits.MicrosPerSecond;
            });
            Assert.Equal(ProjectLimits.MaxDurationUs, r.Document.Output.ManualDurationUs);
            HasIssue(r, "output.manualDurationUs", IssueCode.OutOfRange);
            Assert.Equal(ProjectLimits.MinDurationUs, Run(d =>
            {
                d.Output.DurationMode = DurationMode.Manual;
                d.Output.ManualDurationUs = 0;
            }).Document.Output.ManualDurationUs);
        }

        [Fact]
        public void ManualWithoutDurationUsesClipDuration()
        {
            var r = Run(d => d.Output.DurationMode = DurationMode.Manual);
            Assert.Equal(ProjectTiming.ClipOutputUs(r.Document.Clip), r.Document.Output.ManualDurationUs);
        }

        [Fact]
        public void MatchVideoWithoutVideoFallsBackToAuto()
        {
            var r = Run(d =>
            {
                d.UserVideo = null;
                d.Output.DurationMode = DurationMode.MatchVideo;
            });
            Assert.Equal(DurationMode.Auto, r.Document.Output.DurationMode);
            Assert.True(Run(d => d.Output.DurationMode = DurationMode.MatchVideo).IsClean);
        }

        [Fact]
        public void ReelNeverExceeds180s()
        {
            foreach (DurationMode mode in Enum.GetValues(typeof(DurationMode)))
            {
                var r = Run(d =>
                {
                    d.Output.DurationMode = mode;
                    d.Output.ManualDurationUs = long.MaxValue;
                    d.Clip.TrimOutTick = int.MaxValue;
                    d.Clip.Speed = 0.5;
                    d.UserVideo.TrimOutUs = long.MaxValue;
                });
                Assert.True(ProjectTiming.ReelDurationUs(r.Document) <= ProjectLimits.MaxDurationUs, mode.ToString());
            }
        }

        [Fact]
        public void OutputFieldsValidated()
        {
            var r = Run(d =>
            {
                d.Output.Aspect = "16:9";
                d.Output.Resolution = 480;
                d.Output.Fps = 24;
            });
            Assert.Equal("9:16", r.Document.Output.Aspect);
            Assert.Null(r.Document.Output.Resolution);
            Assert.Null(r.Document.Output.Fps);
            var ok = Run(d =>
            {
                d.Output.Resolution = 720;
                d.Output.Fps = 30;
            });
            Assert.True(ok.IsClean);
            Assert.NotNull(Run(d => d.Output = null).Document.Output);
        }

        // ---- §3.8 export history ----

        [Fact]
        public void InvalidExportRecordsDropped()
        {
            var r = Run(d =>
            {
                d.Exports.Add(new ExportRecord { At = "yesterday", Res = 1080, Fps = 60 });
                d.Exports.Add(new ExportRecord { At = Now, Res = 1081, Fps = 60 });
                d.Exports.Add(new ExportRecord { At = Now, Res = 720, Fps = 25 });
                d.Exports.Add(null);
                d.Exports.Add(new ExportRecord { At = Now, Res = 720, Fps = 30, Ms = -4 });
            });
            Assert.Equal(2, r.Document.Exports.Count);
            Assert.Equal(0, r.Document.Exports[1].Ms);
            Assert.Equal(4, r.Issues.Count(i => i.Code == IssueCode.InvalidValue && i.Path.StartsWith("exports[")));
        }

        [Fact]
        public void ExportHistoryCappedKeepingNewest()
        {
            var r = Run(d =>
            {
                d.Exports.Clear();
                for (int i = 0; i < 105; i++) d.Exports.Add(new ExportRecord { At = Now, Res = 720, Fps = 30, Ms = i });
            });
            Assert.Equal(ProjectLimits.MaxExportRecords, r.Document.Exports.Count);
            Assert.Equal(5, r.Document.Exports[0].Ms);
            Assert.Equal(104, r.Document.Exports[99].Ms);
            Assert.Empty(Run(d => d.Exports = null).Document.Exports);
        }

        // ---- root fields ----

        [Theory]
        [InlineData("2026-10-18T12:00:00Z", true)]
        [InlineData("2026-10-18T12:00:00.123Z", true)]
        [InlineData("2026-10-18T12:00:00.1234567Z", true)]
        [InlineData("2026-10-18T12:00:00+03:00", true)]
        [InlineData("2026-10-18T12:00:00-05:30", true)]
        [InlineData("2026-10-18T12:00:00", false)]
        [InlineData("2026-10-18", false)]
        [InlineData("10/18/2026 12:00", false)]
        [InlineData("2026-13-18T12:00:00Z", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void TimestampFormat(string value, bool ok)
        {
            Assert.Equal(ok, ProjectValidator.IsTimestamp(value));
        }

        [Fact]
        public void InvalidTimestampsReplaced()
        {
            var r = Run(d => d.CreatedAt = "garbage");
            Assert.Equal(r.Document.UpdatedAt, r.Document.CreatedAt);
            var r2 = Run(d =>
            {
                d.CreatedAt = null;
                d.UpdatedAt = "x";
            });
            Assert.Equal(ProjectDefaults.EpochIso, r2.Document.CreatedAt);
            Assert.Equal(ProjectDefaults.EpochIso, r2.Document.UpdatedAt);
            HasIssue(r2, "updatedAt", IssueCode.InvalidTimestamp);
        }

        [Fact]
        public void NameCleaned()
        {
            Assert.Equal(ProjectDefaults.UntitledName, Run(d => d.Name = "  ").Document.Name);
            Assert.Equal(ProjectDefaults.UntitledName, Run(d => d.Name = null).Document.Name);
            Assert.Equal("ab", Run(d => d.Name = "a\nb").Document.Name);
            Assert.Equal(ProjectLimits.MaxNameLength, Run(d => d.Name = new string('n', 500)).Document.Name.Length);
        }

        [Fact]
        public void WrongSchemaVersionIsError()
        {
            HasIssue(Run(d => d.SchemaVersion = 2), "schemaVersion", IssueCode.InvalidValue, IssueSeverity.Error);
            HasIssue(Run(d => d.SchemaVersion = 0), "schemaVersion", IssueCode.InvalidValue, IssueSeverity.Error);
        }

        // ---- properties ----

        [Fact]
        public void RandomGarbageSanitizesIntoRangesAndIsIdempotent()
        {
            var rng = Rng.FromSeed(0xB411_5EEDUL, 7);
            for (int iter = 0; iter < 300; iter++)
            {
                var d = RandomDocument(ref rng);
                var first = ProjectValidator.Sanitize(d, "fixed-id");
                Assert.False(first.HasErrors, string.Join("\n", first.Issues));
                AssertInRanges(first.Document);
                var second = ProjectValidator.Sanitize(first.Document, "fixed-id");
                Assert.True(second.IsClean, "iteration " + iter + ": " + string.Join("\n", second.Issues));
                Assert.Equal(ProjectJson.Serialize(first.Document), ProjectJson.Serialize(second.Document));
            }
        }

        [Fact]
        public void SanitizeIsDeterministic()
        {
            var rng = Rng.FromSeed(99, 1);
            var d = RandomDocument(ref rng);
            var a = ProjectValidator.Sanitize(d, "x");
            var b = ProjectValidator.Sanitize(d, "x");
            Assert.Equal(ProjectJson.Serialize(a.Document), ProjectJson.Serialize(b.Document));
            Assert.Equal(a.Issues.Select(i => i.ToString()), b.Issues.Select(i => i.ToString()));
        }

        static void AssertInRanges(ProjectDocument d)
        {
            Assert.True(ProjectPaths.IsSafeId(d.Id));
            Assert.True(ProjectPaths.IsSafeRelativePath(d.Clip.Replay));
            Assert.InRange(d.Clip.Speed, 0.5, 2.0);
            Assert.InRange(d.Clip.FinaleSlowMo.Seconds, 0, 3);
            Assert.InRange(d.Clip.FinaleSlowMo.Factor, 0.25, 0.5);
            Assert.True(d.Clip.TrimInTick < d.Clip.TrimOutTick);
            Assert.InRange(d.Layout.Ratio, 0.30, 0.70);
            Assert.InRange(d.Layout.Divider.Px, 0, 16);
            Assert.InRange(d.Layout.CornerRadius, 0, 48);
            Assert.InRange(d.Layout.Padding, 0, 48);
            Assert.True(ProjectValidator.IsColor(d.Layout.Divider.Color));
            if (d.Layout.Pip != null) Assert.InRange(d.Layout.Pip.SizeFraction, 0.20, 0.60);
            Assert.True(d.Texts.Count <= 5);
            Assert.Equal(d.Texts.Count, d.Texts.Select(t => t.Id).Distinct().Count());
            foreach (var t in d.Texts)
            {
                Assert.True(ProjectPaths.IsSafeId(t.Id));
                Assert.InRange(t.Size, 12, 160);
                if (t.Stroke != null) Assert.InRange(t.Stroke.Px, 0, 8);
                Assert.True(ProjectValidator.IsColor(t.Fill));
                Assert.True(t.InUs < t.OutUs);
            }
            Assert.InRange(d.Audio.Sim, 0, 2);
            Assert.InRange(d.Audio.User, 0, 2);
            if (d.UserVideo != null)
            {
                Assert.True(ProjectPaths.IsSafeRelativePath(d.UserVideo.File));
                Assert.Contains(d.UserVideo.Rotation, new[] { 0, 90, 180, 270 });
                Assert.InRange(d.UserVideo.Zoom, 1, ProjectLimits.MaxZoom);
                Assert.InRange(d.UserVideo.Volume, 0, 2);
                Assert.True(d.UserVideo.TrimInUs < d.UserVideo.TrimOutUs);
            }
            Assert.True(ProjectTiming.ReelDurationUs(d) <= ProjectLimits.MaxDurationUs);
            foreach (var e in d.Exports) Assert.True(e.File == null || ProjectPaths.IsSafeRelativePath(e.File));
        }

        static readonly string[] s_strings =
        {
            null, "", "ok_id", "../x", "/abs", "C:\\x", "#FFFFFF", "#12345678", "#zzz", "media/user.mp4", "a/../b", "t1", "t1",
            "2026-10-18T12:00:00Z", "nope", "\u0000", "9:16",
        };

        static string S(ref Rng r) => s_strings[r.NextInt(0, s_strings.Length)];

        static double D(ref Rng r)
        {
            switch (r.NextInt(0, 8))
            {
                case 0: return double.NaN;
                case 1: return double.PositiveInfinity;
                case 2: return -1e9;
                case 3: return 1e9;
                default: return r.NextFloat() * 4 - 1;
            }
        }

        static int I(ref Rng r)
        {
            switch (r.NextInt(0, 6))
            {
                case 0: return int.MinValue;
                case 1: return int.MaxValue;
                case 2: return r.NextInt(-400, 400);
                default: return r.NextInt(-20, 20000);
            }
        }

        static long L(ref Rng r)
        {
            switch (r.NextInt(0, 6))
            {
                case 0: return long.MinValue;
                case 1: return long.MaxValue;
                default: return (long)r.NextInt(-1000, 400000) * 1000;
            }
        }

        static ProjectDocument RandomDocument(ref Rng r)
        {
            var d = new ProjectDocument
            {
                Id = S(ref r),
                Name = S(ref r),
                CreatedAt = S(ref r),
                UpdatedAt = S(ref r),
                Clip = new ClipSettings
                {
                    Replay = S(ref r),
                    ModeId = "ring_escape",
                    TrimInTick = I(ref r),
                    TrimOutTick = I(ref r),
                    Speed = D(ref r),
                    FinaleSlowMo = r.NextInt(0, 5) == 0 ? null : new FinaleSlowMo { Seconds = D(ref r), Factor = D(ref r) },
                    AfterEnd = (SimAfterEnd)r.NextInt(-1, 6),
                    SoundPack = S(ref r),
                    Melody = S(ref r),
                },
                UserVideo = r.NextInt(0, 3) == 0 ? null : new UserVideoSettings
                {
                    File = S(ref r),
                    TrimInUs = L(ref r),
                    TrimOutUs = L(ref r),
                    OffsetUs = L(ref r),
                    Rotation = I(ref r),
                    Fill = (SlotFill)r.NextInt(-1, 3),
                    Pan = r.NextInt(0, 4) == 0 ? null : new[] { D(ref r), D(ref r) },
                    Zoom = D(ref r),
                    Volume = D(ref r),
                    AfterEnd = (VideoAfterEnd)r.NextInt(-1, 6),
                },
                Layout = r.NextInt(0, 6) == 0 ? null : new LayoutSettings
                {
                    Type = (LayoutType)r.NextInt(0, 7),
                    Ratio = D(ref r),
                    Divider = r.NextInt(0, 5) == 0 ? null : new DividerSettings { Px = I(ref r), Color = S(ref r) },
                    CornerRadius = I(ref r),
                    Padding = I(ref r),
                    Pip = r.NextInt(0, 2) == 0 ? null : new PipSettings { Shape = (PipShape)r.NextInt(-1, 3), SizeFraction = D(ref r), X = D(ref r), Y = D(ref r) },
                },
                Audio = new AudioSettings
                {
                    Sim = D(ref r),
                    User = D(ref r),
                    Music = r.NextInt(0, 2) == 0 ? null : new MusicSettings { Track = S(ref r), Volume = D(ref r), StartUs = L(ref r), FadeInUs = L(ref r), FadeOutUs = L(ref r) },
                },
                Output = new OutputSettings
                {
                    Aspect = S(ref r),
                    DurationMode = (DurationMode)r.NextInt(-1, 4),
                    ManualDurationUs = r.NextInt(0, 2) == 0 ? (long?)null : L(ref r),
                    Resolution = r.NextInt(0, 2) == 0 ? (int?)null : I(ref r),
                    Fps = r.NextInt(0, 2) == 0 ? (int?)null : I(ref r),
                },
            };
            int texts = r.NextInt(0, 9);
            for (int i = 0; i < texts; i++)
            {
                d.Texts.Add(r.NextInt(0, 8) == 0 ? null : new TextLayer
                {
                    Id = S(ref r),
                    Template = S(ref r),
                    Text = S(ref r),
                    Font = S(ref r),
                    Size = I(ref r),
                    Fill = S(ref r),
                    Stroke = r.NextInt(0, 2) == 0 ? null : new TextStroke { Px = I(ref r), Color = S(ref r) },
                    Plate = (TextPlate)r.NextInt(-1, 4),
                    Pos = new[] { D(ref r), D(ref r) },
                    InUs = L(ref r),
                    OutUs = L(ref r),
                    Anim = S(ref r),
                });
            }
            int exports = r.NextInt(0, 4);
            for (int i = 0; i < exports; i++)
            {
                d.Exports.Add(new ExportRecord { At = S(ref r), Res = r.NextInt(0, 2) == 0 ? 720 : I(ref r), Fps = r.NextInt(0, 2) == 0 ? 60 : I(ref r), Ms = L(ref r), File = S(ref r) });
            }
            return d;
        }
    }
}
