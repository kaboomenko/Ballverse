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
            Assert.Contains("id is missing", all);
            Assert.Contains("name is missing", all);
            Assert.Contains("notes[0].pitch", all);
            Assert.Contains("notes[1].pitch", all);
            Assert.Contains("notes[2].durWeight", all);
            Assert.Contains("notes[3].durWeight", all);
            Assert.Contains("notes[3].velocity", all);
            Assert.Contains("notes[4].pitch is missing", all);
            Assert.Contains("notes[5].pitch must be", all);
            Assert.Contains("notes[5].velocity is missing", all);
            Assert.Contains("notes[6].is not an object", all);
        }

        static string Doc(string id, string notes)
        {
            return "{\"id\":" + id + ",\"name\":\"A\",\"notes\":[" + notes + "]}";
        }

        /// <summary>
        /// The shared rule table with the Remote Config melody entries (core/config RemoteConfigParser.ParseMelody):
        /// whatever config accepts must build an audio Melody, and what it rejects must be rejected here.
        /// </summary>
        [Theory]
        [InlineData("\"Ode To Joy\"", "{\"pitch\":64,\"durWeight\":1,\"velocity\":0.5}", true)] // any non-blank id
        [InlineData("\" ode \"", "{\"pitch\":0,\"durWeight\":1e-3,\"velocity\":0}", true)]
        [InlineData("\"a\"", "{\"pitch\":127.0,\"durWeight\":2,\"velocity\":1}", true)] // integral float pitch
        [InlineData("\"a\"", "{\"pitch\":\"64\",\"durWeight\":1,\"velocity\":0.5}", false)] // string pitch
        [InlineData("\"a\"", "{\"pitch\":64,\"durWeight\":\"1\",\"velocity\":0.5}", false)] // string durWeight
        [InlineData("\"a\"", "{\"pitch\":64,\"durWeight\":1,\"velocity\":\"0.5\"}", false)] // string velocity
        [InlineData("\"a\"", "{\"pitch\":64,\"velocity\":0.5}", false)] // durWeight required
        [InlineData("\"a\"", "{\"pitch\":64,\"durWeight\":1}", false)] // velocity required
        [InlineData("\"a\"", "{\"pitch\":64.5,\"durWeight\":1,\"velocity\":0.5}", false)]
        [InlineData("\"a\"", "{\"pitch\":64,\"durWeight\":1,\"velocity\":1.0000001}", false)]
        [InlineData("\"a\"", "{\"pitch\":64,\"durWeight\":1e-50,\"velocity\":0.5}", false)] // rounds to float 0
        [InlineData("\"  \"", "{\"pitch\":64,\"durWeight\":1,\"velocity\":0.5}", false)]
        [InlineData("7", "{\"pitch\":64,\"durWeight\":1,\"velocity\":0.5}", false)] // id must be a string
        public void RulesMatchRemoteConfig(string id, string note, bool valid)
        {
            Assert.Equal(valid, MelodyJson.TryParse(Doc(id, note), out Melody m, out var errors));
            Assert.Equal(valid, errors.Count == 0);
            if (valid) Assert.Equal(m.Id, m.Id.Trim());
        }

        [Fact]
        public void IdAndNameAreTrimmedAndLongMelodiesAreAccepted()
        {
            var notes = new StringBuilder();
            for (int i = 0; i < 5000; i++) notes.Append(i == 0 ? "" : ",").Append("{\"pitch\":60,\"durWeight\":1,\"velocity\":1}");
            Assert.True(MelodyJson.TryParse(Doc("\"  Ode To Joy \"", notes.ToString()), out Melody m, out var errors), string.Join(";", errors));
            Assert.Equal("Ode To Joy", m.Id);
            Assert.Equal(5000, m.Count);
            var big = new MelodyNote[5000];
            for (int i = 0; i < big.Length; i++) big[i] = new MelodyNote(60, 1, 1);
            Assert.Equal(5000, new Melody("Ode To Joy", " Name ", big).Count);
            Assert.Equal("Name", new Melody("x", " Name ", big).Name);
        }

        [Fact]
        public void TryParseTokenValidatesConfigEntries()
        {
            var entry = Newtonsoft.Json.Linq.JToken.Parse("{\"id\":\"x\",\"name\":\"X\",\"notes\":[{\"pitch\":60,\"durWeight\":1,\"velocity\":1}]}");
            Assert.True(MelodyJson.TryParse(entry, out Melody m, out _));
            Assert.Equal(60, m[0].Pitch);
            Assert.False(MelodyJson.TryParse((Newtonsoft.Json.Linq.JToken)null, out m, out var errors));
            Assert.Null(m);
            Assert.NotEmpty(errors);
            Assert.False(MelodyJson.TryParse(Newtonsoft.Json.Linq.JToken.Parse("[1]"), out _, out _));
        }

        [Fact]
        public void TryCreateNeverThrows()
        {
            Assert.True(Melody.TryCreate("Any Id", "N", new[] { new MelodyNote(60, 1, 1) }, out Melody m, out var errors));
            Assert.Empty(errors);
            Assert.Equal("Any Id", m.Id);
            Assert.False(Melody.TryCreate(" ", null, null, out m, out errors));
            Assert.Null(m);
            Assert.Equal(3, errors.Count);
            Assert.False(Melody.TryCreate("a", "A", new[] { new MelodyNote(60, float.NaN, 2f) }, out m, out errors));
            Assert.Equal(2, errors.Count);
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
        [InlineData("{\"id\":\"a\",\"name\":\"A\",\"notes\":[{\"pitch\":99999999999999999999999,\"durWeight\":1,\"velocity\":1}]}")]
        [InlineData("{\"id\":\"a\",\"name\":\"A\",\"notes\":[null]}")]
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
        public void JsonTextLengthIsLimited()
        {
            Assert.False(MelodyJson.TryParse(new string(' ', MelodyJson.MaxJsonLength) + "{}", out _, out var errors));
            Assert.Contains("exceeds", errors[0]);
        }

        [Fact]
        public void ConstructorValidatesAndCopies()
        {
            Assert.Throws<ArgumentException>(() => new Melody("ok", "Ok", new MelodyNote[0]));
            Assert.Throws<ArgumentException>(() => new Melody("ok", "Ok", new[] { new MelodyNote(200, 1, 1) }));
            Assert.Throws<ArgumentException>(() => new Melody(" ", "Ok", new[] { new MelodyNote(60, 1, 1) }));
            Assert.Throws<ArgumentException>(() => new Melody("ok", null, new[] { new MelodyNote(60, 1, 1) }));
            var notes = new[] { new MelodyNote(60, 1, 1) };
            var m = new Melody("ok", "Ok", notes);
            notes[0].Pitch = 10;
            Assert.Equal(60, m[0].Pitch);
        }
    }
}
