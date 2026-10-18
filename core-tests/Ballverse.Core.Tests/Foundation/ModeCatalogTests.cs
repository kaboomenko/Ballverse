using System.Linq;
using Ballverse.Core;
using Xunit;

namespace Ballverse.Core.Tests.Foundation
{
    public class ModeCatalogTests
    {
        [Fact]
        public void HasFifteenModesInCodeOrder()
        {
            Assert.Equal(15, ModeCatalog.All.Count);
            for (int i = 0; i < 15; i++) Assert.Equal((ModeCode)(i + 1), ModeCatalog.All[i].Code);
        }

        [Fact]
        public void FreeByDefaultIsM01ToM05()
        {
            var free = ModeCatalog.All.Where(m => m.FreeByDefault).Select(m => m.Code).ToArray();
            Assert.Equal(new[] { ModeCode.M01, ModeCode.M02, ModeCode.M03, ModeCode.M04, ModeCode.M05 }, free);
        }

        [Fact]
        public void IdsAreUniqueAndResolvable()
        {
            Assert.Equal(15, ModeCatalog.All.Select(m => m.Id).Distinct().Count());
            foreach (var m in ModeCatalog.All)
            {
                Assert.True(ModeCatalog.TryGetById(m.Id, out var found));
                Assert.Same(m, found);
                Assert.Same(m, ModeCatalog.Get(m.Code));
            }
            Assert.False(ModeCatalog.TryGetById("nope", out _));
            Assert.False(ModeCatalog.TryGetById(null, out _));
        }

        [Theory]
        [InlineData("M01", true, ModeCode.M01)]
        [InlineData("M15", true, ModeCode.M15)]
        [InlineData("M16", false, default(ModeCode))]
        [InlineData("M00", false, default(ModeCode))]
        [InlineData("m01", false, default(ModeCode))]
        [InlineData("M1", false, default(ModeCode))]
        [InlineData("M+1", false, default(ModeCode))]
        [InlineData(null, false, default(ModeCode))]
        public void ParsesCodes(string input, bool ok, ModeCode expected)
        {
            Assert.Equal(ok, ModeCatalog.TryParseCode(input, out var code));
            if (ok) Assert.Equal(expected, code);
        }

        [Fact]
        public void WinnerModesMatchSpec()
        {
            // §2.5: director forced winner for M03, M06, M07, M08, M09 (team variant); M10 and M14 have optional competitive variants.
            var winners = ModeCatalog.All.Where(m => m.HasWinner).Select(m => m.Code).ToArray();
            Assert.Contains(ModeCode.M03, winners);
            Assert.Contains(ModeCode.M06, winners);
            Assert.Contains(ModeCode.M07, winners);
            Assert.Contains(ModeCode.M08, winners);
            Assert.Contains(ModeCode.M09, winners);
        }
    }
}
