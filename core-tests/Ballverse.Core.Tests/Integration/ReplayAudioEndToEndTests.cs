using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core.Audio;
using Ballverse.Core.Integration;
using Ballverse.Core.Replay;
using Ballverse.Core.Tests.Audio;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Integration
{
    /// <summary>Replay EVNT records → Audio SoundEvents → offline mix, from a real .bvr byte stream.</summary>
    public class ReplayAudioEndToEndTests
    {
        const int BodylessId = 1000;

        /// <summary>A deterministic "run": hits of 8 bodies with varying impulses, ring breaks, a bodyless finish.</summary>
        static List<ReplayEvent> SimulatedEvents(int durationTicks)
        {
            var rng = Rng.FromSeed(0xA0D10UL, 2);
            var events = new List<ReplayEvent>();
            for (uint t = 0; t < durationTicks; t++)
            {
                if (t % 3 == 0)
                {
                    int body = rng.NextInt(0, 8);
                    events.Add(new ReplayEvent(t, ReplayEventType.Hit, body, ReplayQuantization.QuantizeImpulse(rng.NextFloat(0.2f, 14f)), 0));
                }
                if (t % 97 == 50) events.Add(new ReplayEvent(t, ReplayEventType.Break, rng.NextInt(0, 8), ReplayQuantization.QuantizeImpulse(6f), 1));
            }
            events.Add(new ReplayEvent((uint)durationTicks - 1, ReplayEventType.Finish, BodylessId, 0, 0));
            return events;
        }

        static byte[] WriteReplay(IReadOnlyList<ReplayEvent> events, uint durationTicks)
        {
            var header = new ReplayHeader("ring_escape", 1, "1.0.0", PhysicsBackend.CoreSim, 7UL, "{}");
            var stream = new MemoryStream();
            var writer = new ReplayWriter(stream, header);
            foreach (var e in events) writer.AddEvent(e);
            writer.Finish(durationTicks);
            return stream.ToArray();
        }

        /// <summary>Size and pan from the body id, as a render stream would give them (quantized values).</summary>
        sealed class BodyTable : ISoundBodySource
        {
            public bool TryGetBody(in ReplayEvent e, out float bodySize, out float pan)
            {
                bodySize = 0.2f + 0.1f * (e.BodyId % 8);
                pan = ReplayQuantization.DequantizeSignedUnit(ReplayQuantization.QuantizeSignedUnit(-1f + 0.25f * (e.BodyId % 8)));
                return e.BodyId != BodylessId;
            }
        }

        [Fact]
        public void TypesConvertByValue()
        {
            foreach (ReplayEventType t in Enum.GetValues(typeof(ReplayEventType)))
            {
                SoundEventType s = ReplaySoundEvents.ToSoundEventType(t);
                Assert.Equal((int)t, (int)s);
                Assert.Equal(t.ToString(), s.ToString());
            }
            Assert.Equal(Enum.GetValues(typeof(ReplayEventType)).Length, Enum.GetValues(typeof(SoundEventType)).Length);
            Assert.Throws<ArgumentOutOfRangeException>(() => ReplaySoundEvents.ToSoundEventType((ReplayEventType)8));
        }

        [Fact]
        public void EventFieldsAreMapped()
        {
            var bodies = new UniformSoundBodySource(0.5f, -0.25f, bodylessId: BodylessId);
            var hit = new ReplayEvent(42, ReplayEventType.Hit, 3, 384, 9);
            SoundEvent s = ReplaySoundEvents.ToSoundEvent(in hit, bodies);
            Assert.Equal(42, s.Tick);
            Assert.Equal(SoundEventType.Hit, s.Type);
            Assert.Equal(3, s.BodyId);
            Assert.Equal(1.5f, s.Impulse); // 384 / 256 N·s
            Assert.Equal(0.5f, s.BodySize);
            Assert.Equal(-0.25f, s.Pan);

            var finish = new ReplayEvent(43, ReplayEventType.Finish, BodylessId, 0, 0);
            Assert.Equal(-1, ReplaySoundEvents.ToSoundEvent(in finish, bodies).BodyId); // no body: exempt from the per-body limit
            Assert.Equal(BodylessId, ReplaySoundEvents.ToSoundEvent(in finish, new UniformSoundBodySource(0.5f)).BodyId);

            var huge = new ReplayEvent(uint.MaxValue, ReplayEventType.Hit, 0, 0, 0);
            Assert.Throws<ArgumentOutOfRangeException>(() => ReplaySoundEvents.ToSoundEvent(in huge, bodies));
            Assert.Throws<ArgumentNullException>(() => ReplaySoundEvents.ToSoundEvent(in hit, null));
        }

        [Fact]
        public void OfflineMixFromAReplayFileEqualsTheMixOfTheSimulationEvents()
        {
            const int duration = 600; // 10 s
            List<ReplayEvent> recorded = SimulatedEvents(duration);
            ReplayFile file = ReplayReader.Read(WriteReplay(recorded, duration));
            var bodies = new BodyTable();
            SoundEvent[] fromFile = ReplaySoundEvents.Convert(file, bodies);
            Assert.Equal(recorded.Count, fromFile.Length);

            // What the simulation would have fed the realtime mixer directly (same quantized impulses and body data).
            var direct = new List<SoundEvent>();
            ReplaySoundEvents.Convert(recorded, bodies, direct);

            SoundPack pack = SoundPackCatalog.Get(SoundPackId.Notes);
            var bank = AudioTestUtil.FullBank();
            long frames = (long)duration * AudioConstants.FramesPerTick + AudioConstants.SampleRate;
            float[] a = EventMixer.RenderOffline(pack, bank, null, fromFile, (int)frames);
            float[] b = EventMixer.RenderOffline(pack, bank, null, direct, (int)frames);
            Assert.Equal(b, a);
            double energy = 0;
            foreach (float x in a) energy += x * x;
            Assert.True(energy > 1.0, "the mix must not be silent");

            // The same events placed through the studio timeline (whole clip, 1×, no video): every tick starts at tick × 800.
            var timeline = new StudioTimeline(new TimelineSpec(new ClipTiming(0, duration, duration), null, DurationMode.Auto, 60));
            ScheduledSoundEvent[] scheduled = ExportSoundSchedule.Build(timeline, fromFile);
            Assert.Equal(fromFile.Length, scheduled.Length);
            for (int i = 0; i < scheduled.Length; i++) Assert.Equal((long)scheduled[i].Event.Tick * AudioConstants.FramesPerTick, scheduled[i].Frame);
            var mixer = new EventMixer(pack, bank);
            var c = new float[frames * 2];
            mixer.RenderOffline(scheduled, c);
            Assert.Equal(a, c);
        }

        [Fact]
        public void MelodyPackPlaysTheRemoteMelodyFromAReplay()
        {
            const int duration = 300;
            List<ReplayEvent> recorded = SimulatedEvents(duration);
            ReplayFile file = ReplayReader.Read(WriteReplay(recorded, duration));
            var melody = new Melody("scale", "Scale", new[] { new MelodyNote(60, 1, 1), new MelodyNote(62, 1, 1), new MelodyNote(64, 1, 1) });
            SoundEvent[] events = ReplaySoundEvents.Convert(file, new BodyTable());
            var mixer = new EventMixer(SoundPackCatalog.Get(SoundPackId.Melody), AudioTestUtil.FullBank(), melody);
            var output = new float[(duration * AudioConstants.FramesPerTick + AudioConstants.SampleRate) * 2];
            mixer.RenderOffline(events, output);
            Assert.True(mixer.VoicesStarted > 10);
            Assert.Equal(0, mixer.MissingSamples);
        }

        [Fact]
        public void ConvertSkipsEventsBeyondTheIntTickRange()
        {
            // QA probe R1: ReplayReader bounds record ticks only by the u32 durationTicks, so a corrupted file with a tick above
            // int.MaxValue reads fine; Convert used to throw ArgumentOutOfRangeException from ToSoundEvent.
            var events = new List<ReplayEvent>
            {
                new ReplayEvent(5, ReplayEventType.Hit, 1, ReplayQuantization.QuantizeImpulse(3f), 0),
                new ReplayEvent(2147483648u, ReplayEventType.Hit, 2, ReplayQuantization.QuantizeImpulse(3f), 0),
                new ReplayEvent(uint.MaxValue, ReplayEventType.Finish, 3, 0, 0),
            };
            ReplayFile file = ReplayReader.Read(WriteReplay(events, uint.MaxValue));
            Assert.Equal(3, file.Events.Count);
            SoundEvent[] converted = ReplaySoundEvents.Convert(file, new BodyTable(), out int skipped);
            Assert.Equal(2, skipped);
            SoundEvent only = Assert.Single(converted);
            Assert.Equal(5, only.Tick);
            Assert.Single(ReplaySoundEvents.Convert(file, new BodyTable()));
            var list = new List<SoundEvent>();
            Assert.Equal(2, ReplaySoundEvents.Convert(file.Events, new BodyTable(), list));
            Assert.Single(list);
            Assert.False(ReplaySoundEvents.IsConvertible(events[1]));
            Assert.True(ReplaySoundEvents.IsConvertible(new ReplayEvent(int.MaxValue, ReplayEventType.Hit, 0, 0, 0)));
            ReplayEvent beyond = events[1];
            Assert.Throws<ArgumentOutOfRangeException>(() => ReplaySoundEvents.ToSoundEvent(in beyond, new BodyTable()));
        }
    }
}
