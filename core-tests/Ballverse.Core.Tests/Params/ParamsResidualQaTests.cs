using System;
using System.Globalization;
using System.Linq;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Xunit;

namespace Ballverse.Core.Tests.Params
{
    /// <summary>Residual QA findings of the merged Params module.</summary>
    public class ParamsResidualQaTests
    {
        [Theory]
        [InlineData("{\"schemaVersion\":2,\"presets\":{\"ring_escape\":[]}}")]
        [InlineData("{\"schemaVersion\":2,\"items\":[]}")]
        public void NewerLibraryWithAChangedRootIsFlaggedAndNotOverwritten(string json)
        {
            var lib = UserPresetLibrary.FromJson(json, out var problems);
            Assert.True(lib.IsFromNewerVersion);
            Assert.Equal(2, lib.LoadedSchemaVersion);
            Assert.Contains(problems, p => p.Contains("presets"));
            Assert.Throws<InvalidOperationException>(() => lib.ToJson());
            Assert.Contains("\"presets\"", lib.ToJson(allowDowngrade: true));
        }

        [Fact]
        public void SoundPacksCanArriveAsData()
        {
            var packs = CommonParams.SoundPacks.Concat(new[] { "Lofi" }).ToArray();
            var common = CommonParams.Build(soundPacks: packs);
            Func<string, ParamSchema> schemaFor = id => id == "ring_escape"
                ? ParamSchema.Merge(CommonParams.ForMode(ModeCatalog.Get(ModeCode.M01), common), TestSchemas.RingEscapeLayer())
                : null;
            var r = RemotePresets.Load("[{\"id\":\"r.lofi\",\"name\":\"Lofi\",\"modeId\":\"ring_escape\",\"values\":{\"soundPack\":\"Lofi\"}}]", schemaFor);
            Assert.Empty(r.Rejected);
            Assert.Equal("Lofi", Assert.Single(r.Accepted).Values.GetString("soundPack"));
            Assert.True(common.Get("melody").VisibleIf != null);
            Assert.Throws<ArgumentException>(() => CommonParams.Build(soundPacks: new[] { "Notes", "Pop" }));
            Assert.Throws<ArgumentException>(() => CommonParams.Build(soundPacks: new[] { "Melody", "Pop" }));
            // Default lists are unchanged.
            Assert.Equal(CommonParams.SoundPacks, ((EnumParam)CommonParams.Build().Get("soundPack")).Options);
        }

        [Theory]
        [InlineData("sv-SE")]
        [InlineData("fa-IR")]
        public void PresetRejectionUsesTheAsciiMinus(string culture)
        {
            var saved = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(culture);
                var r = RemotePresets.Load("{not json", id => null);
                Assert.StartsWith("#-1 ", Assert.Single(r.Rejected).ToString());
            }
            finally
            {
                CultureInfo.CurrentCulture = saved;
            }
        }

        [Fact]
        public void IsTrueAndIsBoolTrueAreEquivalentRestatements()
        {
            var layer = new ParamSchemaBuilder()
                .Add(new BoolParam(CommonParams.HudTimer, false).WithVisibleIf(VisibleIf.Is(CommonParams.Hud, ParamValue.Bool(true))))
                .BuildLayer();
            var merged = ParamSchema.Merge(CommonParams.Schema, layer);
            var hidden = merged.CreateDefaults().With(CommonParams.Hud, ParamValue.Bool(false));
            Assert.False(merged.IsVisible(CommonParams.HudTimer, hidden));
            Assert.True(merged.IsVisible(CommonParams.HudTimer, merged.CreateDefaults()));
            // A genuinely different condition is still rejected.
            var conflicting = new ParamSchemaBuilder()
                .Add(new BoolParam(CommonParams.HudTimer, false).WithVisibleIf(VisibleIf.Is(CommonParams.Hud, ParamValue.Bool(false))))
                .BuildLayer();
            Assert.Throws<ParamSchemaException>(() => ParamSchema.Merge(CommonParams.Schema, conflicting));
        }
    }
}
