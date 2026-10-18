using Ballverse.Core;
using Ballverse.Core.Replay;
using Xunit;

namespace Ballverse.Core.Tests.Replay
{
    public class StateHashBuilderTests
    {
        static ulong HashOf(float x, float y)
        {
            var b = new StateHashBuilder();
            b.AddVector(x, y);
            return b.Digest();
        }

        [Fact]
        public void EqualStatesHashEqual()
        {
            Assert.Equal(HashOf(1.25f, -3f), HashOf(1.25f, -3f));
            Assert.Equal(HashOf(0f, 0f), HashOf(-0f, -0f));
            Assert.Equal(HashOf(float.NaN, 0f), HashOf(float.NaN, 0f));
        }

        [Fact]
        public void DifferentStatesHashDifferently()
        {
            Assert.NotEqual(HashOf(1f, 2f), HashOf(2f, 1f));
            Assert.NotEqual(HashOf(1f, 2f), HashOf(1.001f, 2f));
        }

        [Fact]
        public void IsXxHash64OfQuantizedLongs()
        {
            var b = new StateHashBuilder();
            b.AddQuantized(1.5f);
            b.AddInt(7);
            b.AddLong(-1);
            var x = new XxHash64();
            x.AppendULong((ulong)(long)(1.5 * StateHashBuilder.DefaultUnitsPerUnit));
            x.AppendInt(7);
            x.AppendULong(ulong.MaxValue);
            Assert.Equal(x.Digest(), b.Digest());
        }

        [Fact]
        public void ResetStartsOver()
        {
            var b = new StateHashBuilder();
            b.AddVector(5f, 6f);
            b.Reset();
            b.AddVector(1f, 2f);
            Assert.Equal(HashOf(1f, 2f), b.Digest());
        }

        static ulong ReferenceHash(params long[] values)
        {
            var x = new XxHash64();
            foreach (long v in values) x.AppendULong((ulong)v);
            return x.Digest();
        }

        [Fact]
        public void SaturationMatchesReferenceLongs()
        {
            var b = new StateHashBuilder();
            b.AddQuantized(float.PositiveInfinity);
            b.AddQuantized(float.NegativeInfinity);
            b.AddQuantized(3e38f);
            b.AddQuantized(-3e38f);
            b.AddQuantized(float.MaxValue, 1);
            Assert.Equal(ReferenceHash(long.MaxValue, long.MinValue + 1, long.MaxValue, long.MinValue + 1, long.MaxValue), b.Digest());
        }

        [Fact]
        public void NaNHashesAsSentinel()
        {
            var b = new StateHashBuilder();
            b.AddQuantized(float.NaN);
            b.AddQuantized(-float.NaN);
            Assert.Equal(ReferenceHash(long.MinValue, long.MinValue), b.Digest());
            Assert.NotEqual(ReferenceHash(long.MinValue + 1, long.MinValue + 1), b.Digest());
        }

        [Fact]
        public void QuantizationRoundsHalfAwayFromZero()
        {
            var b = new StateHashBuilder();
            b.AddQuantized(2.5f, 1);
            b.AddQuantized(-2.5f, 1);
            b.AddQuantized(0.4f, 1);
            b.AddQuantized(-0f, 1);
            Assert.Equal(ReferenceHash(3, -3, 0, 0), b.Digest());
        }

        [Fact]
        public void ExtremeValuesSaturate()
        {
            var b = new StateHashBuilder();
            b.AddQuantized(float.PositiveInfinity);
            b.AddQuantized(float.NegativeInfinity);
            b.AddQuantized(3e38f);
            Assert.NotEqual(0UL, b.Digest());
            Assert.Throws<System.ArgumentOutOfRangeException>(() => b.AddQuantized(1f, 0));
        }
    }
}
