using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Xunit;
using static Ballverse.Core.Tests.Params.TestSchemas;

namespace Ballverse.Core.Tests.Params
{
    /// <summary>Merge of attributes a mode override sets explicitly (spec §2.5, §7.6).</summary>
    public class MergeOverrideAttributeTests
    {
        static ParamSchema M09(bool teamDefault = false)
        {
            var layer = new ParamSchemaBuilder()
                .Add(new BoolParam("teamMode", teamDefault).InGroup(ParamGroup.Mode, 10))
                .Add(new IntParam(CommonParams.ForcedWinner, 0, 6, 1, 0).WithVisibleIf(VisibleIf.IsTrue("teamMode")))
                .BuildLayer();
            return ParamSchema.Merge(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M09)), layer);
        }

        [Fact]
        public void ModeCanMakeForcedWinnerConditionalForTheM09TeamVariant()
        {
            // spec §2.5: M09 offers a forced winner only in the team variant
            var s = M09();
            var fw = (IntParam)s.Get(CommonParams.ForcedWinner);
            Assert.NotNull(fw.VisibleIf);
            Assert.Equal("teamMode", fw.VisibleIf.TargetId);
            Assert.Equal(6, fw.Max);
            Assert.Equal(ParamGroup.Director, fw.Group);
            Assert.Equal(20, fw.Order);
            Assert.False(fw.Live);

            var solo = s.CreateDefaults();
            Assert.False(s.IsVisible(CommonParams.ForcedWinner, solo));
            Assert.DoesNotContain(s.GetVisible(solo), d => d.Id == CommonParams.ForcedWinner);
            var team = solo.With("teamMode", ParamValue.Bool(true));
            Assert.True(s.IsVisible(CommonParams.ForcedWinner, team));
        }

        [Fact]
        public void CommonConditionIsKeptWhenTheOverrideRestatesItOrOmitsIt()
        {
            var same = new ParamSchemaBuilder()
                .Add(new EnumParam(CommonParams.Melody, new[] { "ode_to_joy", "fur_elise" }, "fur_elise")
                    .WithVisibleIf(VisibleIf.In(CommonParams.SoundPack, "Melody")))
                .BuildLayer();
            var s = ParamSchema.Merge(CommonParams.Schema, same);
            Assert.Equal(CommonParams.SoundPack, s.Get(CommonParams.Melody).VisibleIf.TargetId);

            var omitted = new ParamSchemaBuilder().Add(new BoolParam(CommonParams.HudPercent, true)).BuildLayer();
            Assert.Equal(CommonParams.Hud, ParamSchema.Merge(CommonParams.Schema, omitted).Get(CommonParams.HudPercent).VisibleIf.TargetId);
        }

        [Fact]
        public void ConflictingConditionThrowsInsteadOfBeingDropped()
        {
            var layer = new ParamSchemaBuilder()
                .Add(new BoolParam("namesOn", true))
                .Add(new StringListParam(CommonParams.LabelNames, 8, 16, new string[0]).WithVisibleIf(VisibleIf.IsTrue("namesOn")))
                .BuildLayer();
            var e = Assert.Throws<ParamSchemaException>(() => ParamSchema.Merge(CommonParams.Schema, layer));
            Assert.Equal(CommonParams.LabelNames, e.ParamId);
            Assert.Contains("visibleIf", e.Message);

            var otherValue = new ParamSchemaBuilder()
                .Add(new ColorParam(CommonParams.BackgroundColor, CommonParams.DefaultBackgroundColor)
                    .WithVisibleIf(VisibleIf.In(CommonParams.Background, "color", "gradient")))
                .BuildLayer();
            Assert.Throws<ParamSchemaException>(() => ParamSchema.Merge(CommonParams.Schema, otherValue));
        }

        [Fact]
        public void ChangingLiveOrPlacementThrowsButRestatingIsAllowed()
        {
            var structural = new ParamSchemaBuilder().Add(new ColorListParam(CommonParams.BallColors, 0, 8, new ColorRgba[0]).AsLive(false)).BuildLayer();
            Assert.Contains("structural", Assert.Throws<ParamSchemaException>(() => ParamSchema.Merge(CommonParams.Schema, structural)).Message);
            var live = new ParamSchemaBuilder().Add(new IntParam(CommonParams.BallCount, 2, 8, 1, 4).AsLive()).BuildLayer();
            Assert.Throws<ParamSchemaException>(() => ParamSchema.Merge(CommonParams.Schema, live));
            var moved = new ParamSchemaBuilder().Add(new IntParam(CommonParams.BallCount, 2, 8, 1, 4).InGroup(ParamGroup.Mode, 5)).BuildLayer();
            Assert.Throws<ParamSchemaException>(() => ParamSchema.Merge(CommonParams.Schema, moved));

            var restated = new ParamSchemaBuilder()
                .Add(new ColorListParam(CommonParams.BallColors, 0, 8, new ColorRgba[0]).AsLive())
                .Add(new IntParam(CommonParams.BallCount, 2, 8, 1, 4).InGroup(ParamGroup.Basic, 20).AsLive(false))
                .BuildLayer();
            var s = ParamSchema.Merge(CommonParams.Schema, restated);
            Assert.True(s.IsLiveEditable(CommonParams.BallColors));
            Assert.Equal(8, ((ColorListParam)s.Get(CommonParams.BallColors)).MaxCount);
            Assert.False(s.IsLiveEditable(CommonParams.BallCount));
        }

        [Fact]
        public void ExplicitLabelKeyOfTheOverrideWins()
        {
            var layer = new ParamSchemaBuilder()
                .Add(new ColorListParam(CommonParams.BallColors, 2, 6, new[] { ColorRgba.Parse("#FF0000"), ColorRgba.Parse("#0000FF") })
                    .WithLabelKey("params.m08.teamColors"))
                .Add(new IntParam(CommonParams.BallCount, 2, 6, 1, 2))
                .BuildLayer();
            var s = ParamSchema.Merge(CommonParams.Schema, layer);
            Assert.Equal("params.m08.teamColors", s.Get(CommonParams.BallColors).LabelKey);
            Assert.Equal("params.ballCount", s.Get(CommonParams.BallCount).LabelKey);
            Assert.True(s.IsLiveEditable(CommonParams.BallColors));
        }
    }

    /// <summary>Themes and melodies delivered as data reach every mode (spec §2.7, §2.10, §9.3).</summary>
    public class DataDeliveredOptionsTests
    {
        static readonly ParamSchema s_common = CommonParams.Build(
            CommonParams.Themes.Concat(new[] { "Aurora" }), CommonParams.Melodies.Concat(new[] { "jingle_bells" }));

        static ParamSchema SchemaFor(string modeId)
        {
            return modeId == "ring_escape"
                ? ParamSchema.Merge(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M01), s_common), RingEscapeLayer())
                : null;
        }

        [Fact]
        public void ForModeWithCommonSchemaKeepsDataOptionsAndFiltersForcedWinner()
        {
            var m01 = CommonParams.ForMode(ModeCatalog.Get(ModeCode.M01), s_common);
            Assert.Contains("jingle_bells", ((EnumParam)m01.Get(CommonParams.Melody)).Options);
            Assert.Contains("Aurora", ((EnumParam)m01.Get(CommonParams.Theme)).Options);
            Assert.False(m01.Contains(CommonParams.ForcedWinner));
            Assert.Equal(s_common.Count - 1, m01.Count);
            Assert.Same(s_common, CommonParams.ForMode(ModeCatalog.Get(ModeCode.M07), s_common));
            Assert.Same(CommonParams.Schema, CommonParams.ForMode(ModeCatalog.Get(ModeCode.M07)));
            Assert.Same(m01, CommonParams.ForMode(ModeCatalog.Get(ModeCode.M02), m01)); // nothing left to remove
        }

        [Fact]
        public void ForModeRejectsBadArguments()
        {
            Assert.Throws<ArgumentNullException>(() => CommonParams.ForMode(ModeCatalog.Get(ModeCode.M01), null));
            Assert.Throws<ArgumentNullException>(() => CommonParams.ForMode(null, s_common));
            Assert.Throws<ArgumentException>(() => CommonParams.ForMode(ModeCatalog.Get(ModeCode.M01), RingEscapeLayer()));
        }

        [Fact]
        public void RemotePresetNamingADataThemeAndMelodyIsAccepted()
        {
            const string json = "[{\"id\":\"r.xmas\",\"name\":\"Xmas\",\"modeId\":\"ring_escape\","
                + "\"values\":{\"theme\":\"Aurora\",\"soundPack\":\"Melody\",\"melody\":\"jingle_bells\"}}]";
            var r = RemotePresets.Load(json, SchemaFor);
            Assert.Empty(r.Rejected);
            var p = Assert.Single(r.Accepted);
            var applied = p.Apply(SchemaFor("ring_escape"), DeviceTier.A);
            Assert.False(applied.HasIssues);
            Assert.Equal("Aurora", applied.Values.GetString(CommonParams.Theme));
            Assert.Equal("jingle_bells", applied.Values.GetString(CommonParams.Melody));
        }
    }

    /// <summary>A library written by a newer build is not silently downgraded on save.</summary>
    public class UserPresetLibraryNewerVersionTests
    {
        const string Entry = "{\"id\":\"u\",\"name\":\"n\",\"modeId\":\"ring_escape\",\"values\":{},\"thumb\":\"x.png\"}";

        [Fact]
        public void NewerVersionIsExposedAndSaveRefusesUnlessForced()
        {
            var lib = UserPresetLibrary.FromJson("{\"schemaVersion\":2,\"presets\":[" + Entry + "]}", out var problems);
            Assert.Single(problems);
            Assert.Single(lib.All);
            Assert.Equal(2, lib.LoadedSchemaVersion);
            Assert.True(lib.IsFromNewerVersion);
            Assert.Contains("newer build", Assert.Throws<InvalidOperationException>(() => lib.ToJson()).Message);
            Assert.Throws<InvalidOperationException>(() => lib.ToJson(false));
            string forced = lib.ToJson(false, allowDowngrade: true);
            Assert.StartsWith("{\"schemaVersion\":1,", forced);
        }

        [Theory]
        [InlineData("{\"schemaVersion\":1,\"presets\":[" + Entry + "]}", 1)]
        [InlineData("{\"presets\":[" + Entry + "]}", 1)]
        [InlineData("{\"schemaVersion\":\"two\",\"presets\":[" + Entry + "]}", 1)]
        [InlineData("{\"schemaVersion\":0,\"presets\":[" + Entry + "]}", 0)]
        [InlineData("{\"schemaVersion\":100000000000000000000000,\"presets\":[" + Entry + "]}", int.MaxValue)]
        [InlineData("not json", 1)]
        public void LoadedVersion(string json, int expected)
        {
            var lib = UserPresetLibrary.FromJson(json, out _);
            Assert.Equal(expected, lib.LoadedSchemaVersion);
            Assert.Equal(expected > UserPresetLibrary.SchemaVersion, lib.IsFromNewerVersion);
            if (!lib.IsFromNewerVersion) Assert.Contains("\"schemaVersion\":1", lib.ToJson(false));
        }

        [Fact]
        public void NewLibraryIsCurrentVersion()
        {
            var lib = new UserPresetLibrary();
            Assert.Equal(UserPresetLibrary.SchemaVersion, lib.LoadedSchemaVersion);
            Assert.False(lib.IsFromNewerVersion);
        }
    }

    /// <summary>Issue and schema-error messages must not depend on the device culture.</summary>
    public class CultureInvariantMessageTests
    {
        static T WithCulture<T>(string name, Func<T> f)
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                return f();
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [Theory]
        [InlineData("sv-SE")]
        [InlineData("fa-IR")]
        [InlineData("ar-SA")]
        public void IntIssueMessagesUseTheAsciiMinus(string culture)
        {
            var schema = new ParamSchemaBuilder()
                .Add(new IntParam("i", -50, 50, 5, -10).WithTierCap(new TierCap(null, null, -20)))
                .Build();
            var issues = WithCulture(culture, () =>
            {
                var list = new List<ParamIssue>();
                schema.SanitizeValue("i", ParamValue.Int(-100), DeviceTier.A, list);
                schema.SanitizeValue("i", ParamValue.Int(100), DeviceTier.A, list);
                schema.SanitizeValue("i", ParamValue.Int(0), DeviceTier.C, list);
                schema.SanitizeValue("i", ParamValue.Int(-12), DeviceTier.A, list);
                return list.Select(x => x.Message).ToList();
            });
            Assert.Equal(new[]
            {
                "-100 is below min -50",
                "100 is above max 50",
                "0 is above the tier C cap -20",
                "-12 snapped to -10",
            }, issues);

            string error = WithCulture(culture, () => Assert.Throws<ParamSchemaException>(() =>
                new ParamSchemaBuilder().Add(new IntParam("j", -5, -10, 1, -7)).Build()).Message);
            Assert.Contains("min -5 is greater than max -10", error);
            error = WithCulture(culture, () => Assert.Throws<ParamSchemaException>(() =>
                new ParamSchemaBuilder().Add(new IntParam("j", -5, 5, 1, -7)).Build()).Message);
            Assert.Contains("default -7 is outside [-5, 5]", error);
        }
    }
}
