using System;
using System.Collections.Generic;
using System.Text;
using Ballverse.Core.Integration;
using Ballverse.Core.Params;
using Ballverse.Core.Replay;
using Ballverse.Core.Tests.Params;
using Xunit;

namespace Ballverse.Core.Tests.Integration
{
    /// <summary>Replay header paramsJson / paramsHash from Params canonical JSON, and PARM live changes as ParamValue JSON.</summary>
    public class ReplayParamsTests
    {
        static ParamSchema Schema => TestSchemas.RingEscape();

        static ParamSet Sample()
        {
            var raw = TestSchemas.Set(
                ("timeScale", ParamValue.Float(1.35)), ("ballCount", ParamValue.Int(3)), ("theme", ParamValue.String("Candy")),
                ("labels", ParamValue.String("names")), ("labelNames", ParamValue.Strings(new[] { "Анна", "Bob \"B\"", "Zoë😀" })),
                ("ringCount", ParamValue.Int(40)), ("seed", ParamValue.Seed(18446744073709551557UL)), ("rotationSpeed", ParamValue.Float(125)));
            return raw.Sanitize(Schema, DeviceTier.B).Values;
        }

        static ReplayHeader Header(ParamSet set)
        {
            return ReplayParams.CreateHeader("ring_escape", 1, "1.0.0 (1)", PhysicsBackend.CoreSim, 42UL, set, Schema);
        }

        [Fact]
        public void HeaderStoresCanonicalJsonAndTheHashesAreIdentical()
        {
            ParamSet set = Sample();
            ReplayHeader h = Header(set);
            Assert.Equal(ParamJson.ToCanonicalJson(set, Schema), h.ParamsJson);
            Assert.Equal(ParamJson.ParamsHash(set, Schema), h.ParamsHash);
            Assert.Equal(ReplayHeader.ComputeParamsHash(h.ParamsJson), h.ParamsHash);
            Assert.Equal(ParamJson.ToCanonicalUtf8(set, Schema), Encoding.UTF8.GetBytes(h.ParamsJson));
            Assert.True(ReplayParams.Matches(h, set, Schema));
            Assert.False(ReplayParams.Matches(h, set.With("ringCount", ParamValue.Int(41)), Schema));
            // Equivalent raw kinds canonicalize to the same header (int 2 for a float parameter is written 2.0).
            ParamSet viaInt = set.With("timeScale", ParamValue.Int(2));
            ParamSet viaFloat = set.With("timeScale", ParamValue.Float(2.0));
            Assert.Equal(Header(viaInt).ParamsHash, Header(viaFloat).ParamsHash);
        }

        [Fact]
        public void UnsanitizedParametersAreRejected()
        {
            ParamSet bad = Sample().With("ringCount", ParamValue.Int(1000));
            Assert.Throws<ArgumentException>(() => Header(bad));
        }

        [Fact]
        public void HeaderAndLiveChangesRoundTripThroughABvrFile()
        {
            ParamSet initial = Sample();
            ReplayHeader header = Header(initial);
            var changes = new[]
            {
                ReplayParams.ParamChange(30, "timeScale", ParamValue.Float(2.5), Schema),
                ReplayParams.ParamChange(30, "trail", ParamValue.String("rainbow"), Schema),
                ReplayParams.ParamChange(90, "ballColors", ParamValue.Colors(new[] { new ColorRgba(255, 0, 102), new ColorRgba(0, 229, 255, 204) }), Schema),
                ReplayParams.ParamChange(120, "labelNames", ParamValue.Strings(new[] { "x\\y", "quote\"" }), Schema),
            };
            Assert.Equal("2.5", changes[0].ValueJson);
            Assert.Equal("\"rainbow\"", changes[1].ValueJson);
            Assert.Equal("[\"#FF0066\",\"#00E5FFCC\"]", changes[2].ValueJson);

            var output = new System.IO.MemoryStream();
            var writer = new ReplayWriter(output, header);
            foreach (var c in changes) writer.AddParamChange(c);
            writer.Finish(600);
            ReplayFile file = ReplayReader.Read(output.ToArray());

            Assert.Equal(header.ParamsHash, file.Header.ParamsHash);
            ReplayParamsResult read = ReplayParams.ReadParams(file.Header, Schema);
            Assert.True(read.IsExact);
            Assert.Empty(read.Issues);
            Assert.Equal(initial, read.Values);

            Assert.Equal(initial, ReplayParams.ParamsAtTick(read.Values, file.ParamChanges, 29, Schema));
            ParamSet at30 = ReplayParams.ParamsAtTick(read.Values, file.ParamChanges, 30, Schema);
            Assert.Equal(2.5, at30.GetFloat("timeScale"), 6);
            Assert.Equal("rainbow", at30.GetString("trail"));
            ParamSet atEnd = ReplayParams.ParamsAtTick(read.Values, file.ParamChanges, 600, Schema);
            Assert.Equal(new[] { "x\\y", "quote\"" }, atEnd.GetStrings("labelNames"));
            Assert.Equal(2, atEnd.GetColors("ballColors").Count);
            for (int i = 0; i < changes.Length; i++)
            {
                Assert.True(ReplayParams.TryReadParamChange(file.ParamChanges[i], Schema, out ParamValue v, out var issues));
                Assert.Empty(issues);
                Assert.Equal(changes[i].ValueJson, ParamJson.WriteValue(v));
            }
        }

