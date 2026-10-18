using System.Globalization;
using System.Linq;
using Ballverse.Core;
using Ballverse.Core.Params;
using Ballverse.UI.Localization;
using Ballverse.UI.Params;
using Xunit;

namespace Ballverse.UI.Tests
{
    public class ParamPanelModelTests
    {
        static ParamPanelModel Make(DeviceTier tier = DeviceTier.A)
        {
            var schema = CommonParams.ForMode(ModeCatalog.Get(ModeCode.M01));
            return new ParamPanelModel(schema, ParamSet.Empty, tier);
        }

        [Fact]
        public void GroupsAndRowsFollowTheSchema()
        {
            var m = Make();
            var groups = m.VisibleGroups();
            Assert.Equal(new[] { ParamGroup.Basic, ParamGroup.Physics, ParamGroup.Look, ParamGroup.Sound, ParamGroup.Director }, groups);
            var basic = m.Rows(ParamGroup.Basic);
            Assert.Equal(new[] { "timeScale", "ballCount", "ballSize", "theme" }, basic.Select(r => r.Id));
            Assert.Equal(ParamEditorKind.Slider, basic[0].Kind);
            Assert.Equal(ParamEditorKind.IntSlider, basic[1].Kind);
            Assert.Equal(ParamEditorKind.Chips, basic[3].Kind);
            var look = m.Rows(ParamGroup.Look).Select(r => r.Id).ToList();
            Assert.DoesNotContain("backgroundColor", look);
            Assert.DoesNotContain("labelNames", look);
            var trail = m.Row("trail");
            Assert.Equal(ParamEditorKind.Segmented, trail.Kind);
            Assert.Equal(ParamEditorKind.Seed, m.Row("seed").Kind);
            Assert.Equal(ParamEditorKind.ColorList, m.Row("ballColors").Kind);
            Assert.Equal(ParamEditorKind.Toggle, m.Row("hud").Kind);
            Assert.True(m.Row("targetDuration").AllowUnlimited);
        }

        [Fact]
        public void EditingAVisibilityTargetReportsTheChange()
        {
            var m = Make();
            var e = m.Edit("background", ParamValue.String("color"), running: false);
            Assert.Equal(ParamEditKind.ApplyNow, e.Kind);
            Assert.True(e.VisibilityChanged);
            Assert.Contains("backgroundColor", m.Rows(ParamGroup.Look).Select(r => r.Id));
            var e2 = m.Edit("glow", ParamValue.Float(0.7), running: false);
            Assert.False(e2.VisibilityChanged);
        }

        [Fact]
        public void LiveEditsApplyWhileRunningStructuralOnesAreStaged()
        {
            var m = Make();
            var live = m.Edit("timeScale", ParamValue.Float(2.0), running: true);
            Assert.Equal(ParamEditKind.ApplyNow, live.Kind);
            Assert.Equal(2.0, m.Applied.Get("timeScale").AsFloat, 3);

            var structural = m.Edit("ballCount", ParamValue.Int(12), running: true);
            Assert.Equal(ParamEditKind.Staged, structural.Kind);
            Assert.True(m.HasPending);
            Assert.True(m.Row("ballCount").Pending);
            Assert.Equal(1, m.Applied.GetInt("ballCount"));
            Assert.Equal(12, m.Effective.GetInt("ballCount"));

            var values = m.CommitPendingForRestart();
            Assert.Equal(12, values.GetInt("ballCount"));
            Assert.False(m.HasPending);
            Assert.Equal(12, m.Applied.GetInt("ballCount"));
        }

        [Fact]
        public void StagingBackToTheAppliedValueClearsPending()
        {
            var m = Make();
            m.Edit("ballCount", ParamValue.Int(12), running: true);
            m.Edit("ballCount", ParamValue.Int(1), running: true);
            Assert.False(m.HasPending);
        }

        [Fact]
        public void EditsAreSanitizedAndUnchangedIsReported()
        {
            var m = Make();
            var e = m.Edit("timeScale", ParamValue.Float(99), running: false);
            Assert.Equal(3.0, e.Value.AsFloat, 3);
            Assert.Equal(ParamEditKind.Unchanged, m.Edit("timeScale", ParamValue.Float(3.0), running: false).Kind);
        }

