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
        public void Release1IsM01M02M05AllFree()
        {
            // Owner decision Р18: release 1 ships exactly Ring Escape, Geometry Bounce and Helix Drop, all free.
            var released = new[] { ModeCode.M01, ModeCode.M02, ModeCode.M05 };
            Assert.Equal(released, ModeCatalog.Released.Select(m => m.Code));
            Assert.Equal(released, ModeCatalog.All.Where(m => m.InRelease).Select(m => m.Code));
            Assert.Equal(released, ModeCatalog.All.Where(m => m.FreeByDefault).Select(m => m.Code));
            foreach (var m in ModeCatalog.All)
            {
                Assert.Equal(m.InRelease, ModeCatalog.IsReleased(m.Code));
                if (m.FreeByDefault) Assert.True(m.InRelease, m.Id + ": free by default but not released");
            }
            Assert.False(ModeCatalog.IsReleased((ModeCode)0));
            Assert.False(ModeCatalog.IsReleased((ModeCode)16));
        }

        [Fact]
        public void Release1IdsAndNames()
        {
            var m02 = ModeCatalog.Get(ModeCode.M02);
            Assert.Equal("geometry_bounce", m02.Id);
            Assert.Equal("Geometry Bounce", m02.NameEn);
            Assert.Equal("Геометрия отскоков", m02.NameRu);
            var m05 = ModeCatalog.Get(ModeCode.M05);
            Assert.Equal("helix_drop", m05.Id);
            Assert.Equal("Helix Drop", m05.NameEn);
            Assert.Equal("Хеликс-дроп", m05.NameRu);
            Assert.Equal("ring_escape", ModeCatalog.Get(ModeCode.M01).Id);
            // Retired ids were never shipped and are not reused.
            Assert.False(ModeCatalog.TryGetById("grow_bounce", out _));
            Assert.False(ModeCatalog.TryGetById("spiral_drop", out _));
            // Codes stay stable.
            Assert.Equal(2, (int)m02.Code);
            Assert.Equal(5, (int)m05.Code);
        }

        [Fact]
        public void ControlFlagsOfReleasedModes()
        {
            // M05 Helix Drop: player control plus the automatic "Авто-ролик" / "Бот" controls (spec §2.9).
            var m05 = ModeCatalog.Get(ModeCode.M05);
            Assert.True(m05.Interactive);
            Assert.True(m05.HasAutoControl);
            Assert.False(m05.HasWinner);
            // M01 "Соревнование" and M02 "Открытая грань" have a winner variant (spec §2.5 release 1).
            Assert.True(ModeCatalog.Get(ModeCode.M01).HasWinner);
            Assert.True(ModeCatalog.Get(ModeCode.M02).HasWinner);
            foreach (var m in ModeCatalog.All)
            {
                if (!m.Interactive) Assert.True(m.HasAutoControl, m.Id);
            }
            Assert.False(ModeCatalog.Get(ModeCode.M04).HasAutoControl);
            Assert.Throws<System.ArgumentException>(() =>
                new ModeInfo(ModeCode.M03, "x", "X", "X", freeByDefault: true, interactive: false, hasWinner: false, inRelease: false, hasAutoControl: true));
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
