using System;
using Ballverse.Core.Projects;
using Xunit;

namespace Ballverse.Core.Tests.Projects
{
    public class AutosaveSchedulerTests
    {
        [Fact]
        public void NothingChangedNoSave()
        {
            var s = new AutosaveScheduler();
            Assert.False(s.IsDirty);
            Assert.Null(s.NextSaveAtMs);
            Assert.False(s.ShouldSave(0));
            Assert.False(s.ShouldSave(long.MaxValue));
            Assert.False(s.OnAppBackgrounded(100));
        }

        [Fact]
        public void SavesOneSecondAfterLastChange()
        {
            var s = new AutosaveScheduler();
            s.MarkChanged(1000);
            Assert.Equal(2000, s.NextSaveAtMs);
            Assert.False(s.ShouldSave(1999));
            Assert.True(s.ShouldSave(2000));
        }

        [Fact]
        public void EachChangeRestartsDebounce()
        {
            var s = new AutosaveScheduler();
            s.MarkChanged(0);
            s.MarkChanged(600);
            s.MarkChanged(1500);
            Assert.False(s.ShouldSave(1000));
            Assert.False(s.ShouldSave(2499));
            Assert.True(s.ShouldSave(2500));
        }

        [Fact]
        public void CompletedSaveClearsDirty()
        {
            var s = new AutosaveScheduler();
            s.MarkChanged(0);
            long token = s.BeginSave();
            s.CompleteSave(token);
            Assert.False(s.IsDirty);
            Assert.False(s.ShouldSave(10000));
            Assert.Null(s.NextSaveAtMs);
        }

        [Fact]
        public void ChangeDuringSaveStaysDirty()
        {
            var s = new AutosaveScheduler();
            s.MarkChanged(0);
            long token = s.BeginSave();
            s.MarkChanged(1100);
            s.CompleteSave(token);
            Assert.True(s.IsDirty);
            Assert.Equal(2100, s.NextSaveAtMs);
            Assert.True(s.ShouldSave(2100));
            s.CompleteSave(s.BeginSave());
            Assert.False(s.IsDirty);
        }

        [Fact]
        public void StaleCompletionDoesNotRegress()
        {
            var s = new AutosaveScheduler();
            s.MarkChanged(0);
            long t1 = s.BeginSave();
            s.MarkChanged(10);
            long t2 = s.BeginSave();
            s.CompleteSave(t2);
            s.CompleteSave(t1);
            Assert.False(s.IsDirty);
            Assert.Throws<ArgumentOutOfRangeException>(() => s.CompleteSave(t2 + 1));
        }

        [Fact]
        public void BackgroundSavesImmediatelyWhenDirty()
        {
            var s = new AutosaveScheduler();
            s.MarkChanged(500);
            Assert.True(s.OnAppBackgrounded(600));
            Assert.True(s.ShouldSave(600));
            Assert.Equal(600, s.NextSaveAtMs);
            s.CompleteSave(s.BeginSave());
            Assert.False(s.OnAppBackgrounded(700));
        }

        [Fact]
        public void FailedSaveRetriesAfterDebounce()
        {
            var s = new AutosaveScheduler();
            s.MarkChanged(0);
            long token = s.BeginSave();
            s.FailSave(token, 1000);
            Assert.True(s.IsDirty);
            Assert.False(s.ShouldSave(1999));
            Assert.True(s.ShouldSave(2000));
        }

        [Fact]
        public void CustomDebounce()
        {
            var s = new AutosaveScheduler(250);
            Assert.Equal(250, s.DebounceMs);
            s.MarkChanged(100);
            Assert.True(s.ShouldSave(350));
            var zero = new AutosaveScheduler(0);
            zero.MarkChanged(5);
            Assert.True(zero.ShouldSave(5));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AutosaveScheduler(-1));
        }

        [Fact]
        public void SimulatedEditingSessionSavesOncePerQuietPeriod()
        {
            // Random bursts of edits: the number of saves equals the number of gaps >= 1 s (plus the final flush).
            var rng = Rng.FromSeed(2026, 10);
            var s = new AutosaveScheduler();
            long now = 0;
            int saves = 0, quietGaps = 0;
            long lastEdit = -1;
            for (int i = 0; i < 500; i++)
            {
                long gap = rng.NextInt(0, 4) == 0 ? rng.NextInt(1000, 5000) : rng.NextInt(1, 900);
                for (long t = now + 1; t <= now + gap; t += 50)
                {
                    if (s.ShouldSave(t))
                    {
                        s.CompleteSave(s.BeginSave());
                        saves++;
                    }
                }
                now += gap;
                if (lastEdit >= 0 && now - lastEdit >= 1000 + 50) quietGaps++;
                s.MarkChanged(now);
                lastEdit = now;
            }
            Assert.True(s.ShouldSave(now + 1000));
            Assert.InRange(saves, quietGaps - 5, quietGaps + 5);
            Assert.True(saves < 500);
        }
    }
}
