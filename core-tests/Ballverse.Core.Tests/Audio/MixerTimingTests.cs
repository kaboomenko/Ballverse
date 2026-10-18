using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    /// <summary>Output placement under playback speeds ≠ 1 (spec §2.2 timeScale, §3.4 studio speed and slow-mo) and trimming.</summary>
    public class MixerTimingTests
    {
        static SoundPack ClickPack()
        {
            var p = new SoundPack { Id = SoundPackId.Notes, I0 = 1f, I1 = 11f };
            p.Rules.Add(new SoundRule(SoundEventType.Finish, "click") { RootMidi = 60, ImpulseVolume = false });
            p.Rules.Add(new SoundRule(SoundEventType.Hit, "click") { RootMidi = 60 });
            return p;
        }

        static InMemorySampleBank ClickBank(int frames = 1)
        {
            var bank = new InMemorySampleBank();
            bank.Add("click", AudioTestUtil.Constant(frames));
            return bank;
        }

        static SoundEvent Finish(int tick)
        {
            return new SoundEvent(tick, SoundEventType.Finish, -1, 0f, 0.5f, -1f);
        }

        static List<long> NonZeroFrames(float[] interleaved)
        {
            var list = new List<long>();
            for (int f = 0; f < interleaved.Length / 2; f++)
            {
                if (interleaved[2 * f] != 0f) list.Add(f);
            }
            return list;
        }

        [Fact]
        public void TimeMapIsExactAndContinuous()
        {
            var map = new PlaybackTimeMap();
            Assert.Equal(0L, map.FrameOfTick(0));
            Assert.Equal(800L * 59, map.FrameOfTick(59));
            Assert.Equal(-800L, map.FrameOfTick(-1)); // before the origin: first segment extrapolated
            map.SetSpeed(60, 3.0);
            Assert.Equal(48000L, map.FrameOfTick(60));
            Assert.Equal(48267L, map.FrameOfTick(61)); // 266.67 rounds to 267
            Assert.Equal(48800L, map.FrameOfTick(63));
            map.SetSpeed(120, 0.25);
            Assert.Equal(48000L + 16000L, map.FrameOfTick(120));
            Assert.Equal(64000L + 3200L, map.FrameOfTick(121));
            Assert.Equal(3, map.SegmentCount);
            map.SetSpeed(120, 0.5); // same tick replaces the speed
            Assert.Equal(3, map.SegmentCount);
            Assert.Equal(64000L + 1600L, map.FrameOfTick(121));
            Assert.Equal(48800L, map.FrameOfTick(63)); // earlier segments unchanged

            long prev = long.MinValue;
            for (int t = -10; t < 400; t++)
            {
                long f = map.FrameOfTick(t);
                Assert.True(f > prev);
                prev = f;
            }

            var shifted = new PlaybackTimeMap(100, 5000, 2.0);
            Assert.Equal(5000L, shifted.FrameOfTick(100));
            Assert.Equal(5400L, shifted.FrameOfTick(101));
        }

        [Fact]
        public void TimeMapRejectsBadInput()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackTimeMap(0, 0, 0.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackTimeMap(0, 0, double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PlaybackTimeMap(0, 0, 17.0));
            var map = new PlaybackTimeMap();
            map.SetSpeed(10, 2.0);
            Assert.Throws<ArgumentOutOfRangeException>(() => map.SetSpeed(9, 1.0));
            Assert.Throws<ArgumentOutOfRangeException>(() => map.SetSpeed(20, double.PositiveInfinity));
        }

        /// <summary>
        /// Realtime emulation: one 800-frame render per displayed frame (60 fps); the simulation advances timeScale ticks per
        /// displayed frame and the adapter stamps each event with its presentation frame. Sound stays in step with the
        /// picture and the queue stays short — at 1 tick = 800 frames it would lag and overflow at 3×.
        /// </summary>
        [Theory]
        [InlineData(3.0, 30, 60)]
        [InlineData(0.25, 6, 20)]
        public void RealtimeTimeScaleKeepsSoundInStepWithPicture(double timeScale, int everyTicks, int seconds)
        {
            var mixer = new EventMixer(ClickPack(), ClickBank(), null, new EventMixerOptions { QueueCapacity = 64 });
            var map = new PlaybackTimeMap(0, 0, timeScale);
            int displayFrames = seconds * 60;
            var output = new float[displayFrames * 800 * 2];
            double simTime = 0;
            int nextTick = 0;
            var expected = new List<long>();
            int maxQueued = 0;
            for (int d = 0; d < displayFrames; d++)
            {
                simTime += timeScale;
                // Simulation thread: ticks whose presentation time falls inside this displayed frame.
                while (nextTick < simTime)
                {
                    if (nextTick % everyTicks == 0)
                    {
                        long frame = map.FrameOfTick(nextTick);
                        Assert.True(mixer.Queue.TryEnqueue(Finish(nextTick), frame));
                        expected.Add(frame);
                    }
                    nextTick++;
                }
                maxQueued = Math.Max(maxQueued, mixer.Queue.Count);
                mixer.Render(new Span<float>(output, d * 1600, 1600));
            }
            Assert.Equal(0, mixer.Queue.Dropped);
            Assert.True(maxQueued <= 2, "queue grew to " + maxQueued);
            Assert.Equal(expected, NonZeroFrames(output));
            Assert.Equal((long)Math.Round(nextTick * 800 / timeScale), map.FrameOfTick(nextTick));
        }

        [Fact]
        public void StudioSpeedPlacesEventsExactly()
        {
            var map = new PlaybackTimeMap(0, 0, 1.5);
            var events = new List<ScheduledSoundEvent>();
            foreach (int t in new[] { 0, 1, 2, 3, 90, 91 }) events.Add(new ScheduledSoundEvent(Finish(t), map.FrameOfTick(t)));
            var mixer = new EventMixer(ClickPack(), ClickBank());
            var o = new float[2 * 50000];
            mixer.RenderOffline(events, o);
            // 800 / 1.5 = 533.33 frames per tick, rounded half up per tick from the segment start.
            Assert.Equal(new List<long> { 0, 533, 1067, 1600, 48000, 48533 }, NonZeroFrames(o));
        }

        [Fact]
        public void FinaleSlowMoStretchesTheEnd()
        {
            var map = new PlaybackTimeMap();
            map.SetSpeed(120, 0.25); // last part of the clip at 0.25×
            var events = new List<ScheduledSoundEvent>();
            foreach (int t in new[] { 60, 119, 120, 121, 124 }) events.Add(new ScheduledSoundEvent(Finish(t), map.FrameOfTick(t)));
            var o = new float[2 * 120000];
            new EventMixer(ClickPack(), ClickBank()).RenderOffline(events, o);
            Assert.Equal(new List<long> { 48000, 95200, 96000, 99200, 108800 }, NonZeroFrames(o));
        }

        [Fact]
        public void ScheduledRealtimeMatchesScheduledOfflineForAnyChunking()
        {
            var bank = AudioTestUtil.FullBank(20000);
            var rng = Rng.FromSeed(0x7135UL);
            var raw = AudioTestUtil.RandomEvents(ref rng, 600, 900, 64);
            var map = new PlaybackTimeMap(0, 0, 1.0);
            map.SetSpeed(200, 2.0);
            map.SetSpeed(500, 0.5);
            map.SetSpeed(800, 3.0);
            var scheduled = new List<ScheduledSoundEvent>();
            foreach (var e in raw) scheduled.Add(new ScheduledSoundEvent(e, map.FrameOfTick(e.Tick)));
            int frames = (int)map.FrameOfTick(901) + 20000;

            var offline = new EventMixer(SoundPackCatalog.Get(SoundPackId.Pop), bank);
            var reference = new float[frames * 2];
            offline.RenderOffline(scheduled, reference);
            Assert.True(offline.VoicesStarted > 100);

            for (int trial = 0; trial < 3; trial++)
            {
                var mixer = new EventMixer(SoundPackCatalog.Get(SoundPackId.Pop), bank, null, new EventMixerOptions { QueueCapacity = 1024 });
                foreach (var s in scheduled) Assert.True(mixer.Queue.TryEnqueue(s.Event, s.Frame));
                var output = new float[frames * 2];
                int pos = 0;
                while (pos < frames)
                {
                    int n = Math.Min(frames - pos, rng.NextInt(0, trial == 0 ? 50 : 4000));
                    mixer.Render(new Span<float>(output, pos * 2, n * 2));
                    pos += n;
                }
                AudioTestUtil.AssertBitIdentical(reference, output);
            }
        }

        [Fact]
        public void SampleVariantsFollowTheSimTickNotTheOutputFrame()
        {
            var bank = AudioTestUtil.FullBank();
            var pack = SoundPackCatalog.Get(SoundPackId.Pop); // three hit variants, chosen by hashing the event
            for (int tick = 0; tick < 40; tick++)
            {
                var e = SoundEvent.Hit(tick, 3, 5f);
                var atTick = new EventMixer(pack, bank);
                atTick.RenderOffline(new List<SoundEvent> { e }, new float[2 * (tick * 800 + 10)]);
                var moved = new EventMixer(pack, bank);
                moved.RenderOffline(new List<ScheduledSoundEvent> { new ScheduledSoundEvent(e, 7) }, new float[2 * 10]);
                Assert.True(atTick.TryGetVoice(0, out VoiceInfo a));
                Assert.True(moved.TryGetVoice(0, out VoiceInfo b));
                Assert.Equal(a.SampleId, b.SampleId);
                Assert.Equal(7L, b.StartFrame);
            }
        }

        [Fact]
        public void RateLimitsApplyOnTheOutputTimeline()
        {
            // Same body twice, 2 ticks apart in the simulation (33 ms) but 3200 frames apart on a 0.5× output (67 ms):
            // the 50 ms per-body limit is about what the listener hears, so both play.
            var mixer = new EventMixer(ClickPack(), ClickBank());
            var events = new List<ScheduledSoundEvent>
            {
                new ScheduledSoundEvent(SoundEvent.Hit(0, 1, 11f), 0),
                new ScheduledSoundEvent(SoundEvent.Hit(2, 1, 11f), 3200),
            };
            mixer.RenderOffline(events, new float[2 * 4000]);
            Assert.Equal(2, mixer.VoicesStarted);
            var tickPlaced = new EventMixer(ClickPack(), ClickBank());
            tickPlaced.RenderOffline(new List<SoundEvent> { events[0].Event, events[1].Event }, new float[2 * 4000]);
            Assert.Equal(1, tickPlaced.VoicesStarted);
        }

        [Fact]
        public void TrimmedRenderEqualsSliceOfFullRender()
        {
            var bank = new InMemorySampleBank();
            bank.Add("click", AudioTestUtil.DecayingSine(330, 48000, false)); // 1 s
            bank.Add("short", AudioTestUtil.DecayingSine(500, 2000, true));
            var pack = new SoundPack { Id = SoundPackId.Notes, I0 = 1f, I1 = 11f };
            pack.Rules.Add(new SoundRule(SoundEventType.Finish, "click") { RootMidi = 60, ImpulseVolume = false });
            pack.Rules.Add(new SoundRule(SoundEventType.Hit, "short") { RootMidi = 60, Semitones = -5 });
            pack.Rules.Add(new SoundRule(SoundEventType.Score, "click") { RootMidi = 60, Semitones = 7, ImpulseVolume = false });
            var events = new List<SoundEvent>
            {
                SoundEvent.Hit(10, 1, 6f, 0.5f, 0.3f), // finished long before the origin
                new SoundEvent(59, SoundEventType.Finish, -1, 0f, 0.5f, -0.4f), // 1 s sound, one tick before the origin
                new SoundEvent(40, SoundEventType.Score, -1, 0f, 0.5f, 0.6f), // pitched up, still playing at the origin
                SoundEvent.Hit(60, 2, 9f, 0.5f, 0.1f),
                SoundEvent.Hit(75, 3, 4f, 0.5f, -0.9f),
            };
            const int origin = 60;
            int frames = 200 * 800;
            float[] full = EventMixer.RenderOffline(pack, bank, null, events, frames);
            float[] trimmed = EventMixer.RenderOffline(pack, bank, null, events, frames - origin * 800, new EventMixerOptions { OriginTick = origin });
            var slice = new float[trimmed.Length];
            Array.Copy(full, origin * 800 * 2, slice, 0, slice.Length);
            Assert.NotEqual(0f, trimmed[0]); // the sound from tick 59 is audible at frame 0
            AudioTestUtil.AssertBitIdentical(slice, trimmed);
        }

        [Fact]
        public void TrimmedRandomRenderMatchesSliceClosely()
        {
            var bank = AudioTestUtil.FullBank(9000);
            var rng = Rng.FromSeed(0x7121UL);
            var events = AudioTestUtil.RandomEvents(ref rng, 300, 600, 64);
            for (int origin = 1; origin < 600; origin += 97)
            {
                int frames = 620 * 800;
                float[] full = EventMixer.RenderOffline(SoundPackCatalog.Get(SoundPackId.Glass), bank, null, events, frames);
                float[] trimmed = EventMixer.RenderOffline(SoundPackCatalog.Get(SoundPackId.Glass), bank, null, events,
                    frames - origin * 800, new EventMixerOptions { OriginTick = origin });
                // Same voices and positions; only the summation order of voices may differ (slot assignment).
                for (int i = 0; i < trimmed.Length; i++)
                    Assert.True(Math.Abs(full[origin * 1600 + i] - trimmed[i]) < 1e-5f, "origin " + origin + " sample " + i);
            }
        }

        [Fact]
        public void SoundFinishedBeforeTheOriginStartsNoVoice()
        {
            var mixer = new EventMixer(ClickPack(), ClickBank(800), null, new EventMixerOptions { OriginTick = 10 });
            var o = new float[2 * 100];
            mixer.RenderOffline(new List<SoundEvent> { Finish(8), Finish(9) }, o);
            Assert.Equal(0, mixer.VoicesStarted); // tick 9 ends exactly at the origin frame
            Assert.All(o, v => Assert.Equal(0f, v));
            mixer.RenderOffline(new List<SoundEvent> { Finish(int.MinValue + 5) }, o); // no overflow far before the origin
            Assert.Equal(0, mixer.VoicesStarted);
        }
    }
}
