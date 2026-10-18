using Ballverse.Core.Projects;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using static Ballverse.Core.Tests.Projects.ProjectTestUtil;

namespace Ballverse.Core.Tests.Projects
{
    public class ProjectModelTests
    {
        [Theory]
        [InlineData("project_v1.json")]
        [InlineData("project_v1_full.json")]
        public void GoldenFileRoundTripsToEquivalentJson(string file)
        {
            string json = ReadData(file);
            var doc = ProjectJson.Deserialize(json);
            string again = ProjectJson.Serialize(doc);
            AssertJsonEquivalent(ProjectJson.ParseObject(json), ProjectJson.ParseObject(again));
            // Stable: a second round trip is byte-identical.
            Assert.Equal(again, ProjectJson.Serialize(ProjectJson.Deserialize(again)));
        }

        [Theory]
        [InlineData("project_v1.json")]
        [InlineData("project_v1_full.json")]
        public void GoldenFilesAreClean(string file)
        {
            var r = ProjectValidator.Sanitize(ProjectJson.Deserialize(ReadData(file)));
            Assert.True(r.IsClean, string.Join("\n", r.Issues));
        }

        [Fact]
        public void VerbatimSpecExampleRoundTrips()
        {
            // §7.4 example as printed: abbreviated id "6f1c…" and no userVideo.afterEnd (added by this module, default freeze).
            var spec = ProjectJson.ParseObject(ReadData("project_v1.json"));
            spec["id"] = "6f1c…";
            ((JObject)spec["userVideo"]).Remove("afterEnd");
            var doc = ProjectJson.ToDocument(spec);
            var back = ProjectJson.ToJObject(doc);
            Assert.Equal("freeze", (string)back["userVideo"]["afterEnd"]);
            ((JObject)back["userVideo"]).Remove("afterEnd");
            AssertJsonEquivalent(spec, back);
            // The abbreviated id is not a usable folder name: an error without a folder id, repaired with one.
            Assert.True(ProjectValidator.Sanitize(doc).HasErrors);
            Assert.False(ProjectValidator.Sanitize(doc, "6f1c").HasErrors);
        }

        [Fact]
        public void SpecExampleFieldsDeserialize()
        {
            var d = Golden();
            Assert.Equal(1, d.SchemaVersion);
            Assert.Equal("Ring Escape 12", d.Name);
            Assert.Equal("2026-10-18T12:00:00Z", d.CreatedAt);
            Assert.Equal("2026-10-18T12:05:00Z", d.UpdatedAt);
            Assert.Equal("clip.bvr", d.Clip.Replay);
            Assert.Equal("ring_escape", d.Clip.ModeId);
            Assert.Equal(1800, d.Clip.TrimOutTick);
            Assert.Equal(2.0, d.Clip.FinaleSlowMo.Seconds);
            Assert.Equal(0.5, d.Clip.FinaleSlowMo.Factor);
            Assert.Equal(SimAfterEnd.Freeze, d.Clip.AfterEnd);
            Assert.Equal("Melody", d.Clip.SoundPack);
            Assert.Equal("ode_to_joy", d.Clip.Melody);
            Assert.Equal("media/user.mp4", d.UserVideo.File);
            Assert.Equal(30000000L, d.UserVideo.TrimOutUs);
            Assert.Equal(SlotFill.Cover, d.UserVideo.Fill);
            Assert.Equal(new[] { 0.0, 0.0 }, d.UserVideo.Pan);
            Assert.Equal(LayoutType.L1, d.Layout.Type);
            Assert.Equal(0.55, d.Layout.Ratio);
            Assert.Equal(6, d.Layout.Divider.Px);
            Assert.Equal("#FFFFFF33", d.Layout.Divider.Color);
            Assert.True(d.Layout.Divider.Glass);
            Assert.Equal(24, d.Layout.CornerRadius);
            Assert.Null(d.Layout.Pip);
            var t = Assert.Single(d.Texts);
            Assert.Equal("Какой шар победит?", t.Text);
            Assert.Equal(TextPlate.Glass, t.Plate);
            Assert.Equal(4, t.Stroke.Px);
            Assert.Equal(new[] { 0.5, 0.12 }, t.Pos);
            Assert.Equal(0.9, d.Audio.Sim);
            Assert.Null(d.Audio.Music);
            Assert.Equal("9:16", d.Output.Aspect);
            Assert.Equal(DurationMode.Auto, d.Output.DurationMode);
            Assert.True(d.Output.Watermark);
            var e = Assert.Single(d.Exports);
            Assert.Equal(1080, e.Res);
            Assert.Equal(28400, e.Ms);
            Assert.Null(e.File);
        }

