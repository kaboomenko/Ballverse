using System;
using Ballverse.Core.Projects;
using Newtonsoft.Json.Linq;
using Xunit;
using static Ballverse.Core.Tests.Projects.ProjectTestUtil;

namespace Ballverse.Core.Tests.Projects
{
    public class ProjectMigratorTests
    {
        // Fake chain 0 -> 1 -> 2 -> 3 used to test the framework (the shipped schema starts at 1 and has no steps yet).
        static ProjectMigrationStep Step0To1()
        {
            return new ProjectMigrationStep(0, o =>
            {
                o["name"] = o["title"];
                o.Remove("title");
                return o;
            });
        }

        static ProjectMigrationStep Step1To2()
        {
            return new ProjectMigrationStep(1, o =>
            {
                o["durationUs"] = (long)o["durationSec"] * 1000000L;
                o.Remove("durationSec");
                return o;
            });
        }

        static ProjectMigrationStep Step2To3()
        {
            return new ProjectMigrationStep(2, o =>
            {
                // Returns a new object instead of mutating, which is allowed too.
                var n = (JObject)o.DeepClone();
                var audio = (JObject)n["audio"];
                audio["sim"] = audio["volume"];
                audio.Remove("volume");
                n["tags"] = new JArray();
                return n;
            });
        }

        static ProjectMigrator Fake()
        {
            // Registration order does not matter.
            return new ProjectMigrator(3, new[] { Step2To3(), Step0To1(), Step1To2() });
        }

        [Fact]
        public void CurrentVersionIsOne()
        {
            Assert.Equal(1, ProjectMigrator.CurrentSchemaVersion);
            Assert.Equal(1, ProjectMigrator.Default.TargetVersion);
            Assert.Equal(1, ProjectMigrator.Default.MinVersion);
        }

        [Fact]
        public void DefaultPassesCurrentGoldenThroughUnchanged()
        {
            var input = ProjectJson.ParseObject(ReadData("project_v1.json"));
            var r = ProjectMigrator.Default.Migrate(input);
            Assert.True(r.Success);
            Assert.False(r.Migrated);
            Assert.Equal(1, r.FromVersion);
            Assert.True(JToken.DeepEquals(input, r.Document));
            Assert.NotSame(input, r.Document);
        }

        [Fact]
        public void FakeChainMigratesV0GoldenToV3()
        {
            var input = ProjectJson.ParseObject(ReadData("Migrations/fake_v0.json"));
            var snapshot = input.DeepClone();
            var r = Fake().Migrate(input);
            Assert.True(r.Success, r.Message);
            Assert.True(r.Migrated);
            Assert.Equal(0, r.FromVersion);
            Assert.Equal(3, r.ToVersion);
            AssertJsonEquivalent(ProjectJson.ParseObject(ReadData("Migrations/fake_v3_expected.json")), r.Document);
            Assert.True(JToken.DeepEquals(snapshot, input), "input must not be mutated");
        }

        [Fact]
        public void FakeChainStartsMidway()
        {
            var r = Fake().Migrate(ProjectJson.ParseObject(ReadData("Migrations/fake_v1.json")));
            Assert.True(r.Success, r.Message);
            AssertJsonEquivalent(ProjectJson.ParseObject(ReadData("Migrations/fake_v1_to_v3_expected.json")), r.Document);
        }

        [Fact]
        public void EachStepRunsOnceInOrderAndVersionIsSetByMigrator()
        {
            var log = new System.Collections.Generic.List<string>();
            ProjectMigrationStep S(int from) => new ProjectMigrationStep(from, o =>
            {
                log.Add(from + ":" + (int)o["schemaVersion"]);
                o["schemaVersion"] = 999; // ignored: the migrator overwrites it
                return o;
            });
            var m = new ProjectMigrator(3, new[] { S(1), S(0), S(2) });
            var r = m.Migrate(JObject.Parse("{\"schemaVersion\":0}"));
            Assert.Equal(new[] { "0:0", "1:1", "2:2" }, log);
            Assert.Equal(3, (int)r.Document["schemaVersion"]);
        }

        [Fact]
        public void AlreadyCurrentIsNoOp()
        {
            var r = Fake().Migrate(JObject.Parse("{\"schemaVersion\":3,\"x\":1}"));
            Assert.True(r.Success);
            Assert.False(r.Migrated);
            Assert.Equal(1, (int)r.Document["x"]);
        }

