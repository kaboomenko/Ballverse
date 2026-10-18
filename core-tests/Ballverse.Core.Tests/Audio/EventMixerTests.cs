using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    public class EventMixerTests
    {
        static SoundPack FixedPack(string sample, int semitones = 0)
        {
            var p = new SoundPack { Id = SoundPackId.Notes, I0 = 1f, I1 = 11f };
            p.Rules.Add(new SoundRule(SoundEventType.Finish, sample) { RootMidi = 60, Semitones = semitones, ImpulseVolume = false });
            p.Rules.Add(new SoundRule(SoundEventType.Hit, sample) { RootMidi = 60, Semitones = semitones });
            return p;
        }

        static SoundEvent Finish(int tick, float pan)
        {
            return new SoundEvent(tick, SoundEventType.Finish, -1, 0f, 0.5f, pan);
        }

        [Fact]
        public void EventTickMapsToExactSampleOffset()
        {
            var bank = new InMemorySampleBank();
            bank.Add("one", AudioTestUtil.Constant(10));
            var events = new List<SoundEvent> { Finish(5, -1f) };
            float[] out1 = EventMixer.RenderOffline(FixedPack("one"), bank, null, events, 5000);
            for (int f = 0; f < 5000; f++)
            {
                float expectedL = f >= 4000 && f < 4010 ? 1f : 0f;
                Assert.Equal(expectedL, out1[2 * f]);
                Assert.Equal(0f, out1[2 * f + 1], 6);
            }
        }

        [Fact]
        public void OriginTickShiftsTimeline()
        {
            var bank = new InMemorySampleBank();
            bank.Add("one", AudioTestUtil.Constant(4));
            var events = new List<SoundEvent> { Finish(65, -1f) };
            float[] o = EventMixer.RenderOffline(FixedPack("one"), bank, null, events, 5000, new EventMixerOptions { OriginTick = 60 });
            Assert.Equal(0f, o[2 * 3999]);
            Assert.Equal(1f, o[2 * 4000]);
        }

        [Fact]
        public void OctaveUpPlaysEveryOtherFrame()
        {
            var bank = new InMemorySampleBank();
            var ramp = new float[100];
            for (int i = 0; i < 100; i++) ramp[i] = i;
            bank.Add("ramp", new AudioSample(ramp, 1));
            float[] o = EventMixer.RenderOffline(FixedPack("ramp", 12), bank, null, new List<SoundEvent> { Finish(0, -1f) }, 200);
            for (int f = 0; f < 50; f++) Assert.Equal(2f * f, o[2 * f]);
            for (int f = 50; f < 200; f++) Assert.Equal(0f, o[2 * f]);
        }

        [Fact]
        public void OctaveDownInterpolatesLinearly()
        {
            var bank = new InMemorySampleBank();
            var ramp = new float[100];
            for (int i = 0; i < 100; i++) ramp[i] = i;
            bank.Add("ramp", new AudioSample(ramp, 1));
            float[] o = EventMixer.RenderOffline(FixedPack("ramp", -12), bank, null, new List<SoundEvent> { Finish(0, -1f) }, 300);
            for (int f = 0; f < 198; f++) Assert.Equal(f * 0.5f, o[2 * f]);
            Assert.Equal(99f, o[2 * 198]);
            Assert.Equal(49.5f, o[2 * 199]); // interpolates toward silence after the last frame
            Assert.Equal(0f, o[2 * 200]);
        }

        [Fact]
        public void ConstantPowerPanOnMonoAndStereoSamples()
        {
            var bank = new InMemorySampleBank();
            bank.Add("mono", AudioTestUtil.Constant(4));
            bank.Add("stereo", new AudioSample(new[] { 1f, 0.5f, 1f, 0.5f }, 2));
            AudioMath.ConstantPowerPan(0.3f, out float pl, out float pr);

            float[] m = EventMixer.RenderOffline(FixedPack("mono"), bank, null, new List<SoundEvent> { Finish(0, 0.3f) }, 4);
            Assert.Equal(pl, m[0]);
            Assert.Equal(pr, m[1]);

            float[] s = EventMixer.RenderOffline(FixedPack("stereo"), bank, null, new List<SoundEvent> { Finish(0, 0.3f) }, 4);
            Assert.Equal(1f * pl, s[0]);
            Assert.Equal(0.5f * pr, s[1]);
        }

        [Fact]
        public void ImpulseVolumeReachesOutput()
        {
            var bank = new InMemorySampleBank();
            bank.Add("one", AudioTestUtil.Constant(4));
            var events = new List<SoundEvent> { SoundEvent.Hit(0, 1, 6f, 0.5f, -1f), SoundEvent.Hit(10, 2, 0.5f, 0.5f, -1f) };
            float[] o = EventMixer.RenderOffline(FixedPack("one"), bank, null, events, 9000);
            Assert.Equal(0.5f, o[0], 6);
            for (int f = 8000; f < 9000; f++) Assert.Equal(0f, o[2 * f]);
        }

        [Fact]
        public void MasterGainScalesAndIsClamped()
        {
            var bank = new InMemorySampleBank();
            bank.Add("one", AudioTestUtil.Constant(4));
            var ev = new List<SoundEvent> { Finish(0, -1f) };
            Assert.Equal(0.25f, EventMixer.RenderOffline(FixedPack("one"), bank, null, ev, 4, new EventMixerOptions { MasterGain = 0.25f })[0]);
            Assert.Equal(4f, EventMixer.RenderOffline(FixedPack("one"), bank, null, ev, 4, new EventMixerOptions { MasterGain = 50f })[0]);
            Assert.Equal(1f, EventMixer.RenderOffline(FixedPack("one"), bank, null, ev, 4, new EventMixerOptions { MasterGain = float.NaN })[0]);
        }

        [Fact]
        public void MissingSampleIsSilentButCounted()
        {
            var bank = new InMemorySampleBank();
            var mixer = new EventMixer(FixedPack("nope"), bank);
            var o = new float[2000];
            mixer.RenderOffline(new List<SoundEvent> { Finish(0, 0f) }, o);
            Assert.All(o, v => Assert.Equal(0f, v));
            Assert.Equal(1, mixer.MissingSamples);
            Assert.Equal(0, mixer.VoicesStarted);
        }

        static List<SoundEvent> BusyEvents(ulong seed, int count, int maxTick)
        {
            var rng = Rng.FromSeed(seed, 11);
            return AudioTestUtil.RandomEvents(ref rng, count, maxTick, 64);
        }

        [Theory]
        [InlineData(SoundPackId.Notes, 2400)]
        [InlineData(SoundPackId.Drums, 2400)]
        [InlineData(SoundPackId.Glass, 120000)]
        [InlineData(SoundPackId.Melody, 120000)]
        public void OutputIsIndependentOfChunkSizes(SoundPackId packId, int sampleFrames)
        {
            var bank = AudioTestUtil.FullBank(sampleFrames);
            var melody = MelodyTests.Scale(13);
            var events = BusyEvents(0xC0FFEEUL + (ulong)packId, 1500, 300);
            int frames = 301 * 800 + 5000;
            var offline = new EventMixer(SoundPackCatalog.Get(packId), bank, melody);
            var reference = new float[frames * 2];
            offline.RenderOffline(events, reference);
            if (sampleFrames > 48000) Assert.True(offline.VoicesStolen > 5 && offline.VoicesStolen + offline.VoicesDroppedQuiet > 20, "voice pool must overflow");

            var rng = Rng.FromSeed(77UL, (ulong)packId);
            for (int trial = 0; trial < 6; trial++)
            {
                var mixer = new EventMixer(SoundPackCatalog.Get(packId), bank, melody, new EventMixerOptions { QueueCapacity = 4096 });
                foreach (var e in events) Assert.True(mixer.Queue.TryEnqueue(e));
                var output = new float[frames * 2];
                int pos = 0;
                while (pos < frames)
                {
                    int max = trial % 2 == 0 ? 64 : 3000;
                    int n = Math.Min(frames - pos, rng.NextInt(0, max + 1));
                    mixer.Render(new Span<float>(output, pos * 2, n * 2));
                    pos += n;
                }
                AudioTestUtil.AssertBitIdentical(reference, output);
            }
        }

        [Fact]
        public void RealtimeWithProgressiveEnqueueMatchesOffline()
        {
            var bank = AudioTestUtil.FullBank(30000);
            var events = BusyEvents(0xBEEFUL, 2000, 600);
            int frames = 601 * 800 + 30000;
            float[] reference = EventMixer.RenderOffline(SoundPackCatalog.Get(SoundPackId.Pop), bank, null, events, frames);
            var mixer = new EventMixer(SoundPackCatalog.Get(SoundPackId.Pop), bank, null, new EventMixerOptions { QueueCapacity = 64 });
            var output = new float[frames * 2];
            var rng = Rng.FromSeed(5UL);
            int next = 0;
            int pos = 0;
            while (pos < frames)
            {
                int n = Math.Min(frames - pos, rng.NextInt(1, 1025));
                // Simulation thread role: queue every event that falls before the end of the next chunk.
                while (next < events.Count && mixer.FrameOfTick(events[next].Tick) < pos + n)
                {
                    if (!mixer.Queue.TryEnqueue(events[next])) break;
                    next++;
                }
                if (next < events.Count && mixer.FrameOfTick(events[next].Tick) < pos + n)
                    n = Math.Max(1, (int)(mixer.FrameOfTick(events[next].Tick) - pos)); // queue full: render up to the next event
                mixer.Render(new Span<float>(output, pos * 2, n * 2));
                pos += n;
            }
            Assert.Equal(events.Count, next);
            AudioTestUtil.AssertBitIdentical(reference, output);
        }

        [Fact]
        public void LateRealtimeEventStartsAtCurrentPosition()
        {
            var bank = new InMemorySampleBank();
            bank.Add("one", AudioTestUtil.Constant(4));
            var mixer = new EventMixer(FixedPack("one"), bank);
            var buf = new float[2000];
            mixer.Render(buf);
            Assert.Equal(1000L, mixer.Position);
            mixer.Queue.TryEnqueue(Finish(0, -1f));
            mixer.Render(buf);
            Assert.Equal(1f, buf[0]);
            Assert.Equal(1f, buf[6]);
            Assert.Equal(0f, buf[8]);
        }

        [Fact]
        public void EventsBeforeOriginAdvanceMelodyButStartNoVoice()
        {
            var bank = AudioTestUtil.FullBank();
            var melody = MelodyTests.Scale(8);
            var events = new List<SoundEvent> { SoundEvent.Hit(30, 1, 6f), SoundEvent.Hit(70, 2, 6f) };
            var mixer = new EventMixer(SoundPackCatalog.Get(SoundPackId.Melody), bank, melody, new EventMixerOptions { OriginTick = 60 });
            var o = new float[2 * 8100];
            mixer.RenderOffline(events, o);
            Assert.Equal(2, mixer.Processor.MelodyCursor.NextIndex);
            Assert.Equal(1, mixer.VoicesStarted);
            Assert.True(mixer.TryGetVoice(0, out VoiceInfo v));
            Assert.Equal(8000L, v.StartFrame);
            Assert.Equal(1, v.Semitones);
            for (int f = 0; f < 8000; f++) Assert.Equal(0f, o[2 * f]);
        }

        [Fact]
        public void ChangingSoundPackRerendersSameEventsWithoutSimulation()
        {
            var bank = AudioTestUtil.FullBank();
            var events = BusyEvents(99UL, 300, 200);
            int frames = 201 * 800;
            float[] notes1 = EventMixer.RenderOffline(SoundPackCatalog.Get(SoundPackId.Notes), bank, null, events, frames);
            float[] pop = EventMixer.RenderOffline(SoundPackCatalog.Get(SoundPackId.Pop), bank, null, events, frames);
            float[] notes2 = EventMixer.RenderOffline(SoundPackCatalog.Get(SoundPackId.Notes), bank, null, events, frames);
            float[] none = EventMixer.RenderOffline(SoundPackCatalog.Get(SoundPackId.None), bank, null, events, frames);
            AudioTestUtil.AssertBitIdentical(notes1, notes2);
            Assert.NotEqual(notes1, pop);
            Assert.All(none, v => Assert.Equal(0f, v));
        }

        [Fact]
        public void OfflineSortsEventsStablyByTick()
        {
            var bank = AudioTestUtil.FullBank();
            var events = BusyEvents(1234UL, 400, 200);
            // Reverse whole tick groups (keeping order inside each tick): the stable sort restores the original sequence.
            var stableReversed = new List<SoundEvent>();
            int i = events.Count - 1;
            while (i >= 0)
            {
                int j = i;
                while (j > 0 && events[j - 1].Tick == events[i].Tick) j--;
                for (int k = j; k <= i; k++) stableReversed.Add(events[k]);
                i = j - 1;
            }
            int frames = 201 * 800;
            float[] a = EventMixer.RenderOffline(SoundPackCatalog.Get(SoundPackId.Notes), bank, null, events, frames);
            float[] b = EventMixer.RenderOffline(SoundPackCatalog.Get(SoundPackId.Notes), bank, null, stableReversed, frames);
            AudioTestUtil.AssertBitIdentical(a, b);
        }

        [Fact]
        public void ResetReproducesOutput()
        {
            var bank = AudioTestUtil.FullBank();
            var events = BusyEvents(42UL, 300, 100);
            var mixer = new EventMixer(SoundPackCatalog.Get(SoundPackId.Chiptune), bank);
            var a = new float[2 * 101 * 800];
            var b = new float[a.Length];
            mixer.RenderOffline(events, a);
            mixer.RenderOffline(events, b);
            AudioTestUtil.AssertBitIdentical(a, b);
        }

        [Fact]
        public void RenderDoesNotAllocateAfterWarmUp()
        {
            var bank = AudioTestUtil.FullBank(40000);
            var mixer = new EventMixer(SoundPackCatalog.Get(SoundPackId.Melody), bank, MelodyTests.Scale(10));
            var buffer = new float[512];
            var rng = Rng.FromSeed(3UL);
            int tick = 0;
            for (int i = 0; i < 50; i++)
            {
                mixer.Queue.TryEnqueue(SoundEvent.Hit(tick, i % 50, 5f, 0.4f, 0.1f));
                mixer.Render(buffer);
                tick++;
            }
            // Warm up with the same event/chunk distribution that is measured, so first-use paths
            // (new event types, new chunk lengths, tier-up) are outside the measured window.
            void RunRandom(int iterations)
            {
                for (int i = 0; i < iterations; i++)
                {
                    int n = rng.NextInt(0, 4);
                    for (int k = 0; k < n; k++)
                    {
                        var type = (SoundEventType)rng.NextInt(0, 8);
                        mixer.Queue.TryEnqueue(new SoundEvent(tick, type, rng.NextInt(-1, 80), rng.NextFloat(0f, 8f), rng.NextFloat(0.1f, 1.5f), 0f));
                    }
                    if ((i & 1) == 0) tick++;
                    mixer.Render(new Span<float>(buffer, 0, 2 * rng.NextInt(0, 257)));
                }
            }
            RunRandom(3000);
            long before = GC.GetAllocatedBytesForCurrentThread();
            RunRandom(3000);
            long after = GC.GetAllocatedBytesForCurrentThread();
            Assert.Equal(0L, after - before);
            Assert.True(mixer.VoicesStarted > 200, "voices " + mixer.VoicesStarted);
        }

        [Fact]
        public void InvalidArguments()
        {
            var bank = AudioTestUtil.FullBank();
            Assert.Throws<ArgumentNullException>(() => new EventMixer(SoundPackCatalog.Get(SoundPackId.Notes), null));
            Assert.Throws<ArgumentNullException>(() => new EventMixer(null, bank));
            var mixer = new EventMixer(SoundPackCatalog.Get(SoundPackId.Notes), bank);
            Assert.Throws<ArgumentException>(() => mixer.Render(new float[3]));
            Assert.Throws<ArgumentNullException>(() => mixer.RenderOffline((IReadOnlyList<SoundEvent>)null, new float[2]));
            Assert.Throws<ArgumentNullException>(() => mixer.RenderOffline((IReadOnlyList<ScheduledSoundEvent>)null, new float[2]));
            Assert.Throws<ArgumentOutOfRangeException>(() => EventMixer.RenderOffline(SoundPackCatalog.Get(SoundPackId.Notes), bank, null, new List<SoundEvent>(), -1));
            Assert.Throws<ArgumentException>(() => new AudioSample(new float[3], 2));
            Assert.Throws<ArgumentOutOfRangeException>(() => new AudioSample(new float[3], 3));
            Assert.Throws<ArgumentException>(() => new AudioSample(new[] { float.NaN }, 1));
            Assert.False(mixer.TryGetVoice(-1, out _));
            Assert.False(mixer.TryGetVoice(40, out _));
        }
    }
}
