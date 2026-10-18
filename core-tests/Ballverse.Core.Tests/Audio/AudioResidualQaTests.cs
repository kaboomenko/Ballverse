using System;
using Ballverse.Core.Audio;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    /// <summary>Residual QA findings of the merged Audio module.</summary>
    public class AudioResidualQaTests
    {
        [Fact]
        public void ScheduledFrameAtLongMinValueDoesNotCrash()
        {
            // QA: skip = -long.MinValue overflowed and RenderVoice indexed out of range for some pitch steps.
            for (int semis = -12; semis <= 12; semis++)
            {
                var pack = new SoundPack { Id = SoundPackId.Notes, I0 = 1f, I1 = 11f };
                pack.Rules.Add(new SoundRule(SoundEventType.Hit, "t/tone") { RootMidi = 60 });
                var bank = AudioTestUtil.FullBank();
                var mixer = new EventMixer(pack, bank);
                var output = new float[4800];
                var events = new[]
                {
                    new ScheduledSoundEvent(SoundEvent.Hit(0, 1, 6f, 0.5f), long.MinValue),
                    new ScheduledSoundEvent(SoundEvent.Hit(1, 2, 6f, 0.5f), 100),
                };
                mixer.RenderOffline(events, output);
                Assert.Equal(1, mixer.ActiveVoiceCount);
            }
        }

        [Fact]
        public void HapticsOnScheduledEventsUseThePresentationFrame()
        {
            // QA: at timeScale 3× tick-timed haptics fired every 26.7 ms of real time; the scheduled overload times the
            // 80 ms rule on the frames the listener feels.
            var limiter = new HapticsLimiter(1f, 11f);
            var map = new PlaybackTimeMap(0, 0, 3.0);
            int fired = 0;
            long lastFrame = long.MinValue;
            for (int tick = 0; tick < 600; tick++)
            {
                var e = new ScheduledSoundEvent(SoundEvent.Hit(tick, 0, 8f), map.FrameOfTick(tick));
                if (limiter.TryTrigger(in e, out float intensity))
                {
                    Assert.True(intensity > 0f);
                    if (lastFrame != long.MinValue) Assert.True(e.Frame - lastFrame >= AudioConstants.HapticsMinIntervalFrames);
                    lastFrame = e.Frame;
                    fired++;
                }
            }
            // 600 ticks at 3× = 3.33 s of wall clock: at most one haptic per 80 ms.
            Assert.InRange(fired, 40, 42);
        }
    }
}