        [Theory]
        [InlineData("{\"schemaVersion\":4}", MigrationError.TooNew)]
        [InlineData("{\"schemaVersion\":2147483647}", MigrationError.TooNew)]
        [InlineData("{}", MigrationError.MissingVersion)]
        [InlineData("{\"SchemaVersion\":1}", MigrationError.MissingVersion)]
        [InlineData("{\"schemaVersion\":null}", MigrationError.InvalidVersion)]
        [InlineData("{\"schemaVersion\":\"1\"}", MigrationError.InvalidVersion)]
        [InlineData("{\"schemaVersion\":1.5}", MigrationError.InvalidVersion)]
        [InlineData("{\"schemaVersion\":1.0}", MigrationError.InvalidVersion)]
        [InlineData("{\"schemaVersion\":-1}", MigrationError.InvalidVersion)]
        [InlineData("{\"schemaVersion\":99999999999}", MigrationError.InvalidVersion)]
        [InlineData("{\"schemaVersion\":true}", MigrationError.InvalidVersion)]
        [InlineData("{\"schemaVersion\":[1]}", MigrationError.InvalidVersion)]
        public void RefusesBadVersions(string json, MigrationError expected)
        {
            var r = Fake().Migrate(ProjectJson.ParseObject(json));
            Assert.False(r.Success);
            Assert.Equal(expected, r.Error);
            Assert.Null(r.Document);
            Assert.NotNull(r.Message);
        }

        [Fact]
        public void DefaultRefusesNewerFile()
        {
            var o = ProjectJson.ParseObject(ReadData("project_v1.json"));
            o["schemaVersion"] = 2;
            var r = ProjectMigrator.Default.Migrate(o);
            Assert.Equal(MigrationError.TooNew, r.Error);
            Assert.Equal(2, r.FromVersion);
            var ex = Assert.Throws<ProjectMigrationException>(() => ProjectMigrator.Default.MigrateOrThrow(o));
            Assert.Equal(MigrationError.TooNew, ex.Error);
            Assert.Equal(2, ex.FileVersion);
        }

        [Fact]
        public void TooOldWhenBelowFirstStep()
        {
            var m = new ProjectMigrator(3, new[] { Step1To2(), Step2To3() });
            Assert.Equal(1, m.MinVersion);
            Assert.Equal(MigrationError.TooOld, m.Migrate(JObject.Parse("{\"schemaVersion\":0}")).Error);
            Assert.Equal(MigrationError.TooOld, ProjectMigrator.Default.Migrate(JObject.Parse("{\"schemaVersion\":0}")).Error);
        }

        [Fact]
        public void FailingStepsReported()
        {
            var throwing = new ProjectMigrator(1, new[] { new ProjectMigrationStep(0, o => throw new InvalidOperationException("boom")) });
            var r = throwing.Migrate(JObject.Parse("{\"schemaVersion\":0}"));
            Assert.Equal(MigrationError.StepFailed, r.Error);
            Assert.Contains("boom", r.Message);

            var nulling = new ProjectMigrator(1, new[] { new ProjectMigrationStep(0, o => null) });
            Assert.Equal(MigrationError.StepFailed, nulling.Migrate(JObject.Parse("{\"schemaVersion\":0}")).Error);

            // A step tripping over an unexpected shape (missing key) is a failure, not a crash.
            var r2 = Fake().Migrate(JObject.Parse("{\"schemaVersion\":1}"));
            Assert.Equal(MigrationError.StepFailed, r2.Error);
            Assert.Throws<ProjectMigrationException>(() => Fake().MigrateOrThrow(JObject.Parse("{\"schemaVersion\":1}")));
        }

        [Fact]
        public void ConstructorValidatesChain()
        {
            Assert.Throws<ArgumentException>(() => new ProjectMigrator(3, new[] { Step0To1(), Step2To3() })); // gap
            Assert.Throws<ArgumentException>(() => new ProjectMigrator(3, new[] { Step0To1(), Step0To1(), Step1To2(), Step2To3() })); // duplicate
            Assert.Throws<ArgumentException>(() => new ProjectMigrator(2, new[] { Step0To1(), Step1To2(), Step2To3() })); // past target
            Assert.Throws<ArgumentException>(() => new ProjectMigrator(3, new[] { Step0To1(), Step1To2() })); // short of target
            Assert.Throws<ArgumentException>(() => new ProjectMigrator(3, new ProjectMigrationStep[] { null }));
            Assert.Throws<ArgumentNullException>(() => new ProjectMigrator(3, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new ProjectMigrationStep(-1, o => o));
            Assert.Throws<ArgumentNullException>(() => new ProjectMigrationStep(0, null));
        }
    }
}
