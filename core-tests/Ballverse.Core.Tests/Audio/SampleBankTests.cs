using System;
using Ballverse.Core.Audio;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    public class SampleBankTests
    {
        [Fact]
        public void DefaultBudgetIs24MegabytesAndIsEnforced()
        {
            var bank = new InMemorySampleBank();
            Assert.Equal(24L * 1024 * 1024, bank.BudgetBytes);
            int frames = 6 * 1024 * 1024 / 2; // 3 M frames mono = 12 MB
            Assert.True(bank.TryAdd("a", new AudioSample(new float[frames], 1)));
            Assert.True(bank.TryAdd("b", new AudioSample(new float[frames], 1))); // exactly 24 MB
            Assert.Equal(InMemorySampleBank.DefaultBudgetBytes, bank.SizeBytes);
            Assert.False(bank.TryAdd("c", new AudioSample(new float[1], 1)));
            Assert.Throws<InvalidOperationException>(() => bank.Add("c", new AudioSample(new float[1], 1)));
            Assert.False(bank.TryGetSample("c", out _));
            Assert.Equal(2, bank.Count);
            // Replacing counts only the difference.
            Assert.True(bank.TryAdd("a", new AudioSample(new float[frames - 10], 1)));
            Assert.True(bank.TryAdd("c", new AudioSample(new float[10], 1)));
            Assert.Equal(InMemorySampleBank.DefaultBudgetBytes, bank.SizeBytes);
            Assert.False(bank.TryAdd("a", new AudioSample(new float[frames], 1)));
            Assert.True(bank.TryGetSample("a", out AudioSample kept));
            Assert.Equal(frames - 10, kept.FrameCount);
        }

        [Fact]
        public void CustomBudgetAndInvalidArguments()
        {
            var bank = new InMemorySampleBank(40);
            Assert.True(bank.TryAdd("x", new AudioSample(new float[10], 2)));
            Assert.False(bank.TryAdd("y", new AudioSample(new float[2], 1)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new InMemorySampleBank(0));
            Assert.Throws<ArgumentException>(() => bank.TryAdd("", new AudioSample(new float[2], 1)));
            Assert.Throws<ArgumentNullException>(() => bank.TryAdd("z", null));
            Assert.False(bank.TryGetSample(null, out _));
        }

        [Fact]
        public void EventTypeCodesEqualReplayEventCodes()
        {
            // core/replay ReplayEventType: Hit 0, Break 1, Eliminate 2, Finish 3, Spawn 4, Despawn 5, Score 6, Custom 7 (spec §7.4).
            Assert.Equal(0, (int)SoundEventType.Hit);
            Assert.Equal(1, (int)SoundEventType.Break);
            Assert.Equal(2, (int)SoundEventType.Eliminate);
            Assert.Equal(3, (int)SoundEventType.Finish);
            Assert.Equal(4, (int)SoundEventType.Spawn);
            Assert.Equal(5, (int)SoundEventType.Despawn);
            Assert.Equal(6, (int)SoundEventType.Score);
            Assert.Equal(7, (int)SoundEventType.Custom);
            foreach (var id in SoundPackCatalog.Ids) Assert.Null(SoundPackCatalog.Get(id).GetRule(SoundEventType.Despawn));
            var p = new SoundEventProcessor(SoundPackCatalog.Get(SoundPackId.Notes));
            var e = new SoundEvent(0, SoundEventType.Despawn, 1, 5f, 0.5f, 0f);
            Assert.Equal(SoundDecision.NoRule, p.Process(in e, out _));
        }
    }
}
