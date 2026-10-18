using System;
using System.Globalization;
using Ballverse.Core.Params;
using Xunit;

namespace Ballverse.Core.Tests.Params
{
    public class CanonicalFloatTests
    {
        [Theory]
        [InlineData(0f, "0.0")]
        [InlineData(1f, "1.0")]
        [InlineData(-1f, "-1.0")]
        [InlineData(0.25f, "0.25")]
        [InlineData(0.1f, "0.1")]
        [InlineData(0.3f, "0.3")]
        [InlineData(1.35f, "1.35")]
        [InlineData(30f, "30.0")]
        [InlineData(1234567f, "1234567.0")]
        [InlineData(1e10f, "10000000000.0")]
        [InlineData(1.5e-5f, "0.000015")]
        [InlineData(-123.456f, "-123.456")]
        [InlineData(16777216f, "16777216.0")]
        [InlineData(float.MaxValue, "340282350000000000000000000000000000000.0")]
        [InlineData(float.Epsilon, "0.000000000000000000000000000000000000000000001")]
        public void FormatsShortestPlain(float value, string expected)
        {
            Assert.Equal(expected, CanonicalFloat.Format(value));
        }

        [Fact]
        public void NegativeZeroIsZero()
        {
            Assert.Equal("0.0", CanonicalFloat.Format(-0f));
        }

        [Fact]
        public void RejectsNonFinite()
        {
            Assert.Throws<ArgumentException>(() => CanonicalFloat.Format(float.NaN));
            Assert.Throws<ArgumentException>(() => CanonicalFloat.Format(float.PositiveInfinity));
        }

        [Fact]
        public void RoundTripsRandomFloatsAndIsShortest()
        {
            var rng = Rng.FromSeed(2026, 7);
            for (int i = 0; i < 20000; i++)
            {
                float f = BitConverter.Int32BitsToSingle((int)rng.NextUInt());
                if (float.IsNaN(f) || float.IsInfinity(f)) continue;
                string s = CanonicalFloat.Format(f);
                Assert.Equal(f == 0 ? 0f : f, float.Parse(s, CultureInfo.InvariantCulture));
                Assert.Contains(".", s);
                Assert.DoesNotContain("E", s);
                // agrees with .NET Core's shortest round-trip digits
                Assert.Equal(double.Parse(f.ToString("R", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture),
                    double.Parse(s, CultureInfo.InvariantCulture));
            }
        }
    }
}
