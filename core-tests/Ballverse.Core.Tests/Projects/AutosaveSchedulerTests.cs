using System;
using System.Collections.Generic;
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
            var s = new AutosaveScheduler(maxWaitMs: 0); // the debounce rule alone; the max wait is tested separately
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

        // ---- running save ----

        [Fact]
        public void NoSecondSaveWhileOneIsRunning()
        {
            var s = new AutosaveScheduler();
            s.MarkChanged(0);
            Assert.True(s.ShouldSave(1000));
            long token = s.BeginSave();
            Assert.True(s.IsSaving);
            Assert.False(s.ShouldSave(1016));
            Assert.False(s.ShouldSave(100000));
            Assert.False(s.OnAppBackgrounded(1020)); // the running save covers every change
            s.CompleteSave(token);
            Assert.False(s.IsSaving);
            Assert.False(s.ShouldSave(100000));
        }

        [Fact]
        public void ChangeDuringRunningSaveIsSavedAfterIt()
        {
            var s = new AutosaveScheduler();
            s.MarkChanged(0);
            long token = s.BeginSave();
            s.MarkChanged(1100);
            Assert.False(s.ShouldSave(2100)); // still running
            Assert.True(s.OnAppBackgrounded(1200)); // newer changes need another save
            s.CompleteSave(token);
            Assert.True(s.ShouldSave(1200)); // right after the running save completes
        }

        [Fact]
        public void FailedSaveEndsTheRunningState()
        {
            var s = new AutosaveScheduler();
            s.MarkChanged(0);
            long token = s.BeginSave();
            s.FailSave(token, 1000);
            Assert.False(s.IsSaving);
            Assert.True(s.ShouldSave(2000));
        }

        [Fact]
        public void StaleCompletionDoesNotEndTheNewerRunningSave()
        {
            var s = new AutosaveScheduler();
            s.MarkChanged(0);
            long t1 = s.BeginSave();
            s.MarkChanged(10);
            long t2 = s.BeginSave();
            s.CompleteSave(t1);
            Assert.True(s.IsSaving);
            s.CompleteSave(t2);
            Assert.False(s.IsSaving);
        }

        // ---- maximum wait during continuous editing ----

        [Fact]
        public void ContinuousEditingStillSavesWithinMaxWait()
        {
            var s = new AutosaveScheduler();
            Assert.Equal(AutosaveScheduler.DefaultMaxWaitMs, s.MaxWaitMs);
            var savesAt = new List<long>();
            for (long t = 0; t <= 60000; t += 16)
            {
                s.MarkChanged(t);
                if (s.ShouldSave(t))
                {
                    savesAt.Add(t);
                    s.CompleteSave(s.BeginSave());
                }
            }
            Assert.NotEmpty(savesAt);
            Assert.InRange(savesAt[0], 5000, 5016);
            for (int i = 1; i < savesAt.Count; i++) Assert.InRange(savesAt[i] - savesAt[i - 1], 4984, 5032);
            Assert.InRange(savesAt.Count, 11, 12);
        }

        [Fact]
        public void MaxWaitCountsFromTheFirstUnsavedChange()
        {
            var s = new AutosaveScheduler(1000, 3000);
            s.MarkChanged(100);
            s.MarkChanged(900);
            s.MarkChanged(1800);
            s.MarkChanged(2700);
            Assert.Equal(3100, s.NextSaveAtMs);
            s.MarkChanged(3500); // past the bound: due now
            Assert.Equal(3500, s.NextSaveAtMs);
            Assert.True(s.ShouldSave(3500));
            s.CompleteSave(s.BeginSave());
            s.MarkChanged(4000); // new window
            Assert.Equal(5000, s.NextSaveAtMs);
        }

        [Fact]
        public void MaxWaitDisabledKeepsPureDebounce()
        {
            var s = new AutosaveScheduler(1000, 0);
            for (long t = 0; t <= 20000; t += 100) s.MarkChanged(t);
            Assert.Equal(21000, s.NextSaveAtMs);
            Assert.Throws<ArgumentOutOfRangeException>(() => new AutosaveScheduler(1000, -1));
        }

        [Fact]
        public void HugeMaxWaitMeansNoBound()
        {
            var s = new AutosaveScheduler(1000, long.MaxValue);
            s.MarkChanged(10_000);
            Assert.False(s.ShouldSave(10_000));
            Assert.Equal(11_000, s.NextSaveAtMs);
            s.MarkChanged(10_900);
            Assert.False(s.ShouldSave(11_500));
            Assert.True(s.ShouldSave(11_900));
        }

        [Fact]
        public void HugeDebounceSavesOnlyOnBackgroundOrMaxWait()
        {
            var s = new AutosaveScheduler(long.MaxValue, 0);
            s.MarkChanged(10_000);
            Assert.False(s.ShouldSave(10_000));
            Assert.Equal(long.MaxValue, s.NextSaveAtMs);
            Assert.False(s.ShouldSave(long.MaxValue - 1));
            Assert.True(s.OnAppBackgrounded(20_000));
            Assert.True(s.ShouldSave(20_000));

            var bounded = new AutosaveScheduler(long.MaxValue, 5000);
            bounded.MarkChanged(10_000);
            Assert.False(bounded.ShouldSave(14_999));
            Assert.True(bounded.ShouldSave(15_000));

            var failing = new AutosaveScheduler(long.MaxValue, long.MaxValue);
            failing.MarkChanged(1);
            Assert.True(failing.OnAppBackgrounded(2));
            long token = failing.BeginSave();
            failing.FailSave(token, 3);
            Assert.False(failing.ShouldSave(4));
            Assert.Equal(long.MaxValue, failing.NextSaveAtMs);
        }
    }
}