        [Fact]
        public void EnumsUseSpecCasing()
        {
            var o = ProjectJson.ToJObject(ProjectJson.Deserialize(ReadData("project_v1_full.json")));
            Assert.Equal("slowMoReplay", (string)o["clip"]["afterEnd"]);
            Assert.Equal("fit", (string)o["userVideo"]["fill"]);
            Assert.Equal("expandSim", (string)o["userVideo"]["afterEnd"]);
            Assert.Equal("L5", (string)o["layout"]["type"]);
            Assert.Equal("circle", (string)o["layout"]["pip"]["shape"]);
            Assert.Equal("solid", (string)o["texts"][0]["plate"]);
            Assert.Equal("manual", (string)o["output"]["durationMode"]);
        }

        [Fact]
        public void EnumsParseCaseInsensitively()
        {
            var o = ProjectJson.ParseObject(ReadData("project_v1.json"));
            o["clip"]["afterEnd"] = "Loop";
            o["layout"]["type"] = "l3";
            o["output"]["durationMode"] = "MatchVideo";
            var d = ProjectJson.ToDocument(o);
            Assert.Equal(SimAfterEnd.Loop, d.Clip.AfterEnd);
            Assert.Equal(LayoutType.L3, d.Layout.Type);
            Assert.Equal(DurationMode.MatchVideo, d.Output.DurationMode);
        }

        [Fact]
        public void NullableSectionsAreWrittenAsNull()
        {
            var d = Golden();
            d.UserVideo = null;
            var o = ProjectJson.ToJObject(d);
            Assert.Equal(JTokenType.Null, o["userVideo"].Type);
            Assert.Equal(JTokenType.Null, o["layout"]["pip"].Type);
            Assert.Equal(JTokenType.Null, o["audio"]["music"].Type);
            Assert.Equal(JTokenType.Null, o["exports"][0]["file"].Type);
            // Optional output fields are omitted while unset.
            Assert.Null(o["output"]["manualDurationUs"]);
            Assert.Null(o["output"]["resolution"]);
            Assert.Null(o["output"]["fps"]);
        }

        [Fact]
        public void KeyOrderFollowsSpecExample()
        {
            var o = ProjectJson.ToJObject(Golden());
            var keys = new System.Collections.Generic.List<string>();
            foreach (var p in o.Properties()) keys.Add(p.Name);
            Assert.Equal(new[] { "schemaVersion", "id", "name", "createdAt", "updatedAt", "clip", "userVideo", "layout", "texts", "audio", "output", "exports" }, keys);
        }

        [Fact]
        public void TimestampsStayStrings()
        {
            var o = ProjectJson.ParseObject("{\"createdAt\":\"2026-10-18T12:00:00.5000000+03:00\"}");
            Assert.Equal(JTokenType.String, o["createdAt"].Type);
            Assert.Equal("2026-10-18T12:00:00.5000000+03:00", (string)o["createdAt"]);
        }

        [Theory]
        [InlineData("")]
        [InlineData("[1,2]")]
        [InlineData("{\"a\":1} {\"b\":2}")]
        [InlineData("{\"a\":1,\"a\":2}")]
        [InlineData("{\"a\":")]
        [InlineData("null")]
        public void ParseObjectRejectsNonObjects(string json)
        {
            Assert.ThrowsAny<JsonException>(() => ProjectJson.ParseObject(json));
        }

        [Fact]
        public void WrongValueTypeFailsDeserialization()
        {
            var o = ProjectJson.ParseObject(ReadData("project_v1.json"));
            o["clip"]["trimOutTick"] = "lots";
            Assert.ThrowsAny<JsonException>(() => ProjectJson.ToDocument(o));
        }

        [Fact]
        public void CloneIsDeep()
        {
            var a = Golden();
            var b = ProjectJson.Clone(a);
            b.Texts[0].Text = "changed";
            b.UserVideo.Pan[0] = 0.5;
            Assert.Equal("Какой шар победит?", a.Texts[0].Text);
            Assert.Equal(0.0, a.UserVideo.Pan[0]);
        }

        [Fact]
        public void NewProjectIsCleanAndUsesDefaults()
        {
            var d = ProjectDefaults.NewProject("abc", "My reel", Now, "marble_race", 1200);
            var r = ProjectValidator.Sanitize(d);
            Assert.True(r.IsClean, string.Join("\n", r.Issues));
            Assert.Equal(ProjectMigrator.CurrentSchemaVersion, d.SchemaVersion);
            Assert.Equal(Now, d.CreatedAt);
            Assert.Equal(Now, d.UpdatedAt);
            Assert.Equal(1200, d.Clip.TrimOutTick);
            Assert.Null(d.UserVideo);
            Assert.Equal(LayoutType.L1, d.Layout.Type);
            Assert.False(d.Output.Watermark);
            Assert.Empty(d.Texts);
            Assert.Empty(d.Exports);
        }

        [Fact]
        public void NewProjectClampsClipLength()
        {
            Assert.Equal(SimConstants.MaxDurationTicks, ProjectDefaults.NewProject("a", "n", Now, "m", int.MaxValue).Clip.TrimOutTick);
            Assert.Equal(1, ProjectDefaults.NewProject("a", "n", Now, "m", 0).Clip.TrimOutTick);
        }
    }
}
