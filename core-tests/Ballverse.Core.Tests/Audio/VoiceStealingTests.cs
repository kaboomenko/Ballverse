using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    public class VoiceStealingTests
    {
        const int Long = 48000;

        static EventMixer NewMixer()
        {
            var bank = new InMemorySampleBank();
            bank.Add("t/tone", AudioTestUtil.Constant(Long));
            return new EventMixer(AudioTestUtil.SimplePack(), bank);
        }

        static float[] Run(EventMixer m, List<SoundEvent> events, int frames)
        {
            var o = new float[frames * 2];
            m.RenderOffline(events, o);
            return o;
        }

        static SoundEvent Hit(int tick, int body, float impulse)
        {
            return SoundEvent.Hit(tick, body, impulse, 0.5f, -1f);
        }

        [Fact]
        public void NeverMoreThan32Voices()
        {
            var m = NewMixer();
            var events = new List<SoundEvent>();
            for (int b = 0; b < 40; b++) events.Add(Hit(0, b, 11f));
            Run(m, events, 10);
            Assert.Equal(32, m.ActiveVoiceCount);
            Assert.Equal(40, m.VoicesStarted);
            Assert.Equal(8, m.VoicesStolen);
        }

        [Fact]
        public void QuietestNominalVoiceIsStolen()
        {
            var m = NewMixer();
            var events = new List<SoundEvent>();
            for (int b = 0; b < 32; b++) events.Add(Hit(0, b, b == 5 ? 6f : 11f));
            events.Add(Hit(1, 40, 11f));
            Run(m, events, 801);
            Assert.True(m.TryGetVoice(5, out VoiceInfo v));
            Assert.Equal(800L, v.StartFrame);
            Assert.Equal(1, m.VoicesStolen);
            for (int s = 0; s < 32; s++)
            {
                Assert.True(m.TryGetVoice(s, out VoiceInfo other));
                if (s != 5) Assert.Equal(0L, other.StartFrame);
            }
        }

        [Fact]
        public void StealingIsEnvelopeAware()
        {
            // Voice 0 starts first at full volume; by tick 32 it has decayed more than the quieter, younger voice 10.
            var m = NewMixer();
            var events = new List<SoundEvent>();
            for (int b = 0; b < 32; b++) events.Add(Hit(b, b, b == 10 ? 10.8f : 11f));
            events.Add(Hit(32, 50, 11f));
            Run(m, events, 32 * 800 + 1);
            Assert.True(m.TryGetVoice(0, out VoiceInfo v0));
            Assert.Equal(32L * 800, v0.StartFrame);
            Assert.True(m.TryGetVoice(10, out VoiceInfo v10));
            Assert.Equal(10L * 800, v10.StartFrame);
        }

        [Fact]
        public void TieBreakPrefersOlderVoice()
        {
            var m = NewMixer();
            var events = new List<SoundEvent>();
            for (int b = 0; b < 32; b++) events.Add(Hit(0, b, 11f));
            events.Add(Hit(1, 40, 11f)); // steals slot 0 (all equal, lowest slot)
            events.Add(Hit(2, 41, 11f)); // slot 0 is now the youngest/loudest; the next oldest equal voice is slot 1
            Run(m, events, 1601);
            Assert.True(m.TryGetVoice(0, out VoiceInfo v0));
            Assert.Equal(800L, v0.StartFrame);
            Assert.True(m.TryGetVoice(1, out VoiceInfo v1));
            Assert.Equal(1600L, v1.StartFrame);
            Assert.True(m.TryGetVoice(2, out VoiceInfo v2));
            Assert.Equal(0L, v2.StartFrame);
        }

        [Fact]
        public void NewSoundQuieterThanAllIsDropped()
        {
            var m = NewMixer();
            var events = new List<SoundEvent>();
            for (int b = 0; b < 32; b++) events.Add(Hit(0, b, 11f));
            events.Add(Hit(1, 40, 1f)); // volume 0.15 < every playing voice
            Run(m, events, 900);
            Assert.Equal(0, m.VoicesStolen);
            Assert.Equal(1, m.VoicesDroppedQuiet);
            for (int s = 0; s < 32; s++)
            {
                Assert.True(m.TryGetVoice(s, out VoiceInfo v));
                Assert.Equal(0L, v.StartFrame);
            }
        }

        [Fact]
        public void StolenVoiceFadesOutLinearly()
        {
            var m = NewMixer();
            var events = new List<SoundEvent>();
            for (int b = 0; b < 32; b++) events.Add(Hit(0, b, 11f));
            events.Add(Hit(1, 40, 11f));
            float[] o = Run(m, events, 2000);
            Assert.Equal(32f, o[2 * 799]);
            for (int k = 0; k < EventMixer.StealFadeFrames; k++)
            {
                float expected = 32f + (EventMixer.StealFadeFrames - k) / (float)EventMixer.StealFadeFrames;
                Assert.Equal(expected, o[2 * (800 + k)], 4);
            }
            Assert.Equal(32f, o[2 * (800 + EventMixer.StealFadeFrames)]);
            Assert.Equal(32f, o[2 * 1999]);
        }

        [Fact]
        public void FinishedVoicesFreeTheirSlots()
        {
            var bank = new InMemorySampleBank();
            bank.Add("t/tone", AudioTestUtil.Constant(100));
            var m = new EventMixer(AudioTestUtil.SimplePack(), bank);
            var events = new List<SoundEvent>();
            for (int b = 0; b < 32; b++) events.Add(Hit(0, b, 11f));
            events.Add(Hit(1, 40, 11f));
            Run(m, events, 801);
            Assert.Equal(0, m.VoicesStolen);
            Assert.Equal(1, m.ActiveVoiceCount);
        }
    }
}
