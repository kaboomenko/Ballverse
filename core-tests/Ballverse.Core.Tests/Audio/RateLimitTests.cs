using System;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    public class RateLimitTests
    {
        [Fact]
        public void SlidingWindowAllowsExactlyNPerWindowWithHalfOpenBoundary()
        {
            var w = new SlidingRateWindow(3, 100);
            Assert.True(w.TryAcquire(0));
            Assert.True(w.TryAcquire(10));
            Assert.True(w.TryAcquire(20));
            Assert.False(w.CanAcquire(99));
            Assert.False(w.TryAcquire(99));
            Assert.True(w.CanAcquire(100));
            Assert.True(w.TryAcquire(100)); // oldest (0) is exactly 100 old → out of (t−100, t]
            Assert.False(w.TryAcquire(109));
            Assert.True(w.TryAcquire(110));
        }

        [Fact]
        public void SlidingWindowClampsBackwardTime()
        {
            var w = new SlidingRateWindow(1, 100);
            Assert.True(w.TryAcquire(500));
            Assert.False(w.TryAcquire(0)); // treated as 500
            Assert.True(w.TryAcquire(600));
            w.Reset();
            Assert.True(w.TryAcquire(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SlidingRateWindow(0, 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SlidingRateWindow(1, 0));
        }

        [Fact]
        public void BodyLimiterBoundaryIsExactly50ms()
        {
            var b = new BodyRateLimiter();
            b.Record(7, 1000);
            Assert.True(b.IsLimited(7, 1000));
            Assert.True(b.IsLimited(7, 1000 + 2399));
            Assert.False(b.IsLimited(7, 1000 + 2400));
            Assert.False(b.IsLimited(8, 1000));
            b.Record(-1, 1000);
            Assert.False(b.IsLimited(-1, 1000));
            b.Reset();
            Assert.False(b.IsLimited(7, 1000));
        }

        static SoundDecision Hit(SoundEventProcessor p, int tick, int body, float impulse = 11f)
        {
            var e = SoundEvent.Hit(tick, body, impulse);
            return p.Process(in e, out _);
        }

        [Fact]
        public void PerBodyLimitInTicks()
        {
            // 50 ms = 2400 frames = 3 ticks: 1 and 2 ticks later refused, 3 ticks later allowed.
            var p = new SoundEventProcessor(AudioTestUtil.SimplePack());
            Assert.Equal(SoundDecision.Played, Hit(p, 0, 1));
            Assert.Equal(SoundDecision.BodyRateLimited, Hit(p, 0, 1));
            Assert.Equal(SoundDecision.BodyRateLimited, Hit(p, 1, 1));
            Assert.Equal(SoundDecision.Played, Hit(p, 1, 2));
            Assert.Equal(SoundDecision.BodyRateLimited, Hit(p, 2, 1));
            Assert.Equal(SoundDecision.Played, Hit(p, 3, 1));
        }

        [Fact]
        public void PerBodyLimitAtExactFrames()
        {
            var p = new SoundEventProcessor(AudioTestUtil.SimplePack());
            var e = SoundEvent.Hit(0, 4, 11f);
            Assert.Equal(SoundDecision.Played, p.Process(in e, 10_000, out _));
            Assert.Equal(SoundDecision.BodyRateLimited, p.Process(in e, 12_399, out _));
            Assert.Equal(SoundDecision.Played, p.Process(in e, 12_400, out _));
        }

        [Fact]
        public void BodilessEventsAreExemptFromPerBodyLimit()
        {
            var p = new SoundEventProcessor(AudioTestUtil.SimplePack());
            for (int i = 0; i < 10; i++) Assert.Equal(SoundDecision.Played, Hit(p, 0, -1));
        }

        [Fact]
        public void GlobalLimitIs40PerSlidingSecond()
        {
            var p = new SoundEventProcessor(AudioTestUtil.SimplePack());
            for (int b = 0; b < 40; b++) Assert.Equal(SoundDecision.Played, Hit(p, 0, b));
            Assert.Equal(SoundDecision.GlobalRateLimited, Hit(p, 0, 100));
            Assert.Equal(SoundDecision.GlobalRateLimited, Hit(p, 59, 101));
            // tick 60 = exactly 1 s later: the 40 sounds at tick 0 have left the window.
            for (int b = 0; b < 40; b++) Assert.Equal(SoundDecision.Played, Hit(p, 60, 200 + b));
            Assert.Equal(SoundDecision.GlobalRateLimited, Hit(p, 60, 300));
        }

        [Fact]
        public void SustainedGlobalRateNeverExceeds40InAnyWindow()
        {
            var p = new SoundEventProcessor(AudioTestUtil.SimplePack());
            var rng = Rng.FromSeed(4040UL);
            var accepted = new System.Collections.Generic.List<long>();
            for (int t = 0; t < 3000; t++)
            {
                int n = rng.NextInt(0, 5);
                for (int k = 0; k < n; k++)
                {
                    var e = SoundEvent.Hit(t, rng.NextInt(-1, 300), rng.NextFloat(0f, 12f));
                    if (p.Process(in e, out _) == SoundDecision.Played) accepted.Add(AudioConstants.TickToFrame(t));
                }
            }
            for (int i = 40; i < accepted.Count; i++) Assert.True(accepted[i] - accepted[i - 40] >= 48000);
            Assert.True(accepted.Count > 1500);
        }

        [Fact]
        public void SilentHitsConsumeNoBudget()
        {
            var p = new SoundEventProcessor(AudioTestUtil.SimplePack(i0: 1f));
            for (int i = 0; i < 100; i++) Assert.Equal(SoundDecision.BelowThreshold, Hit(p, 0, 5, 0.5f));
            Assert.Equal(SoundDecision.Played, Hit(p, 0, 5, 1f));
            for (int b = 0; b < 39; b++) Assert.Equal(SoundDecision.Played, Hit(p, 0, 100 + b));
        }

        [Fact]
        public void NoRuleAndNonePack()
        {
            var p = new SoundEventProcessor(SoundPackCatalog.Get(SoundPackId.None));
            Assert.Equal(SoundDecision.NoRule, Hit(p, 0, 1));
            var q = new SoundEventProcessor(AudioTestUtil.SimplePack());
            var e = new SoundEvent(0, SoundEventType.Spawn, 1, 5f, 0.5f, 0f);
            Assert.Equal(SoundDecision.NoRule, q.Process(in e, out _));
            var bogus = new SoundEvent(0, (SoundEventType)42, 1, 5f, 0.5f, 0f);
            Assert.Equal(SoundDecision.NoRule, q.Process(in bogus, out _));
        }

        [Fact]
        public void NonImpulseRulePlaysAtRuleGainRegardlessOfImpulse()
        {
            var p = new SoundEventProcessor(AudioTestUtil.SimplePack());
            var e = new SoundEvent(0, SoundEventType.Finish, -1, 0f, 0.5f, 0f);
            Assert.Equal(SoundDecision.Played, p.Process(in e, out VoiceRequest r));
            Assert.Equal(1f, r.Gain);
        }

        [Fact]
        public void RequestCarriesVolumePanAndPitch()
        {
            var pack = AudioTestUtil.SimplePack();
            pack.Gain = 0.5f;
            pack.Rules[0].Gain = 0.8f;
            pack.Rules[0].Semitones = 3;
            var p = new SoundEventProcessor(pack);
            var e = new SoundEvent(10, SoundEventType.Hit, 1, 6f, 0.5f, 2f);
            Assert.Equal(SoundDecision.Played, p.Process(in e, out VoiceRequest r));
            Assert.Equal(0.5f * 0.8f * 0.5f, r.Gain, 6);
            Assert.Equal(1f, r.Pan);
            Assert.Equal(3, r.Semitones);
            Assert.Equal(8000L, r.Frame);
            Assert.Equal("t/tone", r.SampleId);
            Assert.Equal(-1, r.MelodyIndex);
        }

        [Fact]
        public void InvalidPackIsRejected()
        {
            var pack = AudioTestUtil.SimplePack();
            pack.I1 = pack.I0;
            Assert.Throws<ArgumentException>(() => new SoundEventProcessor(pack));
            Assert.Throws<ArgumentNullException>(() => new SoundEventProcessor(null));
        }

        [Fact]
        public void ProcessorSnapshotsPack()
        {
            var pack = AudioTestUtil.SimplePack();
            var p = new SoundEventProcessor(pack);
            pack.Rules.Clear();
            Assert.Equal(SoundDecision.Played, Hit(p, 0, 1));
        }
    }
}
