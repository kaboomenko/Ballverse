using System;
using Ballverse.Core.Audio;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    public class HapticsLimiterTests
    {
        [Fact]
        public void AtMostOncePer80ms()
        {
            var h = new HapticsLimiter(1f, 11f);
            Assert.True(h.TryTrigger(0, 11f, out float i0));
            Assert.Equal(1f, i0);
            Assert.False(h.TryTrigger(3839, 11f, out _));
            Assert.True(h.TryTrigger(3840, 11f, out _));
        }

        [Fact]
        public void TickBoundary()
        {
            // 80 ms = 4.8 ticks: 4 ticks later is too soon, 5 ticks later allowed.
            var h = new HapticsLimiter(1f, 11f);
            Assert.True(h.TryTrigger(SoundEvent.Hit(0, 1, 5f), out _));
            Assert.False(h.TryTrigger(SoundEvent.Hit(4, 2, 5f), out _));
            Assert.True(h.TryTrigger(SoundEvent.Hit(5, 3, 5f), out _));
        }

        [Fact]
        public void IntensityFollowsImpulseCurveAndWeakHitsDoNotConsume()
        {
            var h = new HapticsLimiter(1f, 11f);
            Assert.False(h.TryTrigger(0, 0.5f, out float none));
            Assert.Equal(0f, none);
            Assert.True(h.TryTrigger(10, 6f, out float half));
            Assert.Equal(0.5f, half, 6);
            Assert.False(h.TryTrigger(20, 1f, out _));
            Assert.True(h.TryTrigger(10 + 3840, 1f, out float min));
            Assert.Equal(0.15f, min);
        }

        [Fact]
        public void DisabledAndReset()
        {
            var h = new HapticsLimiter(SoundPackCatalog.Get(SoundPackId.Notes));
            h.Enabled = false;
            Assert.False(h.TryTrigger(0, 100f, out _));
            h.Enabled = true;
            Assert.True(h.TryTrigger(0, 100f, out _));
            Assert.False(h.TryTrigger(1, 100f, out _));
            h.Reset();
            Assert.True(h.TryTrigger(1, 100f, out _));
            Assert.Throws<ArgumentException>(() => new HapticsLimiter(1f, 1f));
            Assert.Throws<ArgumentException>(() => new HapticsLimiter(float.NaN, 1f));
        }
    }
}
