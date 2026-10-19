using System.Linq;
using Ballverse.Core.Config;
using Ballverse.Core.Monetization;
using Xunit;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>
    /// Release 1 has one source of truth for its modes (main INTEGRATION.md, merge task for the Monetization track):
    /// <see cref="MonetizationConfig.Release1"/> takes its modes from Foundation's <see cref="ModeCatalog.Released"/> and its
    /// free set equals the Remote Config default (spec §5.0.2, §9.3 <c>free_modes</c> = <c>["M01","M02","M05"]</c>).
    /// </summary>
    public class MonetizationCrossModuleTests
    {
        [Fact]
        public void Release1ModesAreFoundationReleasedModes()
        {
            Assert.Equal(ModeCatalog.Released.Select(m => m.Code).ToArray(), MonetizationConfig.Release1.ReleasedModes.ToArray());
            for (int m = 0; m <= 16; m++)
                Assert.Equal(ModeCatalog.IsReleased((ModeCode)m), MonetizationConfig.Release1.IsReleased((ModeCode)m));
        }

        [Fact]
        public void Release1FreeSetIsTheRemoteConfigDefault()
        {
            var defaults = RemoteConfigValues.Defaults.EffectiveFreeModes.OrderBy(m => m).ToArray();
            Assert.Equal(defaults, MonetizationConfig.Release1.FreeModes.ToArray());
            Assert.Equal(defaults, MonetizationConfig.FullCatalog.FreeModes.ToArray());
            Assert.Equal(ModeCatalog.All.Where(m => m.FreeByDefault).Select(m => m.Code).ToArray(), defaults);
        }

        [Fact]
        public void FullCatalogModesAreTheWholeFoundationCatalog()
        {
            Assert.Equal(ModeCatalog.All.Select(m => m.Code).ToArray(), MonetizationConfig.FullCatalog.ReleasedModes.ToArray());
        }
    }
}
