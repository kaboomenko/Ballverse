using System.Collections.Generic;
using Ballverse.Core;
using Xunit;

namespace Ballverse.Core.Tests.Foundation
{
    public class RngTests
    {
        [Fact]
        public void MatchesPcg32ReferenceVectors()
        {
            // pcg32-demo (pcg-c-basic): pcg32_srandom_r(&rng, 42u, 54u)
            var rng = new Rng(42UL, 54UL);
            uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
            foreach (uint e in expected) Assert.Equal(e, rng.NextUInt());
        }

        [Fact]
        public void SameSeedAndStreamGiveSameSequence()
        {
            var a = Rng.FromSeed(123456789UL, 3);
            var b = Rng.FromSeed(123456789UL, 3);
            for (int i = 0; i < 1000; i++) Assert.Equal(a.NextULong(), b.NextULong());
        }

        [Fact]
        public void DifferentStreamsDiverge()
        {
            var a = Rng.FromSeed(1UL, 0);
            var b = Rng.FromSeed(1UL, 1);
            int equal = 0;
            for (int i = 0; i < 100; i++) if (a.NextUInt() == b.NextUInt()) equal++;
            Assert.True(equal < 3);
        }

        [Fact]
        public void CopyingStructForksTheStream()
        {
            var a = Rng.FromSeed(77UL);
            var copy = a;
            Assert.Equal(a.NextUInt(), copy.NextUInt());
        }

        [Theory]
        [InlineData(1u)]
        [InlineData(2u)]
        [InlineData(7u)]
        [InlineData(1000u)]
        [InlineData(0x80000000u)]
        [InlineData(0xFFFFFFFFu)]
        public void BoundedValuesStayInRange(uint bound)
        {
            var rng = Rng.FromSeed(9UL);
            for (int i = 0; i < 10000; i++) Assert.True(rng.NextUInt(bound) < bound);
        }

        [Fact]
        public void NextIntCoversWholeRangeUniformly()
        {
            var rng = Rng.FromSeed(2024UL);
            var counts = new int[6];
            const int n = 60000;
            for (int i = 0; i < n; i++) counts[rng.NextInt(-3, 3) + 3]++;
            foreach (int c in counts) Assert.InRange(c, n / 6 - 600, n / 6 + 600);
        }

        [Fact]
        public void NextIntHandlesExtremeRange()
        {
            var rng = Rng.FromSeed(5UL);
            for (int i = 0; i < 1000; i++) rng.NextInt(int.MinValue, int.MaxValue);
        }

        [Fact]
        public void FloatsAreInUnitInterval()
        {
            var rng = Rng.FromSeed(11UL);
            for (int i = 0; i < 100000; i++)
            {
                float f = rng.NextFloat();
                Assert.True(f >= 0f && f < 1f);
                double d = rng.NextDouble();
                Assert.True(d >= 0.0 && d < 1.0);
            }
        }

        [Fact]
        public void InvalidBoundsThrow()
        {
            var rng = Rng.FromSeed(1UL);
            Assert.Throws<System.ArgumentOutOfRangeException>(() => rng.NextUInt(0));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => rng.NextInt(5, 5));
        }

        [Fact]
        public void NeighbouringSeedsDoNotCollide()
        {
            var firsts = new HashSet<uint>();
            for (ulong s = 0; s < 1000; s++) firsts.Add(Rng.FromSeed(s).NextUInt());
            Assert.True(firsts.Count > 995);
        }
    }
}