        [Fact]
        public void OnlyLiveParametersAreRecorded()
        {
            // spec §2.2: structural changes restart the run instead of being recorded.
            Assert.Throws<ArgumentException>(() => ReplayParams.ParamChange(1, "ringCount", ParamValue.Int(20), Schema));
            Assert.Throws<ArgumentException>(() => ReplayParams.ParamChange(1, "rotationSpeed", ParamValue.Float(60), Schema));
            Assert.Throws<ArgumentException>(() => ReplayParams.ParamChange(1, "nope", ParamValue.Int(1), Schema));
            Assert.Throws<ArgumentException>(() => ReplayParams.ParamChange(1, null, ParamValue.Int(1), Schema));
            // A live value must already be sanitized (timeScale step 0.05, range 0.25–3).
            Assert.Throws<ArgumentException>(() => ReplayParams.ParamChange(1, "timeScale", ParamValue.Float(9.0), Schema));
            Assert.Throws<ArgumentException>(() => ReplayParams.ParamChange(1, "timeScale", ParamValue.Float(1.33), Schema));
        }

        [Fact]
        public void ReadingNeverThrowsOnStoredData()
        {
            Assert.False(ReplayParams.TryReadParamChange(new ReplayParamChange(1, "ringCount", "20"), Schema, out _, out _));
            Assert.False(ReplayParams.TryReadParamChange(new ReplayParamChange(1, "ghost", "1"), Schema, out _, out _));
            Assert.False(ReplayParams.TryReadParamChange(new ReplayParamChange(1, "timeScale", "{oops"), Schema, out _, out _));
            Assert.True(ReplayParams.TryReadParamChange(new ReplayParamChange(1, "timeScale", "99"), Schema, out ParamValue clamped, out var issues));
            Assert.Equal(3.0, clamped.AsFloat);
            Assert.NotEmpty(issues);

            // A header written by an older schema (fewer ids, an unknown one): defaults fill in, the hash no longer matches.
            var old = new ReplayHeader("ring_escape", 1, "0.9", PhysicsBackend.CoreSim, 1UL, "{\"ringCount\":50,\"retired\":true}");
            ReplayParamsResult r = ReplayParams.ReadParams(old, Schema);
            Assert.False(r.IsExact);
            Assert.Equal(50, r.Values.GetInt("ringCount"));
            Assert.Equal(Schema.Count, r.Values.Count);
            Assert.Contains(r.Issues, i => i.Code == ParamIssueCode.Missing && i.ParamId == "timeScale");
            Assert.Contains(r.Issues, i => i.Code == ParamIssueCode.Unknown && i.ParamId == "retired");

            var notObject = new ReplayHeader("ring_escape", 1, "0.9", PhysicsBackend.CoreSim, 1UL, "[1,2]");
            ReplayParamsResult n = ReplayParams.ReadParams(notObject, Schema);
            Assert.False(n.IsExact);
            Assert.Equal(Schema.CreateDefaults(DeviceTier.A), n.Values);
            Assert.Single(n.Issues);
        }

        [Fact]
        public void ParamsAtTickSkipsUndecodableChanges()
        {
            ParamSet initial = Sample();
            var changes = new List<ReplayParamChange>
            {
                new ReplayParamChange(5, "ringCount", "20"), // structural: ignored
                new ReplayParamChange(6, "trail", "\"long\""),
                new ReplayParamChange(7, "trail", "\"nonsense\""), // invalid option: sanitized to the default
            };
            Assert.Equal("long", ReplayParams.ParamsAtTick(initial, changes, 6, Schema).GetString("trail"));
            ParamSet at7 = ReplayParams.ParamsAtTick(initial, changes, 7, Schema);
            Assert.Equal("short", at7.GetString("trail"));
            Assert.Equal(initial.GetInt("ringCount"), at7.GetInt("ringCount"));
        }
    }
}
