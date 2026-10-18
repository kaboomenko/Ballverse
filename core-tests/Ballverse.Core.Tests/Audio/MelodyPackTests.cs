using System.Collections.Generic;
using Ballverse.Core.Audio;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    public class MelodyPackTests
    {
        static SoundEventProcessor MelodyProcessor(int notes = 8)
        {
            return new SoundEventProcessor(SoundPackCatalog.Get(SoundPackId.Melody), MelodyTests.Scale(notes));
        }

        [Fact]
        public void HitsPlayMelodyNotesInOrderRelativeToSampleRoot()
        {
            var p = MelodyProcessor(4);
            var semis = new List<int>();
            for (int i = 0; i < 6; i++)
            {
                var e = SoundEvent.Hit(i * 10, i, 6f);
                Assert.Equal(SoundDecision.Played, p.Process(in e, out VoiceRequest r));
                Assert.Equal(i % 4, r.MelodyIndex);
                semis.Add(r.Semitones);
            }
            // piano sample root is C4 (60); scale notes 60..63
            Assert.Equal(new[] { 0, 1, 2, 3, 0, 1 }, semis);
        }

        [Fact]
        public void MelodyLimitIs20PerSecondAndExcessDoesNotAdvance()
        {
            var p = MelodyProcessor(64);
            for (int b = 0; b < 20; b++)
            {
                var e = SoundEvent.Hit(0, b, 6f);
                Assert.Equal(SoundDecision.Played, p.Process(in e, out _));
            }
            var extra = SoundEvent.Hit(30, 99, 6f);
            Assert.Equal(SoundDecision.MelodyRateLimited, p.Process(in extra, out _));
            Assert.Equal(20, p.MelodyCursor.NextIndex);
            var later = SoundEvent.Hit(60, 98, 6f);
            Assert.Equal(SoundDecision.Played, p.Process(in later, out VoiceRequest r));
            Assert.Equal(20, r.MelodyIndex);
        }

        [Fact]
        public void MelodyNotesCountTowardTheOverall40PerSecond()
        {
            var p = MelodyProcessor(64);
            for (int b = 0; b < 20; b++)
            {
                var e = SoundEvent.Hit(0, b, 6f);
                Assert.Equal(SoundDecision.Played, p.Process(in e, out _));
            }
            // 20 notes used half of the 40/s total: only 20 other sounds fit in the same second.
            for (int i = 0; i < 20; i++)
            {
                var e = new SoundEvent(0, SoundEventType.Score, -1, 0f, 0.5f, 0f);
                Assert.Equal(SoundDecision.Played, p.Process(in e, out _));
            }
            var over = new SoundEvent(0, SoundEventType.Score, -1, 0f, 0.5f, 0f);
            Assert.Equal(SoundDecision.GlobalRateLimited, p.Process(in over, out _));
        }

        [Fact]
        public void NoteRefusedByTheOverallLimitDoesNotAdvanceMelody()
        {
            var p = MelodyProcessor(64);
            for (int i = 0; i < 40; i++)
            {
                var e = new SoundEvent(0, SoundEventType.Score, -1, 0f, 0.5f, 0f);
                Assert.Equal(SoundDecision.Played, p.Process(in e, out _));
            }
            var hit = SoundEvent.Hit(1, 1, 6f);
            Assert.Equal(SoundDecision.GlobalRateLimited, p.Process(in hit, out _));
            Assert.Equal(0, p.MelodyCursor.NextIndex);
            var later = SoundEvent.Hit(60, 1, 6f); // exactly 1 s after the 40 sounds
            Assert.Equal(SoundDecision.Played, p.Process(in later, out VoiceRequest r));
            Assert.Equal(0, r.MelodyIndex);
        }

        [Fact]
        public void PitchesBeyondTheResamplerRangeAreOctaveFoldedAndReported()
        {
            var melody = new Melody("wide", "Wide", new[] { new MelodyNote(120, 1, 1), new MelodyNote(5, 1, 1), new MelodyNote(72, 1, 1) });
            var p = new SoundEventProcessor(SoundPackCatalog.Get(SoundPackId.Melody), melody);
            Assert.Equal(2, p.MelodyNotesOutOfRange);
            var expected = new[] { 48, -43, 12 }; // 120−60 = 60 → 48 (C, an octave low); 5−60 = −55 → −43 (F); in range unchanged
            for (int i = 0; i < 3; i++)
            {
                var e = SoundEvent.Hit(i * 10, i, 6f);
                Assert.Equal(SoundDecision.Played, p.Process(in e, out VoiceRequest r));
                Assert.Equal(expected[i], r.Semitones);
                Assert.Equal(0, ((r.Semitones - (melody[i].Pitch - 60)) % 12 + 12) % 12);
            }
            Assert.Equal(0, MelodyProcessor(8).MelodyNotesOutOfRange);
            Assert.Equal(48, SoundEventProcessor.FoldSemitones(48));
            Assert.Equal(37, SoundEventProcessor.FoldSemitones(49));
            Assert.Equal(-48, SoundEventProcessor.FoldSemitones(-48));
            Assert.Equal(-37, SoundEventProcessor.FoldSemitones(-49));
        }

        [Fact]
        public void RejectedHitsDoNotAdvanceMelody()
        {
            var p = MelodyProcessor(8);
            var quiet = SoundEvent.Hit(0, 1, 0.01f);
            Assert.Equal(SoundDecision.BelowThreshold, p.Process(in quiet, out _));
            var a = SoundEvent.Hit(0, 1, 6f);
            Assert.Equal(SoundDecision.Played, p.Process(in a, out _));
            var sameBody = SoundEvent.Hit(1, 1, 6f);
            Assert.Equal(SoundDecision.BodyRateLimited, p.Process(in sameBody, out _));
            Assert.Equal(1, p.MelodyCursor.NextIndex);
        }

        [Fact]
        public void VelocityScalesGainAndZeroVelocityIsAMutedRest()
        {
            var melody = new Melody("v", "V", new[] { new MelodyNote(60, 1, 0.5f), new MelodyNote(62, 1, 0f), new MelodyNote(64, 1, 1f) });
            var p = new SoundEventProcessor(SoundPackCatalog.Get(SoundPackId.Melody), melody);
            var e0 = SoundEvent.Hit(0, 1, 100f);
            Assert.Equal(SoundDecision.Played, p.Process(in e0, out VoiceRequest r0));
            Assert.Equal(0.5f, r0.Gain, 6);
            var e1 = SoundEvent.Hit(0, 2, 100f);
            Assert.Equal(SoundDecision.Muted, p.Process(in e1, out _));
            var e2 = SoundEvent.Hit(0, 3, 100f);
            Assert.Equal(SoundDecision.Played, p.Process(in e2, out VoiceRequest r2));
            Assert.Equal(4, r2.Semitones);
        }

        [Fact]
        public void WithoutMelodyFallsBackToPentatonicAndGlobalLimit()
        {
            var p = new SoundEventProcessor(SoundPackCatalog.Get(SoundPackId.Melody));
            Assert.Null(p.MelodyCursor);
            for (int b = 0; b < 40; b++)
            {
                var e = SoundEvent.Hit(0, b, 6f, 1.5f);
                Assert.Equal(SoundDecision.Played, p.Process(in e, out VoiceRequest r));
                Assert.Equal(48 - 60, r.Semitones);
                Assert.Equal(-1, r.MelodyIndex);
            }
            var over = SoundEvent.Hit(0, 99, 6f);
            Assert.Equal(SoundDecision.GlobalRateLimited, p.Process(in over, out _));
        }
    }
}
