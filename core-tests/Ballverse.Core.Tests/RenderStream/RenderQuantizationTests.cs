using System;
using Ballverse.Core;
using Ballverse.Core.RenderStream;
using Xunit;

namespace Ballverse.Core.Tests.RenderStream
{
    public class RenderQuantizationTests
    {
        static readonly RenderQuantization Q = RenderQuantization.Default;

        [Fact]
        public void DefaultResolutionMatchesSpec()
        {
            Assert.Equal(16f, Q.PositionRange);
            Assert.Equal(8f, Q.ScaleMax);
            Assert.Equal(32.0 / 65536.0, Q.PositionStep, 12);   // ≈ 0.49 mm
            Assert.InRange(Q.PositionStep, 0.00048, 0.00049);
            Assert.Equal(2 * Math.PI / 65536.0, RenderQuantization.AngleStep, 12);
            Assert.Equal(8.0 / 65535.0, Q.ScaleStep, 12);
        }

        [Fact]
        public void ExhaustiveRoundTripOfQuantizedValues()
        {
            var custom = new RenderQuantization(10.3f, 2.7f);
            for (int i = 0; i < 65536; i++)
            {
                short s = (short)(i - 32768);
                ushort u = (ushort)i;
                Assert.Equal(s, Q.QuantizePosition(Q.DequantizePosition(s)));
                Assert.Equal(s, custom.QuantizePosition(custom.DequantizePosition(s)));
                Assert.Equal(u, RenderQuantization.QuantizeAngle(RenderQuantization.DequantizeAngle(u)));
                Assert.Equal(u, Q.QuantizeScale(Q.DequantizeScale(u)));
                Assert.Equal(u, custom.QuantizeScale(custom.DequantizeScale(u)));
            }
        }

        [Fact]
        public void RandomParametersRoundTrip()
        {
            var rng = Rng.FromSeed(77);
            for (int k = 0; k < 20; k++)
            {
                var q = new RenderQuantization(rng.NextFloat(0.5f, 1000f), rng.NextFloat(0.1f, 100f));
                for (int i = 0; i < 2000; i++)
                {
                    short s = (short)rng.NextUInt();
                    ushort u = (ushort)rng.NextUInt();
                    Assert.Equal(s, q.QuantizePosition(q.DequantizePosition(s)));
                    Assert.Equal(u, q.QuantizeScale(q.DequantizeScale(u)));
                }
            }
        }

        [Fact]
        public void QuantizationErrorIsAtMostHalfAStep()
        {
            var rng = Rng.FromSeed(78);
            for (int i = 0; i < 10000; i++)
            {
                float x = rng.NextFloat(-15.99f, 15.99f);
                Assert.True(Math.Abs(Q.DequantizePosition(Q.QuantizePosition(x)) - x) <= Q.PositionStep / 2 + 1e-6);
                float s = rng.NextFloat(0f, 8f);
                Assert.True(Math.Abs(Q.DequantizeScale(Q.QuantizeScale(s)) - s) <= Q.ScaleStep / 2 + 1e-6);
                float a = rng.NextFloat(0f, 6.28f);
                Assert.True(Math.Abs(RenderQuantization.DequantizeAngle(RenderQuantization.QuantizeAngle(a)) - a) <= RenderQuantization.AngleStep / 2 + 1e-6);
            }
        }

        [Fact]
        public void PositionsClampAndHandleNonFinite()
        {
            Assert.Equal(0, Q.QuantizePosition(0f));
            Assert.Equal(2048, Q.QuantizePosition(1f));
            Assert.Equal(-2048, Q.QuantizePosition(-1f));
            Assert.Equal(short.MaxValue, Q.QuantizePosition(16f));
            Assert.Equal(short.MaxValue, Q.QuantizePosition(1e9f));
            Assert.Equal(short.MinValue, Q.QuantizePosition(-16f));
            Assert.Equal(short.MinValue, Q.QuantizePosition(-1e9f));
            Assert.Equal(short.MaxValue, Q.QuantizePosition(float.PositiveInfinity));
            Assert.Equal(short.MinValue, Q.QuantizePosition(float.NegativeInfinity));
            Assert.Equal(0, Q.QuantizePosition(float.NaN));
            Assert.Equal(-16f, Q.DequantizePosition(short.MinValue));
        }

        [Fact]
        public void AnglesWrap()
        {
            Assert.Equal(0, RenderQuantization.QuantizeAngle(0f));
            Assert.Equal(16384, RenderQuantization.QuantizeAngle((float)(Math.PI / 2)));
            Assert.Equal(49152, RenderQuantization.QuantizeAngle((float)(-Math.PI / 2)));
            Assert.Equal(32768, RenderQuantization.QuantizeAngle((float)(3 * Math.PI)));
            Assert.Equal(0, RenderQuantization.QuantizeAngle((float)(2 * Math.PI)));
            Assert.Equal(0, RenderQuantization.QuantizeAngle(float.NaN));
            Assert.Equal(0, RenderQuantization.QuantizeAngle(float.PositiveInfinity));
            Assert.InRange(RenderQuantization.DequantizeAngle(65535), 6.28f, (float)(2 * Math.PI));
        }

        [Fact]
        public void ScalesClamp()
        {
            Assert.Equal(0, Q.QuantizeScale(-1f));
            Assert.Equal(0, Q.QuantizeScale(float.NaN));
            Assert.Equal(ushort.MaxValue, Q.QuantizeScale(8f));
            Assert.Equal(ushort.MaxValue, Q.QuantizeScale(100f));
            Assert.Equal(8f, Q.DequantizeScale(ushort.MaxValue));
        }

        [Fact]
        public void QuantizeBodyUsesAllChannels()
        {
            var b = Q.QuantizeBody(4, BodyKind.Polygon, 1f, -1f, (float)Math.PI, 8f, 9, BodyFlags.Visible | BodyFlags.Trail);
            Assert.Equal(new BodyState(4, BodyKind.Polygon, 2048, -2048, 32768, 65535, 9, BodyFlags.Visible | BodyFlags.Trail), b);
        }

        [Fact]
        public void ParameterValidationAndEquality()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderQuantization(0f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderQuantization(-1f, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderQuantization(float.NaN, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderQuantization(float.PositiveInfinity, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderQuantization(1f, 0f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderQuantization(1f, 2e6f));
            Assert.Equal(new RenderQuantization(16f, 8f), RenderQuantization.Default);
            Assert.NotEqual(new RenderQuantization(16f, 4f), RenderQuantization.Default);
            Assert.Equal(new RenderQuantization(3f, 4f).GetHashCode(), new RenderQuantization(3f, 4f).GetHashCode());
        }
    }
}
