using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    public class SoundPackTests
    {
        [Fact]
        public void CatalogHasEveryPackFromSpecTableAndAllValidate()
        {
            Assert.Equal(new[] { "Notes", "Melody", "Pop", "Glass", "Drums", "Asmr", "Chiptune", "None" },
                ToNames(SoundPackCatalog.Ids));
            foreach (var id in SoundPackCatalog.Ids)
            {
                var p = SoundPackCatalog.Get(id);
                Assert.Equal(id, p.Id);
                Assert.Empty(p.Validate());
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => SoundPackCatalog.Get((SoundPackId)99));
        }

        [Fact]
        public void CatalogReturnsIndependentInstances()
        {
            var a = SoundPackCatalog.Get(SoundPackId.Notes);
            a.Rules.Clear();
            Assert.NotEmpty(SoundPackCatalog.Get(SoundPackId.Notes).Rules);
        }

        [Fact]
        public void NonePackHasNoRules()
        {
            var p = SoundPackCatalog.Get(SoundPackId.None);
            foreach (SoundEventType t in Enum.GetValues(typeof(SoundEventType))) Assert.Null(p.GetRule(t));
        }

        [Fact]
        public void NotesPackHitIsPentatonicBySizeC3ToC6()
        {
            var p = SoundPackCatalog.Get(SoundPackId.Notes);
            var hit = p.GetRule(SoundEventType.Hit);
            Assert.Equal(PitchMode.PentatonicBySize, hit.Pitch);
            Assert.Equal(48, SoundPitch.PentatonicMidi(1.5f, p.SizeMin, p.SizeMax, hit.LowMidi, hit.Steps));
            Assert.Equal(48, SoundPitch.PentatonicMidi(10f, p.SizeMin, p.SizeMax, hit.LowMidi, hit.Steps));
            Assert.Equal(84, SoundPitch.PentatonicMidi(0.1f, p.SizeMin, p.SizeMax, hit.LowMidi, hit.Steps));
            Assert.Equal(84, SoundPitch.PentatonicMidi(0f, p.SizeMin, p.SizeMax, hit.LowMidi, hit.Steps));
        }

        [Fact]
        public void PentatonicPitchIsMonotoneAndAlwaysInScale()
        {
            var allowed = new HashSet<int> { 0, 2, 4, 7, 9 };
            int prev = int.MaxValue;
            var distinct = new HashSet<int>();
            for (int i = 0; i <= 2000; i++)
            {
                float size = 0.05f + i * 0.00075f; // 0.05 .. 1.55
                int midi = SoundPitch.PentatonicMidi(size, 0.1f, 1.5f, 48, 15);
                Assert.True(midi <= prev, "bigger must not be higher");
                Assert.Contains((midi - 48) % 12, allowed);
                Assert.InRange(midi, 48, 84);
                prev = midi;
                distinct.Add(midi);
            }
            Assert.Equal(16, distinct.Count); // 15 steps + tonic
        }

        [Fact]
        public void PentatonicHandlesNaNAndDegenerateInput()
        {
            int mid = SoundPitch.PentatonicMidi(float.NaN, 0.1f, 1.5f, 48, 15);
            Assert.InRange(mid, 48, 84);
            Assert.Equal(127, SoundPitch.PentatonicMidi(0f, 0.1f, 1.5f, 120, 15));
            Assert.Equal(48, SoundPitch.PentatonicMidi(0.5f, 0.1f, 1.5f, 48, 0));
        }

        [Fact]
        public void SizeSemitoneOffsetSpansRange()
        {
            Assert.Equal(5, SoundPitch.SizeSemitoneOffset(0.1f, 0.1f, 1.5f, 5));
            Assert.Equal(-5, SoundPitch.SizeSemitoneOffset(1.5f, 0.1f, 1.5f, 5));
            Assert.Equal(0, SoundPitch.SizeSemitoneOffset(0.8f, 0.1f, 1.5f, 5));
        }

        [Fact]
        public void SampleSelectionIsPureAndCoversAllSamples()
        {
            var seen = new int[4];
            for (int t = 0; t < 400; t++)
            {
                var e = SoundEvent.Hit(t, t % 7, 1f, 0.5f);
                int a = SoundPitch.SelectSample(SampleSelection.Hash, 4, in e, 0.1f, 1.5f);
                int b = SoundPitch.SelectSample(SampleSelection.Hash, 4, in e, 0.1f, 1.5f);
                Assert.Equal(a, b);
                seen[a]++;
            }
            foreach (int c in seen) Assert.InRange(c, 60, 140);

            var big = SoundEvent.Hit(0, 1, 1f, 1.5f);
            var small = SoundEvent.Hit(0, 1, 1f, 0.1f);
            Assert.Equal(0, SoundPitch.SelectSample(SampleSelection.BySize, 4, in big, 0.1f, 1.5f));
            Assert.Equal(3, SoundPitch.SelectSample(SampleSelection.BySize, 4, in small, 0.1f, 1.5f));
            Assert.Equal(0, SoundPitch.SelectSample(SampleSelection.Hash, 1, in big, 0.1f, 1.5f));
        }

        [Fact]
        public void JsonRoundTripPreservesPack()
        {
            foreach (var id in SoundPackCatalog.Ids)
            {
                string json = SoundPackJson.Serialize(SoundPackCatalog.Get(id));
                Assert.True(SoundPackJson.TryParse(json, out SoundPack back, out var errors), string.Join(";", errors));
                Assert.Equal(json, SoundPackJson.Serialize(back));
            }
            string notes = SoundPackJson.Serialize(SoundPackCatalog.Get(SoundPackId.Notes));
            Assert.Contains("\"id\": \"Notes\"", notes);
            Assert.Contains("\"pitch\": \"PentatonicBySize\"", notes);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("[")]
        [InlineData("[1,2,3]")]
        [InlineData("null")]
        [InlineData("42")]
        [InlineData("{\"id\":\"Bogus\"}")]
        [InlineData("{\"id\":\"Notes\",\"i0\":2,\"i1\":1}")]
        [InlineData("{\"id\":\"Notes\",\"i0\":0,\"i1\":1,\"rules\":[{\"eventType\":\"Hit\"}]}")]
        [InlineData("{\"id\":\"Notes\",\"i0\":0,\"i1\":1,\"rules\":[{\"eventType\":\"Hit\",\"samples\":[\"a\"]},{\"eventType\":\"Hit\",\"samples\":[\"b\"]}]}")]
        [InlineData("{\"id\":\"Notes\",\"i0\":0,\"i1\":1,\"rules\":[{\"eventType\":\"Hit\",\"samples\":[\"a\"],\"rootMidi\":300}]}")]
        [InlineData("{\"id\":\"Notes\",\"i0\":0,\"i1\":1,\"rules\":[{\"eventType\":\"Hit\",\"samples\":[\"\"]}]}")]
        [InlineData("{\"id\":\"Notes\",\"i0\":0,\"i1\":1,\"gain\":9,\"rules\":[]}")]
        [InlineData("{\"id\":\"Notes\",\"i0\":0,\"i1\":1,\"rules\":[{\"eventType\":17,\"samples\":[\"a\"]}]}")]
        [InlineData("{\"id\":\"Notes\",\"i0\":0,\"i1\":1,\"rules\":[null]}")]
        [InlineData("{\"id\":\"Notes\",\"i0\":\"abc\"}")]
        public void TryParseRejectsBadDataWithoutThrowing(string json)
        {
            Assert.False(SoundPackJson.TryParse(json, out SoundPack pack, out var errors));
            Assert.Null(pack);
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void ValidateReportsEveryProblem()
        {
            var p = new SoundPack { Id = SoundPackId.Pop, I0 = float.NaN, I1 = 0f, Gain = -1f, SizeMin = 2f, SizeMax = 1f };
            p.Rules.Add(new SoundRule(SoundEventType.Hit, "x") { Steps = -1, SemitoneRange = 99, Semitones = 60, Gain = float.NaN, LowMidi = -1 });
            var errors = p.Validate();
            Assert.True(errors.Count >= 9, string.Join("\n", errors));
            p.Rules = null;
            Assert.Contains("rules must be an array", p.Validate());
        }

        static string[] ToNames(IReadOnlyList<SoundPackId> ids)
        {
            var s = new string[ids.Count];
            for (int i = 0; i < s.Length; i++) s[i] = ids[i].ToString();
            return s;
        }
    }
}