        [Fact]
        public void SyncAppliedDropsPendingEditsAlreadyApplied()
        {
            var m = Make();
            m.Edit("ballCount", ParamValue.Int(12), running: true);
            m.Edit("ballSize", ParamValue.Float(1.0), running: true);
            var applied = m.Applied.With("ballCount", ParamValue.Int(12));
            m.SyncApplied(applied);
            Assert.Equal(1, m.PendingCount);
            Assert.True(m.IsPending("ballSize"));
        }

        [Fact]
        public void TierCapLowersTheSliderMaximum()
        {
            var layer = new ParamSchemaBuilder()
                .Add(new IntParam("ballCount", 1, 100, 1, 10).WithTierCap(new TierCap(100, 60, 30)))
                .BuildLayer();
            var schema = ParamSchema.Merge(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M01)), layer);
            var m = new ParamPanelModel(schema, ParamSet.Empty, DeviceTier.C);
            var row = m.Row("ballCount");
            Assert.Equal(30, row.Max);
            Assert.True(row.TierCapped);
            Assert.Equal(30, m.Edit("ballCount", ParamValue.Int(80), running: false).Value.AsInt);
        }
    }

    public class ParamValueFormatterTests
    {
        static Localizer L(string lang)
        {
            return new Localizer(new FileTables(), lang);
        }

        [Theory]
        [InlineData(1.0, 0)]
        [InlineData(0.5, 1)]
        [InlineData(0.05, 2)]
        [InlineData(0.01, 2)]
        [InlineData(0.001, 3)]
        [InlineData(0.0001, 3)]
        public void Decimals(double step, int expected)
        {
            Assert.Equal(expected, ParamValueFormatter.DecimalsForStep(step));
        }

        [Fact]
        public void UnitsAreLocalized()
        {
            Assert.Equal("1.50×", ParamValueFormatter.FormatWithUnit(1.5, 0.05, "x", L("en")));
            Assert.Equal("1,50×", ParamValueFormatter.FormatWithUnit(1.5, 0.05, "x", L("ru")));
            Assert.Equal("30 m/s", ParamValueFormatter.FormatWithUnit(30, 1, "m/s", L("en")));
            Assert.Equal("30 м/с", ParamValueFormatter.FormatWithUnit(30, 1, "m/s", L("ru")));
            Assert.Equal("7", ParamValueFormatter.FormatWithUnit(7, 1, null, L("en")));
            Assert.Equal("7 furlongs", ParamValueFormatter.FormatWithUnit(7, 1, "furlongs", L("en")));
        }

        [Fact]
        public void RowValues()
        {
            var m = new ParamPanelModel(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M01)), ParamSet.Empty, DeviceTier.A);
            var en = L("en");
            Assert.Equal("Unlimited", ParamValueFormatter.Format(m.Row("targetDuration"), en));
            Assert.Equal("Short", ParamValueFormatter.Format(m.Row("trail"), en));
            Assert.Equal("On", ParamValueFormatter.Format(m.Row("hud"), en));
            Assert.Equal("Theme colours", ParamValueFormatter.Format(m.Row("ballColors"), en));
            Assert.Equal("80%", ParamValueFormatter.Format(m.Row("sfxVolume"), en));
            Assert.Equal("Short", ParamValueFormatter.Format(m.Row("trail"), en));
            Assert.Equal("Короткий", ParamValueFormatter.Format(m.Row("trail"), L("ru")));
        }

        [Theory]
        [InlineData(0.26, 0.25, 3.0, 0.05, 0.25)]
        [InlineData(0.28, 0.25, 3.0, 0.05, 0.30)]
        [InlineData(10.0, 0.25, 3.0, 0.05, 3.0)]
        [InlineData(-1.0, 0.0, 1.0, 0.1, 0.0)]
        [InlineData(0.97, 0.0, 0.95, 0.1, 0.9)]
        public void SnapToStep(double v, double min, double max, double step, double expected)
        {
            Assert.Equal(expected, ParamValueFormatter.SnapToStep(v, min, max, step), 6);
        }
    }
}
