using System;
using System.IO;
using System.Text;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Xunit;

namespace Ballverse.Core.Tests.Audio
{
    public class MelodyTests
    {
        static string TestFile(string name)
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Audio", name));
        }

        internal static Melody Scale(int count = 8)
        {
            var notes = new MelodyNote[count];
            for (int i = 0; i < count; i++) notes[i] = new MelodyNote(60 + i, 1f, 1f);
            return new Melody("scale", "Scale", notes);
        }

        [Fact]
        public void ParsesValidMelodyFile()
        {
            Assert.True(MelodyJson.TryParse(TestFile("ode_to_joy.json"), out Melody m, out var errors), string.Join(";", errors));
            Assert.Empty(errors);
            Assert.Equal("ode_to_joy", m.Id);
            Assert.Equal(15, m.Count);
            Assert.Equal(64, m[0].Pitch);
            Assert.Equal(0.8f, m[0].Velocity);
            Assert.Equal(1.5f, m[12].DurWeight);
        }

        [Fact]
        public void InvalidFileReportsEachProblem()
        {
            Assert.False(MelodyJson.TryParse(TestFile("melody_invalid.json"), out Melody m, out var errors));
            Assert.Null(m);
            string all = string.Join("\n", errors);
            Assert.Contains("id may contain only", all);
            Assert.Contains("name is missing", all);
            Assert.Contains("notes[0].pitch", all);
            Assert.Contains("notes[1].pitch", all);
            Assert.Contains("notes[2].durWeight", all);
            Assert.Contains("notes[3].durWeight", all);
            Assert.Contains("notes[3].velocity", all);
            Assert.Contains("notes[4].pitch is missing", all);
            Assert.Contains("notes[5].is null", all);
        }

        [Fact]
        public void MissingDurWeightAndVelocityDefaultToOne()
        {
            Assert.True(MelodyJson.TryParse("{\"id\":\"a\",\"name\":\"A\",\"notes\":[{\"pitch\":0},{\"pitch\":127}]}", out Melody m, out _));
            Assert.Equal(1f, m[0].DurWeight);
            Assert.Equal(1f, m[0].Velocity);
            Assert.Equal(127, m[1].Pitch);
        }

        [Fact]
        public void RoundTripThroughSerialize()
        {
            Assert.True(MelodyJson.TryParse(TestFile("ode_to_joy.json"), out Melody m, out _));
            string json = MelodyJson.Serialize(m);
            Assert.True(MelodyJson.TryParse(json, out Melody back, out _));
            Assert.Equal(json, MelodyJson.Serialize(back));
            Assert.Contains("\"durWeight\"", json);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("{")]
        [InlineData("[]")]
        [InlineData("null")]
        [InlineData("true")]
        [InlineData("{\"id\":\"a\",\"name\":\"A\"}")]
        [InlineData("{\"id\":\"a\",\"name\":\"A\",\"notes\":[]}")]
        [InlineData("{\"id\":\"a\",\"name\":\"A\",\"notes\":{}}")]
        [InlineData("{\"id\":\"a\",\"name\":\"A\",\"notes\":[{\"pitch\":\"high\"}]}")]
        [InlineData("{\"id\":\"a\",\"name\":\"A\",\"notes\":[{\"pitch\":-1}]}")]
        [InlineData("{\"id\":\"a\",\"name\":\"A\",\"notes\":[{\"pitch\":1e400}]}")]
        [InlineData("{\"id\":\"a\",\"name\":\"A\",\"notes\":[{\"pitch\":60,\"velocity\":-0.01}]}")]
        [InlineData("{\"id\":\"a\",\"name\":\"A\",\"notes\":[{\"pitch\":60,\"durWeight\":1e-60}]}")]
        [InlineData("{\"id\":\"a\",\"name\":\"A\",\"notes\":[{\"pitch\":60,\"durWeight\":1e300}]}")]
        [InlineData("{\"id\":\"\",\"name\":\"A\",\"notes\":[{\"pitch\":60}]}")]
        [InlineData("{\"id\":5,\"name\":{},\"notes\":[{\"pitch\":60}]}")]
        public void BadInputNeverThrows(string json)
        {
            Assert.False(MelodyJson.TryParse(json, out Melody m, out var errors));
            Assert.Null(m);
            Assert.NotEmpty(errors);
        }

        [Fact]
        public void RandomGarbageNeverThrows()
        {
            var rng = Rng.FromSeed(0xA0D10UL, 7);
            string valid = TestFile("ode_to_joy.json");
            for (int i = 0; i < 300; i++)
            {
                // Mutate a valid document byte-wise or produce random text.
                var sb = new StringBuilder(valid);
                int edits = rng.NextInt(1, 6);
                for (int e = 0; e < edits; e++)
                {
                    int pos = rng.NextInt(0, sb.Length);
                    sb[pos] = (char)rng.NextInt(32, 127);
                }
                MelodyJson.TryParse(sb.ToString(), out _, out var errors1);
                Assert.NotNull(errors1);

                var junk = new char[rng.NextInt(1, 64)];
                for (int k = 0; k < junk.Length; k++) junk[k] = (char)rng.NextInt(1, 0x3000);
                Assert.False(MelodyJson.TryParse(new string(junk), out _, out _));
            }
        }

        [Fact]
        public void LimitsOnSizeAreEnforced()
        {
            var sb = new StringBuilder("{\"id\":\"big\",\"name\":\"Big\",\"notes\":[");
            for (int i = 0; i <= MelodyJson.MaxNotes; i++) sb.Append(i == 0 ? "" : ",").Append("{\"pitch\":60}");
            sb.Append("]}");
            Assert.False(MelodyJson.TryParse(sb.ToString(), out _, out var errors));
            Assert.Contains("at most", errors[0]);

            Assert.False(MelodyJson.TryParse(new string(' ', MelodyJson.MaxJsonLength) + "{}", out _, out _));
            Assert.False(MelodyJson.TryParse("{\"id\":\"" + new string('a', 65) + "\",\"name\":\"A\",\"notes\":[{\"pitch\":60}]}", out _, out _));
        }

        [Fact]
        public void ConstructorValidatesAndCopies()
        {
            Assert.Throws<ArgumentException>(() => new Melody("ok", "Ok", new MelodyNote[0]));
            Assert.Throws<ArgumentException>(() => new Melody("ok", "Ok", new[] { new MelodyNote(200, 1, 1) }));
            Assert.Throws<ArgumentException>(() => new Melody("OK", "Ok", new[] { new MelodyNote(60, 1, 1) }));
            var notes = new[] { new MelodyNote(60, 1, 1) };
            var m = new Melody("ok", "Ok", notes);
            notes[0].Pitch = 10;
            Assert.Equal(60, m[0].Pitch);
        }
    }
}
