using System;
using Ballverse.Core.Audio;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    public class SoundEventQueueTests
    {
        [Fact]
        public void FifoWithWrapAround()
        {
            var q = new SoundEventQueue(4);
            Assert.Equal(4, q.Capacity);
            int nextIn = 0, nextOut = 0;
            for (int round = 0; round < 50; round++)
            {
                for (int i = 0; i < 3; i++) Assert.True(q.TryEnqueue(SoundEvent.Hit(nextIn++, 0, 1f)));
                Assert.True(q.TryPeek(out SoundEvent p));
                Assert.Equal(nextOut, p.Tick);
                for (int i = 0; i < 3; i++)
                {
                    Assert.True(q.TryDequeue(out SoundEvent e));
                    Assert.Equal(nextOut++, e.Tick);
                }
                Assert.Equal(0, q.Count);
            }
            Assert.False(q.TryDequeue(out _));
            Assert.False(q.TryPeek(out _));
        }

        [Fact]
        public void FullQueueRefusesAndCountsDrops()
        {
            var q = new SoundEventQueue(3);
            Assert.Equal(4, q.Capacity);
            for (int i = 0; i < 4; i++) Assert.True(q.TryEnqueue(SoundEvent.Hit(i, 0, 1f)));
            Assert.False(q.TryEnqueue(SoundEvent.Hit(9, 0, 1f)));
            Assert.Equal(1, q.Dropped);
            Assert.Equal(4, q.Count);
            q.Clear();
            Assert.Equal(0, q.Count);
            Assert.True(q.TryEnqueue(SoundEvent.Hit(10, 0, 1f)));
        }

        [Fact]
        public void InvalidCapacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new SoundEventQueue(0));
            Assert.Equal(2, new SoundEventQueue(1).Capacity);
        }
    }
}
