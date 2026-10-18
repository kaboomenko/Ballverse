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

        [Fact]
        public void OutputFramesTravelWithTheirEvents()
        {
            var q = new SoundEventQueue(8);
            Assert.True(q.TryEnqueue(SoundEvent.Hit(1, 0, 1f)));
            Assert.True(q.TryEnqueue(SoundEvent.Hit(2, 0, 1f), 12345L));
            Assert.True(q.TryPeek(out SoundEvent e, out long f));
            Assert.Equal(1, e.Tick);
            Assert.Equal(SoundEventQueue.AtTick, f);
            Assert.True(q.TryDequeue(out _));
            Assert.True(q.TryPeek(out e, out f));
            Assert.Equal(2, e.Tick);
            Assert.Equal(12345L, f);
        }

        [Fact]
        public void SpscQueueConcurrentProducerConsumerKeepsFifo()
        {
            const int count = 60000;
            var q = new SoundEventQueue(256);
            int errors = 0;
            var producer = new System.Threading.Thread(() =>
            {
                for (int i = 0; i < count; i++)
                {
                    var e = SoundEvent.Hit(i, i & 1023, 1f);
                    while (!q.TryEnqueue(e, (long)i * 3)) System.Threading.Thread.Yield();
                }
            });
            producer.Start();
            int next = 0;
            while (next < count)
            {
                if (q.TryPeek(out SoundEvent e, out long frame))
                {
                    if (e.Tick != next || frame != (long)next * 3 || e.BodyId != (next & 1023)) errors++;
                    q.TryDequeue(out _);
                    next++;
                }
                else System.Threading.Thread.Yield();
            }
            producer.Join();
            Assert.Equal(0, errors);
            Assert.Equal(0, q.Count);
            Assert.True(q.Dropped > 0); // the producer did hit a full queue and retried
        }
    }
}
