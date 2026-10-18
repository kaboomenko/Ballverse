using System;
using Ballverse.Core;
using Ballverse.Core.Replay;
using Xunit;

namespace Ballverse.Core.Tests.Replay
{
    public class ReplayQuantizationTests
    {
        [Fact]
        public void CoordinateRoundTripWithinHalfMillimetre()
        {
            var rng = Rng.FromSeed(1, 1);
            for (int i = 0; i < 20000; i++)
            {
                float x = rng.NextFloat(-SimConstants.ArenaHeight, SimConstants.ArenaHeight);
                float back = ReplayQuantization.DequantizeCoordinate(ReplayQuantization.QuantizeCoordinate(x));
                Assert.True(Math.Abs(back - x) <= 0.0005f + 1e-6f, x + " -> " + back);
            }
        }

        [Fact]
        public void CoordinateEdgeCases()
        {
            Assert.Equal(0, ReplayQuantization.QuantizeCoordinate(0f));
            Assert.Equal(0, ReplayQuantization.QuantizeCoordinate(-0f));
            Assert.Equal(4500, ReplayQuantization.QuantizeCoordinate(4.5f));
            Assert.Equal(-8000, ReplayQuantization.QuantizeCoordinate(-8f));
            Assert.Equal(1, ReplayQuantization.QuantizeCoordinate(0.0005f));     // half away from zero
            Assert.Equal(-1, ReplayQuantization.QuantizeCoordinate(-0.0005f));
            Assert.Equal(ReplayQuantization.MaxCoordinateUnits, ReplayQuantization.QuantizeCoordinate(1e9f));
            Assert.Equal(-ReplayQuantization.MaxCoordinateUnits, ReplayQuantization.QuantizeCoordinate(float.NegativeInfinity));
            Assert.Throws<ArgumentException>(() => ReplayQuantization.QuantizeCoordinate(float.NaN));
        }

        static double AngleDistance(double a, double b)
        {
            double d = Math.Abs(a - b) % (2 * Math.PI);
            return Math.Min(d, 2 * Math.PI - d);
        }

        [Fact]
        public void AngleRoundTripWithinHalfStep()
        {
            var rng = Rng.FromSeed(2, 1);
            double bound = Math.PI / ReplayQuantization.AngleSteps + 1e-5;
            for (int i = 0; i < 20000; i++)
            {
                float a = rng.NextFloat(-20f, 20f);
                float back = ReplayQuantization.DequantizeAngle(ReplayQuantization.QuantizeAngle(a));
                Assert.InRange(back, 0f, (float)(2 * Math.PI));
                Assert.True(AngleDistance(a, back) <= bound, a + " -> " + back);
            }
        }

        [Fact]
        public void AngleEdgeCases()
        {
            Assert.Equal(0, ReplayQuantization.QuantizeAngle(0f));
            Assert.Equal(16384, ReplayQuantization.QuantizeAngle((float)(Math.PI / 2)));
            Assert.Equal(32768, ReplayQuantization.QuantizeAngle((float)Math.PI));
            Assert.Equal(49152, ReplayQuantization.QuantizeAngle((float)(-Math.PI / 2)));
            Assert.Equal(0, ReplayQuantization.QuantizeAngle((float)(2 * Math.PI)));     // wraps
            Assert.Equal(0, ReplayQuantization.QuantizeAngle(-1e-9f));
            Assert.Throws<ArgumentException>(() => ReplayQuantization.QuantizeAngle(float.NaN));
            Assert.Throws<ArgumentException>(() => ReplayQuantization.QuantizeAngle(float.PositiveInfinity));
        }

        [Fact]
        public void ExhaustiveAngleDequantizeQuantizeIsIdentity()
        {
            for (int q = 0; q < 65536; q++)
            {
                Assert.Equal((ushort)q, ReplayQuantization.QuantizeAngle(ReplayQuantization.DequantizeAngle((ushort)q)));
            }
        }

