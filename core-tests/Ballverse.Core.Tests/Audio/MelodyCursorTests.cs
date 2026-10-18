using System;
using Ballverse.Core.Audio;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    public class MelodyCursorTests
    {
        [Fact]
        public void EachHitPlaysNextNoteAndLoops()
        {
            var c = new MelodyCursor(MelodyTests.Scale(3));
            int[] expected = { 60, 61, 62, 60, 61, 62, 60 };
            for (int i = 0; i < expected.Length; i++)
            {
                Assert.True(c.TryAdvance(i * 4800L, out MelodyNote n));
                Assert.Equal(expected[i], n.Pitch);
            }
            Assert.Equal(2, c.Loops);
            Assert.Equal(1, c.NextIndex);
        }

        [Fact]
        public void TwentyNotesPerSecondThenExcessDoesNotAdvance()
        {
            var c = new MelodyCursor(MelodyTests.Scale(64));
            for (int i = 0; i < 20; i++) Assert.True(c.TryAdvance(0, out _));
            Assert.False(c.TryAdvance(0, out _));
            Assert.False(c.TryAdvance(47999, out _));
            Assert.Equal(20, c.NextIndex);
            // Exactly one second after the oldest: allowed, and plays note 20 (nothing was skipped).
            Assert.True(c.TryAdvance(48000, out MelodyNote n));
            Assert.Equal(80, n.Pitch);
            // All 20 notes at frame 0 left the window together, so 19 more fit at 48000, then the limit applies again.
            for (int i = 0; i < 19; i++) Assert.True(c.TryAdvance(48000, out _));
            Assert.False(c.TryAdvance(48000, out _));
            Assert.Equal(40, c.NextIndex);
        }

        [Fact]
        public void SustainedRateIsTwentyPerSecond()
        {
            var c = new MelodyCursor(MelodyTests.Scale(5));
            int played = 0;
            // A hit every tick (60/s) for 10 s.
            for (int t = 0; t < 600; t++) if (c.TryAdvance(t * 800L, out _)) played++;
            Assert.Equal(200, played);
        }

        [Fact]
        public void ResetRewinds()
        {
            var c = new MelodyCursor(MelodyTests.Scale(4));
            for (int i = 0; i < 20; i++) c.TryAdvance(0, out _);
            c.Reset();
            Assert.Equal(0, c.NextIndex);
            Assert.Equal(0, c.Loops);
            Assert.True(c.TryAdvance(0, out MelodyNote n));
            Assert.Equal(60, n.Pitch);
        }

        [Fact]
        public void NullMelodyThrows()
        {
            Assert.Throws<ArgumentNullException>(() => new MelodyCursor(null));
        }
    }
}