        [Fact]
        public void ExhaustiveUnitDequantizeQuantizeIsIdentity()
        {
            for (int q = 0; q <= 65535; q++)
            {
                Assert.Equal((ushort)q, ReplayQuantization.QuantizeUnit(ReplayQuantization.DequantizeUnit((ushort)q)));
            }
            for (int q = -32767; q <= 32767; q++)
            {
                Assert.Equal((short)q, ReplayQuantization.QuantizeSignedUnit(ReplayQuantization.DequantizeSignedUnit((short)q)));
            }
        }

        [Fact]
        public void UnitRoundTripWithinHalfStep()
        {
            var rng = Rng.FromSeed(3, 1);
            for (int i = 0; i < 20000; i++)
            {
                float v = rng.NextFloat();
                Assert.True(Math.Abs(ReplayQuantization.DequantizeUnit(ReplayQuantization.QuantizeUnit(v)) - v) <= 0.5 / 65535 + 1e-7);
                float s = rng.NextFloat(-1f, 1f);
                Assert.True(Math.Abs(ReplayQuantization.DequantizeSignedUnit(ReplayQuantization.QuantizeSignedUnit(s)) - s) <= 0.5 / 32767 + 1e-7);
            }
            Assert.Equal(0, ReplayQuantization.QuantizeUnit(-3f));
            Assert.Equal(65535, ReplayQuantization.QuantizeUnit(1f));
            Assert.Equal(65535, ReplayQuantization.QuantizeUnit(7f));
            Assert.Equal(-32767, ReplayQuantization.QuantizeSignedUnit(-2f));
            Assert.Equal(32767, ReplayQuantization.QuantizeSignedUnit(1f));
            Assert.Throws<ArgumentException>(() => ReplayQuantization.QuantizeUnit(float.NaN));
        }

        [Fact]
        public void ImpulseQuantization()
        {
            Assert.Equal(0, ReplayQuantization.QuantizeImpulse(-1f));
            Assert.Equal(256, ReplayQuantization.QuantizeImpulse(1f));
            Assert.Equal(ushort.MaxValue, ReplayQuantization.QuantizeImpulse(1e6f));
            var rng = Rng.FromSeed(4, 1);
            for (int i = 0; i < 10000; i++)
            {
                float v = rng.NextFloat(0f, 255f);
                Assert.True(Math.Abs(ReplayQuantization.DequantizeImpulse(ReplayQuantization.QuantizeImpulse(v)) - v) <= 0.5f / 256 + 1e-4f);
            }
        }

        [Fact]
        public void FixedQuantization()
        {
            Assert.Equal(1571, ReplayQuantization.QuantizeFixed(1.5708f, 1000));
            Assert.Equal(-2500, ReplayQuantization.QuantizeFixed(-2.5f, 1000));
            Assert.Equal(int.MaxValue, ReplayQuantization.QuantizeFixed(1e12f, 1000));
            Assert.Equal(int.MinValue, ReplayQuantization.QuantizeFixed(-1e12f, 1000));
            Assert.Equal(-2.5f, ReplayQuantization.DequantizeFixed(-2500, 1000));
            Assert.Throws<ArgumentOutOfRangeException>(() => ReplayQuantization.QuantizeFixed(1f, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => ReplayQuantization.DequantizeFixed(1, -1));
        }

        [Fact]
        public void QuantizedInputsSurviveTheReplay()
        {
            var ms = new System.IO.MemoryStream();
            var w = new ReplayWriter(ms, ReplayTestUtil.Header());
            // M11: angle + force; M04: x coordinate.
            w.AddInput(10, 1, new int[] { ReplayQuantization.QuantizeAngle(1.2f), ReplayQuantization.QuantizeUnit(0.75f) });
            w.AddInput(20, 2, new[] { ReplayQuantization.QuantizeCoordinate(-3.217f) });
            w.Finish(20);
            var inputs = ReplayReader.Read(ms.ToArray()).Inputs;
            Assert.Equal(1.2f, ReplayQuantization.DequantizeAngle((ushort)inputs[0].Values[0]), 3);
            Assert.Equal(0.75f, ReplayQuantization.DequantizeUnit((ushort)inputs[0].Values[1]), 4);
            Assert.Equal(-3.217f, ReplayQuantization.DequantizeCoordinate(inputs[1].Values[0]), 3);
        }
    }
}
